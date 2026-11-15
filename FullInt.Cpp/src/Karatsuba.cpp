// Port of FullInt.Core/Karatsuba.cs. Limbs are big-endian (index 0 = most significant
// word), so the low half of a number is its last m words: halves are plain subspans.
//
//   a = a1·B^m + a0,  b = b1·B^m + b0          (a0, b0: the low m words)
//   z0 = a0·b0,  z2 = a1·b1,  zm = |a1 - a0| · |b1 - b0|
//   a·b = z2·B^2m + (z0 + z2 ∓ zm)·B^m + z0    (∓: minus when (a1-a0) and (b1-b0) have the same sign)
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cstring>
#include <memory>
#include <stdexcept>

namespace fullint {

namespace {

// the C# version rents from ArrayPool per call; here one buffer per thread, grown on demand
// (the recursion of one Multiply uses it in nested slices, never two Multiply at once)
Span ThreadScratch(size_t words)
{
    thread_local std::unique_ptr<uint64_t[]> buffer;
    thread_local size_t capacity = 0;
    if (capacity < words)
    {
        buffer = std::make_unique_for_overwrite<uint64_t[]>(words);
        capacity = words;
    }
    return Span(buffer.get(), words);
}

}  // namespace

void Karatsuba::Multiply(CSpan a, CSpan b, Span r)
{
    Multiply(a, b, r, Threshold, Toom3Threshold);
}

void Karatsuba::Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold)
{
    Multiply(a, b, r, threshold, toom3Threshold, Toom4Threshold);
}

void Karatsuba::Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold)
{
    Multiply(a, b, r, threshold, toom3Threshold, toom4Threshold, FftThreshold);
}

void Karatsuba::Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
{
    if (fftThreshold < 8) throw std::out_of_range("fftThreshold");
    if (threshold < 2) throw std::out_of_range("threshold");
    if (toom3Threshold < 3) throw std::out_of_range("toom3Threshold");
    if (toom4Threshold < 4) throw std::out_of_range("toom4Threshold");
    if (r.size() != a.size() + b.size()) throw std::invalid_argument("r.size() must be a.size() + b.size()");

    Mul(a, b, r, ThreadScratch(ScratchLength(a.size(), b.size())), threshold, toom3Threshold, toom4Threshold, fftThreshold);
}

// r = a · b, r.size() == a.size() + b.size(). The scratch is dirty on entry: everything taken
// from it is fully written before it is read.
void Karatsuba::Mul(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
{
    if (a.size() < b.size()) std::swap(a, b);
    size_t la = a.size(), lb = b.size();

    if (lb < (size_t)threshold)
    {
        FullIntCore::Mult(a, b, r);   // basecase: the column multiply
        return;
    }

    size_t m = (la + 1) / 2;
    if (lb <= m)
    {
        MulUnbalanced(a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
        return;
    }

    if (lb >= (size_t)fftThreshold)
    {
        FftMul(a, b, r, threshold, toom3Threshold, toom4Threshold, fftThreshold, 0);
        return;
    }

    if (lb >= (size_t)toom4Threshold && lb > 3 * ((la + 3) / 4))   // b reaches its top quarter
    {
        Toom4(a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
        return;
    }

    if (lb >= (size_t)toom3Threshold && lb > 2 * ((la + 2) / 3))   // b reaches its top third
    {
        Toom3(a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
        return;
    }

    size_t len = la + lb;
    CSpan a1 = a.subspan(0, la - m), a0 = a.subspan(la - m);
    CSpan b1 = b.subspan(0, lb - m), b0 = b.subspan(lb - m);

    // z0 and z2 go straight into their final places in r (they do not overlap)
    Span z0 = r.subspan(len - 2 * m);        // 2m words
    Span z2 = r.subspan(0, len - 2 * m);     // (la - m) + (lb - m) words

    Span da = scratch.subspan(0, m);
    Span db = scratch.subspan(m, m);
    Span zm = scratch.subspan(2 * m, 2 * m);
    Span mid = scratch.subspan(4 * m, 2 * m + 1);
    Span rest = scratch.subspan(6 * m + 1);  // for the recursive calls, one at a time

    Mul(a0, b0, z0, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
    Mul(a1, b1, z2, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);

    int sa = FullIntCore::Sub(a1, a0, da);   // da = |a1 - a0|, sa = sign of (a1 - a0)
    int sb = FullIntCore::Sub(b1, b0, db);

    // mid = z0 + z2 - (a1 - a0)(b1 - b0) = a1·b0 + a0·b1  (never negative, fits 2m + 1 words)
    FullIntCore::Add(z0, z2, mid);
    if (sa != 0 && sb != 0)
    {
        Mul(da, db, zm, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
        if (sa == sb)
            FullIntCore::Sub(mid, zm, mid);
        else
            FullIntCore::Add(mid, zm, mid);
    }

    // r += mid · B^m. In place: the add reads both words before writing, from the low end.
    // r[0 .. len - m) can be one word shorter than mid; that top word of mid is then zero.
    Span rUp = r.subspan(0, len - m);
    FullIntCore::Add(rUp, mid, rUp);
}

// a much longer than b (b.size() <= half of a): a is cut into chunks of b.size() words from
// the low end, each chunk·b (balanced, so Karatsuba again) is added in at its place.
void Karatsuba::MulUnbalanced(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
{
    size_t la = a.size(), lb = b.size(), len = la + lb;
    std::memset(r.data(), 0, r.size_bytes());

    Span tmp = scratch.subspan(0, 2 * lb);
    Span rest = scratch.subspan(2 * lb);

    for (size_t done = 0; done < la; done += lb)    // done = low words of a already multiplied
    {
        size_t c = std::min(lb, la - done);
        CSpan chunk = a.subspan(la - done - c, c);
        Span t = tmp.subspan(0, c + lb);
        Mul(chunk, b, t, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);

        // only the c + lb words at this offset change: everything below fits there, no carry out
        Span target = r.subspan(len - done - c - lb, c + lb);
        FullIntCore::Add(target, t, target);
    }
}

}  // namespace fullint
