// Port of FullInt.Core/Toom3.cs.
//
// Toom-3 (Toom-Cook 3-way), evaluation points 0, 1, -1, -2, inf; Bodrato's interpolation:
//   a = a2·X² + a1·X + a0,  b likewise,  X = B^k  (a0, a1: k words, a2: the rest)
//   r(t) = a(t)·b(t) at t = 0, 1, -1, -2, inf  (five products of about k words)
//   r3 = (r(-2) - r(1)) / 3,  r1 = (r(1) - r(-1)) / 2,  r2 = r(-1) - r(0)
//   r3 = (r2 - r3) / 2 + 2·r(inf),  r2 = r2 + r1 - r(inf),  r1 = r1 - r3
//   a·b = r(0) + r1·X + r2·X² + r3·X³ + r(inf)·X⁴
// Values at -1 and -2 and the interpolation temporaries are signed (sign-magnitude: a span
// plus a bool); the final r1, r2, r3 are never negative. Needs lb > 2k (checked by the caller).
#include <fullint/Karatsuba.h>

#include <cstring>
#include <stdexcept>

namespace fullint {

void Karatsuba::Toom3(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold)
{
    size_t la = a.size(), lb = b.size(), len = la + lb;
    size_t k = (la + 2) / 3;
    CSpan a0 = a.subspan(la - k), a1 = a.subspan(la - 2 * k, k), a2 = a.subspan(0, la - 2 * k);
    CSpan b0 = b.subspan(lb - k), b1 = b.subspan(lb - 2 * k, k), b2 = b.subspan(0, lb - 2 * k);

    size_t e = k + 1;          // words of a value at 1, -1, -2 (|p(-2)| < 5·X, |p(1)| < 3·X)
    size_t l = 2 * e;          // words of their products and of every interpolation value (< 34·X²)
    Span p1 = scratch.subspan(0, e), pm1 = scratch.subspan(e, e), pm2 = scratch.subspan(2 * e, e);
    Span q1 = scratch.subspan(3 * e, e), qm1 = scratch.subspan(4 * e, e), qm2 = scratch.subspan(5 * e, e);
    size_t o = 6 * e;
    Span r1 = scratch.subspan(o, l), rm1 = scratch.subspan(o + l, l), rm2 = scratch.subspan(o + 2 * l, l);
    Span t = scratch.subspan(o + 3 * l, l), u = scratch.subspan(o + 4 * l, l);
    Span rest = scratch.subspan(o + 5 * l);

    bool nPm1, nPm2, nQm1, nQm2;
    Evaluate(a0, a1, a2, p1, pm1, pm2, nPm1, nPm2);
    Evaluate(b0, b1, b2, q1, qm1, qm2, nQm1, nQm2);

    Mul(p1, q1, r1, rest, threshold, toom3Threshold);
    Mul(pm1, qm1, rm1, rest, threshold, toom3Threshold);
    Mul(pm2, qm2, rm2, rest, threshold, toom3Threshold);
    bool nRm1 = nPm1 != nQm1, nRm2 = nPm2 != nQm2;

    // r(0) and r(inf) go straight into their final places in r; the 2k words between them are cleared
    Span r0 = r.subspan(len - 2 * k);
    Span rInf = r.subspan(0, len - 4 * k);
    Mul(a0, b0, r0, rest, threshold, toom3Threshold);
    Mul(a2, b2, rInf, rest, threshold, toom3Threshold);
    std::memset(r.data() + (len - 4 * k), 0, 2 * k * sizeof(uint64_t));

    uint64_t rem;
    // r3 = (r(-2) - r(1)) / 3                      -> u
    bool n3 = SignedAdd(rm2, nRm2, r1, true, t);
    FullIntCore::Div(t, 3, u, &rem);                // exact
    // r1 = (r(1) - r(-1)) / 2                      -> r1
    bool n1 = SignedAdd(r1, false, rm1, !nRm1, r1);
    FullIntCore::ShiftRight(r1, 1, r1);             // exact
    // r2 = r(-1) - r(0)                            -> rm1
    bool n2 = SignedAdd(rm1, nRm1, r0, true, rm1);
    // r3 = (r2 - r3) / 2 + 2·r(inf)                -> t
    n3 = SignedAdd(rm1, n2, u, !n3, t);
    FullIntCore::ShiftRight(t, 1, t);               // exact
    n3 = SignedAdd(t, n3, rInf, false, t);
    n3 = SignedAdd(t, n3, rInf, false, t);
    // r2 = r2 + r1 - r(inf)                        -> rm1
    n2 = SignedAdd(rm1, n2, r1, n1, rm1);
    n2 = SignedAdd(rm1, n2, rInf, true, rm1);
    // r1 = r1 - r3                                 -> r1
    n1 = SignedAdd(r1, n1, t, !n3, r1);

    if (n1 || n2 || n3)
    {
        throw std::logic_error("Toom3: negative interpolation result (internal error)");
    }

    // r += r1·X + r2·X² + r3·X³  (in place, from the low end; everything fits len words)
    Span up1 = r.subspan(0, len - k), up2 = r.subspan(0, len - 2 * k), up3 = r.subspan(0, len - 3 * k);
    FullIntCore::Add(up1, r1, up1);
    FullIntCore::Add(up2, rm1, up2);
    FullIntCore::Add(up3, t, up3);
}

// p(1) = x0 + x1 + x2,  p(-1) = x0 - x1 + x2,  p(-2) = (p(-1) + x2)·2 - x0  (all e = k + 1 words)
void Karatsuba::Evaluate(CSpan x0, CSpan x1, CSpan x2, Span p1, Span pm1, Span pm2, bool& negPm1, bool& negPm2)
{
    FullIntCore::Add(x0, x2, pm2);                       // pm2 = x0 + x2 (temporary)
    FullIntCore::Add(pm2, x1, p1);                       // p(1)
    negPm1 = FullIntCore::Sub(pm2, x1, pm1) < 0;         // p(-1), |x0 + x2 - x1|
    bool n = SignedAdd(pm1, negPm1, x2, false, pm2);     // p(-1) + x2
    FullIntCore::Add(pm2, pm2, pm2);                     // ·2
    negPm2 = SignedAdd(pm2, n, x0, true, pm2);           // - x0
}

// r = (±x) + (±y) in sign-magnitude; returns the sign of r (true = negative).
// r may alias x or y; all values fit r (the add's carry out is always zero here).
bool Karatsuba::SignedAdd(CSpan x, bool xNegative, CSpan y, bool yNegative, Span r)
{
    if (xNegative == yNegative)
    {
        FullIntCore::Add(x, y, r);
        return xNegative;
    }
    int c = FullIntCore::Sub(x, y, r);   // |x - y|, c = sign of (|x| - |y|)
    return c > 0 ? xNegative : c < 0 ? yNegative : false;
}

}  // namespace fullint
