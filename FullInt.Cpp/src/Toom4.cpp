// Port of FullInt.Core/Toom4.cs.
//
// Toom-4 (Toom-Cook 4-way): 7 products of about k words instead of Toom-3's 5 of k·4/3.
//   a = a3·X³ + a2·X² + a1·X + a0,  b likewise,  X = B^k  (a0..a2: k words, a3: the rest)
//   r(t) = a(t)·b(t) at t = 0, 1, -1, 2, -2, 1/2, inf;  r = c6·t⁶ + ... + c0
//   W1 = r(1), Wm1 = r(-1), W2 = r(2), Wm2 = r(-2), Wh = 64·r(1/2) = (8·a(1/2))·(8·b(1/2))
// Interpolation by the even and odd coefficients (every c is >= 0: a product of
// non-negative polynomials, so only W(-1) and W(-2) carry a sign):
//   O1 = (W1 - Wm1)/2 = c1 + c3 + c5          E1 = (W1 + Wm1)/2 = c0 + c2 + c4 + c6
//   O2 = (W2 - Wm2)/4 = c1 + 4c3 + 16c5       E2 = (W2 + Wm2)/2 = c0 + 4c2 + 16c4 + 64c6
//   S = E1 - c0 - c6 = c2 + c4,  T = E2 - c0 - 64c6 = 4c2 + 16c4
//   c4 = (T - 4S)/12,  c2 = S - c4
//   Ho = (Wh - 64c0 - 16c2 - 4c4 - c6)/2 = 16c1 + 4c3 + c5
//   U = O2 - O1 = 3c3 + 15c5,  V = Ho - O1 = 15c1 + 3c3
//   c3 = (15·O1 - V - U)/9,  c5 = (U - 3c3)/15,  c1 = O1 - c3 - c5
// Every division is exact; every difference is >= 0 (checked). Needs lb > 3k (checked by
// the caller).
#include <fullint/Karatsuba.h>

#include <cstring>
#include <stdexcept>

namespace fullint {

namespace {

// r[0 .. upTo) += c  (c without its leading zero words: the value fits there)
void AddAt(Span r, CSpan c, size_t upTo)
{
    size_t z = 0;
    while (z + 1 < c.size() && c[z] == 0) z++;
    Span up = r.subspan(0, upTo);
    FullIntCore::Add(up, c.subspan(z), up);
}

}  // namespace

void Karatsuba::Toom4(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim)
{
    size_t la = a.size(), lb = b.size(), len = la + lb;
    size_t k = (la + 3) / 4;
    CSpan a0 = a.subspan(la - k), a1 = a.subspan(la - 2 * k, k), a2 = a.subspan(la - 3 * k, k), a3 = a.subspan(0, la - 3 * k);
    CSpan b0 = b.subspan(lb - k), b1 = b.subspan(lb - 2 * k, k), b2 = b.subspan(lb - 3 * k, k), b3 = b.subspan(0, lb - 3 * k);

    size_t e = k + 1;          // words of a value at 1, -1, 2, -2, 1/2 (each < 15·X)
    size_t l = 2 * e;          // words of their products and of every interpolation value (< 225·X²)
    Span p1 = scratch.subspan(0, e), pm1 = scratch.subspan(e, e), p2 = scratch.subspan(2 * e, e);
    Span pm2 = scratch.subspan(3 * e, e), ph = scratch.subspan(4 * e, e);
    Span q1 = scratch.subspan(5 * e, e), qm1 = scratch.subspan(6 * e, e), q2 = scratch.subspan(7 * e, e);
    Span qm2 = scratch.subspan(8 * e, e), qh = scratch.subspan(9 * e, e);
    Span e1 = scratch.subspan(10 * e, e), e2 = scratch.subspan(11 * e, e);
    size_t o = 12 * e;
    Span w1 = scratch.subspan(o, l), wm1 = scratch.subspan(o + l, l), w2 = scratch.subspan(o + 2 * l, l);
    Span wm2 = scratch.subspan(o + 3 * l, l), wh = scratch.subspan(o + 4 * l, l);
    Span t1 = scratch.subspan(o + 5 * l, l), t2 = scratch.subspan(o + 6 * l, l), t3 = scratch.subspan(o + 7 * l, l);
    Span rest = scratch.subspan(o + 8 * l);

    bool nPm1, nPm2, nQm1, nQm2;
    Evaluate4(a0, a1, a2, a3, p1, pm1, p2, pm2, ph, e1, e2, nPm1, nPm2);
    Evaluate4(b0, b1, b2, b3, q1, qm1, q2, qm2, qh, e1, e2, nQm1, nQm2);

    Mul(p1, q1, w1, rest, lim);
    Mul(pm1, qm1, wm1, rest, lim);
    Mul(p2, q2, w2, rest, lim);
    Mul(pm2, qm2, wm2, rest, lim);
    Mul(ph, qh, wh, rest, lim);
    bool nWm1 = nPm1 != nQm1, nWm2 = nPm2 != nQm2;

    // c0 and c6 go straight into their final places in r; the 4k words between them are cleared
    Span c0 = r.subspan(len - 2 * k);
    Span c6 = r.subspan(0, len - 6 * k);
    Mul(a0, b0, c0, rest, lim);
    Mul(a3, b3, c6, rest, lim);
    std::memset(r.data() + (len - 6 * k), 0, 4 * k * sizeof(uint64_t));

    bool neg = false;
    // O1 -> t1, E1 -> wm1
    neg |= SignedAdd(w1, false, wm1, !nWm1, t1);
    neg |= SignedAdd(w1, false, wm1, nWm1, wm1);
    FullIntCore::ShiftRight(t1, 1, t1);
    FullIntCore::ShiftRight(wm1, 1, wm1);
    // O2 -> w1, E2 -> wm2
    neg |= SignedAdd(w2, false, wm2, !nWm2, w1);
    neg |= SignedAdd(w2, false, wm2, nWm2, wm2);
    FullIntCore::ShiftRight(w1, 2, w1);
    FullIntCore::ShiftRight(wm2, 1, wm2);
    // S = E1 - c0 - c6 -> wm1;  T = E2 - c0 - 64·c6 -> wm2
    neg |= FullIntCore::Sub(wm1, c0, wm1) < 0;
    neg |= FullIntCore::Sub(wm1, c6, wm1) < 0;
    neg |= FullIntCore::Sub(wm2, c0, wm2) < 0;
    FullIntCore::ShiftLeft(c6, 6, w2);
    neg |= FullIntCore::Sub(wm2, w2, wm2) < 0;
    // c4 = (T - 4S) / 12 -> t2;  c2 = S - c4 -> wm1
    FullIntCore::ShiftLeft(CSpan(wm1).subspan(1), 2, w2);
    neg |= FullIntCore::Sub(wm2, w2, wm2) < 0;
    FullIntCore::DivExact(wm2, 12, t2);                   // exact (the shift by 2 inside)
    neg |= FullIntCore::Sub(wm1, t2, wm1) < 0;
    // Ho = (Wh - 64·c0 - 16·c2 - 4·c4 - c6) / 2 -> wh
    FullIntCore::ShiftLeft(c0, 6, w2);
    neg |= FullIntCore::Sub(wh, w2, wh) < 0;
    FullIntCore::ShiftLeft(CSpan(wm1).subspan(1), 4, w2);
    neg |= FullIntCore::Sub(wh, w2, wh) < 0;
    FullIntCore::ShiftLeft(CSpan(t2).subspan(1), 2, w2);
    neg |= FullIntCore::Sub(wh, w2, wh) < 0;
    neg |= FullIntCore::Sub(wh, c6, wh) < 0;
    FullIntCore::ShiftRight(wh, 1, wh);
    // U = O2 - O1 -> w1;  V = Ho - O1 -> wh
    neg |= FullIntCore::Sub(w1, t1, w1) < 0;
    neg |= FullIntCore::Sub(wh, t1, wh) < 0;
    // c3 = (15·O1 - V - U) / 9 -> t3
    FullIntCore::ShiftLeft(CSpan(t1).subspan(1), 4, w2);
    neg |= FullIntCore::Sub(w2, t1, w2) < 0;
    neg |= FullIntCore::Sub(w2, wh, w2) < 0;
    neg |= FullIntCore::Sub(w2, w1, w2) < 0;
    FullIntCore::DivExact(w2, 9, t3);                     // exact
    // c5 = (U - 3·c3) / 15 -> wm2
    FullIntCore::Add(t3, t3, w2);
    FullIntCore::Add(w2, t3, w2);
    neg |= FullIntCore::Sub(w1, w2, w1) < 0;
    FullIntCore::DivExact(w1, 15, wm2);                   // exact
    // c1 = O1 - c3 - c5 -> t1
    neg |= FullIntCore::Sub(t1, t3, t1) < 0;
    neg |= FullIntCore::Sub(t1, wm2, t1) < 0;

    if (neg) throw std::logic_error("Toom4: negative interpolation value (internal error)");

    // r += c1·X + c2·X² + c3·X³ + c4·X⁴ + c5·X⁵  (in place; everything fits len words)
    AddAt(r, t1, len - k);
    AddAt(r, wm1, len - 2 * k);
    AddAt(r, t3, len - 3 * k);
    AddAt(r, t2, len - 4 * k);
    AddAt(r, wm2, len - 5 * k);
}

// p(1), p(-1), p(2), p(-2) and 8·p(1/2) of x3·X³ + x2·X² + x1·X + x0 (all e = k + 1 words;
// t, u: e words of scratch)
void Karatsuba::Evaluate4(CSpan x0, CSpan x1, CSpan x2, CSpan x3, Span p1, Span pm1, Span p2, Span pm2, Span ph,
                          Span t, Span u, bool& negPm1, bool& negPm2)
{
    FullIntCore::Add(x0, x2, t);                         // x0 + x2
    FullIntCore::Add(x1, x3, u);                         // x1 + x3
    FullIntCore::Add(t, u, p1);                          // p(1)
    negPm1 = FullIntCore::Sub(t, u, pm1) < 0;            // p(-1)
    FullIntCore::ShiftLeft(x2, 2, t);                    // 4·x2
    FullIntCore::Add(t, x0, t);                          // x0 + 4·x2
    FullIntCore::ShiftLeft(x3, 2, u);                    // 4·x3
    FullIntCore::Add(u, x1, u);                          // x1 + 4·x3
    FullIntCore::Add(u, u, u);                           // 2·x1 + 8·x3
    FullIntCore::Add(t, u, p2);                          // p(2)
    negPm2 = FullIntCore::Sub(t, u, pm2) < 0;            // p(-2)
    FullIntCore::Add(x0, x0, ph);                        // 8·p(1/2) = ((2·x0 + x1)·2 + x2)·2 + x3
    FullIntCore::Add(ph, x1, ph);
    FullIntCore::Add(ph, ph, ph);
    FullIntCore::Add(ph, x2, ph);
    FullIntCore::Add(ph, ph, ph);
    FullIntCore::Add(ph, x3, ph);
}

}  // namespace fullint
