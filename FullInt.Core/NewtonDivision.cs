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
        /// <summary>
        /// The engine divides through the Newton reciprocal from this many words of divisor and
        /// of quotient. Tuned with `FullInt.Benchmarks newton` (Zen 3, x64), Newton / Burnikel–Ziegler
        /// at 2N / N: 1.0 at 16384 words, 0.82 at 32768, 0.73 at 65536.
        /// </summary>
        public static int Threshold { get; set; } = Environment.Is64BitProcess ? 16384 : 65536;   // x86: 0.92 at 65536, 1.04 at 32768

        /// <summary>
        /// Same, when the quotient is at least twice the divisor (the reciprocal serves several
        /// blocks): 4N / N 0.88-0.92 at 8192 words, 0.71 at 16384, 0.51-0.56 at 65536.
        /// </summary>
        public static int LongQuotientThreshold { get; set; } = 8192;   // x86: 0.88 at 8192, 0.8 at 32768-65536

        /// <summary>Whether the engine should divide a by b (lengths in words) through the Newton reciprocal.</summary>
        public static bool Use(int dividendLength, int divisorLength)
        {
            int qn = dividendLength - divisorLength;
            return divisorLength >= Threshold && qn >= Threshold
                || divisorLength >= LongQuotientThreshold && qn >= 2 * divisorLength;
        }

        /// <summary>Reciprocals of at most this many words are one division (at least 2).</summary>
        public static int InverseBase { get; set; } = 64;

        /// <summary>
        /// Quotient blocks per divisor length (1: blocks of n words; 2: n/2, a reciprocal of half
        /// the size); 0: 1 for a quotient of at least two divisor lengths, otherwise 2 (measured best).
        /// </summary>
        public static int Split { get; set; } = 0;

        /// <summary>
        /// The core.Div contract: a and b without leading zero words, a.Length &gt;= b.Length;
        /// q: a.Length - b.Length + 1 words, r: b.Length + 2 words (the value right-aligned).
        /// </summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r) =>
            DivRem(core, a, b, q, r, Split, InverseBase);

        /// <summary>Same, with explicit block split (0: automatic) and reciprocal base (at least 2): tests and tuning.</summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int split, int inverseBase)
        {
            if (split < 0) throw new ArgumentOutOfRangeException(nameof(split));
            if (inverseBase < 2) throw new ArgumentOutOfRangeException(nameof(inverseBase));
            int la = a.Length, n = b.Length;
            int sigma = FullHelper.LeadingZeroCount(b[0]);

            // B' = B << sigma: n words, top bit set;  A' = A << sigma
            var bShifted = new ulong[n + 1];
            core.ShiftLeft(b, sigma, bShifted);
            Span<ulong> bn = bShifted.AsSpan(1);
            var aShifted = new ulong[la + 1];
            core.ShiftLeft(a, sigma, aShifted);
            Span<ulong> at = FullHelper.DeleteZeros(aShifted);

            q.Clear();
            var rem = new ulong[n];
            if (at.Length < n || at.Length == n && CompareEqualLength(at, bn) < 0)
            {
                at.CopyTo(rem.AsSpan(n - at.Length));                    // A' < B': q = 0
                Finish(core, rem, sigma, n, r);
                return;
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
                if (split == 0) split = qn >= 2 * n ? 1 : 2;
                int blocks = Math.Max(1, (qn + n - 1) / n) * split;
                int s = Math.Min(n, (qn + blocks - 1) / blocks);
                ulong[] inv = Inverse(core, bn.Slice(0, s), inverseBase);    // s + 1 words

                int t = qn - (qn - 1) / s * s;                            // the first block: 1..s words
                var x = new ulong[n + s];
                var prod = new ulong[2 * s + 1];
                var tb = new ulong[n + s];
                for (int pos = n; pos < at.Length; pos += t, t = s)
                {
                    // X = [R, next t words]
                    Span<ulong> xs = x.AsSpan(0, n + t);
                    rem.CopyTo(xs);
                    at.Slice(pos, t).CopyTo(xs.Slice(n));
                    Span<ulong> qb = quotient.AsSpan(1 + pos - n, t);

                    // q̂ = top t words of X · top t + 1 words of I, over β^t (t + 1 words, kept below β^t)
                    Span<ulong> p = prod.AsSpan(0, 2 * t + 1);
                    Karatsuba.Multiply(core, xs.Slice(0, t), inv.AsSpan(0, t + 1), p);
                    if (p[0] != 0) qb.Fill(ulong.MaxValue);
                    else p.Slice(1, t).CopyTo(qb);

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

            // q: the low q.Length words (the quotient fits; any words above are zero)
            if (q.Length >= quotient.Length) quotient.CopyTo(q.Slice(q.Length - quotient.Length));
            else quotient.AsSpan(quotient.Length - q.Length).CopyTo(q);
            Finish(core, rem, sigma, n, r);
        }

        /// <summary>
        /// I ≈ β^(2s)/D for D of s words with its top bit set: s + 1 words, within a few units
        /// of the floor (tests).
        /// </summary>
        public static ulong[] Inverse(IFullIntCore core, Span<ulong> d, int inverseBase)
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
            ulong[] ih = Inverse(core, d.Slice(0, h), inverseBase);      // h + 1 words

            // e = β^(s+h) - D·Ih  (|e| < β^(s+2))
            var p = new ulong[s + h + 1];
            Karatsuba.Multiply(core, d, ih, p);
            bool eNegative = p[0] != 0;
            if (eNegative) p[0]--;                                        // |e| = P - β^(s+h)
            else NegateLow(p.AsSpan(1));                                  // |e| = β^(s+h) - P
            // Ih·e/β^(2h): e without its low h - 1 words (the error that drops is below 2/β)
            Span<ulong> et = FullHelper.DeleteZeros(p.AsSpan(0, s + 2));

            var inv = new ulong[s + 1];
            ih.CopyTo(inv, 0);
            if (et.Length == 1 && et[0] == 0) return inv;
            var c = new ulong[ih.Length + et.Length];
            Karatsuba.Multiply(core, ih, et, c);
            Span<ulong> cs = FullHelper.DeleteZeros(c.AsSpan(0, c.Length - (h + 1)));   // / β^(h+1)
            if (cs.Length == 1 && cs[0] == 0) return inv;
            if (eNegative) core.Sub(inv, cs, inv);
            else core.Add(inv, cs, inv);
            return inv;
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
