using System;

namespace FullInt.Core
{
    /// <summary>
    /// Division through a Newton reciprocal (Barrett-style blocks), for long divisors: the
    /// reciprocal costs about 1.5 products, each quotient block two, so the whole costs a fixed
    /// number of products where Burnikel–Ziegler grows with log n (measured: 2.6 products at
    /// 2048 words, 4.4 at 32768).
    ///
    /// Limbs are big-endian (index 0 = most significant word).
    ///
    ///   B' = B &lt;&lt; sigma (n words, top bit set), A' = A &lt;&lt; sigma.
    ///   Reciprocal of the top s words D of B': I ≈ β^(2s)/D, s + 1 words (in (β^s, 2β^s]):
    ///     s small: one division;  otherwise h = s/2 + 1 (a guard word, so the error stays a few
    ///     units), Ih ≈ β^(2h)/D_h (D_h: the top h words of D, recursively),
    ///     e = β^(s+h) - D·Ih (signed, small),  I = Ih·β^(s-h) + Ih·e/β^(2h).
    ///   Quotient blocks of t &lt;= s words from the top: X = [R, next t words of A'] &lt; B'·β^t,
    ///     q̂ = (top t words of X)·(top t + 1 words of I) / β^t  (a few units off the true q),
    ///     R = X - q̂·B', then q̂ -= 1 while that is negative, q̂ += 1 while R &gt;= B'.
    /// </summary>
    public static class NewtonDivision
    {
        // x86 with the NTT kernels (AVX2): the products are 5-10 times faster there, and Newton
        // pays off as early as on x64 (read before the thresholds below are initialized)
        private static readonly bool X86Ntt = !Environment.Is64BitProcess && FullIntCoreWindowsX86.NttAvailable();

        /// <summary>
        /// The engine divides through the Newton reciprocal from this many words of divisor and
        /// of quotient. Before the NTT (`FullInt.Benchmarks newton`, Zen 3, Newton /
        /// Burnikel–Ziegler at 2N / N): x64 0.87 at 4096 words, 0.81 at 8192, 0.67 at 16384;
        /// x86 0.91 at 16384, 0.82 at 32768. With the NTT (C++ x64, 2N / N of GMP, Newton /
        /// Burnikel–Ziegler): a % b 2048 0.94 / 1.03, 4096 0.87 / 1.08, 8192 0.81 / 0.97; a / b
        /// (<see cref="Quotient"/>) 2048 1.05 / 1.20, 4096 0.90 / 1.23, 8192 0.82 / 1.27. C#
        /// (`newton`, Newton / Burnikel–Ziegler): x64 % 2048 0.94, 4096 0.84, 16384 0.55; / (the
        /// quotients alone) 1024 0.95, 2048 0.88, 4096 0.70, 16384 0.60; x86 with the NTT % 1024
        /// 0.78, 4096 0.59, 32768 0.47; / 1024 0.70, 4096 0.51, 32768 0.43.
        /// </summary>
        public static int Threshold { get; set; } = Environment.Is64BitProcess ? 2048 : X86Ntt ? 1024 : 16384;

        /// <summary>
        /// Same, when the quotient is at least twice the divisor (the reciprocal serves several
        /// blocks): 4N / N x64 0.70 at 4096 words (2048: 0.93), 0.60 at 8192, 0.41 at 65536;
        /// x86 0.83 at 8192, 0.76 at 16384, 0.61 at 65536.
        /// </summary>
        public static int LongQuotientThreshold { get; set; } = Environment.Is64BitProcess ? 2048 : X86Ntt ? 1024 : 8192;

        /// <summary>
        /// Whether the engine should divide a by b (lengths in words) through the Newton reciprocal
        /// (quotientOnly, a / b, through <see cref="Quotient"/>: the same sizes, no remainder in its
        /// last block).
        /// </summary>
        public static bool Use(int dividendLength, int divisorLength, bool quotientOnly = false)
        {
            int qn = dividendLength - divisorLength;
            return divisorLength >= Threshold && qn >= Threshold
                || divisorLength >= LongQuotientThreshold && qn >= 2 * divisorLength;
        }

        /// <summary>
        /// The products whose low part alone is needed (q̂·B', D·Ih) go modulo β^m - 1 through the
        /// cyclic FFT from this many words (below: the full product). x64 (Zen 3), Newton / Burnikel–Ziegler
        /// with it from 2048: 2N / N 0.87 at 4096 words (without: 1.14), 0.75-0.81 at 8192,
        /// 0.67 at 16384, 0.51 at 32768; 4N / N 0.70 at 4096 (0.97), 0.60 at 8192, 0.41 at 65536.
        /// x86 (managed butterflies): from 4096 (2N / N at 65536: 0.69, with the FFT threshold 0.93).
        /// </summary>
        public static int CyclicFrom { get; set; } = Environment.Is64BitProcess ? 2048 : X86Ntt ? 1024 : 4096;

        /// <summary>Reciprocals of at most this many words are one division (at least 2).</summary>
        public static int InverseBase { get; set; } = 64;

        /// <summary>
        /// Quotient blocks per divisor length (1: blocks of n words; 2: n/2, a reciprocal of half
        /// the size); 0: 1 for a quotient of at least two divisor lengths or a divisor below
        /// <see cref="SplitFrom"/> words, otherwise 2 (measured best; <see cref="Quotient"/> too:
        /// a reciprocal of half the length outweighs the remainder of the first block).
        /// </summary>
        public static int Split { get; set; } = 0;

        /// <summary>
        /// The automatic split halves the blocks from this many divisor words (2N / N, x64, of
        /// Burnikel–Ziegler: split 2 0.87 / 0.81 / 0.67 / 0.51 at 4096 / 8192 / 16384 / 32768, split 1
        /// 1.12 / 0.75 / 0.82 / 0.66).
        /// </summary>
        public static int SplitFrom { get; set; } = 0;

        /// <summary>
        /// The core.Div contract: a and b without leading zero words, a.Length &gt;= b.Length;
        /// q: a.Length - b.Length + 1 words, r: b.Length + 2 words (the value right-aligned).
        /// </summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r) =>
            DivRem(core, a, b, q, r, Split, InverseBase, 0);

        /// <summary>Same, with explicit block split (0: automatic) and reciprocal base (at least 2): tests and tuning.</summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int split, int inverseBase) =>
            DivRem(core, a, b, q, r, split, inverseBase, 0);

        /// <summary>
        /// Same, with the products modulo β^m - 1 (the cyclic FFT) from cyclicFrom words (0: from
        /// <see cref="CyclicFrom"/>; tests: small values put the wraparound on small numbers).
        /// </summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int split, int inverseBase, int cyclicFrom)
        {
            if (cyclicFrom <= 0) cyclicFrom = CyclicFrom;
            if (split < 0) throw new ArgumentOutOfRangeException(nameof(split));
            if (inverseBase < 2) throw new ArgumentOutOfRangeException(nameof(inverseBase));
            int la = a.Length, n = b.Length;
            int sigma = FullHelper.LeadingZeroCount(b[0]);

            // B' = B << sigma: n words, top bit set
            var bShifted = new ulong[n + 1];
            core.ShiftLeft(b, sigma, bShifted);
            Run(core, a, bShifted.AsSpan(1), sigma, null, split, inverseBase, cyclicFrom, q, r);
        }

        /// <summary>
        /// The quotient alone (a / b; the <see cref="DivRem(IFullIntCore, Span{ulong}, Span{ulong}, Span{ulong}, Span{ulong})"/>
        /// contract without r): A·β / B with one guard word below the quotient, and the last
        /// block's remainder not computed - the guard word away from 0 and β - 1 (the block's
        /// error is a few units) says the truncation is exact; otherwise (about 2^-59 of the
        /// time) the whole division.
        /// </summary>
        public static void Quotient(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q)
        {
            int n = b.Length;
            int sigma = FullHelper.LeadingZeroCount(b[0]);
            var bShifted = new ulong[n + 1];
            core.ShiftLeft(b, sigma, bShifted);
            var r = new ulong[n + 2];
            if (Run(core, a, bShifted.AsSpan(1), sigma, null, Split, InverseBase, CyclicFrom, q, r, true)) return;
            Run(core, a, bShifted.AsSpan(1), sigma, null, Split, InverseBase, CyclicFrom, q, r);
        }

        /// <summary>
        /// A divisor used many times (the powers of ToString): B &lt;&lt; sigma and its whole
        /// reciprocal computed once; <see cref="DivRem(IFullIntCore, Span{ulong}, Prepared, Span{ulong}, Span{ulong})"/>
        /// then divides in blocks of n words with it, nothing recomputed.
        /// </summary>
        public sealed class Prepared
        {
            internal readonly ulong[] Shifted;   // n + 1 words: [0] = the word shifted out (0), then B'
            internal readonly ulong[] Inverse;   // n + 1 words: ~ β^(2n) / B'
            internal readonly int Sigma;
            // with the NTT (n from half of NttThreshold) the two products of every block keep their
            // fixed factor transformed: I' (the reciprocal without its top word) for q̂, and B' for
            // q̂·B' modulo β^m - 1 (cyclic: m from n + 2, below CyclicFrom too)
            internal NttOps.Image? InverseImage, DivisorImage;

            internal Prepared(ulong[] shifted, ulong[] inverse, int sigma)
            {
                Shifted = shifted; Inverse = inverse; Sigma = sigma;
            }
        }

        /// <summary>B prepared for <see cref="DivRem(IFullIntCore, Span{ulong}, Prepared, Span{ulong}, Span{ulong})"/> (B without leading zero words).</summary>
        public static Prepared Prepare(IFullIntCore core, Span<ulong> b)
        {
            int n = b.Length;
            int sigma = FullHelper.LeadingZeroCount(b[0]);
            var shifted = new ulong[n + 1];
            core.ShiftLeft(b, sigma, shifted);
            var p = new Prepared(shifted, Inverse(core, shifted.AsSpan(1), InverseBase, CyclicFrom), sigma);
            p.InverseImage = Karatsuba.NttPrepare(core, p.Inverse.AsSpan(1), 2 * n, false, n);
            int m = p.InverseImage != null ? Karatsuba.NttCyclicLength(core, n + 2) : 0;
            if (m != 0) p.DivisorImage = NttOps.Prepare(shifted.AsSpan(1), m, true);
            return p;
        }

        /// <summary>The core.Div contract (q: a.Length - b.Length + 1 words, r: b.Length + 2) with a prepared divisor.</summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Prepared p, Span<ulong> q, Span<ulong> r) =>
            Run(core, a, p.Shifted.AsSpan(1), p.Sigma, p.Inverse, 1, InverseBase, CyclicFrom, q, r, false, p);

        // the division by B' = B << sigma (n words, top bit set); inverseN: B''s whole reciprocal
        // (n + 1 words, the blocks then take n words, shorter ones truncate it), or null: one of
        // the length the split asks for, computed here. quotientOnly: the guard word scheme of
        // Quotient; false when the guard word is too close to a boundary (q then not written)
        private static bool Run(IFullIntCore core, Span<ulong> a, Span<ulong> bn, int sigma, ulong[]? inverseN, int split,
            int inverseBase, int cyclicFrom, Span<ulong> q, Span<ulong> r, bool quotientOnly = false, Prepared? prepared = null)
        {
            int la = a.Length, n = bn.Length;
            // quotientOnly: A'·β, the quotient one guard word longer
            var aShifted = new ulong[la + 1 + (quotientOnly ? 1 : 0)];
            core.ShiftLeft(a, sigma, aShifted.AsSpan(0, la + 1));
            Span<ulong> at = FullHelper.DeleteZeros(aShifted);

            q.Clear();
            var rem = new ulong[n];
            if (at.Length < n || at.Length == n && CompareEqualLength(at, bn) < 0)
            {
                at.CopyTo(rem.AsSpan(n - at.Length));                    // A' < B': q = 0
                if (!quotientOnly) Finish(core, rem, sigma, n, r);
                return true;
            }

            int qn = at.Length - n;                                       // quotient words below the top one
            var quotient = new ulong[qn + 1];
            at.Slice(0, n).CopyTo(rem);
            if (CompareEqualLength(rem, bn) >= 0)
            {
                core.Sub(rem, bn, rem);
                quotient[0] = 1;
            }

            if (qn > 0)
            {
                int s;
                ulong[] inv;
                if (inverseN != null)
                {
                    s = n;
                    inv = inverseN;
                }
                else
                {
                    if (split == 0) split = qn >= 2 * n || n < SplitFrom ? 1 : 2;
                    // blocks of n / split words (k divisor lengths of quotient: of qn / (k·split)
                    // if shorter): a quotient a word or two over (the shift of A, the guard word)
                    // goes into a short first block, the others keep the even length
                    int k = Math.Max(1, qn / n);
                    s = Math.Max(1, Math.Min(n / split, qn / (k * split)));
                    inv = Inverse(core, bn.Slice(0, s), inverseBase, cyclicFrom);   // s + 1 words
                }

                int t = qn - (qn - 1) / s * s;                            // the first block: 1..s words
                var x = new ulong[n + s];
                var prod = new ulong[2 * s + 1];
                var tb = new ulong[n + s];
                // long: T = q̂·B' only modulo β^m - 1 (m >= n + 2, the cyclic FFT, about half the
                // product): R = X - T is below 8·B' < β^m / 2 in size, so its residue gives it back
                // (a prepared divisor with its transform: always the cyclic product, with that transform)
                NttOps.Image? bImage = prepared?.DivisorImage, iImage = prepared?.InverseImage;
                bool wrap = bImage != null || n + 2 >= cyclicFrom;
                int mw = bImage != null ? bImage.Words : wrap ? Karatsuba.MulModLength(core, n + 2, cyclicFrom) : 0;
                var xm = new ulong[mw];
                var tm = new ulong[mw];
                for (int pos = n; pos < at.Length; pos += t, t = s)
                {
                    // X = [R, next t words]
                    Span<ulong> xs = x.AsSpan(0, n + t);
                    rem.CopyTo(xs);
                    at.Slice(pos, t).CopyTo(xs.Slice(n));
                    Span<ulong> qb = quotient.AsSpan(1 + pos - n, t);
                    bool last = pos + t == at.Length;

                    // q̂ = top t words of X · top t + 1 words of I, over β^t (kept below β^t). I's top
                    // word v is 1 (2 for a power of two): X·I = X·v·β^t + X·I', I' its next t words -
                    // a t by t product, whose length stays on the transform sizes (t + 1 would not)
                    Span<ulong> p = prod.AsSpan(0, 2 * t + 1);
                    if (iImage != null && t == s) NttOps.MultiplyImage(xs.Slice(0, t), iImage, p.Slice(1));
                    else Karatsuba.Multiply(core, xs.Slice(0, t), inv.AsSpan(1, t), p.Slice(1));
                    p[0] = 0;
                    for (ulong v = inv[0]; v > 0; v--)
                        p[0] += core.Add(p.Slice(1, t), xs.Slice(0, t), p.Slice(1, t));
                    if (p[0] != 0) qb.Fill(ulong.MaxValue);
                    else p.Slice(1, t).CopyTo(qb);
                    if (quotientOnly && last)
                    {
                        // q̂ is within a few units of the block's quotient: with the guard word (its
                        // lowest) away from the boundaries, dropping it leaves the exact quotient
                        ulong g = qb[t - 1];
                        if (g < 16 || g > ulong.MaxValue - 16) return false;
                        break;
                    }

                    // (a short block - the first one, a word or two - takes the direct n by t product:
                    // the cyclic one would cost the whole transform)
                    if (wrap && 8 * t >= n)
                    {
                        Karatsuba.FoldMod(core, xs, xm);
                        if (bImage != null) NttOps.MultiplyImage(qb, bImage, tm);
                        else Karatsuba.MultiplyMod(core, bn, qb, tm, cyclicFrom);
                        bool negative = SignedResidue(core, xm, tm);   // R = X - T, |R| in xm
                        // R < 0: R += B', q̂ -= 1 (|R| > B' stays negative: |R| - B'; else B' - |R|)
                        while (negative)
                        {
                            Decrement(qb);
                            negative = core.Sub(xm, bn, xm) > 0;
                        }
                        while (!IsZero(xm.AsSpan(0, mw - n)) || CompareEqualLength(xm.AsSpan(mw - n), bn) >= 0)
                        {
                            core.Sub(xm, bn, xm);
                            Increment(qb);
                        }
                        xm.AsSpan(mw - n).CopyTo(rem);
                        continue;
                    }

                    if (t >= 2)
                    {
                        // T = q̂·B' only modulo β^(n+2) (the low half of the product, Mulders): R =
                        // X - T is below 8·B' < β^(n+2) / 2 in size, its low n + 2 words give it back
                        int ml = n + 2;
                        Span<ulong> tl = tb.AsSpan(0, ml), xl = x.AsSpan(0, ml);
                        Karatsuba.MultiplyLow(core, bn, qb, tl);
                        xs.Slice(xs.Length - ml).CopyTo(xl);       // (xl starts where xs does: a forward copy)
                        bool negative = SignedLow(core, xl, tl);   // R = X - T, |R| in xl
                        while (negative)
                        {
                            Decrement(qb);
                            negative = core.Sub(xl, bn, xl) > 0;
                        }
                        while (!IsZero(xl.Slice(0, 2)) || CompareEqualLength(xl.Slice(2), bn) >= 0)
                        {
                            core.Sub(xl, bn, xl);
                            Increment(qb);
                        }
                        xl.Slice(2).CopyTo(rem);
                        continue;
                    }

                    // T = q̂·B';  q̂ -= 1 while T > X;  R = X - T;  q̂ += 1 while R >= B'
                    Span<ulong> tt = tb.AsSpan(0, n + t);
                    Karatsuba.Multiply(core, bn, qb, tt);
                    while (core.Compare(tt, xs) > 0)
                    {
                        core.Sub(tt, bn, tt);
                        Decrement(qb);
                    }
                    core.Sub(xs, tt, xs);
                    while (!IsZero(xs.Slice(0, t)) || CompareEqualLength(xs.Slice(t), bn) >= 0)
                    {
                        core.Sub(xs, bn, xs);
                        Increment(qb);
                    }
                    xs.Slice(t).CopyTo(rem);
                }
            }

            // q: the low q.Length words (the quotient fits; any words above are zero); quotientOnly:
            // without the guard word
            Span<ulong> qv = quotient.AsSpan(0, quotient.Length - (quotientOnly ? 1 : 0));
            if (q.Length >= qv.Length) qv.CopyTo(q.Slice(q.Length - qv.Length));
            else qv.Slice(qv.Length - q.Length).CopyTo(q);
            if (!quotientOnly) Finish(core, rem, sigma, n, r);
            return true;
        }

        /// <summary>
        /// I ≈ β^(2s)/D for D of s words with its top bit set: s + 1 words, within a few units
        /// of the floor (tests).
        /// </summary>
        public static ulong[] Inverse(IFullIntCore core, Span<ulong> d, int inverseBase) =>
            Inverse(core, d, inverseBase, CyclicFrom);

        /// <summary>Same, with the products modulo β^m - 1 from cyclicFrom words (tests).</summary>
        public static ulong[] Inverse(IFullIntCore core, Span<ulong> d, int inverseBase, int cyclicFrom)
        {
            int s = d.Length;
            if (s <= inverseBase || s < 4)
            {
                // floor(β^(2s) / D): the quotient has s + 2 words, the top one zero
                var num = new ulong[2 * s + 1];
                num[0] = 1;
                var qq = new ulong[s + 2];
                var rr = new ulong[s + 2];
                core.Div(num, d, qq, rr);
                return qq.AsSpan(1).ToArray();
            }

            int h = s / 2 + 1;
            ulong[] ih = Inverse(core, d.Slice(0, h), inverseBase, cyclicFrom);   // h + 1 words

            // e = β^(s+h) - D·Ih  (|e| < β^(s+2))
            bool eNegative;
            Span<ulong> et;
            if (s + 3 >= cyclicFrom)
            {
                // long: D·Ih only modulo β^m - 1 (m >= s + 3): e = β^((s+h) mod m) - P̃ there, and
                // |e| < β^m / 2 gives it back
                int m = Karatsuba.MulModLength(core, s + 3, cyclicFrom);
                var pm = new ulong[m];
                Karatsuba.MultiplyMod(core, d, ih, pm, cyclicFrom);
                var em = new ulong[m];
                em[m - 1 - (s + h) % m] = 1;
                eNegative = SignedResidue(core, em, pm);
                // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
                et = FullHelper.DeleteZeros(em.AsSpan(0, m - (h - 1)));
            }
            else
            {
                var p = new ulong[s + h + 1];
                Karatsuba.Multiply(core, d, ih, p);
                eNegative = p[0] != 0;
                if (eNegative) p[0]--;                                    // |e| = P - β^(s+h)
                else NegateLow(p.AsSpan(1));                              // |e| = β^(s+h) - P
                // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
                et = FullHelper.DeleteZeros(p.AsSpan(0, s + 2));
            }

            var inv = new ulong[s + 1];
            ih.CopyTo(inv, 0);
            if (et.Length == 1 && et[0] == 0) return inv;
            // Ih·e with Ih's top word v (1, or 2 for a power of two) apart: e·v·β^h + Ih'·e
            int le = et.Length;
            var c = new ulong[ih.Length + le];
            Karatsuba.Multiply(core, ih.AsSpan(1), et, c.AsSpan(1));
            for (ulong v = ih[0]; v > 0; v--)
                c[0] += core.Add(c.AsSpan(1, le), et, c.AsSpan(1, le));
            Span<ulong> cs = FullHelper.DeleteZeros(c.AsSpan(0, c.Length - (h + 1)));   // / β^(h+1)
            if (cs.Length == 1 && cs[0] == 0) return inv;
            if (eNegative) core.Sub(inv, cs, inv);
            else core.Add(inv, cs, inv);
            return inv;
        }

        // x = |x - y| for the value v ≡ x - y mod β^m - 1 (x, y: m-word residues) with |v| < β^m / 2;
        // returns v < 0. x - y is v or v ± (β^m - 1): above β^m / 2 in size it is the latter,
        // and (β^m - 1) - |x - y| is the complement of its words.
        private static bool SignedResidue(IFullIntCore core, Span<ulong> x, Span<ulong> y)
        {
            bool negative = core.Sub(x, y, x) < 0;
            if ((x[0] >> 63) != 0)
            {
                for (int i = 0; i < x.Length; i++) x[i] = ~x[i];
                negative = !negative;
            }
            return negative && !IsZero(x);
        }

        // x = |x - y| for the value v = x - y mod β^m (x, y: m-word low parts) with |v| < β^m / 2;
        // returns v < 0. x - y is v or v ± β^m: above β^m / 2 in size it is the latter, and
        // β^m - |x - y| is its two's complement.
        private static bool SignedLow(IFullIntCore core, Span<ulong> x, Span<ulong> y)
        {
            bool negative = core.Sub(x, y, x) < 0;
            if ((x[0] >> 63) != 0)
            {
                for (int i = 0; i < x.Length; i++) x[i] = ~x[i];
                Increment(x);
                negative = !negative;
            }
            return negative && !IsZero(x);
        }

        // r = R >> sigma: n words, right-aligned in r
        private static void Finish(IFullIntCore core, ulong[] rem, int sigma, int n, Span<ulong> r)
        {
            r.Clear();
            core.ShiftRight(rem, sigma, r.Slice(r.Length - n));
        }

        private static int CompareEqualLength(Span<ulong> x, Span<ulong> y)
        {
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return x[i] > y[i] ? 1 : -1;
            return 0;
        }

        private static bool IsZero(Span<ulong> x)
        {
            foreach (ulong w in x) if (w != 0) return false;
            return true;
        }

        private static void Increment(Span<ulong> x)
        {
            for (int i = x.Length - 1; i >= 0; i--)
                if (++x[i] != 0) return;
        }

        private static void Decrement(Span<ulong> x)
        {
            for (int i = x.Length - 1; i >= 0; i--)
                if (x[i]-- != 0) return;
        }

        // x = β^len - x (two's complement in place)
        private static void NegateLow(Span<ulong> x)
        {
            for (int i = 0; i < x.Length; i++) x[i] = ~x[i];
            Increment(x);
        }
    }
}
