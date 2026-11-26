using System;
using System.Buffers;

namespace FullNumerics
{
    internal static partial class Karatsuba
    {
        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced), the product goes through the Schönhage–Strassen FFT (Fft.cs) instead of
        /// Toom levels. Tuned with `FullInt.Benchmarks kara fft` (Zen 3), the FFT / the Toom levels
        /// (with Toom-6): x64 (the FftAdx.asm butterflies) 1.05 at 4096 words, 0.83-0.92 at 8192,
        /// 0.76 at 16384, 0.64 at 32768, 0.56 at 65536, 0.52 at 131072; x86 (managed butterflies)
        /// 1.2 at 32768, 1.01-1.03 at 65536, 0.85 at 131072.
        /// </summary>
        public static int FftThreshold { get; set; } = Environment.Is64BitProcess ? 8192 : 98304;

        // Schönhage–Strassen multiplication, the product r = a·b of L = la + lb words:
        //   a and b are cut into pieces of M words (piece i = the words of weight B^(M·i)),
        //   K = 2^k >= the number of product pieces, so the cyclic convolution of length K does
        //   not wrap: c_i = sum a_j·b_(i-j), and r = sum c_i·B^(M·i).
        //   The convolution is computed modulo p = 2^N + 1 with N >= 128·M + k + 1 bits (every
        //   c_i < p) and N a multiple of 64 and of K/2: then w = 2^(2N/K) is a K-th root of unity
        //   mod p (2^N = -1, so 2^(2N) = 1), and every twiddle multiply is a shift.
        //   forward FFT of a and b (decimation in frequency: natural in, bit-reversed out),
        //   pointwise products mod p (the multiplication dispatcher again), inverse FFT
        //   (decimation in time: bit-reversed in, natural out), times K^-1 = 2^(2N - k).
        // Values mod p are kept in n1 = N/64 + 1 words, in [0, 2^N].
        // A butterfly is two passes (FftOps): u, v = u + v, u - v in place, and the twiddle shift
        // into a spare vector - the vectors are reached through a slot table, so the spare and
        // the shifted vector swap slots instead of a copy back.
        private static void FftMul(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r,
            Limits lim, int forceK)
        {
            bool native = NativeFft(core);
            ChooseFft(a.Length, b.Length, forceK, native, out int k, out int m, out int nBits);
            Convolve(core, a, b, r, k, m, nBits, lim, native);
        }

        // an instance property: a static field of the x64 core named here would be initialized
        // when this method is compiled - in a 32-bit process too, loading the x64 DLL
        private static bool NativeFft(IFullIntCore core) => core is FullIntCoreNative64 x64 && x64.HasFftKernels;

        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced) the product goes through the AVX2 number-theoretic transforms (NttOps.cs,
        /// NttAvx2.asm; x64 and x86 with AVX2), up to products of 2^21 words; the FFT above them.
        /// The transform lengths 2^k and 3·2^k keep it within 1.5 times the product; from half
        /// the threshold too when the product fills the transform (NttOps.Worth). With the 3·2^k
        /// lengths (C#, `kara ntt`, x64): from 896, 896 words 0.55, 512 (filled) 0.70, 768 0.65,
        /// 1024 0.46, 1280 0.66; 384 (filled, from 768) 1.08: 896 rather than 768.
        /// `FullInt.Benchmarks kara ntt` (Zen 3), with the NTT / the Toom and FFT levels alone:
        /// x64 768 words 0.78 (from 1024: 0.99), 896 0.64, 1024 0.60, 1536 0.57, 2048 0.45-0.49,
        /// 4096 0.35, 8192-131072 0.24-0.29 (C++ x64, whose Toom levels are faster: 0.73 at 1024,
        /// 0.40-0.50 from 4096, 0.42-0.63 of GMP); x86 (32-bit multiplies in the Toom levels,
        /// none in the transforms) 96 words 0.85, 112 0.64, 128 0.57, 512 0.28, 2048 0.17, 8192
        /// 0.11, 32768 0.08. x86 from 256 (from 128 when filled): with 96 the NTT took products
        /// from 48 words (the half), there 1.1-4.4 times the Toom levels (48: a·b 2.9, a² 4.3;
        /// C++ against GMP 2.4 / 3.6).
        /// </summary>
        public static int NttThreshold { get; set; } = Environment.Is64BitProcess ? 896 : 256;

        // the transforms of a fixed factor (NttOps.Image) when the core has the NTT kernels and the
        // products of words words would take them (the rule of the dispatcher); otherwise null
        internal static NttOps.Image? NttPrepare(IFullIntCore core, Span<ulong> b, int words, bool cyclic, int shorter)
        {
            if (!NativeNtt(core) || shorter < NttThreshold / 2 || !NttOps.Worth(words, shorter >= NttThreshold)) return null;
            return NttOps.Prepare(b, words, cyclic);
        }

        internal static int NttCyclicLength(IFullIntCore core, int minWords) => NativeNtt(core) ? NttOps.CyclicLength(minWords) : 0;

        // the same rule as NativeFft: the x64 core's flag through an instance property
        private static bool NativeNtt(IFullIntCore core) =>
            core is FullIntCoreNative64 x64 ? x64.HasNttKernels : core is FullIntCoreNative32 x86 && x86.HasNttKernels;

        /// <summary>
        /// The words of a product modulo β^m - 1 that <see cref="MultiplyMod"/> takes for at least
        /// <paramref name="minWords"/> words: through the cyclic NTT (a power of two; AVX2) or the
        /// cyclic FFT (no zero padding, about half the transform of the full product) from
        /// <see cref="FftThreshold"/> words, otherwise minWords itself (the full product, folded).
        /// </summary>
        public static int MulModLength(IFullIntCore core, int minWords) => MulModLength(core, minWords, FftThreshold);

        /// <summary>Same, cyclic from cyclicFrom words (tests: the cyclic FFT on small numbers).</summary>
        public static int MulModLength(IFullIntCore core, int minWords, int cyclicFrom)
        {
            if (minWords < cyclicFrom) return minWords;
            if (NativeNtt(core))
            {
                int mn = NttOps.CyclicLength(minWords);   // the cyclic NTT: a power of two
                if (mn != 0) return mn;
            }
            ChooseFftCyclic(minWords, false, NativeFft(core), out int k, out int m, out _);
            return m << k;
        }

        /// <summary>
        /// r = a·b mod (β^m - 1), m = r.Length (from <see cref="MulModLength"/>), in [0, β^m - 1)
        /// (β^m - 1 itself, the other zero, is written as 0); a and b at most m words.
        ///   cyclic: K = 2^k pieces of M words, K·M = m, the convolution without zero padding wraps
        ///   (c_i = sum over j + l = i mod K), so sum c_i·β^(M·i) = a·b mod β^(KM) - 1.
        /// </summary>
        public static void MultiplyMod(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r) =>
            MultiplyMod(core, a, b, r, FftThreshold);

        /// <summary>Same, cyclic from cyclicFrom words (tests).</summary>
        public static void MultiplyMod(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r, int cyclicFrom)
        {
            int m = r.Length;
            var lim = new Limits(Threshold, Toom3Threshold, Toom4Threshold, Toom6Threshold, FftThreshold, SqrThreshold, NttThreshold);
            if (m >= cyclicFrom && NativeNtt(core) && NttOps.CyclicLength(m) == m)
            {
                NttOps.MultiplyCyclic(a, b, r);   // the cyclic NTT (MulModLength's powers of two)
                return;
            }
            if (m >= cyclicFrom)
            {
                bool native = NativeFft(core);
                ChooseFftCyclic(m, true, native, out int k, out int pm, out int nBits);
                if (pm << k == m)
                {
                    int n1 = nBits / 64 + 1;
                    ulong[] acc = ArrayPool<ulong>.Shared.Rent(m + n1 + 1);
                    try
                    {
                        Span<ulong> accs = acc.AsSpan(0, m + n1 + 1);
                        Convolve(core, a, b, accs, k, pm, nBits, lim, native);
                        FoldMod(core, accs, r);
                    }
                    finally
                    {
                        ArrayPool<ulong>.Shared.Return(acc);
                    }
                    return;
                }
            }
            ulong[] prod = ArrayPool<ulong>.Shared.Rent(a.Length + b.Length);
            try
            {
                Span<ulong> p = prod.AsSpan(0, a.Length + b.Length);
                Multiply(core, a, b, p);
                FoldMod(core, p, r);
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(prod);
            }
        }

        /// <summary>r = x mod (β^m - 1), m = r.Length, in [0, β^m - 1): the m-word chunks of x added with the end-around carry.</summary>
        public static void FoldMod(IFullIntCore core, Span<ulong> x, Span<ulong> r)
        {
            int m = r.Length;
            r.Clear();
            for (int end = x.Length; end > 0; end -= m)
            {
                int start = Math.Max(0, end - m);
                Span<ulong> chunk = FullHelper.DeleteZeros(x.Slice(start, end - start));
                ulong c = core.Add(r, chunk, r);
                while (c != 0)                                              // β^m = 1
                {
                    c = 1;
                    for (int i = m - 1; i >= 0 && c != 0; i--) c = ++r[i] == 0 ? 1UL : 0UL;
                }
            }
            foreach (ulong w in r) if (w != ulong.MaxValue) return;
            r.Clear();                                                      // β^m - 1 = 0
        }

        // k, M, N for a cyclic product modulo β^(K·M) - 1 of at least mMin words (exact: K·M = mMin,
        // only the K that divide it)
        private static void ChooseFftCyclic(int mMin, bool exact, bool native, out int k, out int m, out int nBits)
        {
            double best = double.MaxValue;
            k = 0; m = 0; nBits = 0;
            for (int kk = 3; kk <= 20; kk++)
            {
                int K = 1 << kk;
                if (exact && mMin % K != 0) continue;
                int mm = (mMin + K - 1) / K;
                int unit = Math.Max(64, K / 2);
                long bits = 128L * mm + kk + 1;
                long nb = (bits + unit - 1) / unit * unit;
                if (nb > int.MaxValue / 4) continue;
                double n1 = nb / 64 + 1;
                double mulCost = n1 < 32 ? 0.5 * n1 * n1 : 2.6 * Math.Pow(n1, 1.55);
                double bfly = native ? 30 + 1.0 * n1 : 20 + 6.0 * n1;
                double cost = K * mulCost + 3.0 * K * kk * bfly;
                if (cost < best)
                {
                    best = cost; k = kk; m = mm; nBits = (int)nb;
                }
                if (mm == 1) break;
            }
        }

        // acc = sum c_i·β^(M·i) of the convolution of a and b in K = 2^k pieces of M words mod 2^N + 1
        // (acc: the words the coefficients land on, cleared here; a coefficient beyond it is zero)
        private static void Convolve(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r,
            int k, int m, int nBits, Limits lim, bool native)
        {
            int len = r.Length;
            int K = 1 << k, n1 = nBits / 64 + 1;

            // vectors: K for a, K for b, one spare each; then the product and the scratch
            int vectors = 2 * K + 2;
            ulong[] buf = ArrayPool<ulong>.Shared.Rent(vectors * n1 + 2 * n1 + ScratchLength(n1, n1));
            int[] slots = ArrayPool<int>.Shared.Rent(vectors);
            try
            {
                Span<ulong> all = buf;
                for (int i = 0; i < vectors; i++) slots[i] = i * n1;
                Span<int> sa = slots.AsSpan(0, K + 1), sb = slots.AsSpan(K + 1, K + 1);   // [K] = the spare
                Span<ulong> prod = all.Slice(vectors * n1, 2 * n1);
                Span<ulong> rest = all.Slice(vectors * n1 + 2 * n1);

                // a square: one transform, the pointwise products squares
                bool square = a == b;
                Split(a, m, K, n1, all, sa);
                if (!square) Split(b, m, K, n1, all, sb);

                Walk(core, native, all, sa, sb, square, K, n1, nBits, prod, rest, lim, 0, K);

                // r = sum (c_i / K)·B^(M·i)
                r.Clear();
                Span<ulong> t = all.Slice(sa[K], n1);                      // the spare
                for (int i = 0; i < K && i * m < len; i++)
                {
                    FftOps.ShlMod(native, all.Slice(sa[i], n1), 2 * nBits - k, t);   // / K
                    Span<ulong> v = FullHelper.DeleteZeros(t);
                    if (v.Length == 1 && v[0] == 0) continue;
                    // only the words c_i lands on (and one for its carry), not the whole r above
                    // them: K adds over the whole product made this step quadratic
                    int end = len - i * m, width = Math.Min(end, v.Length + 1);
                    Span<ulong> target = r.Slice(end - width, width);
                    if (core.Add(target, v, target) != 0)
                        for (int j = end - width - 1; j >= 0; j--)
                            if (++r[j] != 0) break;
                }
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(buf);
                ArrayPool<int>.Shared.Return(slots);
            }
        }

        // k, M, N for an la x lb product: the smallest estimated cost (or forceK = k for tests)
        private static void ChooseFft(int la, int lb, int forceK, bool native, out int k, out int m, out int nBits)
        {
            int len = la + lb;
            double best = double.MaxValue;
            k = 0; m = 0; nBits = 0;
            for (int kk = forceK > 0 ? forceK : 3; kk <= (forceK > 0 ? forceK : 20); kk++)
            {
                int K = 1 << kk;
                int mm = (len + K - 1) / K;
                while ((la + mm - 1) / mm + (lb + mm - 1) / mm - 1 > K) mm++;   // no wrap
                int unit = Math.Max(64, K / 2);
                long bits = 128L * mm + kk + 1;
                long nb = (bits + unit - 1) / unit * unit;
                if (nb > int.MaxValue / 4) continue;
                double n1 = nb / 64 + 1;
                // ns, measured (Zen 3): an n-word product ~2.6·n^1.55 from 32 words; a butterfly
                // (two passes) ~30 + 1·n with the native kernels, ~20 + 6·n managed
                double mulCost = n1 < 32 ? 0.5 * n1 * n1 : 2.6 * Math.Pow(n1, 1.55);
                double bfly = native ? 30 + 1.0 * n1 : 20 + 6.0 * n1;
                double cost = K * mulCost + 3.0 * K * kk * bfly;
                if (cost < best || forceK > 0)
                {
                    best = cost; k = kk; m = mm; nBits = (int)nb;
                }
                if (mm == 1) break;
            }
        }

        // the pieces of x (M words each, from the low end) into the K vectors, zero-padded
        private static void Split(Span<ulong> x, int m, int K, int n1, Span<ulong> all, Span<int> slots)
        {
            int lx = x.Length;
            for (int i = 0; i < K; i++)
            {
                Span<ulong> v = all.Slice(slots[i], n1);
                v.Clear();
                int end = lx - i * m;
                if (end <= 0) continue;
                int start = Math.Max(0, end - m);
                Span<ulong> piece = x.Slice(start, end - start);
                piece.CopyTo(v.Slice(n1 - piece.Length));
            }
        }

        // x = prod mod p (prod: 2·n1 words, < 2^(2N) + 2^(N+1) + 1): prod = hi·2^N + lo == lo - hi
        private static void ReduceProduct(IFullIntCore core, Span<ulong> prod, Span<ulong> x)
        {
            int n1 = x.Length, w = n1 - 1;
            x[0] = 0;
            prod.Slice(2 * n1 - w, w).CopyTo(x.Slice(1));
            Span<ulong> hi = FullHelper.DeleteZeros(prod.Slice(0, 2 * n1 - w));
            if (hi.Length == 1 && hi[0] == 0) return;
            if (core.Sub(x, hi, x) < 0) NegateMod(x);   // |lo - hi| in x: lo - hi + p = p - |lo - hi|
        }

        // x = p - x for 0 < x <= 2^N:  p - x = (2^N - 1 - x) + 2, the low part complemented + 2
        private static void NegateMod(Span<ulong> x)
        {
            int w = x.Length - 1;
            if (x[0] != 0) { x[0] = 0; x[w] = 1; return; }                  // x = 2^N: p - x = 1
            for (int i = 1; i <= w; i++) x[i] = ~x[i];
            ulong add = 2;
            for (int i = w; i >= 1 && add != 0; i--)
            {
                ulong y = x[i] + add;
                add = y < add ? 1UL : 0UL;
                x[i] = y;
            }
            x[0] = add;                                                       // x = 1: the result is 2^N
        }

        // forward DIF, pointwise products, inverse DIT in one depth-first walk: a block's top
        // level (a and b), its two halves recursively, a leaf's product, then the block's inverse
        // top level - the same butterflies with the same twiddles as level by level, each subtree
        // done while its vectors are in the cache (C++: -3..-4% at 32768-65536 words)
        private static void Walk(IFullIntCore core, bool native, Span<ulong> all, Span<int> sa, Span<int> sb, bool square,
            int K, int n1, int nBits, Span<ulong> prod, Span<ulong> rest, Limits lim, int start, int size)
        {
            if (size == 1)
            {
                Span<ulong> x = all.Slice(sa[start], n1), y = square ? x : all.Slice(sb[start], n1);
                Mul(core, x, y, prod, rest, lim);
                ReduceProduct(core, prod, x);
                return;
            }
            int half = size / 2;
            ForwardLevel(native, all, sa, K, n1, nBits, start, half);
            if (!square) ForwardLevel(native, all, sb, K, n1, nBits, start, half);
            Walk(core, native, all, sa, sb, square, K, n1, nBits, prod, rest, lim, start, half);
            Walk(core, native, all, sa, sb, square, K, n1, nBits, prod, rest, lim, start + half, half);
            InverseLevel(native, all, sa, K, n1, nBits, start, half);
        }

        // one level of the decimation in frequency on the block [start, start + 2·half)
        private static void ForwardLevel(bool native, Span<ulong> all, Span<int> slots, int K, int n1, int nBits, int start, int half)
        {
            int step = nBits / half;                                        // w_(2·half) = 2^(N / half)
            for (int j = 0; j < half; j++)
            {
                int iu = start + j, ix = start + j + half;
                Span<ulong> u = all.Slice(slots[iu], n1), x = all.Slice(slots[ix], n1);
                FftOps.AddSub(native, u, x);
                if (j == 0) continue;
                FftOps.ShlMod(native, x, j * step, all.Slice(slots[K], n1));
                (slots[ix], slots[K]) = (slots[K], slots[ix]);
            }
        }

        // one level of the decimation in time with w^-1 on the block (without the 1/K)
        private static void InverseLevel(bool native, Span<ulong> all, Span<int> slots, int K, int n1, int nBits, int start, int half)
        {
            int step = nBits / half;
            for (int j = 0; j < half; j++)
            {
                int iu = start + j, ix = start + j + half;
                if (j != 0)
                {
                    FftOps.ShlMod(native, all.Slice(slots[ix], n1), 2 * nBits - j * step, all.Slice(slots[K], n1));
                    (slots[ix], slots[K]) = (slots[K], slots[ix]);
                }
                FftOps.AddSub(native, all.Slice(slots[iu], n1), all.Slice(slots[ix], n1));
            }
        }
    }
}
