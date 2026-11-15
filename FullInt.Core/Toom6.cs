using System;

namespace FullInt.Core
{
    public static partial class Karatsuba
    {
        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced), a level splits in six (Toom-6) instead of four. Tuned with
        /// `FullInt.Benchmarks kara toom6` (Zen 3), with Toom-6 / the levels up to Toom-4:
        /// x64 no gain below 1024 words, 0.90-0.94 at 1024-2048, 0.83-0.86 at 6144-8192;
        /// x86 (a 32-bit process) 0.97 at 512, 0.88-0.93 at 1024-2048, 0.81-0.86 at 3072-8192.
        /// </summary>
        public static int Toom6Threshold { get; set; } = Environment.Is64BitProcess ? 1024 : 512;

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
        // Every division is exact (verified on random and all-ones coefficients). Values that
        // must be >= 0 are checked. Needs lb > 5k so that b5 is not empty (checked by the caller).
        private static void Toom6(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, Limits lim)
        {
            int la = a.Length, lb = b.Length, len = la + lb;
            int k = (la + 5) / 6;
            Span<ulong> a0 = a.Slice(la - k), a1 = a.Slice(la - 2 * k, k), a2 = a.Slice(la - 3 * k, k);
            Span<ulong> a3 = a.Slice(la - 4 * k, k), a4 = a.Slice(la - 5 * k, k), a5 = a.Slice(0, la - 5 * k);
            Span<ulong> b0 = b.Slice(lb - k), b1 = b.Slice(lb - 2 * k, k), b2 = b.Slice(lb - 3 * k, k);
            Span<ulong> b3 = b.Slice(lb - 4 * k, k), b4 = b.Slice(lb - 5 * k, k), b5 = b.Slice(0, lb - 5 * k);

            int e = k + 1;          // words of a value at a point (each < 1365·X)
            int l = 2 * e;          // words of the products and of every interpolation value (< 2^22·X²)
            Span<ulong> pa = scratch.Slice(0, 9 * e), pb = scratch.Slice(9 * e, 9 * e);
            Span<ulong> tmp = scratch.Slice(18 * e, 3 * e);
            int o = 21 * e;
            Span<ulong> w1 = scratch.Slice(o, l), wm1 = scratch.Slice(o + l, l), w2 = scratch.Slice(o + 2 * l, l);
            Span<ulong> wm2 = scratch.Slice(o + 3 * l, l), w4 = scratch.Slice(o + 4 * l, l), wm4 = scratch.Slice(o + 5 * l, l);
            Span<ulong> wh = scratch.Slice(o + 6 * l, l), wmh = scratch.Slice(o + 7 * l, l), wq = scratch.Slice(o + 8 * l, l);
            Span<ulong> s1 = scratch.Slice(o + 9 * l, l), s2 = scratch.Slice(o + 10 * l, l), s3 = scratch.Slice(o + 11 * l, l);
            Span<ulong> s4 = scratch.Slice(o + 12 * l, l), s5 = scratch.Slice(o + 13 * l, l);
            Span<ulong> rest = scratch.Slice(o + 14 * l);

            // values: [0] 1, [1] -1, [2] 2, [3] -2, [4] 4, [5] -4, [6] 32·(1/2), [7] 32·(-1/2), [8] 1024·(1/4)
            int na = Evaluate6(core, a0, a1, a2, a3, a4, a5, pa, e, tmp);
            int nb = Evaluate6(core, b0, b1, b2, b3, b4, b5, pb, e, tmp);
            int ns = na ^ nb;       // bit i: the product at the negative point i is negative

            Mul(core, pa.Slice(0, e), pb.Slice(0, e), w1, rest, lim);
            Mul(core, pa.Slice(e, e), pb.Slice(e, e), wm1, rest, lim);
            Mul(core, pa.Slice(2 * e, e), pb.Slice(2 * e, e), w2, rest, lim);
            Mul(core, pa.Slice(3 * e, e), pb.Slice(3 * e, e), wm2, rest, lim);
            Mul(core, pa.Slice(4 * e, e), pb.Slice(4 * e, e), w4, rest, lim);
            Mul(core, pa.Slice(5 * e, e), pb.Slice(5 * e, e), wm4, rest, lim);
            Mul(core, pa.Slice(6 * e, e), pb.Slice(6 * e, e), wh, rest, lim);
            Mul(core, pa.Slice(7 * e, e), pb.Slice(7 * e, e), wmh, rest, lim);
            Mul(core, pa.Slice(8 * e, e), pb.Slice(8 * e, e), wq, rest, lim);
            bool nWm1 = (ns & 1) != 0, nWm2 = (ns & 2) != 0, nWm4 = (ns & 4) != 0, nWmh = (ns & 8) != 0;

            // c0 and c10 go straight into their final places in r; the 8k words between them are cleared
            Span<ulong> c0 = r.Slice(len - 2 * k);
            Span<ulong> c10 = r.Slice(0, len - 10 * k);
            Mul(core, a0, b0, c0, rest, lim);
            Mul(core, a5, b5, c10, rest, lim);
            r.Slice(len - 10 * k, 8 * k).Clear();

            Span<ulong> one = stackalloc ulong[1];
            bool neg = false;

            // the pairs: E1 -> s1, O1 -> w1;  E2 -> wm1, O2 -> w2;  E4 -> wm2, O4 -> w4;  Eh -> wm4, Oh -> wh
            neg |= SignedAdd(core, w1, false, wm1, nWm1, s1);
            neg |= SignedAdd(core, w1, false, wm1, !nWm1, w1);
            core.ShiftRight(s1, 1, s1);
            core.ShiftRight(w1, 1, w1);
            neg |= SignedAdd(core, w2, false, wm2, nWm2, wm1);
            neg |= SignedAdd(core, w2, false, wm2, !nWm2, w2);
            core.ShiftRight(wm1, 1, wm1);
            core.ShiftRight(w2, 2, w2);
            neg |= SignedAdd(core, w4, false, wm4, nWm4, wm2);
            neg |= SignedAdd(core, w4, false, wm4, !nWm4, w4);
            core.ShiftRight(wm2, 1, wm2);
            core.ShiftRight(w4, 3, w4);
            neg |= SignedAdd(core, wh, false, wmh, nWmh, wm4);
            neg |= SignedAdd(core, wh, false, wmh, !nWmh, wh);
            core.ShiftRight(wm4, 1, wm4);
            core.ShiftRight(wh, 2, wh);

            // even: A1 -> s1, A4 -> wm1, A16 -> wm2, Ah -> wm4
            neg |= core.Sub(s1, c0, s1) < 0;
            neg |= core.Sub(s1, c10, s1) < 0;
            neg |= core.Sub(wm1, c0, wm1) < 0;
            core.ShiftLeft(c10, 10, s2);
            neg |= core.Sub(wm1, s2, wm1) < 0;
            core.ShiftRight(wm1, 2, wm1);
            neg |= core.Sub(wm2, c0, wm2) < 0;
            core.ShiftLeft(c10, 20, s2);
            neg |= core.Sub(wm2, s2, wm2) < 0;
            core.ShiftRight(wm2, 4, wm2);
            core.ShiftLeft(c0, 10, s2);
            neg |= core.Sub(wm4, s2, wm4) < 0;
            neg |= core.Sub(wm4, c10, wm4) < 0;
            core.ShiftRight(wm4, 2, wm4);
            // u = (A4 + Ah - 20·A1)/45 -> s3;  v = A1 - u -> s1
            core.Add(wm1, wm4, s2);
            MulSmall(core, s1, 20, s3, one);
            neg |= core.Sub(s2, s3, s2) < 0;
            DivExact(core, s2, 45, s3);
            neg |= core.Sub(s1, s3, s1) < 0;
            // D = A4 - Ah -> wm4 (signed)
            bool nD = SignedAdd(core, wm1, false, wm4, true, wm4);
            // G = 2·A16 - 4097u - 272v -> wm2 (signed)
            core.Add(wm2, wm2, wm2);
            MulSmall(core, s3, 4097, s2, one);
            bool nG = SignedAdd(core, wm2, false, s2, true, wm2);
            MulSmall(core, s1, 272, s2, one);
            nG = SignedAdd(core, wm2, nG, s2, true, wm2);
            // x = (G - 20D)/2835 -> wm1;  y = (D - 63x)/12 -> wm2
            MulSmall(core, wm4, 20, s2, one);
            bool nX = SignedAdd(core, wm2, nG, s2, !nD, wm2);
            DivExact(core, wm2, 2835, wm1);
            MulSmall(core, wm1, 63, s2, one);
            bool nY = SignedAdd(core, wm4, nD, s2, !nX, wm4);
            DivExact(core, wm4, 12, wm2);
            // c2 = (u - x)/2 -> s2, c8 = (u + x)/2 -> s3, c4 = (v - y)/2 -> wm1, c6 = (v + y)/2 -> s1
            neg |= SignedAdd(core, s3, false, wm1, !nX, s2);
            neg |= SignedAdd(core, s3, false, wm1, nX, s3);
            neg |= SignedAdd(core, s1, false, wm2, !nY, wm1);
            neg |= SignedAdd(core, s1, false, wm2, nY, s1);
            core.ShiftRight(s2, 1, s2);
            core.ShiftRight(s3, 1, s3);
            core.ShiftRight(wm1, 1, wm1);
            core.ShiftRight(s1, 1, s1);
            Span<ulong> c2 = s2, c4 = wm1, c6 = s1, c8 = s3;

            // Oq = (Wq - 2^20·c0 - 2^16·c2 - 2^12·c4 - 2^8·c6 - 2^4·c8 - c10)/4 -> wq
            core.ShiftLeft(c0, 20, s4);
            neg |= core.Sub(wq, s4, wq) < 0;
            core.ShiftLeft(c2.Slice(1), 16, s4);
            neg |= core.Sub(wq, s4, wq) < 0;
            core.ShiftLeft(c4.Slice(1), 12, s4);
            neg |= core.Sub(wq, s4, wq) < 0;
            core.ShiftLeft(c6.Slice(1), 8, s4);
            neg |= core.Sub(wq, s4, wq) < 0;
            core.ShiftLeft(c8.Slice(1), 4, s4);
            neg |= core.Sub(wq, s4, wq) < 0;
            neg |= core.Sub(wq, c10, wq) < 0;
            core.ShiftRight(wq, 2, wq);

            // odd: P -> wm2, M -> w2 (signed), Q -> wm4, N -> w4 (signed)
            core.Add(w2, wh, wm2);
            bool nM = SignedAdd(core, w2, false, wh, true, w2);
            core.Add(w4, wq, wm4);
            bool nN = SignedAdd(core, w4, false, wq, true, w4);
            // P - 32·O1 -> wm2,  Q - 512·O1 -> wm4
            core.ShiftLeft(w1.Slice(1), 5, s4);
            neg |= core.Sub(wm2, s4, wm2) < 0;
            core.ShiftLeft(w1.Slice(1), 9, s4);
            neg |= core.Sub(wm4, s4, wm4) < 0;
            // u = (wm4 - 100·wm2)/42525 -> wh;  v = (wm2 - 225u)/36 -> wq;  c5 = O1 - u - v -> w1
            MulSmall(core, wm2, 100, s4, one);
            neg |= core.Sub(wm4, s4, wm4) < 0;
            DivExact(core, wm4, 42525, wh);
            MulSmall(core, wh, 225, s4, one);
            neg |= core.Sub(wm2, s4, wm2) < 0;
            DivExact(core, wm2, 36, wq);
            neg |= core.Sub(w1, wh, w1) < 0;
            neg |= core.Sub(w1, wq, w1) < 0;
            // x = (N - 68M)/48195 -> wm2;  y = (M - 255x)/60 -> wm4
            MulSmall(core, w2, 68, s4, one);
            nX = SignedAdd(core, w4, nN, s4, !nM, w4);
            DivExact(core, w4, 48195, wm2);
            MulSmall(core, wm2, 255, s4, one);
            nY = SignedAdd(core, w2, nM, s4, !nX, w2);
            DivExact(core, w2, 60, wm4);
            // c1 = (u - x)/2 -> s4, c9 = (u + x)/2 -> wh, c3 = (v - y)/2 -> s5, c7 = (v + y)/2 -> wq
            neg |= SignedAdd(core, wh, false, wm2, !nX, s4);
            neg |= SignedAdd(core, wh, false, wm2, nX, wh);
            neg |= SignedAdd(core, wq, false, wm4, !nY, s5);
            neg |= SignedAdd(core, wq, false, wm4, nY, wq);
            core.ShiftRight(s4, 1, s4);
            core.ShiftRight(wh, 1, wh);
            core.ShiftRight(s5, 1, s5);
            core.ShiftRight(wq, 1, wq);

            if (neg)
            {
                throw new InvalidOperationException("Toom6: negative interpolation value (internal error)");
            }

            // r += c1·X + ... + c9·X⁹  (in place; everything fits len words)
            AddAt(core, r, s4, len - k);
            AddAt(core, r, c2, len - 2 * k);
            AddAt(core, r, s5, len - 3 * k);
            AddAt(core, r, c4, len - 4 * k);
            AddAt(core, r, w1, len - 5 * k);
            AddAt(core, r, c6, len - 6 * k);
            AddAt(core, r, wq, len - 7 * k);
            AddAt(core, r, c8, len - 8 * k);
            AddAt(core, r, wh, len - 9 * k);
        }

        // r = x·c (x: l words with a zero top word, r: l words, r != x)
        private static void MulSmall(IFullIntCore core, Span<ulong> x, ulong c, Span<ulong> r, Span<ulong> one)
        {
            one[0] = c;
            core.Mult(x.Slice(1), one, r);
        }

        // q = x / d, the division known to be exact
        private static void DivExact(IFullIntCore core, Span<ulong> x, ulong d, Span<ulong> q)
        {
            core.Div(x, d, q, out ulong rem);
            if (rem != 0) throw new InvalidOperationException("Toom6: inexact division (internal error)");
        }

        // the nine values of x5·X⁵ + ... + x0 (e words each, into v at i·e): p(1), p(-1), p(2), p(-2),
        // p(4), p(-4), 32·p(1/2), 32·p(-1/2), 1024·p(1/4). Returns the signs of the four negative
        // points as bits 0..3. tmp: 3e words.
        private static int Evaluate6(IFullIntCore core, Span<ulong> x0, Span<ulong> x1, Span<ulong> x2, Span<ulong> x3,
            Span<ulong> x4, Span<ulong> x5, Span<ulong> v, int e, Span<ulong> tmp)
        {
            Span<ulong> t = tmp.Slice(0, e), u = tmp.Slice(e, e), s = tmp.Slice(2 * e, e);
            int signs = 0;

            // 1: even x0 + x2 + x4, odd x1 + x3 + x5
            core.Add(x0, x2, t);
            core.Add(t, x4, t);
            core.Add(x1, x3, u);
            core.Add(u, x5, u);
            signs |= Pair(core, t, u, v.Slice(0, e), v.Slice(e, e)) ? 1 : 0;

            // 2: even x0 + 4·x2 + 16·x4, odd 2·x1 + 8·x3 + 32·x5
            core.ShiftLeft(x2, 2, t);
            core.Add(t, x0, t);
            core.ShiftLeft(x4, 4, s);
            core.Add(t, s, t);
            core.ShiftLeft(x1, 1, u);
            core.ShiftLeft(x3, 3, s);
            core.Add(u, s, u);
            core.ShiftLeft(x5, 5, s);
            core.Add(u, s, u);
            signs |= Pair(core, t, u, v.Slice(2 * e, e), v.Slice(3 * e, e)) ? 2 : 0;

            // 4: even x0 + 16·x2 + 256·x4, odd 4·x1 + 64·x3 + 1024·x5
            core.ShiftLeft(x2, 4, t);
            core.Add(t, x0, t);
            core.ShiftLeft(x4, 8, s);
            core.Add(t, s, t);
            core.ShiftLeft(x1, 2, u);
            core.ShiftLeft(x3, 6, s);
            core.Add(u, s, u);
            core.ShiftLeft(x5, 10, s);
            core.Add(u, s, u);
            signs |= Pair(core, t, u, v.Slice(4 * e, e), v.Slice(5 * e, e)) ? 4 : 0;

            // 1/2 (times 32): even 32·x0 + 8·x2 + 2·x4, odd 16·x1 + 4·x3 + x5
            core.ShiftLeft(x0, 5, t);
            core.ShiftLeft(x2, 3, s);
            core.Add(t, s, t);
            core.ShiftLeft(x4, 1, s);
            core.Add(t, s, t);
            core.ShiftLeft(x1, 4, u);
            core.ShiftLeft(x3, 2, s);
            core.Add(u, s, u);
            core.Add(u, x5, u);
            signs |= Pair(core, t, u, v.Slice(6 * e, e), v.Slice(7 * e, e)) ? 8 : 0;

            // 1/4 (times 1024): 1024·x0 + 256·x1 + 64·x2 + 16·x3 + 4·x4 + x5
            Span<ulong> q = v.Slice(8 * e, e);
            core.ShiftLeft(x0, 10, q);
            core.ShiftLeft(x1, 8, s);
            core.Add(q, s, q);
            core.ShiftLeft(x2, 6, s);
            core.Add(q, s, q);
            core.ShiftLeft(x3, 4, s);
            core.Add(q, s, q);
            core.ShiftLeft(x4, 2, s);
            core.Add(q, s, q);
            core.Add(q, x5, q);
            return signs;
        }

        // plus = even + odd, minus = |even - odd|; returns true when even - odd < 0
        private static bool Pair(IFullIntCore core, Span<ulong> even, Span<ulong> odd, Span<ulong> plus, Span<ulong> minus)
        {
            core.Add(even, odd, plus);
            return core.Sub(even, odd, minus) < 0;
        }
    }
}
