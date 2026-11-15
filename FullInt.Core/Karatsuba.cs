using System;
using System.Buffers;

namespace FullInt.Core
{
    /// <summary>
    /// Karatsuba multiplication on top of the core's basecase Mult (the hand-written asm on win-x64).
    /// The split, the recursion and the scratch buffers are here; every leaf product, add and sub
    /// is a core call. Limbs are big-endian (index 0 = most significant word), so the low half
    /// of a number is its last m words: halves are plain slices, nothing is copied.
    ///
    ///   a = a1·B^m + a0,  b = b1·B^m + b0          (a0, b0: the low m words)
    ///   z0 = a0·b0,  z2 = a1·b1,  zm = |a1 - a0| · |b1 - b0|
    ///   a·b = z2·B^2m + (z0 + z2 ∓ zm)·B^m + z0    (∓: minus when (a1-a0) and (b1-b0) have the same sign)
    ///
    /// The subtractive form keeps every intermediate at m or 2m(+1) words, without carry words.
    ///
    /// This class is the multiplication dispatcher: below <see cref="Threshold"/> the basecase,
    /// from <see cref="Toom3Threshold"/> a Toom-3 level (Toom3.cs), from <see cref="Toom4Threshold"/>
    /// a Toom-4 level (Toom4.cs), in between a Karatsuba level;
    /// every subproduct chooses again by its own size.
    /// </summary>
    public static partial class Karatsuba
    {
        /// <summary>
        /// Below this many words in the shorter operand, the basecase (column) multiply is used.
        /// Tuned with `FullInt.Benchmarks kara` on win-x64 (Zen 3), after the add/sub asm rewrite:
        /// the large sizes are equally fast for 40..64, and 64 keeps everything below it on the
        /// basecase. GMP uses ~30; ours is higher because the column asm is fast for its size
        /// and each core call from C# still costs ~10-15 ns.
        /// </summary>
        public static int Threshold { get; set; } = 64;

        /// <summary>Scratch words needed by <see cref="Multiply(IFullIntCore, Span{ulong}, Span{ulong}, Span{ulong}, int)"/>.</summary>
        public static int ScratchLength(int lenA, int lenB) => 16 * Math.Max(lenA, lenB) + 1024;

        /// <summary>r = a · b with r.Length == a.Length + b.Length.</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r) =>
            Multiply(core, a, b, r, Threshold);

        /// <summary>Same, with an explicit Karatsuba threshold (tests and tuning; at least 2).</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold) =>
            Multiply(core, a, b, r, threshold, Toom3Threshold);

        /// <summary>Same, with explicit Karatsuba (at least 2) and Toom-3 (at least 3) thresholds.</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold, int toom3Threshold) =>
            Multiply(core, a, b, r, threshold, toom3Threshold, Toom4Threshold);

        /// <summary>Same, with explicit Karatsuba (at least 2), Toom-3 (at least 3) and Toom-4 (at least 4) thresholds.</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold, int toom3Threshold, int toom4Threshold) =>
            Multiply(core, a, b, r, threshold, toom3Threshold, toom4Threshold, FftThreshold);

        /// <summary>The FFT product alone with K = 2^k pieces (tests: the FFT on small numbers).</summary>
        public static void FftMultiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int k)
        {
            if (r.Length != a.Length + b.Length) throw new ArgumentException("r.Length must be a.Length + b.Length", nameof(r));
            if (k < 1 || k > 20) throw new ArgumentOutOfRangeException(nameof(k));
            FftMul(core, a, b, r, Threshold, Toom3Threshold, Toom4Threshold, FftThreshold, k);
        }

        /// <summary>Same, with an explicit FFT threshold too (at least 8).</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
        {
            if (fftThreshold < 8) throw new ArgumentOutOfRangeException(nameof(fftThreshold));
            if (threshold < 2) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (toom3Threshold < 3) throw new ArgumentOutOfRangeException(nameof(toom3Threshold));
            if (toom4Threshold < 4) throw new ArgumentOutOfRangeException(nameof(toom4Threshold));
            if (r.Length != a.Length + b.Length) throw new ArgumentException("r.Length must be a.Length + b.Length", nameof(r));

            ulong[] scratch = ArrayPool<ulong>.Shared.Rent(ScratchLength(a.Length, b.Length));
            try
            {
                Mul(core, a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(scratch);
            }
        }

        // r = a · b, r.Length == a.Length + b.Length. The scratch is dirty on entry: everything
        // taken from it is fully written before it is read.
        private static void Mul(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
        {
            if (a.Length < b.Length)
            {
                var t = a; a = b; b = t;
            }
            int la = a.Length, lb = b.Length;

            if (lb < threshold)
            {
                core.Mult(a, b, r);   // basecase: the column multiply
                return;
            }

            int m = (la + 1) / 2;
            if (lb <= m)
            {
                MulUnbalanced(core, a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
                return;
            }

            if (lb >= fftThreshold)
            {
                FftMul(core, a, b, r, threshold, toom3Threshold, toom4Threshold, fftThreshold, 0);
                return;
            }

            if (lb >= toom4Threshold && lb > 3 * ((la + 3) / 4))   // b reaches its top quarter
            {
                Toom4(core, a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
                return;
            }

            if (lb >= toom3Threshold && lb > 2 * ((la + 2) / 3))   // b reaches its top third
            {
                Toom3(core, a, b, r, scratch, threshold, toom3Threshold, toom4Threshold, fftThreshold);
                return;
            }

            int len = la + lb;
            Span<ulong> a1 = a.Slice(0, la - m), a0 = a.Slice(la - m);
            Span<ulong> b1 = b.Slice(0, lb - m), b0 = b.Slice(lb - m);

            // z0 and z2 go straight into their final places in r (they do not overlap)
            Span<ulong> z0 = r.Slice(len - 2 * m);        // 2m words
            Span<ulong> z2 = r.Slice(0, len - 2 * m);     // (la - m) + (lb - m) words

            Span<ulong> da = scratch.Slice(0, m);
            Span<ulong> db = scratch.Slice(m, m);
            Span<ulong> zm = scratch.Slice(2 * m, 2 * m);
            Span<ulong> mid = scratch.Slice(4 * m, 2 * m + 1);
            Span<ulong> rest = scratch.Slice(6 * m + 1);  // for the recursive calls, one at a time

            Mul(core, a0, b0, z0, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
            Mul(core, a1, b1, z2, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);

            int sa = core.Sub(a1, a0, da);   // da = |a1 - a0|, sa = sign of (a1 - a0)
            int sb = core.Sub(b1, b0, db);

            // mid = z0 + z2 - (a1 - a0)(b1 - b0) = a1·b0 + a0·b1  (never negative, fits 2m + 1 words)
            core.Add(z0, z2, mid);
            if (sa != 0 && sb != 0)
            {
                Mul(core, da, db, zm, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
                if (sa == sb)
                    core.Sub(mid, zm, mid);
                else
                    core.Add(mid, zm, mid);
            }

            // r += mid · B^m. In place: the add reads both words before writing, from the low end.
            // r[0 .. len - m) can be one word shorter than mid; that top word of mid is then zero.
            Span<ulong> rUp = r.Slice(0, len - m);
            core.Add(rUp, mid, rUp);
        }

        // a much longer than b (b.Length <= half of a): a is cut into chunks of b.Length words
        // from the low end, each chunk·b (balanced, so Karatsuba again) is added in at its place.
        private static void MulUnbalanced(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold)
        {
            int la = a.Length, lb = b.Length, len = la + lb;
            r.Clear();

            Span<ulong> tmp = scratch.Slice(0, 2 * lb);
            Span<ulong> rest = scratch.Slice(2 * lb);

            for (int done = 0; done < la; done += lb)    // done = low words of a already multiplied
            {
                int c = Math.Min(lb, la - done);
                Span<ulong> chunk = a.Slice(la - done - c, c);
                Span<ulong> t = tmp.Slice(0, c + lb);
                Mul(core, chunk, b, t, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);

                // only the c + lb words at this offset change: everything below fits there, no carry out
                Span<ulong> target = r.Slice(len - done - c - lb, c + lb);
                core.Add(target, t, target);
            }
        }
    }
}
