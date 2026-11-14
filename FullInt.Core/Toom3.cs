using System;

namespace FullInt.Core
{
    public static partial class Karatsuba
    {
        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced), a level splits in three (Toom-3) instead of two. Tuned with
        /// `FullInt.Benchmarks kara`; see docs/benchmarks.
        /// </summary>
        public static int Toom3Threshold { get; set; } = 256;

        // Toom-3 (Toom-Cook 3-way), evaluation points 0, 1, -1, -2, inf; Bodrato's interpolation:
        //   a = a2·X² + a1·X + a0,  b likewise,  X = B^k  (a0, a1: k words, a2: the rest)
        //   r(t) = a(t)·b(t) at t = 0, 1, -1, -2, inf  (five products of about k words)
        //   r3 = (r(-2) - r(1)) / 3,  r1 = (r(1) - r(-1)) / 2,  r2 = r(-1) - r(0)
        //   r3 = (r2 - r3) / 2 + 2·r(inf),  r2 = r2 + r1 - r(inf),  r1 = r1 - r3
        //   a·b = r(0) + r1·X + r2·X² + r3·X³ + r(inf)·X⁴
        // Values at -1 and -2 and the interpolation temporaries are signed (sign-magnitude:
        // a span plus a bool); the final r1, r2, r3 are never negative.
        // Needs lb > 2k so that b2 is not empty (checked by the caller).
        private static void Toom3(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, int threshold, int toom3Threshold)
        {
            int la = a.Length, lb = b.Length, len = la + lb;
            int k = (la + 2) / 3;
            Span<ulong> a0 = a.Slice(la - k), a1 = a.Slice(la - 2 * k, k), a2 = a.Slice(0, la - 2 * k);
            Span<ulong> b0 = b.Slice(lb - k), b1 = b.Slice(lb - 2 * k, k), b2 = b.Slice(0, lb - 2 * k);

            int e = k + 1;          // words of a value at 1, -1, -2 (|p(-2)| < 5·X, |p(1)| < 3·X)
            int l = 2 * e;          // words of their products and of every interpolation value (< 34·X²)
            Span<ulong> p1 = scratch.Slice(0, e), pm1 = scratch.Slice(e, e), pm2 = scratch.Slice(2 * e, e);
            Span<ulong> q1 = scratch.Slice(3 * e, e), qm1 = scratch.Slice(4 * e, e), qm2 = scratch.Slice(5 * e, e);
            int o = 6 * e;
            Span<ulong> r1 = scratch.Slice(o, l), rm1 = scratch.Slice(o + l, l), rm2 = scratch.Slice(o + 2 * l, l);
            Span<ulong> t = scratch.Slice(o + 3 * l, l), u = scratch.Slice(o + 4 * l, l);
            Span<ulong> rest = scratch.Slice(o + 5 * l);

            Evaluate(core, a0, a1, a2, p1, pm1, pm2, out bool nPm1, out bool nPm2);
            Evaluate(core, b0, b1, b2, q1, qm1, qm2, out bool nQm1, out bool nQm2);

            Mul(core, p1, q1, r1, rest, threshold, toom3Threshold);
            Mul(core, pm1, qm1, rm1, rest, threshold, toom3Threshold);
            Mul(core, pm2, qm2, rm2, rest, threshold, toom3Threshold);
            bool nRm1 = nPm1 != nQm1, nRm2 = nPm2 != nQm2;

            // r(0) and r(inf) go straight into their final places in r; the 2k words between them are cleared
            Span<ulong> r0 = r.Slice(len - 2 * k);
            Span<ulong> rInf = r.Slice(0, len - 4 * k);
            Mul(core, a0, b0, r0, rest, threshold, toom3Threshold);
            Mul(core, a2, b2, rInf, rest, threshold, toom3Threshold);
            r.Slice(len - 4 * k, 2 * k).Clear();

            // r3 = (r(-2) - r(1)) / 3                      -> u
            bool n3 = SignedAdd(core, rm2, nRm2, r1, true, t);
            core.Div(t, 3UL, u, out _);                     // exact
            // r1 = (r(1) - r(-1)) / 2                      -> r1
            bool n1 = SignedAdd(core, r1, false, rm1, !nRm1, r1);
            core.ShiftRight(r1, 1, r1);                     // exact
            // r2 = r(-1) - r(0)                            -> rm1
            bool n2 = SignedAdd(core, rm1, nRm1, r0, true, rm1);
            // r3 = (r2 - r3) / 2 + 2·r(inf)                -> t
            n3 = SignedAdd(core, rm1, n2, u, !n3, t);
            core.ShiftRight(t, 1, t);                       // exact
            n3 = SignedAdd(core, t, n3, rInf, false, t);
            n3 = SignedAdd(core, t, n3, rInf, false, t);
            // r2 = r2 + r1 - r(inf)                        -> rm1
            n2 = SignedAdd(core, rm1, n2, r1, n1, rm1);
            n2 = SignedAdd(core, rm1, n2, rInf, true, rm1);
            // r1 = r1 - r3                                 -> r1
            n1 = SignedAdd(core, r1, n1, t, !n3, r1);

            if (n1 || n2 || n3)
            {
                throw new InvalidOperationException("Toom3: negative interpolation result (internal error)");
            }

            // r += r1·X + r2·X² + r3·X³  (in place, from the low end; everything fits len words)
            Span<ulong> up1 = r.Slice(0, len - k), up2 = r.Slice(0, len - 2 * k), up3 = r.Slice(0, len - 3 * k);
            core.Add(up1, r1, up1);
            core.Add(up2, rm1, up2);
            core.Add(up3, t, up3);
        }

        // p(1) = x0 + x1 + x2,  p(-1) = x0 - x1 + x2,  p(-2) = (p(-1) + x2)·2 - x0  (all e = k + 1 words)
        private static void Evaluate(IFullIntCore core, Span<ulong> x0, Span<ulong> x1, Span<ulong> x2,
            Span<ulong> p1, Span<ulong> pm1, Span<ulong> pm2, out bool negPm1, out bool negPm2)
        {
            core.Add(x0, x2, pm2);                               // pm2 = x0 + x2 (temporary)
            core.Add(pm2, x1, p1);                               // p(1)
            negPm1 = core.Sub(pm2, x1, pm1) < 0;                 // p(-1), |x0 + x2 - x1|
            bool n = SignedAdd(core, pm1, negPm1, x2, false, pm2);   // p(-1) + x2
            core.Add(pm2, pm2, pm2);                             // ·2
            negPm2 = SignedAdd(core, pm2, n, x0, true, pm2);     // - x0
        }

        // r = (±x) + (±y) in sign-magnitude; returns the sign of r (true = negative).
        // r may alias x or y; all values fit r (the add's carry out is always zero here).
        private static bool SignedAdd(IFullIntCore core, Span<ulong> x, bool xNegative, Span<ulong> y, bool yNegative, Span<ulong> r)
        {
            if (xNegative == yNegative)
            {
                core.Add(x, y, r);
                return xNegative;
            }
            int c = core.Sub(x, y, r);   // |x - y|, c = sign of (|x| - |y|)
            return c > 0 ? xNegative : c < 0 ? yNegative : false;
        }
    }
}
