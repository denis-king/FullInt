using System;

namespace FullInt.Core
{
    public static partial class Karatsuba
    {
        /// <summary>
        /// Below this many words in the shorter operand <see cref="MultiplyLow"/> is the core's
        /// MultLow (MulLoAdx.asm on x64 with BMI2 + ADX). C++ x64 (Zen 3), of the full product:
        /// 0.72-0.83 at 64-256 words, 0.80-0.92 at 512-2048.
        /// </summary>
        public static int MulLoThreshold { get; set; } = 96;

        /// <summary>
        /// r = a·b mod β^n, n = r.Length (the low half of the product). Mulders' recursion:
        ///   low_n(a·b) = low_n(a0·b0) + β^k·(low_l(a1·b0) + low_l(a0·b1)),  k = 0.7n, l = n - k
        ///   (a0, b0: the low k words, a1, b1: the next l words; the words above n do not matter)
        /// one full product of k words and two short ones of l words.
        /// </summary>
        public static void MultiplyLow(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            int n = r.Length;
            a = Low(a, n);
            b = Low(b, n);
            if (a.Length == 0 || b.Length == 0) { r.Clear(); return; }
            int la = a.Length, lb = b.Length;
            if (Math.Min(la, lb) < MulLoThreshold || n < 8)
            {
                if (n <= la + lb) { core.MultLow(a, b, r); return; }
                r.Clear();
                core.Mult(a, b, r.Slice(n - la - lb));
                return;
            }

            int k = (n * 7 + 9) / 10, l = n - k;
            // low_n(a0·b0): a0, b0 the low k words (2k >= n words of product)
            Span<ulong> a0 = Low(a, k), b0 = Low(b, k);
            var full = new ulong[a0.Length + b0.Length];
            Multiply(core, a0, b0, full);
            r.Clear();
            int w = Math.Min(n, full.Length);
            full.AsSpan(full.Length - w).CopyTo(r.Slice(n - w));
            if (l == 0) return;
            // + β^k·(low_l(a1·b0) + low_l(a0·b1)) mod β^n: into r's top l words
            var t = new ulong[l];
            Span<ulong> top = r.Slice(0, l);
            Span<ulong> a1 = Mid(a, k, l), b1 = Mid(b, k, l);
            if (a1.Length != 0)
            {
                MultiplyLow(core, a1, Low(b, l), t);
                core.Add(top, t, top);                     // a carry out of the top: dropped (mod β^n)
            }
            if (b1.Length != 0)
            {
                MultiplyLow(core, Low(a, l), b1, t);
                core.Add(top, t, top);
            }
        }

        // the low w words of x (all of x if shorter)
        private static Span<ulong> Low(Span<ulong> x, int w) => x.Length <= w ? x : x.Slice(x.Length - w);

        // words [k, k + l) from the low end of x (empty if x is shorter than k)
        private static Span<ulong> Mid(Span<ulong> x, int k, int l)
        {
            if (x.Length <= k) return Span<ulong>.Empty;
            int top = Math.Min(x.Length, k + l);
            return x.Slice(x.Length - top, top - k);
        }
    }
}
