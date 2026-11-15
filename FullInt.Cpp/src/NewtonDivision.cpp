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

// x = |x - y| for the value v = x - y mod β^m - 1 (x, y: m-word residues) with |v| < β^m / 2;
// returns v < 0. x - y is v or v ± (β^m - 1): above β^m / 2 in size it is the latter, and
// (β^m - 1) - |x - y| is the complement of its words.
bool SignedResidue(Span x, CSpan y)
{
    bool negative = FullIntCore::Sub(x, y, x) < 0;
    if ((x[0] >> 63) != 0)
    {
        for (auto& w : x) w = ~w;
        negative = !negative;
    }
    return negative && !IsZero(x);
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
    DivRem(a, b, q, r, split, inverseBase, 0);
}

void NewtonDivision::DivRem(CSpan a, CSpan b, Span q, Span r, int split, int inverseBase, size_t cyclicFrom)
{
    if (cyclicFrom == 0) cyclicFrom = (size_t)CyclicFrom;
    if (split < 0) throw std::out_of_range("split");
    if (inverseBase < 2) throw std::out_of_range("inverseBase");
    size_t n = b.size();
    int sigma = FullHelper::LeadingZeroCount(b[0]);

    // B' = B << sigma: n words, top bit set
    Words bShifted(n + 1);
    FullIntCore::ShiftLeft(b, sigma, bShifted);
    Run(a, CSpan(bShifted).subspan(1), sigma, nullptr, split, inverseBase, cyclicFrom, q, r);
}

NewtonDivision::Prepared NewtonDivision::Prepare(CSpan b)
{
    Prepared p;
    size_t n = b.size();
    p.sigma = FullHelper::LeadingZeroCount(b[0]);
    p.shifted.assign(n + 1, 0);
    FullIntCore::ShiftLeft(b, p.sigma, p.shifted);
    p.inverse = Inverse(CSpan(p.shifted).subspan(1), InverseBase, (size_t)CyclicFrom);   // n + 1 words
    return p;
}

void NewtonDivision::DivRem(CSpan a, const Prepared& p, Span q, Span r)
{
    Run(a, CSpan(p.shifted).subspan(1), p.sigma, &p.inverse, 1, InverseBase, (size_t)CyclicFrom, q, r);
}

// the division by B' = B << sigma (n words, top bit set); inverseN: B''s whole reciprocal (n + 1
// words, the blocks then take n words, any shorter ones truncate it), or none: one of the
// length the split asks for, computed here
void NewtonDivision::Run(CSpan a, CSpan bn, int sigma, const std::vector<uint64_t>* inverseN, int split, int inverseBase,
                         size_t cyclicFrom, Span q, Span r)
{
    size_t la = a.size(), n = bn.size();
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
        size_t s;
        Words own;
        const Words* inv;
        if (inverseN)
        {
            s = n;
            inv = inverseN;
        }
        else
        {
            if (split == 0) split = qn >= 2 * n || n < (size_t)SplitFrom ? 1 : 2;
            size_t blocks = std::max<size_t>(1, (qn + n - 1) / n) * (size_t)split;
            s = std::min(n, (qn + blocks - 1) / blocks);
            own = Inverse(bn.subspan(0, s), inverseBase, cyclicFrom);   // s + 1 words
            inv = &own;
        }

        size_t t = qn - (qn - 1) / s * s;                            // the first block: 1..s words
        Words x(n + s), prod(2 * s + 1), tb(n + s);
        // long: T = q̂·B' only modulo β^m - 1 (m >= n + 2, the cyclic FFT, about half the
        // product): R = X - T is below 8·B' < β^m / 2 in size, so its residue gives it back
        bool wrap = n + 2 >= cyclicFrom;
        size_t mw = wrap ? Karatsuba::MulModLength(n + 2, cyclicFrom) : 0;
        Words xm(mw), tm(mw);
        for (size_t pos = n; pos < at.size(); pos += t, t = s)
        {
            // X = [R, next t words]
            Span xs = Span(x).subspan(0, n + t);
            std::copy(rem.begin(), rem.end(), xs.begin());
            std::copy(at.begin() + pos, at.begin() + pos + t, xs.begin() + n);
            Span qb = Span(quotient).subspan(1 + pos - n, t);

            // q̂ = top t words of X · top t + 1 words of I, over β^t (kept below β^t)
            Span p = Span(prod).subspan(0, 2 * t + 1);
            Karatsuba::Multiply(xs.subspan(0, t), CSpan(*inv).subspan(0, t + 1), p);
            if (p[0] != 0) std::fill(qb.begin(), qb.end(), ~0ull);
            else std::copy(p.begin() + 1, p.begin() + 1 + t, qb.begin());

            if (wrap)
            {
                Karatsuba::FoldMod(xs, xm);
                Karatsuba::MultiplyMod(bn, qb, tm, cyclicFrom);
                bool negative = SignedResidue(xm, tm);              // R = X - T, |R| in xm
                // R < 0: R += B', q̂ -= 1 (|R| > B' stays negative: |R| - B'; else B' - |R|)
                while (negative)
                {
                    Decrement(qb);
                    negative = FullIntCore::Sub(xm, bn, xm) > 0;
                }
                while (!IsZero(CSpan(xm).subspan(0, mw - n)) || CompareEqualLength(CSpan(xm).subspan(mw - n), bn) >= 0)
                {
                    FullIntCore::Sub(xm, bn, xm);
                    Increment(qb);
                }
                std::copy(xm.end() - n, xm.end(), rem.begin());
                continue;
            }

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
    return Inverse(d, inverseBase, (size_t)CyclicFrom);
}

std::vector<uint64_t> NewtonDivision::Inverse(CSpan d, int inverseBase, size_t cyclicFrom)
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
    Words ih = Inverse(d.subspan(0, h), inverseBase, cyclicFrom);   // h + 1 words

    // e = β^(s+h) - D·Ih  (|e| < β^(s+2))
    bool eNegative;
    Words p;
    CSpan et;
    if (s + 3 >= cyclicFrom)
    {
        // long: D·Ih only modulo β^m - 1 (m >= s + 3): e = β^((s+h) mod m) - P̃ there, and
        // |e| < β^m / 2 gives it back
        size_t m = Karatsuba::MulModLength(s + 3, cyclicFrom);
        Words pm(m);
        Karatsuba::MultiplyMod(d, ih, pm, cyclicFrom);
        p.assign(m, 0);
        p[m - 1 - (s + h) % m] = 1;
        eNegative = SignedResidue(p, pm);
        // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
        et = DeleteZeros(CSpan(p).subspan(0, m - (h - 1)));
    }
    else
    {
        p.assign(s + h + 1, 0);
        Karatsuba::Multiply(d, ih, p);
        eNegative = p[0] != 0;
        if (eNegative) p[0]--;                                        // |e| = P - β^(s+h)
        else NegateLow(Span(p).subspan(1));                           // |e| = β^(s+h) - P
        // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
        et = DeleteZeros(CSpan(p).subspan(0, s + 2));
    }

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
