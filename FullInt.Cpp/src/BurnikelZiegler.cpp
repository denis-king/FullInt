// Port of FullInt.Core/BurnikelZiegler.cs (Burnikel, Ziegler: "Fast Recursive Division", 1998).
// Limbs big-endian (index 0 = most significant word): halves and blocks are plain subspans.
//
//   Div2n1n(A, B):  A (2n words) < B·β^n, B (n words) with its top bit set
//     A = [A1 A2 A3 A4] (n/2 words each)
//     Q1, R = Div3n2n([A1 A2 A3], B);  Q2, S = Div3n2n([R A4], B);  Q = [Q1 Q2], remainder S
//   Div3n2n(A, B):  A (3k words) < B·β^k, B = [B1 B2] (k words each)
//     A1 < B1:  Q̂, R1 = Div2n1n([A1 A2], B1)      otherwise  Q̂ = β^k - 1, R1 = A2 + B1
//     R̂ = [R1 A3] - Q̂·B2;  while R̂ < 0: R̂ += B, Q̂ -= 1   (at most twice)
#include <fullint/BurnikelZiegler.h>
#include <fullint/FullHelper.h>
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cstring>
#include <stdexcept>
#include <vector>

namespace fullint {

namespace {

// Work words from a per-thread stack: taken and given back in LIFO order (the scopes nest), the
// stack grown to the deepest use seen, when it is empty. A new std::vector per buffer (several per
// division, tens of KB each) cost far more than the copies in them on Windows: the heap gives the
// pages of large freed blocks back, so each allocation faulted them in again, zero-filled -
// 2N / N at 1024 words ran at 1.4x GMP until the heap happened to settle after ~0.1 s, 1.0x after.
struct WorkStack
{
    uint64_t* buf = nullptr;
    size_t cap = 0, top = 0;    // top: the words taken from buf
    size_t used = 0, peak = 0;  // all words in use (from buf or not) and their maximum
    ~WorkStack() { delete[] buf; }
};
thread_local WorkStack workStack;

class Words   // n work words; zeroed (as the std::vector before) unless told otherwise
{
public:
    explicit Words(size_t n, bool zeroed = true) : n_(n)
    {
        WorkStack& w = workStack;
        if (w.used == 0 && w.cap < w.peak)   // nothing in use: grow to the deepest use seen
        {
            delete[] w.buf;
            w.buf = new uint64_t[w.peak];
            w.cap = w.peak;
        }
        if (w.top + n <= w.cap)
        {
            p_ = w.buf + w.top;
            w.top += n;
            stacked_ = true;
        }
        else
        {
            own_ = std::make_unique_for_overwrite<uint64_t[]>(n);
            p_ = own_.get();
        }
        w.used += n;
        if (w.used > w.peak) w.peak = w.used;
        if (zeroed && n) std::memset(p_, 0, n * sizeof(uint64_t));
    }
    ~Words()
    {
        workStack.used -= n_;
        if (stacked_) workStack.top -= n_;
    }
    Words(const Words&) = delete;
    Words& operator=(const Words&) = delete;

    uint64_t* data() { return p_; }
    const uint64_t* data() const { return p_; }
    size_t size() const { return n_; }
    uint64_t* begin() { return p_; }
    uint64_t* end() { return p_ + n_; }
    const uint64_t* begin() const { return p_; }
    const uint64_t* end() const { return p_ + n_; }
    uint64_t& operator[](size_t i) { return p_[i]; }
    operator Span() { return Span(p_, n_); }
    operator CSpan() const { return CSpan(p_, n_); }

private:
    uint64_t* p_;
    size_t n_;
    bool stacked_ = false;
    std::unique_ptr<uint64_t[]> own_;
};

CSpan DeleteZeros(CSpan v)
{
    size_t z = 0;
    while (z + 1 < v.size() && v[z] == 0) z++;
    return v.subspan(z);
}

int CompareEqualLength(CSpan x, CSpan y)
{
    for (size_t i = 0; i < x.size(); i++)
        if (x[i] != y[i]) return x[i] > y[i] ? 1 : -1;
    return 0;
}

void Clear(Span s) { std::memset(s.data(), 0, s.size_bytes()); }

// work words of the quotient path: on the stack up to 160 words (the small divisions, where an
// allocation per buffer cost more than the split saved), the work stack above; not cleared
struct LocalWords
{
    explicit LocalWords(size_t n) : n_(n), big_(n > std::size(inline_) ? n : 0, false) {}
    Span span() { return Span(data(), n_); }
    uint64_t* data() { return n_ > std::size(inline_) ? big_.data() : inline_; }

private:
    uint64_t inline_[160];
    size_t n_;
    Words big_;
};

}  // namespace

void BurnikelZiegler::DivRem(CSpan a, CSpan b, Span q, Span r)
{
    DivRem(a, b, q, r, Threshold);
}

void BurnikelZiegler::DivRem(CSpan a, CSpan b, Span q, Span r, int threshold)
{
    if (threshold < 2) throw std::out_of_range("threshold");
    size_t la = a.size(), lb = b.size();

    // n = j·m >= lb, m = 2^k the smallest with m·threshold > lb, so j < threshold
    size_t m = 1;
    while (m * (size_t)threshold <= lb) m *= 2;
    size_t n = (lb + m - 1) / m * m;
    int sigma = (int)((n - lb) * 64) + FullHelper::LeadingZeroCount(b[0]);

    // B' = B << sigma: exactly n words, top bit set
    Words bShifted(lb + sigma / 64 + 1);
    FullIntCore::ShiftLeft(b, sigma, bShifted);
    CSpan bn = CSpan(bShifted).subspan(bShifted.size() - n);

    // A' = A << sigma: [top: n + h words][c blocks of n words], 0 <= h < n
    Words aShifted(la + sigma / 64 + 1);
    FullIntCore::ShiftLeft(a, sigma, aShifted);
    CSpan at = DeleteZeros(aShifted);
    size_t c = (at.size() - n) / n, h = at.size() - n - c * n;
    Words quotient(1 + h + c * n);   // [0 or 1][h words][c blocks]

    // The top part. Its top n words are below β^n <= 2·B' (B' has its top bit set): if not
    // below B', that quotient word is 1 and B' is subtracted once. Then its h quotient
    // words by one unbalanced step (Div3n2n with a k = h split of B'). The textbook pads
    // A' to whole blocks with its top bit clear instead: an extra, nearly empty block that
    // costs a whole Div2n1n (half of all numbers; 4096 / 2048 words ~2x, 8192 / 4096 more).
    Words ri(n), tt(n + h, false);
    std::copy(at.begin(), at.begin() + n + h, tt.begin());
    // one work buffer for the whole recursion (was a few vectors per node): ScratchWords(n)
    Words scratch(ScratchWords(n, threshold));
    Span topN = Span(tt).subspan(0, n);
    if (CompareEqualLength(topN, bn) >= 0)
    {
        FullIntCore::Sub(topN, bn, topN);
        quotient[0] = 1;
    }
    if (h == 0) std::copy(topN.begin(), topN.end(), ri.begin());
    else Div3n2n(tt, bn, Span(quotient).subspan(1, h), ri, threshold, scratch);

    // then from the top: Z = [R, next block], one n-word quotient block per step
    Words zz(2 * n);
    for (size_t p = 0; p < c; p++)
    {
        std::copy(ri.begin(), ri.end(), zz.begin());
        std::copy(at.begin() + n + h + p * n, at.begin() + n + h + (p + 1) * n, zz.begin() + n);
        Div2n1n(zz, bn, Span(quotient).subspan(1 + h + p * n, n), ri, threshold, scratch);
    }

    // q: the low q.size() words (the quotient fits; any words above are zero)
    Clear(q);
    if (q.size() >= quotient.size()) std::copy(quotient.begin(), quotient.end(), q.end() - quotient.size());
    else std::copy(quotient.end() - q.size(), quotient.end(), q.begin());

    // r = R' >> sigma: lb words, right-aligned in r
    Clear(r);
    FullIntCore::ShiftRight(ri, sigma, r.subspan(r.size() - lb));
}

// A (2n) < B·β^n, B (n) with its top bit set -> Q (n), R (n)
void BurnikelZiegler::Div2n1n(CSpan a, CSpan b, Span q, Span r, int threshold, Span scratch)
{
    size_t n = b.size();
    if (n < (size_t)threshold)
    {
        Basecase(a, b, q, r, scratch);
        return;
    }

    // an odd n splits into k1 = ceil(n/2) and k2 = floor(n/2) quotient words (was: the kernel
    // on the whole block, quadratic)
    size_t k1 = (n + 1) / 2, k2 = n / 2;
    Span r1 = scratch.subspan(0, n + k2);   // [R, A4] for the second step (fully written)
    Span rest = scratch.subspan(n + k2);
    Div3n2n(a.subspan(0, n + k1), b, q.subspan(0, k1), r1.subspan(0, n), threshold, rest);
    std::copy(a.begin() + n + k1, a.begin() + 2 * n, r1.begin() + n);
    Div3n2n(r1, b, q.subspan(k1, k2), r, threshold, rest);
}

// A (n + k) < B·β^k, B = [B1 B2] (n, top bit set; B1 k words) -> Q (k), R (n)
void BurnikelZiegler::Div3n2n(CSpan a, CSpan b, Span q, Span r, int threshold, Span scratch)
{
    // k = q.size(): n / 2 inside the recursion; any 1 <= k < n for the top part of DivRem
    // (A: n + k words, B1: the top k words of B, B2: the other n - k)
    size_t n = b.size(), k = q.size();
    CSpan a1 = a.subspan(0, k), a2 = a.subspan(k, k), a3 = a.subspan(2 * k, n - k);
    CSpan b1 = b.subspan(0, k), b2 = b.subspan(k, n - k);

    // X = [R1, A3] in n + 1 words (R1 can take k + 1 words)
    Span x = scratch.subspan(0, n + 1), d = scratch.subspan(n + 1, n), rest = scratch.subspan(2 * n + 1);
    Span r1 = x.subspan(0, k + 1);
    if (CompareEqualLength(a1, b1) < 0)
    {
        x[0] = 0;
        Div2n1n(a.subspan(0, 2 * k), b1, q, r1.subspan(1), threshold, rest);
    }
    else
    {
        // A1 = B1 here (A < B·β^k): Q̂ = β^k - 1, R1 = [A1 A2] - Q̂·B1 = A2 + B1
        std::fill(q.begin(), q.end(), ~0ull);
        FullIntCore::Add(a2, b1, r1);
    }
    std::copy(a3.begin(), a3.end(), x.begin() + k + 1);

    // R̂ = X - Q̂·B2, sign-magnitude in x
    Karatsuba::Multiply(q, b2, d);   // writes all of d
    bool negative = FullIntCore::Sub(x, d, x) < 0;

    // R̂ < 0: R̂ += B, Q̂ -= 1. With |R̂| in x: |R̂| > B stays negative (|R̂| - B),
    // otherwise R̂ = B - |R̂| >= 0. Sub gives |x - B| either way.
    const uint64_t one[1] = { 1 };
    while (negative)
    {
        FullIntCore::Sub(q, CSpan(one, 1), q);
        negative = FullIntCore::Sub(x, b, x) > 0;
    }

    std::copy(x.begin() + 1, x.end(), r.begin());   // R < B: the top word is zero
}

void BurnikelZiegler::Quotient(CSpan a, CSpan b, Span q)
{
    Quotient(a, b, q, Threshold);
}

void BurnikelZiegler::Quotient(CSpan a, CSpan b, Span q, int threshold)
{
    if (threshold < 2) throw std::out_of_range("threshold");
    size_t la = a.size(), n = b.size();
    LocalWords rTmp(n + 2);
    if (la - n + 2 < (size_t)QuotientSplitFrom && n <= la - n + 4)
    {
        // neither split nor cut below (see Approx): the exact division directly
        ExactQuotient(a, b, q, threshold);
        return;
    }
    LocalWords ae(la + 1);                                           // A·β
    std::copy(a.begin(), a.end(), ae.data());
    ae.data()[la] = 0;
    LocalWords qa(la - n + 2);
    int err = Approx(ae.span(), b, qa.span(), threshold);
    uint64_t low = qa.data()[la - n + 1];
    if (low >= (uint64_t)err && low <= ~0ull - (uint64_t)err)
    {
        std::copy(qa.data(), qa.data() + (la - n + 1), q.begin());
        return;
    }
    Exact(a, b, q, rTmp.span(), threshold);
}

// Q̃ ~ floor(A / B), returns E with |Q̃ - floor(A / B)| <= E, Q̃ >= 0. A may have leading zero
// words, B not; q: at least the quotient's length, right-aligned, fully written.
//   B longer than the quotient + 2 words: both cut down to that (the dropped words change the
//   quotient by at most 1: B_t >= β^(qn+1), A/B >= A_t/(B_t + 1) >= A_t/B_t - 1/β).
//   Otherwise the top half of the quotient exactly (with its remainder R), the low half
//   approximately from [R, the low words of A] (which is then cut down again).
int BurnikelZiegler::Approx(CSpan a, CSpan b, Span q, int threshold)
{
    Clear(q);
    a = DeleteZeros(a);
    size_t la = a.size(), n = b.size();
    if (la < n || (la == n && CompareEqualLength(a, b) < 0)) return 0;
    size_t qn = la - n + 1;

    if (n > qn + 2)
    {
        size_t d = n - (qn + 2);
        return Approx(a.subspan(0, la - d), b.subspan(0, n - d), q, threshold) + 1;
    }
    // below: the split costs more than it saves (and from 4 on only does the low half get
    // shorter: qn = 2 would split into a low half of 2 again, for ever)
    if (qn < (size_t)std::max(4, QuotientSplitFrom))
    {
        ExactQuotient(a, b, q, threshold);
        return 0;
    }

    size_t k = qn / 2;
    Span qq = q.subspan(q.size() - qn);
    LocalWords rem(n + 2);
    Exact(a.subspan(0, la - k), b, qq.subspan(0, qn - k), rem.span(), threshold);
    LocalWords x(n + k);                                             // [R, the low k words of A]
    std::copy(rem.data() + 2, rem.data() + n + 2, x.data());
    std::copy(a.end() - k, a.end(), x.data() + n);
    LocalWords ql(k + 1);
    int e = Approx(x.span(), b, ql.span(), threshold);
    FullIntCore::Add(qq, ql.span(), qq);                             // the low k words are zero; a carry goes up
    return e;
}

// exact: q (right-aligned, at least the quotient's length) = A / B, r (n + 2 words) the
// remainder right-aligned; A may have leading zero words, B not
void BurnikelZiegler::Exact(CSpan a, CSpan b, Span q, Span r, int threshold)
{
    Clear(q);
    Clear(r);
    a = DeleteZeros(a);
    size_t la = a.size(), n = b.size();
    if (la < n || (la == n && CompareEqualLength(a, b) < 0))
    {
        std::copy(a.begin(), a.end(), r.end() - la);
        return;
    }
    Span qb = q.subspan(q.size() - (la - n + 1));
    if (n >= 2 * (size_t)threshold && la - n >= 2 * (size_t)threshold) DivRem(a, b, qb, r, threshold);
    else FullIntCore::Div(a, b, qb, r);
}

// exact, the quotient alone (q as Exact): a kernel-sized block by the kernel's approximate
// quotient (it settles a doubtful guard word itself)
void BurnikelZiegler::ExactQuotient(CSpan a, CSpan b, Span q, int threshold)
{
    a = DeleteZeros(a);
    size_t la = a.size(), n = b.size();
    if ((n >= 2 * (size_t)threshold && la - n >= 2 * (size_t)threshold) || la <= n)
    {
        LocalWords rTmp(n + 2);
        Exact(a, b, q, rTmp.span(), threshold);
        return;
    }
    Clear(q);
    FullIntCore::Quotient(a, b, q.subspan(q.size() - (la - n + 1)));
}

// the kernel division on one block: A (2n) < B·β^n -> Q (n), R (n)
void BurnikelZiegler::Basecase(CSpan a, CSpan b, Span q, Span r, Span scratch)
{
    size_t n = b.size();
    CSpan dividend = DeleteZeros(a);   // the kernels expect no leading zeros
    Clear(q);
    if (dividend.size() < n)   // A < β^(n-1) <= B
    {
        Clear(r);
        std::copy(dividend.begin(), dividend.end(), r.end() - dividend.size());
        return;
    }

    Span qb = scratch.subspan(0, dividend.size() - n + 1), rb = scratch.subspan(qb.size(), n + 2);
    Clear(qb);
    Clear(rb);
    FullIntCore::Div(dividend, b, qb, rb);

    // the quotient fits n words (A < B·β^n): any word above is zero
    if (qb.size() <= n) std::copy(qb.begin(), qb.end(), q.end() - qb.size());
    else std::copy(qb.end() - n, qb.end(), q.begin());
    std::copy(rb.begin() + 2, rb.end(), r.begin());
}

}  // namespace fullint
