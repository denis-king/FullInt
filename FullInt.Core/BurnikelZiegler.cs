using System;

namespace FullInt.Core
{
    /// <summary>
    /// Burnikel–Ziegler recursive division (Burnikel, Ziegler: "Fast Recursive Division", 1998)
    /// on top of the core: the leaves are the core's Div (the asm Knuth division), the products
    /// go through <see cref="Karatsuba"/>. The cost follows the multiplication: about 2·M(n)·log n
    /// instead of the basecase's n².
    ///
    /// Limbs are big-endian (index 0 = most significant word): a number's top half is its first
    /// words, so halves and blocks are plain slices.
    ///
    ///   Div2n1n(A, B):  A (2n words) &lt; B·β^n, B (n words) with its top bit set
    ///     A = [A1 A2 A3 A4] (n/2 words each)
    ///     Q1, R = Div3n2n([A1 A2 A3], B);  Q2, S = Div3n2n([R A4], B);  Q = [Q1 Q2], remainder S
    ///   Div3n2n(A, B):  A (3k words) &lt; B·β^k, B = [B1 B2] (k words each)
    ///     A1 &lt; B1:  Q̂, R1 = Div2n1n([A1 A2], B1)      otherwise  Q̂ = β^k - 1, R1 = A2 + B1
    ///     R̂ = [R1 A3] - Q̂·B2;  while R̂ &lt; 0: R̂ += B, Q̂ -= 1   (at most twice)
    ///
    /// The divisor is shifted left so that its length is n = j·2^k (j &lt; Threshold, so the
    /// recursion halves down to the basecase) and its top bit is set; the dividend is shifted by
    /// the same and cut into blocks of n words, divided from the top, schoolbook-style.
    /// </summary>
    public static class BurnikelZiegler
    {
        /// <summary>
        /// Inside the recursion, blocks below this many words (or odd) go to the core's Div.
        /// The engine uses Burnikel–Ziegler from 2·Threshold words of divisor and of quotient:
        /// below that the shifts and work buffers cost more than the recursion saves.
        /// Tuned with `FullInt.Benchmarks bz` on win-x64 (Zen 3), 2N / N words: thresholds
        /// 80..192 are equal within noise at 512..4096 words (4096: 0.35 of the core Div alone,
        /// 2048: 0.45, 1024: 0.65); a 128-word division is 10-20% slower through it, 192 words
        /// gains ~10%.
        /// </summary>
        public static int Threshold { get; set; } = 96;

        /// <summary>
        /// The core.Div contract: a and b without leading zero words, a.Length &gt;= b.Length;
        /// q: a.Length - b.Length + 1 words, r: b.Length + 2 words (the value right-aligned).
        /// </summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r) =>
            DivRem(core, a, b, q, r, Threshold);

        /// <summary>Same, with an explicit threshold (tests and tuning; at least 2).</summary>
        public static void DivRem(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int threshold)
        {
            if (threshold < 2) throw new ArgumentOutOfRangeException(nameof(threshold));
            int la = a.Length, lb = b.Length;

            // n = j·m >= lb, m = 2^k the smallest with m·threshold > lb, so j < threshold
            int m = 1;
            while ((long)m * threshold <= lb) m *= 2;
            int n = (lb + m - 1) / m * m;
            int sigma = (n - lb) * 64 + FullHelper.LeadingZeroCount(b[0]);

            // B' = B << sigma: exactly n words, top bit set
            var bShifted = new ulong[lb + sigma / 64 + 1];
            core.ShiftLeft(b, sigma, bShifted);
            Span<ulong> bn = bShifted.AsSpan(bShifted.Length - n);

            // A' = A << sigma: [top: n + h words][c blocks of n words], 0 <= h < n
            var aShifted = new ulong[la + sigma / 64 + 1];
            core.ShiftLeft(a, sigma, aShifted);
            Span<ulong> at = FullHelper.DeleteZeros(aShifted);
            int c = (at.Length - n) / n, h = at.Length - n - c * n;
            var quotient = new ulong[1 + h + c * n];   // [0 or 1][h words][c blocks]

            // The top part. Its top n words are below β^n <= 2·B' (B' has its top bit set): if not
            // below B', that quotient word is 1 and B' is subtracted once. Then its h quotient
            // words by one unbalanced step (Div3n2n with a k = h split of B'). The textbook pads
            // A' to whole blocks with its top bit clear instead: an extra, nearly empty block that
            // costs a whole Div2n1n (half of all numbers; 4096 / 2048 words ~2x, 8192 / 4096 more).
            var ri = new ulong[n];
            var tt = at.Slice(0, n + h).ToArray();
            Span<ulong> topN = tt.AsSpan(0, n);
            if (CompareEqualLength(topN, bn) >= 0)
            {
                core.Sub(topN, bn, topN);
                quotient[0] = 1;
            }
            if (h == 0) topN.CopyTo(ri);
            else Div3n2n(core, tt, bn, quotient.AsSpan(1, h), ri, threshold);

            // then from the top: Z = [R, next block], one n-word quotient block per step
            var zz = new ulong[2 * n];
            for (int p = 0; p < c; p++)
            {
                ri.CopyTo(zz);
                at.Slice(n + h + p * n, n).CopyTo(zz.AsSpan(n));
                Div2n1n(core, zz, bn, quotient.AsSpan(1 + h + p * n, n), ri, threshold);
            }

            // q: the low q.Length words (the quotient fits; any words above are zero)
            q.Clear();
            if (q.Length >= quotient.Length) quotient.CopyTo(q.Slice(q.Length - quotient.Length));
            else quotient.AsSpan(quotient.Length - q.Length).CopyTo(q);

            // r = R' >> sigma: lb words, right-aligned in r
            r.Clear();
            core.ShiftRight(ri, sigma, r.Slice(r.Length - lb));
        }

        // A (2n) < B·β^n, B (n) with its top bit set -> Q (n), R (n)
        private static void Div2n1n(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int threshold)
        {
            int n = b.Length;
            if ((n & 1) != 0 || n < threshold)
            {
                Basecase(core, a, b, q, r);
                return;
            }

            int k = n / 2;
            var r1 = new ulong[3 * k];   // [R, A4] for the second step
            Div3n2n(core, a.Slice(0, 3 * k), b, q.Slice(0, k), r1.AsSpan(0, n), threshold);
            a.Slice(3 * k, k).CopyTo(r1.AsSpan(n));
            Div3n2n(core, r1, b, q.Slice(k, k), r, threshold);
        }

        // A (n + k) < B·β^k, B = [B1 B2] (n, top bit set; B1 k words) -> Q (k), R (n)
        private static void Div3n2n(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r, int threshold)
        {
            // k = q.Length: n / 2 inside the recursion; any 1 <= k < n for the top part of DivRem
            // (A: n + k words, B1: the top k words of B, B2: the other n - k)
            int n = b.Length, k = q.Length;
            Span<ulong> a1 = a.Slice(0, k), a2 = a.Slice(k, k), a3 = a.Slice(2 * k, n - k);
            Span<ulong> b1 = b.Slice(0, k), b2 = b.Slice(k, n - k);

            // X = [R1, A3] in n + 1 words (R1 can take k + 1 words)
            var x = new ulong[n + 1];
            Span<ulong> r1 = x.AsSpan(0, k + 1);
            if (CompareEqualLength(a1, b1) < 0)
            {
                Div2n1n(core, a.Slice(0, 2 * k), b1, q, r1.Slice(1), threshold);
            }
            else
            {
                // A1 = B1 here (A < B·β^k): Q̂ = β^k - 1, R1 = [A1 A2] - Q̂·B1 = A2 + B1
                q.Fill(ulong.MaxValue);
                core.Add(a2, b1, r1);
            }
            a3.CopyTo(x.AsSpan(k + 1));

            // R̂ = X - Q̂·B2, sign-magnitude in x
            var d = new ulong[n];
            Karatsuba.Multiply(core, q, b2, d);
            bool negative = core.Sub(x, d, x) < 0;

            // R̂ < 0: R̂ += B, Q̂ -= 1. With |R̂| in x: |R̂| > B stays negative (|R̂| - B),
            // otherwise R̂ = B - |R̂| >= 0. Sub gives |x - B| either way.
            Span<ulong> one = stackalloc ulong[] { 1UL };
            while (negative)
            {
                core.Sub(q, one, q);
                negative = core.Sub(x, b, x) > 0;
            }

            x.AsSpan(1).CopyTo(r);   // R < B: the top word is zero
        }

        // the core's division on one block: A (2n) < B·β^n -> Q (n), R (n)
        private static void Basecase(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> q, Span<ulong> r)
        {
            int n = b.Length;
            Span<ulong> dividend = FullHelper.DeleteZeros(a);   // the kernels expect no leading zeros
            q.Clear();
            if (dividend.Length < n)   // A < β^(n-1) <= B
            {
                r.Clear();
                dividend.CopyTo(r.Slice(n - dividend.Length));
                return;
            }

            var qb = new ulong[dividend.Length - n + 1];
            var rb = new ulong[n + 2];
            core.Div(dividend, b, qb, rb);

            // the quotient fits n words (A < B·β^n): any word above is zero
            if (qb.Length <= n) qb.CopyTo(q.Slice(n - qb.Length));
            else qb.AsSpan(qb.Length - n).CopyTo(q);
            rb.AsSpan(2).CopyTo(r);
        }

        private static int CompareEqualLength(Span<ulong> x, Span<ulong> y)
        {
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i]) return x[i] > y[i] ? 1 : -1;
            }
            return 0;
        }
    }
}
