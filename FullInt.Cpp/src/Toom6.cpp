// Port of FullInt.Core/Toom6.cs.
//
// Toom-6 (Toom-Cook 6-way): 11 products of about k words instead of Toom-4's 7 of k·3/2.
//   a = a5·X⁵ + ... + a0,  b likewise,  X = B^k  (a0..a4: k words, a5: the rest)
//   r(t) = a(t)·b(t) = c10·t¹⁰ + ... + c0 at t = 0, inf, ±1, ±2, ±4, ±1/2, 1/4:
//   W(±1), W(±2), W(±4); Wh(±) = 2^10·r(±1/2) = (32·a(±1/2))·(32·b(±1/2)); Wq = 2^20·r(1/4)
// Every c is >= 0 (a product of non-negative polynomials). The pairs split into even and
// odd parts:
//   E1 = (W1 + Wm1)/2 = sum c_2j,         O1 = (W1 - Wm1)/2 = sum c_2j+1
//   E2 = (W2 + Wm2)/2 = sum c_2j·4^j,     O2 = (W2 - Wm2)/4 = sum c_2j+1·4^j
//   E4 = (W4 + Wm4)/2 = sum c_2j·16^j,    O4 = (W4 - Wm4)/8 = sum c_2j+1·16^j
//   Eh = (Wh + Wmh)/2 = sum c_2j·2^(10-2j),  Oh = (Wh - Wmh)/4 = sum c_2j+1·4^(4-j)
// Even: c0, c10 known; with e = (c2, c4, c6, c8) as e0 + e1·s + e2·s² + e3·s³
//   A1 = E1 - c0 - c10,  A4 = (E2 - c0 - 2^10·c10)/4,  A16 = (E4 - c0 - 2^20·c10)/16,
//   Ah = (Eh - 2^10·c0 - c10)/4 = 64e0 + 16e1 + 4e2 + e3  (s = 4 reversed)
//   u = e0 + e3 = (A4 + Ah - 20·A1)/45,  v = e1 + e2 = A1 - u
//   D = A4 - Ah = 63x + 12y  (x = e3 - e0, y = e2 - e1, signed)
//   x = (2·A16 - 4097u - 272v - 20D)/2835,  y = (D - 63x)/12
//   e0 = (u - x)/2, e3 = (u + x)/2, e1 = (v - y)/2, e2 = (v + y)/2
// Odd: d = (c1, c3, c5, c7, c9) as d0 + ... + d4·s⁴ at s = 1, 4, 16 and reversed at 4 and 16:
//   Oq = (Wq - sum c_2j·4^(10-2j))/4 = 65536d0 + 4096d1 + 256d2 + 16d3 + d4
//   P = O2 + Oh, M = O2 - Oh, Q = O4 + Oq, N = O4 - Oq;  u = d0 + d4, v = d1 + d3, w = d2,
//   x = d4 - d0, y = d3 - d1:
//   P - 32·O1 = 225u + 36v,  Q - 512·O1 = 65025u + 3600v
//   u = ((Q - 512·O1) - 100·(P - 32·O1))/42525,  v = (P - 32·O1 - 225u)/36,  w = O1 - u - v
//   x = (N - 68M)/48195,  y = (M - 255x)/60
//   d0 = (u - x)/2, d4 = (u + x)/2, d1 = (v - y)/2, d3 = (v + y)/2
// Every division is exact; values that must be >= 0 are checked. Needs lb > 5k (checked by
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

// r = x·c (x: l words with a zero top word, r: l words, r != x)
void MulSmall(CSpan x, uint64_t c, Span r)
{
    uint64_t one[1] = { c };
    FullIntCore::Mult(x.subspan(1), CSpan(one, 1), r);
}

// q = x / d, the division known to be exact
void DivExact(CSpan x, uint64_t d, Span q)
{
    if (FullIntCore::DivExact(x, d, q) != 0) throw std::logic_error("Toom6: inexact division (internal error)");
}

// plus = even + odd, minus = |even - odd|; returns true when even - odd < 0
bool Pair(CSpan even, CSpan odd, Span plus, Span minus)
{
    FullIntCore::Add(even, odd, plus);
    return FullIntCore::Sub(even, odd, minus) < 0;
}

}  // namespace

void Karatsuba::Toom6(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim)
{
    size_t la = a.size(), lb = b.size(), len = la + lb;
    size_t k = (la + 5) / 6;
    CSpan a0 = a.subspan(la - k), a1 = a.subspan(la - 2 * k, k), a2 = a.subspan(la - 3 * k, k);
    CSpan a3 = a.subspan(la - 4 * k, k), a4 = a.subspan(la - 5 * k, k), a5 = a.subspan(0, la - 5 * k);
    CSpan b0 = b.subspan(lb - k), b1 = b.subspan(lb - 2 * k, k), b2 = b.subspan(lb - 3 * k, k);
    CSpan b3 = b.subspan(lb - 4 * k, k), b4 = b.subspan(lb - 5 * k, k), b5 = b.subspan(0, lb - 5 * k);

    size_t e = k + 1;          // words of a value at a point (each < 1365·X)
    size_t l = 2 * e;          // words of the products and of every interpolation value (< 2^22·X²)
    Span pa = scratch.subspan(0, 9 * e), pb = scratch.subspan(9 * e, 9 * e);
    Span tmp = scratch.subspan(18 * e, 3 * e);
    size_t o = 21 * e;
    Span w1 = scratch.subspan(o, l), wm1 = scratch.subspan(o + l, l), w2 = scratch.subspan(o + 2 * l, l);
    Span wm2 = scratch.subspan(o + 3 * l, l), w4 = scratch.subspan(o + 4 * l, l), wm4 = scratch.subspan(o + 5 * l, l);
    Span wh = scratch.subspan(o + 6 * l, l), wmh = scratch.subspan(o + 7 * l, l), wq = scratch.subspan(o + 8 * l, l);
    Span s1 = scratch.subspan(o + 9 * l, l), s2 = scratch.subspan(o + 10 * l, l), s3 = scratch.subspan(o + 11 * l, l);
    Span s4 = scratch.subspan(o + 12 * l, l), s5 = scratch.subspan(o + 13 * l, l);
    Span rest = scratch.subspan(o + 14 * l);

    // values: [0] 1, [1] -1, [2] 2, [3] -2, [4] 4, [5] -4, [6] 32·(1/2), [7] 32·(-1/2), [8] 1024·(1/4)
    int na = Evaluate6(a0, a1, a2, a3, a4, a5, pa, e, tmp);
    int nb = Evaluate6(b0, b1, b2, b3, b4, b5, pb, e, tmp);
    int ns = na ^ nb;          // bit i: the product at the negative point i is negative

    Span ws[9] = { w1, wm1, w2, wm2, w4, wm4, wh, wmh, wq };
    for (size_t i = 0; i < 9; i++)
        Mul(pa.subspan(i * e, e), pb.subspan(i * e, e), ws[i], rest, lim);
    bool nWm1 = (ns & 1) != 0, nWm2 = (ns & 2) != 0, nWm4 = (ns & 4) != 0, nWmh = (ns & 8) != 0;

    // c0 and c10 go straight into their final places in r; the 8k words between them are cleared
    Span c0 = r.subspan(len - 2 * k);
    Span c10 = r.subspan(0, len - 10 * k);
    Mul(a0, b0, c0, rest, lim);
    Mul(a5, b5, c10, rest, lim);
    std::memset(r.data() + (len - 10 * k), 0, 8 * k * sizeof(uint64_t));

    bool neg = false;

    // the pairs: E1 -> s1, O1 -> w1;  E2 -> wm1, O2 -> w2;  E4 -> wm2, O4 -> w4;  Eh -> wm4, Oh -> wh
    neg |= SignedAdd(w1, false, wm1, nWm1, s1);
    neg |= SignedAdd(w1, false, wm1, !nWm1, w1);
    FullIntCore::ShiftRight(s1, 1, s1);
    FullIntCore::ShiftRight(w1, 1, w1);
    neg |= SignedAdd(w2, false, wm2, nWm2, wm1);
    neg |= SignedAdd(w2, false, wm2, !nWm2, w2);
    FullIntCore::ShiftRight(wm1, 1, wm1);
    FullIntCore::ShiftRight(w2, 2, w2);
    neg |= SignedAdd(w4, false, wm4, nWm4, wm2);
    neg |= SignedAdd(w4, false, wm4, !nWm4, w4);
    FullIntCore::ShiftRight(wm2, 1, wm2);
    FullIntCore::ShiftRight(w4, 3, w4);
    neg |= SignedAdd(wh, false, wmh, nWmh, wm4);
    neg |= SignedAdd(wh, false, wmh, !nWmh, wh);
    FullIntCore::ShiftRight(wm4, 1, wm4);
    FullIntCore::ShiftRight(wh, 2, wh);

    // even: A1 -> s1, A4 -> wm1, A16 -> wm2, Ah -> wm4
    neg |= FullIntCore::Sub(s1, c0, s1) < 0;
    neg |= FullIntCore::Sub(s1, c10, s1) < 0;
    neg |= FullIntCore::Sub(wm1, c0, wm1) < 0;
    FullIntCore::ShiftLeft(c10, 10, s2);
    neg |= FullIntCore::Sub(wm1, s2, wm1) < 0;
    FullIntCore::ShiftRight(wm1, 2, wm1);
    neg |= FullIntCore::Sub(wm2, c0, wm2) < 0;
    FullIntCore::ShiftLeft(c10, 20, s2);
    neg |= FullIntCore::Sub(wm2, s2, wm2) < 0;
    FullIntCore::ShiftRight(wm2, 4, wm2);
    FullIntCore::ShiftLeft(c0, 10, s2);
    neg |= FullIntCore::Sub(wm4, s2, wm4) < 0;
    neg |= FullIntCore::Sub(wm4, c10, wm4) < 0;
    FullIntCore::ShiftRight(wm4, 2, wm4);
    // u = (A4 + Ah - 20·A1)/45 -> s3;  v = A1 - u -> s1
    FullIntCore::Add(wm1, wm4, s2);
    MulSmall(s1, 20, s3);
    neg |= FullIntCore::Sub(s2, s3, s2) < 0;
    DivExact(s2, 45, s3);
    neg |= FullIntCore::Sub(s1, s3, s1) < 0;
    // D = A4 - Ah -> wm4 (signed)
    bool nD = SignedAdd(wm1, false, wm4, true, wm4);
    // G = 2·A16 - 4097u - 272v -> wm2 (signed)
    FullIntCore::Add(wm2, wm2, wm2);
    MulSmall(s3, 4097, s2);
    bool nG = SignedAdd(wm2, false, s2, true, wm2);
    MulSmall(s1, 272, s2);
    nG = SignedAdd(wm2, nG, s2, true, wm2);
    // x = (G - 20D)/2835 -> wm1;  y = (D - 63x)/12 -> wm2
    MulSmall(wm4, 20, s2);
    bool nX = SignedAdd(wm2, nG, s2, !nD, wm2);
    DivExact(wm2, 2835, wm1);
    MulSmall(wm1, 63, s2);
    bool nY = SignedAdd(wm4, nD, s2, !nX, wm4);
    DivExact(wm4, 12, wm2);
    // c2 = (u - x)/2 -> s2, c8 = (u + x)/2 -> s3, c4 = (v - y)/2 -> wm1, c6 = (v + y)/2 -> s1
    neg |= SignedAdd(s3, false, wm1, !nX, s2);
    neg |= SignedAdd(s3, false, wm1, nX, s3);
    neg |= SignedAdd(s1, false, wm2, !nY, wm1);
    neg |= SignedAdd(s1, false, wm2, nY, s1);
    FullIntCore::ShiftRight(s2, 1, s2);
    FullIntCore::ShiftRight(s3, 1, s3);
    FullIntCore::ShiftRight(wm1, 1, wm1);
    FullIntCore::ShiftRight(s1, 1, s1);
    Span c2 = s2, c4 = wm1, c6 = s1, c8 = s3;

    // Oq = (Wq - 2^20·c0 - 2^16·c2 - 2^12·c4 - 2^8·c6 - 2^4·c8 - c10)/4 -> wq
    FullIntCore::ShiftLeft(c0, 20, s4);
    neg |= FullIntCore::Sub(wq, s4, wq) < 0;
    FullIntCore::ShiftLeft(CSpan(c2).subspan(1), 16, s4);
    neg |= FullIntCore::Sub(wq, s4, wq) < 0;
    FullIntCore::ShiftLeft(CSpan(c4).subspan(1), 12, s4);
    neg |= FullIntCore::Sub(wq, s4, wq) < 0;
    FullIntCore::ShiftLeft(CSpan(c6).subspan(1), 8, s4);
    neg |= FullIntCore::Sub(wq, s4, wq) < 0;
    FullIntCore::ShiftLeft(CSpan(c8).subspan(1), 4, s4);
    neg |= FullIntCore::Sub(wq, s4, wq) < 0;
    neg |= FullIntCore::Sub(wq, c10, wq) < 0;
    FullIntCore::ShiftRight(wq, 2, wq);

    // odd: P -> wm2, M -> w2 (signed), Q -> wm4, N -> w4 (signed)
    FullIntCore::Add(w2, wh, wm2);
    bool nM = SignedAdd(w2, false, wh, true, w2);
    FullIntCore::Add(w4, wq, wm4);
    bool nN = SignedAdd(w4, false, wq, true, w4);
    // P - 32·O1 -> wm2,  Q - 512·O1 -> wm4
    FullIntCore::ShiftLeft(CSpan(w1).subspan(1), 5, s4);
    neg |= FullIntCore::Sub(wm2, s4, wm2) < 0;
    FullIntCore::ShiftLeft(CSpan(w1).subspan(1), 9, s4);
    neg |= FullIntCore::Sub(wm4, s4, wm4) < 0;
    // u = (wm4 - 100·wm2)/42525 -> wh;  v = (wm2 - 225u)/36 -> wq;  c5 = O1 - u - v -> w1
    MulSmall(wm2, 100, s4);
    neg |= FullIntCore::Sub(wm4, s4, wm4) < 0;
    DivExact(wm4, 42525, wh);
    MulSmall(wh, 225, s4);
    neg |= FullIntCore::Sub(wm2, s4, wm2) < 0;
    DivExact(wm2, 36, wq);
    neg |= FullIntCore::Sub(w1, wh, w1) < 0;
    neg |= FullIntCore::Sub(w1, wq, w1) < 0;
    // x = (N - 68M)/48195 -> wm2;  y = (M - 255x)/60 -> wm4
    MulSmall(w2, 68, s4);
    nX = SignedAdd(w4, nN, s4, !nM, w4);
    DivExact(w4, 48195, wm2);
    MulSmall(wm2, 255, s4);
    nY = SignedAdd(w2, nM, s4, !nX, w2);
    DivExact(w2, 60, wm4);
    // c1 = (u - x)/2 -> s4, c9 = (u + x)/2 -> wh, c3 = (v - y)/2 -> s5, c7 = (v + y)/2 -> wq
    neg |= SignedAdd(wh, false, wm2, !nX, s4);
    neg |= SignedAdd(wh, false, wm2, nX, wh);
    neg |= SignedAdd(wq, false, wm4, !nY, s5);
    neg |= SignedAdd(wq, false, wm4, nY, wq);
    FullIntCore::ShiftRight(s4, 1, s4);
    FullIntCore::ShiftRight(wh, 1, wh);
    FullIntCore::ShiftRight(s5, 1, s5);
    FullIntCore::ShiftRight(wq, 1, wq);

    if (neg) throw std::logic_error("Toom6: negative interpolation value (internal error)");

    // r += c1·X + ... + c9·X⁹  (in place; everything fits len words)
    AddAt(r, s4, len - k);
    AddAt(r, c2, len - 2 * k);
    AddAt(r, s5, len - 3 * k);
    AddAt(r, c4, len - 4 * k);
    AddAt(r, w1, len - 5 * k);
    AddAt(r, c6, len - 6 * k);
    AddAt(r, wq, len - 7 * k);
    AddAt(r, c8, len - 8 * k);
    AddAt(r, wh, len - 9 * k);
}

// the nine values of x5·X⁵ + ... + x0 (e words each, into v at i·e): p(1), p(-1), p(2), p(-2),
// p(4), p(-4), 32·p(1/2), 32·p(-1/2), 1024·p(1/4). Returns the signs of the four negative
// points as bits 0..3. tmp: 3e words.
int Karatsuba::Evaluate6(CSpan x0, CSpan x1, CSpan x2, CSpan x3, CSpan x4, CSpan x5, Span v, size_t e, Span tmp)
{
    Span t = tmp.subspan(0, e), u = tmp.subspan(e, e), s = tmp.subspan(2 * e, e);
    int signs = 0;

    // 1: even x0 + x2 + x4, odd x1 + x3 + x5
    FullIntCore::Add(x0, x2, t);
    FullIntCore::Add(t, x4, t);
    FullIntCore::Add(x1, x3, u);
    FullIntCore::Add(u, x5, u);
    signs |= Pair(t, u, v.subspan(0, e), v.subspan(e, e)) ? 1 : 0;

    // 2: even x0 + 4·x2 + 16·x4, odd 2·x1 + 8·x3 + 32·x5
    FullIntCore::ShiftLeft(x2, 2, t);
    FullIntCore::Add(t, x0, t);
    FullIntCore::ShiftLeft(x4, 4, s);
    FullIntCore::Add(t, s, t);
    FullIntCore::ShiftLeft(x1, 1, u);
    FullIntCore::ShiftLeft(x3, 3, s);
    FullIntCore::Add(u, s, u);
    FullIntCore::ShiftLeft(x5, 5, s);
    FullIntCore::Add(u, s, u);
    signs |= Pair(t, u, v.subspan(2 * e, e), v.subspan(3 * e, e)) ? 2 : 0;

    // 4: even x0 + 16·x2 + 256·x4, odd 4·x1 + 64·x3 + 1024·x5
    FullIntCore::ShiftLeft(x2, 4, t);
    FullIntCore::Add(t, x0, t);
    FullIntCore::ShiftLeft(x4, 8, s);
    FullIntCore::Add(t, s, t);
    FullIntCore::ShiftLeft(x1, 2, u);
    FullIntCore::ShiftLeft(x3, 6, s);
    FullIntCore::Add(u, s, u);
    FullIntCore::ShiftLeft(x5, 10, s);
    FullIntCore::Add(u, s, u);
    signs |= Pair(t, u, v.subspan(4 * e, e), v.subspan(5 * e, e)) ? 4 : 0;

    // 1/2 (times 32): even 32·x0 + 8·x2 + 2·x4, odd 16·x1 + 4·x3 + x5
    FullIntCore::ShiftLeft(x0, 5, t);
    FullIntCore::ShiftLeft(x2, 3, s);
    FullIntCore::Add(t, s, t);
    FullIntCore::ShiftLeft(x4, 1, s);
    FullIntCore::Add(t, s, t);
    FullIntCore::ShiftLeft(x1, 4, u);
    FullIntCore::ShiftLeft(x3, 2, s);
    FullIntCore::Add(u, s, u);
    FullIntCore::Add(u, x5, u);
    signs |= Pair(t, u, v.subspan(6 * e, e), v.subspan(7 * e, e)) ? 8 : 0;

    // 1/4 (times 1024): 1024·x0 + 256·x1 + 64·x2 + 16·x3 + 4·x4 + x5
    Span q = v.subspan(8 * e, e);
    FullIntCore::ShiftLeft(x0, 10, q);
    FullIntCore::ShiftLeft(x1, 8, s);
    FullIntCore::Add(q, s, q);
    FullIntCore::ShiftLeft(x2, 6, s);
    FullIntCore::Add(q, s, q);
    FullIntCore::ShiftLeft(x3, 4, s);
    FullIntCore::Add(q, s, q);
    FullIntCore::ShiftLeft(x4, 2, s);
    FullIntCore::Add(q, s, q);
    FullIntCore::Add(q, x5, q);
    return signs;
}

}  // namespace fullint
