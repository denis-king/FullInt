using System;
using System.Buffers;

namespace FullInt.Core
{
    public static partial class Karatsuba
    {
        /// <summary>
        /// From this many words in the shorter operand (and when the operands are not too
        /// unbalanced), the product goes through the Schönhage–Strassen FFT (Fft.cs) instead of
        /// Toom levels. Tuned with `FullInt.Benchmarks kara fft` (Zen 3): 0.8-0.95 of the Toom
        /// levels from 32768 words, 0.6 at 131072; up to 16384 no gain (the butterflies, several
        /// core calls each, are the cost so far).
        /// </summary>
        public static int FftThreshold { get; set; } = 32768;

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
        private static void FftMul(IFullIntCore core, Span<ulong> a, Span<ulong> b, Span<ulong> r,
            int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold, int forceK)
        {
            int la = a.Length, lb = b.Length, len = la + lb;
            ChooseFft(la, lb, forceK, out int k, out int m, out int nBits);
            int K = 1 << k, w = nBits / 64, n1 = w + 1;

            ulong[] buf = ArrayPool<ulong>.Shared.Rent(2 * K * n1 + 4 * n1 + 4 + ScratchLength(n1, n1));
            try
            {
                Span<ulong> all = buf;
                Span<ulong> va = all.Slice(0, K * n1), vb = all.Slice(K * n1, K * n1);
                int o = 2 * K * n1;
                Span<ulong> p = all.Slice(o, n1), t1 = all.Slice(o + n1, n1), t2 = all.Slice(o + 2 * n1, n1 + 1);
                Span<ulong> prod = all.Slice(o + 3 * n1 + 1, 2 * n1);
                Span<ulong> rest = all.Slice(o + 5 * n1 + 1);
                p.Clear();
                p[0] = 1;
                p[n1 - 1] = 1;           // p = 2^N + 1

                Split(a, m, K, n1, va);
                Split(b, m, K, n1, vb);

                var ctx = new FftContext(core, k, nBits, n1, p, t1, t2);
                ForwardDif(ref ctx, va);
                ForwardDif(ref ctx, vb);

                for (int i = 0; i < K; i++)
                {
                    Span<ulong> x = va.Slice(i * n1, n1), y = vb.Slice(i * n1, n1);
                    Mul(core, x, y, prod, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
                    ReduceProduct(ref ctx, prod, x);
                }

                InverseDit(ref ctx, va);

                // r = sum (c_i / K)·B^(M·i)
                r.Clear();
                for (int i = 0; i < K && i * m < len; i++)
                {
                    Span<ulong> c = va.Slice(i * n1, n1);
                    MulPow2Mod(ref ctx, c, 2 * nBits - k, t1);         // / K
                    Span<ulong> v = FullHelper.DeleteZeros(t1);
                    if (v.Length == 1 && v[0] == 0) continue;
                    Span<ulong> up = r.Slice(0, len - i * m);
                    core.Add(up, v, up);
                }
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(buf);
            }
        }

        private ref struct FftContext
        {
            public readonly IFullIntCore Core;
            public readonly int K, LogK, NBits, N1;
            public readonly Span<ulong> P, T1, T2;
            public FftContext(IFullIntCore core, int logK, int nBits, int n1, Span<ulong> p, Span<ulong> t1, Span<ulong> t2)
            {
                Core = core; LogK = logK; K = 1 << logK; NBits = nBits; N1 = n1; P = p; T1 = t1; T2 = t2;
            }
        }

        // k, M, N for an la x lb product: the smallest estimated cost (or forceK = k for tests)
        private static void ChooseFft(int la, int lb, int forceK, out int k, out int m, out int nBits)
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
                // ns, measured (Zen 3): an n-word product ~2.6·n^1.55 from 32 words, a butterfly
                // ~150 + 5·n (several core calls)
                double mulCost = n1 < 32 ? 0.5 * n1 * n1 : 2.6 * Math.Pow(n1, 1.55);
                double cost = K * mulCost + 3.0 * K * kk * (150 + 5.0 * n1);
                if (cost < best || forceK > 0)
                {
                    best = cost; k = kk; m = mm; nBits = (int)nb;
                }
                if (mm == 1) break;
            }
        }

        // the pieces of x (M words each, from the low end) into K vectors of n1 words, zero-padded
        private static void Split(Span<ulong> x, int m, int K, int n1, Span<ulong> v)
        {
            v.Clear();
            int lx = x.Length;
            for (int i = 0; i < K; i++)
            {
                int end = lx - i * m;
                if (end <= 0) break;
                int start = Math.Max(0, end - m);
                Span<ulong> piece = x.Slice(start, end - start);
                piece.CopyTo(v.Slice((i + 1) * n1 - piece.Length, piece.Length));
            }
        }

        // dst = (x + y) mod p   (x, y in [0, p))
        private static void AddMod(ref FftContext c, Span<ulong> x, Span<ulong> y, Span<ulong> dst)
        {
            c.Core.Add(x, y, dst);
            if (dst[0] != 0 && c.Core.Compare(dst, c.P) >= 0) c.Core.Sub(dst, c.P, dst);
        }

        // dst = (x - y) mod p
        private static void SubMod(ref FftContext c, Span<ulong> x, Span<ulong> y, Span<ulong> dst)
        {
            if (c.Core.Sub(x, y, dst) < 0) c.Core.Sub(c.P, dst, dst);
        }

        // dst = x·2^s mod p, 0 <= s < 2N; dst must not be x
        private static void MulPow2Mod(ref FftContext c, Span<ulong> x, int s, Span<ulong> dst)
        {
            int n1 = c.N1, w = n1 - 1;
            bool negate = false;
            if (s >= c.NBits) { s -= c.NBits; negate = true; }
            int ws = s >> 6, bs = s & 63;
            Span<ulong> t = c.T2;                                   // n1 + 1 words: x << bs
            if (bs == 0) { t[0] = 0; x.CopyTo(t.Slice(1)); }
            else c.Core.ShiftLeft(x, bs, t);
            // x·2^s = t·B^ws = t_lo·B^ws + t_hi·2^N == t_lo·B^ws - t_hi
            //   t_lo: the low w - ws words of t, t_hi: the ws + 2 words above them
            dst[0] = 0;
            t.Slice(ws + 2, w - ws).CopyTo(dst.Slice(1, w - ws));
            dst.Slice(1 + w - ws).Clear();
            Span<ulong> hi = FullHelper.DeleteZeros(t.Slice(0, ws + 2));
            if (!(hi.Length == 1 && hi[0] == 0) && c.Core.Sub(dst, hi, dst) < 0)
                c.Core.Sub(c.P, dst, dst);
            if (negate && !FullHelper.IsZeroSpan(dst))
                c.Core.Sub(c.P, dst, dst);
        }

        // x = prod mod p (prod: 2·n1 words, < 2^(2N) + 2^(N+1) + 1)
        private static void ReduceProduct(ref FftContext c, Span<ulong> prod, Span<ulong> x)
        {
            int n1 = c.N1, w = n1 - 1;
            // prod = hi·2^N + lo, lo: the low w words
            x[0] = 0;
            prod.Slice(2 * n1 - w, w).CopyTo(x.Slice(1));
            Span<ulong> hi = FullHelper.DeleteZeros(prod.Slice(0, 2 * n1 - w));
            if (!(hi.Length == 1 && hi[0] == 0) && c.Core.Sub(x, hi, x) < 0)
                c.Core.Sub(c.P, x, x);
        }

        // decimation in frequency, natural order in, bit-reversed out
        private static void ForwardDif(ref FftContext c, Span<ulong> v)
        {
            int K = c.K, n1 = c.N1;
            for (int half = K / 2; half >= 1; half /= 2)
            {
                int step = c.NBits / half;                           // w_(2·half) = 2^(N / half)
                for (int start = 0; start < K; start += 2 * half)
                    for (int j = 0; j < half; j++)
                    {
                        Span<ulong> u = v.Slice((start + j) * n1, n1), x = v.Slice((start + j + half) * n1, n1);
                        SubMod(ref c, u, x, c.T1);
                        AddMod(ref c, u, x, u);
                        if (j == 0) c.T1.CopyTo(x);
                        else MulPow2Mod(ref c, c.T1, j * step, x);
                    }
            }
        }

        // decimation in time with w^-1, bit-reversed order in, natural out (without the 1/K)
        private static void InverseDit(ref FftContext c, Span<ulong> v)
        {
            int K = c.K, n1 = c.N1;
            for (int half = 1; half < K; half *= 2)
            {
                int step = c.NBits / half;
                for (int start = 0; start < K; start += 2 * half)
                    for (int j = 0; j < half; j++)
                    {
                        Span<ulong> u = v.Slice((start + j) * n1, n1), x = v.Slice((start + j + half) * n1, n1);
                        if (j == 0) x.CopyTo(c.T1);
                        else MulPow2Mod(ref c, x, 2 * c.NBits - j * step, c.T1);
                        SubMod(ref c, u, c.T1, x);
                        AddMod(ref c, u, c.T1, u);
                    }
            }
        }
    }
}
