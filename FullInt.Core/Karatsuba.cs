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
    /// a Toom-4 level (Toom4.cs), from <see cref="Toom6Threshold"/> a Toom-6 level (Toom6.cs),
    /// from <see cref="FftThreshold"/> the FFT (Fft.cs), in between a Karatsuba level;
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
        public static int Threshold { get; set; } = !Environment.Is64BitProcess ? 32 : ArmCpu.Narrow ? 32 : 64;

        // AArch64 with a narrow multiplier (ArmCpu; `kara` on a Neoverse-N1, the AArch64 kernels
        // through the .so, 2026-11-25): of the basecase with 64 / 32: 48 words 0.98 / 0.77, 64 0.78 /
        // 0.64, 128 0.59 / 0.48, 1024 0.19 / 0.16; 24 is as good from 64 words but splits 24-31 into
        // 12-word halves 1.2 times slower than the basecase there.

        // x86 (a 32-bit process): a word product is four 32x32 multiplies, so every level pays
        // off earlier. `kara`, `kara toom`, `kara toom4` in a 32-bit .NET 8 process (Zen 3):
        // Karatsuba 24..32 best (128 words 0.56 of the basecase, 64: 0.65; 512: 0.34 / 0.41);
        // Toom-3 from 128 (2048 words 0.76 of Karatsuba alone, 256: 0.82); Toom-4 from 256
        // (1024-4096 words 0.87-0.91).

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
            FftMul(core, a, b, r, new Limits(Threshold, Toom3Threshold, Toom4Threshold, Toom6Threshold, FftThreshold, SqrThreshold, int.MaxValue), k);
        }

        /// <summary>
        /// The NTT product alone (tests: the transforms on any size). False, with r untouched, when
        /// the core has no NTT kernels (AVX2) or the product is too long for them.
        /// </summary>
        public static bool NttMultiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            if (r.Length != a.Length + b.Length) throw new ArgumentException("r.Length must be a.Length + b.Length", nameof(r));
            if (a.Length == 0 || b.Length == 0 || !NativeNtt(core) || !NttOps.Fits(r.Length)) return false;
            NttOps.Multiply(a, b, r);
            return true;
        }

        /// <summary>Same, with an explicit FFT threshold too (at least 8).</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold) =>
            Multiply(core, a, b, r, threshold, toom3Threshold, toom4Threshold, Toom6Threshold, fftThreshold);

        /// <summary>Same, with explicit Karatsuba (at least 2), Toom-3 (at least 3), Toom-4 (at least 4), Toom-6 (at least 6) and FFT (at least 8) thresholds.</summary>
        public static void Multiply(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int threshold, int toom3Threshold, int toom4Threshold, int toom6Threshold, int fftThreshold)
        {
            if (fftThreshold < 8) throw new ArgumentOutOfRangeException(nameof(fftThreshold));
            if (threshold < 2) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (toom3Threshold < 3) throw new ArgumentOutOfRangeException(nameof(toom3Threshold));
            if (toom4Threshold < 4) throw new ArgumentOutOfRangeException(nameof(toom4Threshold));
            if (toom6Threshold < 6) throw new ArgumentOutOfRangeException(nameof(toom6Threshold));
            if (r.Length != a.Length + b.Length) throw new ArgumentException("r.Length must be a.Length + b.Length", nameof(r));

            // the NTT only with the default thresholds: explicit ones (tests, tuning) keep the
            // levels they name
            bool defaults = threshold == Threshold && toom3Threshold == Toom3Threshold && toom4Threshold == Toom4Threshold
                && toom6Threshold == Toom6Threshold && fftThreshold == FftThreshold;
            var lim = new Limits(threshold, toom3Threshold, toom4Threshold, toom6Threshold, fftThreshold,
                threshold == Threshold ? SqrThreshold : threshold, defaults ? NttThreshold : int.MaxValue);
            ulong[] scratch = ArrayPool<ulong>.Shared.Rent(ScratchLength(a.Length, b.Length));
            try
            {
                Mul(core, a, b, r, scratch, lim);
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(scratch);
            }
        }

        // the thresholds of one Multiply call, passed down the recursion
        private readonly struct Limits
        {
            public readonly int Kara, Toom3, Toom4, Toom6, Fft, SqrKara, Ntt;
            public Limits(int kara, int toom3, int toom4, int toom6, int fft, int sqrKara, int ntt)
            {
                Kara = kara; Toom3 = toom3; Toom4 = toom4; Toom6 = toom6; Fft = fft; SqrKara = sqrKara; Ntt = ntt;
            }
        }

        /// <summary>
        /// Squares (a·a with a and b the same span) below this many words go to the core's Sqr
        /// (SqrAdx.asm), from it a Karatsuba square level: the square kernel is about half the
        /// multiply, so this level pays off later than for products. Tuned with
        /// `FullInt.Benchmarks kara sqr` (Zen 3, x64): 64..128 within noise, 96 best at 96..192
        /// words (0.95 / 0.90 / 0.78 of the kernel alone); a whole square is 0.63-0.75 of a
        /// product of the same size at 64..16384 words. x86 (x86/SqrAdx.asm): 48 best (0.94 / 0.77
        /// / 0.73 of the kernel alone at 64 / 96 / 128 words), a square 0.57-0.71 of a product.
        /// A Multiply call with an explicit Karatsuba threshold other than <see cref="Threshold"/>
        /// (tests, tuning) uses that one for squares too.
        /// </summary>
        public static int SqrThreshold { get; set; } = Environment.Is64BitProcess ? 96 : 48;

        // r = a · b, r.Length == a.Length + b.Length. The scratch is dirty on entry: everything
        // taken from it is fully written before it is read.
        private static void Mul(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, Limits lim)
        {
            if (a.Length < b.Length)
            {
                var t = a; a = b; b = t;
            }
            int la = a.Length, lb = b.Length;
            // a square (the same span twice, as the Toom and FFT levels pass their values of a
            // square): one evaluation, every subproduct a square again, the square kernel below
            bool square = a == b;

            if (square ? lb < lim.SqrKara : lb < lim.Kara)
            {
                if (square) core.Sqr(a, r);
                else core.Mult(a, b, r);   // basecase: the column multiply
                return;
            }

            int m = (la + 1) / 2;
            if (lb <= m)
            {
                MulUnbalanced(core, a, b, r, scratch, lim);
                return;
            }

            if (lb >= lim.Ntt / 2 && NttOps.Worth(la + lb, lb >= lim.Ntt) && NativeNtt(core))
            {
                NttOps.Multiply(a, b, r);
                return;
            }

            if (lb >= lim.Fft)
            {
                FftMul(core, a, b, r, lim, 0);
                return;
            }

            if (lb >= lim.Toom6 && lb > 5 * ((la + 5) / 6) && la >= 36)   // b reaches its top sixth
            {
                Toom6(core, a, b, r, scratch, lim);
                return;
            }

            if (lb >= lim.Toom4 && lb > 3 * ((la + 3) / 4))   // b reaches its top quarter
            {
                Toom4(core, a, b, r, scratch, lim);
                return;
            }

            if (lb >= lim.Toom3 && lb > 2 * ((la + 2) / 3))   // b reaches its top third
            {
                Toom3(core, a, b, r, scratch, lim);
                return;
            }

            int len = la + lb;
            Span<ulong> a1 = a.Slice(0, la - m), a0 = a.Slice(la - m);
            Span<ulong> b1 = b.Slice(0, lb - m), b0 = b.Slice(lb - m);

            // z0 and z2 go straight into their final places in r (they do not overlap)
            Span<ulong> z0 = r.Slice(len - 2 * m);        // 2m words
            Span<ulong> z2 = r.Slice(0, len - 2 * m);     // (la - m) + (lb - m) words

            Span<ulong> da = scratch.Slice(0, m);
            Span<ulong> db = square ? da : scratch.Slice(m, m);
            Span<ulong> zm = scratch.Slice(2 * m, 2 * m);
            Span<ulong> mid = scratch.Slice(4 * m, 2 * m + 1);
            Span<ulong> rest = scratch.Slice(6 * m + 1);  // for the recursive calls, one at a time

            Mul(core, a0, b0, z0, rest, lim);
            Mul(core, a1, b1, z2, rest, lim);

            int sa = core.Sub(a1, a0, da);   // da = |a1 - a0|, sa = sign of (a1 - a0)
            int sb = square ? sa : core.Sub(b1, b0, db);   // a square: (a1 - a0)², subtracted

            // mid = z0 + z2 - (a1 - a0)(b1 - b0) = a1·b0 + a0·b1  (never negative, fits 2m + 1 words)
            core.Add(z0, z2, mid);
            if (sa != 0 && sb != 0)
            {
                Mul(core, da, db, zm, rest, lim);
                if (sa == sb)
                    core.Sub(mid, zm, mid);
                else
                    core.Add(mid, zm, mid);
            }

            // r += mid · B^m. In place: the add reads both words before writing, from the low end.
            // r[0 .. len - m) can be one word shorter than mid; that top word of mid is then zero.
            AddAt(core, r, mid, len - m);
        }

        // a much longer than b (b.Length <= half of a): a is cut into chunks of b.Length words
        // from the low end, each chunk·b (balanced, so Karatsuba again) is added in at its place.
        private static void MulUnbalanced(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, Span<ulong> scratch, Limits lim)
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
                Mul(core, chunk, b, t, rest, lim);

                // only the c + lb words at this offset change: everything below fits there, no carry out
                Span<ulong> target = r.Slice(len - done - c - lb, c + lb);
                core.Add(target, t, target);
            }
        }
    }
}
