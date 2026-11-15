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

using Words = std::vector<uint64_t>;   // zeroed work buffers (C#: new ulong[])

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
    Words ri(n), tt(at.begin(), at.begin() + n + h);
    Span topN = Span(tt).subspan(0, n);
    if (CompareEqualLength(topN, bn) >= 0)
    {
        FullIntCore::Sub(topN, bn, topN);
        quotient[0] = 1;
    }
    if (h == 0) std::copy(topN.begin(), topN.end(), ri.begin());
    else Div3n2n(tt, bn, Span(quotient).subspan(1, h), ri, threshold);

    // then from the top: Z = [R, next block], one n-word quotient block per step
    Words zz(2 * n);
    for (size_t p = 0; p < c; p++)
    {
        std::copy(ri.begin(), ri.end(), zz.begin());
        std::copy(at.begin() + n + h + p * n, at.begin() + n + h + (p + 1) * n, zz.begin() + n);
        Div2n1n(zz, bn, Span(quotient).subspan(1 + h + p * n, n), ri, threshold);
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
void BurnikelZiegler::Div2n1n(CSpan a, CSpan b, Span q, Span r, int threshold)
{
    size_t n = b.size();
    if ((n & 1) != 0 || n < (size_t)threshold)
    {
        Basecase(a, b, q, r);
        return;
    }

    size_t k = n / 2;
    Words r1(3 * k);   // [R, A4] for the second step
    Div3n2n(a.subspan(0, 3 * k), b, q.subspan(0, k), Span(r1).subspan(0, n), threshold);
    std::copy(a.begin() + 3 * k, a.begin() + 4 * k, r1.begin() + n);
    Div3n2n(r1, b, q.subspan(k, k), r, threshold);
}

// A (n + k) < B·β^k, B = [B1 B2] (n, top bit set; B1 k words) -> Q (k), R (n)
void BurnikelZiegler::Div3n2n(CSpan a, CSpan b, Span q, Span r, int threshold)
{
    // k = q.size(): n / 2 inside the recursion; any 1 <= k < n for the top part of DivRem
    // (A: n + k words, B1: the top k words of B, B2: the other n - k)
    size_t n = b.size(), k = q.size();
    CSpan a1 = a.subspan(0, k), a2 = a.subspan(k, k), a3 = a.subspan(2 * k, n - k);
    CSpan b1 = b.subspan(0, k), b2 = b.subspan(k, n - k);

    // X = [R1, A3] in n + 1 words (R1 can take k + 1 words)
    Words x(n + 1);
    Span r1 = Span(x).subspan(0, k + 1);
    if (CompareEqualLength(a1, b1) < 0)
    {
        Div2n1n(a.subspan(0, 2 * k), b1, q, r1.subspan(1), threshold);
    }
    else
    {
        // A1 = B1 here (A < B·β^k): Q̂ = β^k - 1, R1 = [A1 A2] - Q̂·B1 = A2 + B1
        std::fill(q.begin(), q.end(), ~0ull);
        FullIntCore::Add(a2, b1, r1);
    }
    std::copy(a3.begin(), a3.end(), x.begin() + k + 1);

    // R̂ = X - Q̂·B2, sign-magnitude in x
    Words d(n);
    Karatsuba::Multiply(q, b2, d);
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

// the kernel division on one block: A (2n) < B·β^n -> Q (n), R (n)
void BurnikelZiegler::Basecase(CSpan a, CSpan b, Span q, Span r)
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

    Words qb(dividend.size() - n + 1), rb(n + 2);
    FullIntCore::Div(dividend, b, qb, rb);

    // the quotient fits n words (A < B·β^n): any word above is zero
    if (qb.size() <= n) std::copy(qb.begin(), qb.end(), q.end() - qb.size());
    else std::copy(qb.end() - n, qb.end(), q.begin());
    std::copy(rb.begin() + 2, rb.end(), r.begin());
}

}  // namespace fullint
