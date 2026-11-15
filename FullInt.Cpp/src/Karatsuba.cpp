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
    Multiply(a, b, r, threshold, toom3Threshold, toom4Threshold, Toom6Threshold, fftThreshold);
}

void Karatsuba::Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold, int toom6Threshold, int fftThreshold)
{
    if (fftThreshold < 8) throw std::out_of_range("fftThreshold");
    if (threshold < 2) throw std::out_of_range("threshold");
    if (toom3Threshold < 3) throw std::out_of_range("toom3Threshold");
    if (toom4Threshold < 4) throw std::out_of_range("toom4Threshold");
    if (toom6Threshold < 6) throw std::out_of_range("toom6Threshold");
    if (r.size() != a.size() + b.size()) throw std::invalid_argument("r.size() must be a.size() + b.size()");

    Limits lim{ threshold, toom3Threshold, toom4Threshold, toom6Threshold, fftThreshold, threshold == Threshold ? SqrThreshold : threshold };
    Mul(a, b, r, ThreadScratch(ScratchLength(a.size(), b.size())), lim);
}

// r = a · b, r.size() == a.size() + b.size(). The scratch is dirty on entry: everything taken
// from it is fully written before it is read.
void Karatsuba::Mul(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim)
{
    if (a.size() < b.size()) std::swap(a, b);
    size_t la = a.size(), lb = b.size();
    // a square (the same words twice, as the Toom and FFT levels pass their values of a square):
    // one evaluation, every subproduct a square again, the square kernel below
    bool square = Same(a, b);

    if (square ? lb < (size_t)lim.SqrKara : lb < (size_t)lim.Kara)
    {
        if (square) FullIntCore::Sqr(a, r);
        else FullIntCore::Mult(a, b, r);   // basecase: the column multiply
        return;
    }

    size_t m = (la + 1) / 2;
    if (lb <= m)
    {
        MulUnbalanced(a, b, r, scratch, lim);
        return;
    }

    if (lb >= (size_t)lim.Fft)
    {
        FftMul(a, b, r, lim, 0);
        return;
    }

    if (lb >= (size_t)lim.Toom6 && lb > 5 * ((la + 5) / 6) && la >= 36)   // b reaches its top sixth
    {
        Toom6(a, b, r, scratch, lim);
        return;
    }

    if (lb >= (size_t)lim.Toom4 && lb > 3 * ((la + 3) / 4))   // b reaches its top quarter
    {
        Toom4(a, b, r, scratch, lim);
        return;
    }

    if (lb >= (size_t)lim.Toom3 && lb > 2 * ((la + 2) / 3))   // b reaches its top third
    {
        Toom3(a, b, r, scratch, lim);
        return;
    }

    size_t len = la + lb;
    CSpan a1 = a.subspan(0, la - m), a0 = a.subspan(la - m);
    CSpan b1 = b.subspan(0, lb - m), b0 = b.subspan(lb - m);

    // z0 and z2 go straight into their final places in r (they do not overlap)
    Span z0 = r.subspan(len - 2 * m);        // 2m words
    Span z2 = r.subspan(0, len - 2 * m);     // (la - m) + (lb - m) words

    Span da = scratch.subspan(0, m);
    Span db = square ? da : scratch.subspan(m, m);
    Span zm = scratch.subspan(2 * m, 2 * m);
    Span mid = scratch.subspan(4 * m, 2 * m + 1);
    Span rest = scratch.subspan(6 * m + 1);  // for the recursive calls, one at a time

    Mul(a0, b0, z0, rest, lim);
    Mul(a1, b1, z2, rest, lim);

    int sa = FullIntCore::Sub(a1, a0, da);   // da = |a1 - a0|, sa = sign of (a1 - a0)
    int sb = square ? sa : FullIntCore::Sub(b1, b0, db);   // a square: (a1 - a0)², subtracted

    // mid = z0 + z2 - (a1 - a0)(b1 - b0) = a1·b0 + a0·b1  (never negative, fits 2m + 1 words)
    FullIntCore::Add(z0, z2, mid);
    if (sa != 0 && sb != 0)
    {
        Mul(da, db, zm, rest, lim);
        if (sa == sb)
            FullIntCore::Sub(mid, zm, mid);
        else
            FullIntCore::Add(mid, zm, mid);
    }

    // r += mid · B^m. In place: the add reads both words before writing, from the low end.
    // r[0 .. len - m) can be one word shorter than mid; that top word of mid is then zero.
    AddAt(r, mid, len - m);
}

// r[0 .. upTo) += c (the value fits there). Only the words c lands on and one for its carry: the
// Add kernel walks its whole first operand (it copies it on), so an add into all of r above
// c's place (as before) cost the length of the product per coefficient - 9 of them per Toom-6
// level, ~63k words of passes instead of ~18k. A carry beyond them goes on by hand (rare).
void Karatsuba::AddAt(Span r, CSpan c, size_t upTo)
{
    size_t z = 0;
    while (z + 1 < c.size() && c[z] == 0) z++;
    CSpan v = c.subspan(z);
    size_t width = std::min(upTo, v.size() + 1);
    Span target = r.subspan(upTo - width, width);
    if (FullIntCore::Add(target, v, target) != 0)
        for (size_t j = upTo - width; j-- > 0;)
            if (++r[j] != 0) break;
}

// a much longer than b (b.size() <= half of a): a is cut into chunks of b.size() words from
// the low end, each chunk·b (balanced, so Karatsuba again) is added in at its place.
void Karatsuba::MulUnbalanced(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim)
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
        Mul(chunk, b, t, rest, lim);

        // only the c + lb words at this offset change: everything below fits there, no carry out
        Span target = r.subspan(len - done - c - lb, c + lb);
        FullIntCore::Add(target, t, target);
    }
}

}  // namespace fullint
