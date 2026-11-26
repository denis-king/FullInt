using System;

namespace FullNumerics
{
    internal static partial class Karatsuba
    {
        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced), a level splits in four (Toom-4) instead of three. Tuned with
        /// `FullInt.Benchmarks kara toom4` on win-x64 (Zen 3): no gain up to 384 words, from 512
        /// words 0.92-0.95 of Karatsuba + Toom-3, 0.85 at 8192; thresholds 384..1024 within noise.
        /// </summary>
        public static int Toom4Threshold { get; set; } = !Environment.Is64BitProcess ? 256 : ArmCpu.Narrow ? 256 : 512;   // x86: see Karatsuba.Threshold

        // AArch64 with a narrow multiplier (ArmCpu; `kara toom4` on a Neoverse-N1): from 256, of
        // Karatsuba + Toom-3 0.83 at 512 words, 0.74 at 1024, 0.73 at 2048, 0.76 at 8192 (from 512:
        // 0.81-0.87).

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
        // Every division is exact; every difference is >= 0 (checked).
        // Needs lb > 3k so that b3 is not empty (checked by the caller).
        private static void Toom4(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch,
            Limits lim)
        {
            int la = a.Length, lb = b.Length, len = la + lb;
            int k = (la + 3) / 4;
            Span<ulong> a0 = a.Slice(la - k), a1 = a.Slice(la - 2 * k, k), a2 = a.Slice(la - 3 * k, k), a3 = a.Slice(0, la - 3 * k);
            Span<ulong> b0 = b.Slice(lb - k), b1 = b.Slice(lb - 2 * k, k), b2 = b.Slice(lb - 3 * k, k), b3 = b.Slice(0, lb - 3 * k);

            int e = k + 1;          // words of a value at 1, -1, 2, -2, 1/2 (each < 15·X)
            int l = 2 * e;          // words of their products and of every interpolation value (< 225·X²)
            Span<ulong> p1 = scratch.Slice(0, e), pm1 = scratch.Slice(e, e), p2 = scratch.Slice(2 * e, e);
            Span<ulong> pm2 = scratch.Slice(3 * e, e), ph = scratch.Slice(4 * e, e);
            Span<ulong> q1 = scratch.Slice(5 * e, e), qm1 = scratch.Slice(6 * e, e), q2 = scratch.Slice(7 * e, e);
            Span<ulong> qm2 = scratch.Slice(8 * e, e), qh = scratch.Slice(9 * e, e);
            Span<ulong> e1 = scratch.Slice(10 * e, e), e2 = scratch.Slice(11 * e, e);
            int o = 12 * e;
            Span<ulong> w1 = scratch.Slice(o, l), wm1 = scratch.Slice(o + l, l), w2 = scratch.Slice(o + 2 * l, l);
            Span<ulong> wm2 = scratch.Slice(o + 3 * l, l), wh = scratch.Slice(o + 4 * l, l);
            Span<ulong> t1 = scratch.Slice(o + 5 * l, l), t2 = scratch.Slice(o + 6 * l, l), t3 = scratch.Slice(o + 7 * l, l);
            Span<ulong> rest = scratch.Slice(o + 8 * l);

            Evaluate4(core, a0, a1, a2, a3, p1, pm1, p2, pm2, ph, e1, e2, out bool nPm1, out bool nPm2);
            bool nQm1 = nPm1, nQm2 = nPm2;
            if (a == b) { q1 = p1; qm1 = pm1; q2 = p2; qm2 = pm2; qh = ph; }   // a square: one evaluation
            else Evaluate4(core, b0, b1, b2, b3, q1, qm1, q2, qm2, qh, e1, e2, out nQm1, out nQm2);

            Mul(core, p1, q1, w1, rest, lim);
            Mul(core, pm1, qm1, wm1, rest, lim);
            Mul(core, p2, q2, w2, rest, lim);
            Mul(core, pm2, qm2, wm2, rest, lim);
            Mul(core, ph, qh, wh, rest, lim);
            bool nWm1 = nPm1 != nQm1, nWm2 = nPm2 != nQm2;

            // c0 and c6 go straight into their final places in r; the 4k words between them are cleared
            Span<ulong> c0 = r.Slice(len - 2 * k);
            Span<ulong> c6 = r.Slice(0, len - 6 * k);
            Mul(core, a0, b0, c0, rest, lim);
            Mul(core, a3, b3, c6, rest, lim);
            r.Slice(len - 6 * k, 4 * k).Clear();

            bool neg = false;
            // O1 -> t1, E1 -> wm1
            neg |= SignedAdd(core, w1, false, wm1, !nWm1, t1);
            neg |= SignedAdd(core, w1, false, wm1, nWm1, wm1);
            core.ShiftRight(t1, 1, t1);
            core.ShiftRight(wm1, 1, wm1);
            // O2 -> w1, E2 -> wm2
            neg |= SignedAdd(core, w2, false, wm2, !nWm2, w1);
            neg |= SignedAdd(core, w2, false, wm2, nWm2, wm2);
            core.ShiftRight(w1, 2, w1);
            core.ShiftRight(wm2, 1, wm2);
            // S = E1 - c0 - c6 -> wm1;  T = E2 - c0 - 64·c6 -> wm2
            neg |= core.Sub(wm1, c0, wm1) < 0;
            neg |= core.Sub(wm1, c6, wm1) < 0;
            neg |= core.Sub(wm2, c0, wm2) < 0;
            core.ShiftLeft(c6, 6, w2);
            neg |= core.Sub(wm2, w2, wm2) < 0;
            // c4 = (T - 4S) / 12 -> t2;  c2 = S - c4 -> wm1
            core.ShiftLeft(wm1.Slice(1), 2, w2);
            neg |= core.Sub(wm2, w2, wm2) < 0;
            core.DivExact(wm2, 12UL, t2);                        // exact (the shift by 2 inside)
            neg |= core.Sub(wm1, t2, wm1) < 0;
            // Ho = (Wh - 64·c0 - 16·c2 - 4·c4 - c6) / 2 -> wh
            core.ShiftLeft(c0, 6, w2);
            neg |= core.Sub(wh, w2, wh) < 0;
            core.ShiftLeft(wm1.Slice(1), 4, w2);
            neg |= core.Sub(wh, w2, wh) < 0;
            core.ShiftLeft(t2.Slice(1), 2, w2);
            neg |= core.Sub(wh, w2, wh) < 0;
            neg |= core.Sub(wh, c6, wh) < 0;
            core.ShiftRight(wh, 1, wh);
            // U = O2 - O1 -> w1;  V = Ho - O1 -> wh
            neg |= core.Sub(w1, t1, w1) < 0;
            neg |= core.Sub(wh, t1, wh) < 0;
            // c3 = (15·O1 - V - U) / 9 -> t3
            core.ShiftLeft(t1.Slice(1), 4, w2);
            neg |= core.Sub(w2, t1, w2) < 0;
            neg |= core.Sub(w2, wh, w2) < 0;
            neg |= core.Sub(w2, w1, w2) < 0;
            core.DivExact(w2, 9UL, t3);                          // exact
            // c5 = (U - 3·c3) / 15 -> wm2
            core.Add(t3, t3, w2);
            core.Add(w2, t3, w2);
            neg |= core.Sub(w1, w2, w1) < 0;
            core.DivExact(w1, 15UL, wm2);                        // exact
            // c1 = O1 - c3 - c5 -> t1
            neg |= core.Sub(t1, t3, t1) < 0;
            neg |= core.Sub(t1, wm2, t1) < 0;

            if (neg)
            {
                throw new InvalidOperationException("Toom4: negative interpolation value (internal error)");
            }

            // r += c1·X + c2·X² + c3·X³ + c4·X⁴ + c5·X⁵  (in place; everything fits len words)
            AddAt(core, r, t1, len - k);
            AddAt(core, r, wm1, len - 2 * k);
            AddAt(core, r, t3, len - 3 * k);
            AddAt(core, r, t2, len - 4 * k);
            AddAt(core, r, wm2, len - 5 * k);
        }

        // r[0 .. upTo) += c (the value fits there). Only the words c lands on and one for its
        // carry: the Add kernel walks its whole first operand (it copies it on), so an add into all
        // of r above c's place (as before) cost the length of the product per coefficient - 9 of
        // them per Toom-6 level, ~63k words of passes instead of ~18k. A carry beyond them goes on
        // by hand (rare). Toom-3 / 4 / 6 and the Karatsuba level (C++: -2..-5% from 512 words).
        private static void AddAt(IFullIntCore core, Span<ulong> r, Span<ulong> c, int upTo)
        {
            Span<ulong> v = FullHelper.DeleteZeros(c);
            int width = Math.Min(upTo, v.Length + 1);
            Span<ulong> target = r.Slice(upTo - width, width);
            if (core.Add(target, v, target) != 0)
                for (int j = upTo - width - 1; j >= 0; j--)
                    if (++r[j] != 0) break;
        }

        // p(1), p(-1), p(2), p(-2) and 8·p(1/2) of x3·X³ + x2·X² + x1·X + x0 (all e = k + 1 words;
        // t, u: e words of scratch)
        private static void Evaluate4(IFullIntCore core, Span<ulong> x0, Span<ulong> x1, Span<ulong> x2, Span<ulong> x3,
            Span<ulong> p1, Span<ulong> pm1, Span<ulong> p2, Span<ulong> pm2, Span<ulong> ph, Span<ulong> t, Span<ulong> u,
            out bool negPm1, out bool negPm2)
        {
            core.Add(x0, x2, t);                                 // x0 + x2
            core.Add(x1, x3, u);                                 // x1 + x3
            core.Add(t, u, p1);                                  // p(1)
            negPm1 = core.Sub(t, u, pm1) < 0;                    // p(-1)
            core.ShiftLeft(x2, 2, t);                            // 4·x2
            core.Add(t, x0, t);                                  // x0 + 4·x2
            core.ShiftLeft(x3, 2, u);                            // 4·x3
            core.Add(u, x1, u);                                  // x1 + 4·x3
            core.Add(u, u, u);                                   // 2·x1 + 8·x3
            core.Add(t, u, p2);                                  // p(2)
            negPm2 = core.Sub(t, u, pm2) < 0;                    // p(-2)
            core.Add(x0, x0, ph);                                // 8·p(1/2) = ((2·x0 + x1)·2 + x2)·2 + x3
            core.Add(ph, x1, ph);
            core.Add(ph, ph, ph);
            core.Add(ph, x2, ph);
            core.Add(ph, ph, ph);
            core.Add(ph, x3, ph);
        }
    }
}
