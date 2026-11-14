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

    // A' = A << sigma, padded to t blocks of n words with the top bit of the top block clear
    Words aShifted(la + sigma / 64 + 1);
    FullIntCore::ShiftLeft(a, sigma, aShifted);
    CSpan at = DeleteZeros(aShifted);
    size_t bits = at.size() * 64 - (size_t)FullHelper::LeadingZeroCount(at[0]);
    size_t t = std::max<size_t>(2, (bits + 1 + 64 * n - 1) / (64 * n));
    Words z(t * n);
    std::copy(at.begin(), at.end(), z.end() - at.size());

    // from the top: Z = [R, next block], one n-word quotient block per step
    Words quotient((t - 1) * n), zz(2 * n), ri(n);
    std::copy(z.begin(), z.begin() + 2 * n, zz.begin());
    for (size_t p = 0; p + 2 <= t; p++)
    {
        Div2n1n(zz, bn, Span(quotient).subspan(p * n, n), ri, threshold);
        if (p + 2 <= t - 1)
        {
            std::copy(ri.begin(), ri.end(), zz.begin());
            std::copy(z.begin() + (p + 2) * n, z.begin() + (p + 3) * n, zz.begin() + n);
        }
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

// A (3k) < B·β^k, B = [B1 B2] (2k, top bit set) -> Q (k), R (2k)
void BurnikelZiegler::Div3n2n(CSpan a, CSpan b, Span q, Span r, int threshold)
{
    size_t k = b.size() / 2;
    CSpan a1 = a.subspan(0, k), a2 = a.subspan(k, k), a3 = a.subspan(2 * k, k);
    CSpan b1 = b.subspan(0, k), b2 = b.subspan(k, k);

    // X = [R1, A3] in 2k + 1 words (R1 can take k + 1 words)
    Words x(2 * k + 1);
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
    Words d(2 * k);
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
