// Port of FullInt.Core/NewtonDivision.cs.
//
//   B' = B << sigma (n words, top bit set), A' = A << sigma.
//   Reciprocal of the top s words D of B': I ~ β^(2s)/D, s + 1 words (in (β^s, 2β^s]):
//     s small: one division;  otherwise h = s/2 + 1 (a guard word, so the error stays a few
//     units), Ih ~ β^(2h)/D_h (D_h: the top h words of D, recursively),
//     e = β^(s+h) - D·Ih (signed, small),  I = Ih·β^(s-h) + Ih·e/β^(2h).
//   Quotient blocks of t <= s words from the top: X = [R, next t words of A'] < B'·β^t,
//     q̂ = (top t words of X)·(top t + 1 words of I) / β^t  (a few units off the true q),
//     R = X - q̂·B', then q̂ -= 1 while that is negative, q̂ += 1 while R >= B'.
#include <fullint/NewtonDivision.h>
#include <fullint/FullHelper.h>
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace fullint {

namespace {

using Words = std::vector<uint64_t>;

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

bool IsZero(CSpan x)
{
    for (uint64_t w : x)
        if (w != 0) return false;
    return true;
}

void Increment(Span x)
{
    for (size_t i = x.size(); i-- > 0;)
        if (++x[i] != 0) return;
}

void Decrement(Span x)
{
    for (size_t i = x.size(); i-- > 0;)
        if (x[i]-- != 0) return;
}

// x = β^len - x (two's complement in place)
void NegateLow(Span x)
{
    for (auto& w : x) w = ~w;
    Increment(x);
}

// r = R >> sigma: n words, right-aligned in r
void Finish(CSpan rem, int sigma, Span r)
{
    std::memset(r.data(), 0, r.size_bytes());
    FullIntCore::ShiftRight(rem, sigma, r.subspan(r.size() - rem.size()));
}

}  // namespace

void NewtonDivision::DivRem(CSpan a, CSpan b, Span q, Span r)
{
    DivRem(a, b, q, r, Split, InverseBase);
}

void NewtonDivision::DivRem(CSpan a, CSpan b, Span q, Span r, int split, int inverseBase)
{
    if (split < 0) throw std::out_of_range("split");
    if (inverseBase < 2) throw std::out_of_range("inverseBase");
    size_t la = a.size(), n = b.size();
    int sigma = FullHelper::LeadingZeroCount(b[0]);

    // B' = B << sigma: n words, top bit set;  A' = A << sigma
    Words bShifted(n + 1);
    FullIntCore::ShiftLeft(b, sigma, bShifted);
    CSpan bn = CSpan(bShifted).subspan(1);
    Words aShifted(la + 1);
    FullIntCore::ShiftLeft(a, sigma, aShifted);
    CSpan at = DeleteZeros(aShifted);

    std::memset(q.data(), 0, q.size_bytes());
    Words rem(n);
    if (at.size() < n || (at.size() == n && CompareEqualLength(at, bn) < 0))
    {
        std::copy(at.begin(), at.end(), rem.end() - at.size());     // A' < B': q = 0
        Finish(rem, sigma, r);
        return;
    }

    size_t qn = at.size() - n;                                       // quotient words below the top one
    Words quotient(qn + 1);
    std::copy(at.begin(), at.begin() + n, rem.begin());
    if (CompareEqualLength(rem, bn) >= 0)
    {
        FullIntCore::Sub(rem, bn, rem);
        quotient[0] = 1;
    }

    if (qn > 0)
    {
        if (split == 0) split = qn >= 2 * n || n < (size_t)SplitFrom ? 1 : 2;
        size_t blocks = std::max<size_t>(1, (qn + n - 1) / n) * (size_t)split;
        size_t s = std::min(n, (qn + blocks - 1) / blocks);
        Words inv = Inverse(bn.subspan(0, s), inverseBase);        // s + 1 words

        size_t t = qn - (qn - 1) / s * s;                            // the first block: 1..s words
        Words x(n + s), prod(2 * s + 1), tb(n + s);
        for (size_t pos = n; pos < at.size(); pos += t, t = s)
        {
            // X = [R, next t words]
            Span xs = Span(x).subspan(0, n + t);
            std::copy(rem.begin(), rem.end(), xs.begin());
            std::copy(at.begin() + pos, at.begin() + pos + t, xs.begin() + n);
            Span qb = Span(quotient).subspan(1 + pos - n, t);

            // q̂ = top t words of X · top t + 1 words of I, over β^t (kept below β^t)
            Span p = Span(prod).subspan(0, 2 * t + 1);
            Karatsuba::Multiply(xs.subspan(0, t), CSpan(inv).subspan(0, t + 1), p);
            if (p[0] != 0) std::fill(qb.begin(), qb.end(), ~0ull);
            else std::copy(p.begin() + 1, p.begin() + 1 + t, qb.begin());

            // T = q̂·B';  q̂ -= 1 while T > X;  R = X - T;  q̂ += 1 while R >= B'
            Span tt = Span(tb).subspan(0, n + t);
            Karatsuba::Multiply(bn, qb, tt);
            while (FullIntCore::Compare(tt, xs) > 0)
            {
                FullIntCore::Sub(tt, bn, tt);
                Decrement(qb);
            }
            FullIntCore::Sub(xs, tt, xs);
            while (!IsZero(xs.subspan(0, t)) || CompareEqualLength(xs.subspan(t), bn) >= 0)
            {
                FullIntCore::Sub(xs, bn, xs);
                Increment(qb);
            }
            std::copy(xs.begin() + t, xs.end(), rem.begin());
        }
    }

    // q: the low q.size() words (the quotient fits; any words above are zero)
    if (q.size() >= quotient.size()) std::copy(quotient.begin(), quotient.end(), q.end() - quotient.size());
    else std::copy(quotient.end() - q.size(), quotient.end(), q.begin());
    Finish(rem, sigma, r);
}

std::vector<uint64_t> NewtonDivision::Inverse(CSpan d, int inverseBase)
{
    size_t s = d.size();
    if (s <= (size_t)inverseBase || s < 4)
    {
        // floor(β^(2s) / D): the quotient has s + 2 words, the top one zero
        Words num(2 * s + 1), qq(s + 2), rr(s + 2);
        num[0] = 1;
        FullIntCore::Div(num, d, qq, rr);
        return Words(qq.begin() + 1, qq.end());
    }

    size_t h = s / 2 + 1;
    Words ih = Inverse(d.subspan(0, h), inverseBase);               // h + 1 words

    // e = β^(s+h) - D·Ih  (|e| < β^(s+2))
    Words p(s + h + 1);
    Karatsuba::Multiply(d, ih, p);
    bool eNegative = p[0] != 0;
    if (eNegative) p[0]--;                                            // |e| = P - β^(s+h)
    else NegateLow(Span(p).subspan(1));                               // |e| = β^(s+h) - P
    // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
    CSpan et = DeleteZeros(CSpan(p).subspan(0, s + 2));

    Words inv(s + 1);
    std::copy(ih.begin(), ih.end(), inv.begin());
    if (et.size() == 1 && et[0] == 0) return inv;
    Words c(ih.size() + et.size());
    Karatsuba::Multiply(ih, et, c);
    CSpan cs = DeleteZeros(CSpan(c).subspan(0, c.size() - (h + 1)));   // / β^(h+1)
    if (cs.size() == 1 && cs[0] == 0) return inv;
    if (eNegative) FullIntCore::Sub(inv, cs, inv);
    else FullIntCore::Add(inv, cs, inv);
    return inv;
}

}  // namespace fullint
