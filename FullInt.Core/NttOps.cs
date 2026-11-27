using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace FullNumerics
{
    /// <summary>
    /// Multiplication through number-theoretic transforms with the AVX2 kernels (NttAvx2.asm, x64 and x86)
    /// or the NEON ones (arm64/NttNeon.s, AArch64; the twiddles there in Shoup's form).
    /// The operands are cut into 32-bit digits; their cyclic convolution of length L (2^k or
    /// 3·2^k, the shortest at least the product's digits: the transform is never more than 1.5
    /// times the product) is computed modulo three primes p &lt; 2^30 with 3·2^22 | p - 1
    /// (943718401 = 225·2^22 + 1, 918552577 = 219·2^22 + 1, 880803841 = 105·2^23 + 1):
    /// forward transforms of both, pointwise products, the inverse transform, times L^-1. Every
    /// convolution term is below (L/2)·2^64 &lt;= 2^85 &lt; p1·p2·p3 (2^89.3), so the Chinese
    /// remainder (Garner) gives it exactly, and the carries put the words together. L &lt;= 2^22
    /// digits: the products up to 2^21 words. A length 3·2^k has a radix-3 level on top
    /// (R3Dif / R3Dit, their twiddles made on the way), then three transforms of 2^k.
    ///
    /// The transforms are radix-2 levels, two at a time where they can be, depth-first: above
    /// <see cref="Block"/> values a pass of the top two levels and the four quarters on their own,
    /// so the rest of the work stays in the cache. The last three levels run inside the registers.
    /// Twiddles in Montgomery form: W[h + j] = w_2h^j·2^32 mod p for every level h, and
    /// Wq = W·p^-1 mod 2^32 next to it; the inverse transform has its own tables.
    /// </summary>
    internal static class NttOps
    {
        public const int MaxLog = 22;
        private const int Block = 1 << 12;

        private static readonly uint[] Primes = { 943718401u, 918552577u, 880803841u };
        private static readonly uint[] Generators = { 7u, 5u, 26u };

        // the twiddles of one prime, for transforms up to Size values; immutable once published
        private sealed class Tables
        {
            public readonly int Size;
            public readonly uint P, PInv;
            public readonly uint[] W, Wq, IW, IWq;   // Size + 8 entries: the kernels read 4 bytes past a vector

            public Tables(int k, int size)
            {
                Size = size;
                uint p = Primes[k];
                P = p;
                uint inv = 1;
                for (int i = 0; i < 5; i++) inv *= 2 - p * inv;
                PInv = inv;
                W = new uint[size + 8]; Wq = new uint[size + 8]; IW = new uint[size + 8]; IWq = new uint[size + 8];
                // the top level h = size/2 by multiplication, the others every other entry of the next
                int h = size / 2;
                ulong w = PowMod(Generators[k], (p - 1) / (ulong)size, p);
                // w^0: in Montgomery form for the x86 kernels; AArch64 (NttNeon.s) takes the twiddles
                // in Shoup's form - w itself and floor(w·2^32 / p): a·w - hi(a·wq)·p in [0, 2p),
                // five NEON instructions against Montgomery's nine (the result is the same)
                ulong cur = Shoup ? 1UL : (1UL << 32) % p;
                for (int j = 0; j < h; j++)
                {
                    W[h + j] = (uint)cur;
                    cur = cur * w % p;
                }
                // w^-j = w^(2h - j) = -w^(h - j)
                IW[h] = W[h];
                for (int j = 1; j < h; j++) IW[h + j] = p - W[2 * h - j];
                for (int lh = h / 2; lh >= 1; lh /= 2)
                {
                    for (int j = 0; j < lh; j++)
                    {
                        W[lh + j] = W[2 * lh + 2 * j];
                        IW[lh + j] = IW[2 * lh + 2 * j];
                    }
                }
                for (int i = 1; i < size; i++)
                {
                    Wq[i] = Shoup ? (uint)(((ulong)W[i] << 32) / p) : W[i] * inv;
                    IWq[i] = Shoup ? (uint)(((ulong)IW[i] << 32) / p) : IW[i] * inv;
                }
            }
        }

        private static readonly Tables[] tables = new Tables[3];
        private static readonly object gate = new object();

        private static Tables Get(int k, int size)
        {
            Tables t = Volatile.Read(ref tables[k]);
            if (t != null && t.Size >= size) return t;
            lock (gate)
            {
                t = tables[k];
                if (t == null || t.Size < size)
                {
                    int s = Math.Max(size, 1 << 12);
                    if (t != null) s = Math.Max(s, Math.Min(2 * t.Size, 1 << MaxLog));
                    t = new Tables(k, s);
                    Volatile.Write(ref tables[k], t);
                }
                return t;
            }
        }

        private static ulong PowMod(ulong a, ulong e, ulong p)
        {
            ulong r = 1;
            a %= p;
            while (e != 0)
            {
                if ((e & 1) != 0) r = r * a % p;
                a = a * a % p;
                e >>= 1;
            }
            return r;
        }

        // Garner's constants: p1, p2, p3, p1^-1 mod p2 and its Shoup factor, p1 mod p3 and its
        // Shoup factor, (p1·p2)^-1 mod p3 and its Shoup factor, p1·p2
        private static readonly ulong[] CrtConstants = MakeCrt();

        private static ulong[] MakeCrt()
        {
            ulong p1 = Primes[0], p2 = Primes[1], p3 = Primes[2];
            ulong inv12 = PowMod(p1 % p2, p2 - 2, p2);
            ulong p1m3 = p1 % p3;
            ulong inv123 = PowMod(p1 % p3 * (p2 % p3) % p3, p3 - 2, p3);
            return new[] { p1, p2, p3, inv12, (inv12 << 32) / p2, p1m3, (p1m3 << 32) / p3, inv123, (inv123 << 32) / p3, p1 * p2 };
        }

        /// <summary>Whether a product of len words fits the transforms.</summary>
        public static bool Fits(int len) => 2L * len <= 1L << MaxLog;

        // the transform length for n digits: the shortest 2^k or 3·2^k (k >= 4) from n
        private static int Length(long n)
        {
            int L = 16;
            while (L < n) L *= 2;
            int l3 = L / 4 * 3;                      // 3·2^(k-2)
            return l3 >= n && l3 >= 48 ? l3 : L;
        }

        /// <summary>
        /// Whether the transforms pay off for a product of len words: from the threshold
        /// (<paramref name="full"/>) whenever they fit; from half of it only when the product fills
        /// the transform (0.9 of it: 512, 768 words on 2048, 3072 digits). C++ x64 (Zen 3), of
        /// GMP, the Toom levels / the transforms: 512 words 1.12 / 1.00-1.03, 768 1.13 / 0.98-1.00,
        /// but 384 (filled) 1.11 / 1.25 and 640 (0.83) 1.12 / 1.16.
        /// </summary>
        public static bool Worth(int len, bool full)
        {
            if (!Fits(len)) return false;
            long nd = 2L * len;
            return full || 10 * nd >= 9L * Length(nd);
        }

        /// <summary>r = a·b, r.Length = a.Length + b.Length (a and b the same span: a square).</summary>
        public static void Multiply(Span<ulong> a, Span<ulong> b, Span<ulong> r) =>
            Convolve(a, b, r, Length(2L * r.Length));

        /// <summary>
        /// The words of a product modulo β^m - 1 that <see cref="MultiplyCyclic"/> takes for at
        /// least minWords words: the shortest m = 2^a or 3·2^a from minWords (the transform of 2m
        /// digits wraps exactly at β^m), or 0 when that is too long.
        /// </summary>
        public static int CyclicLength(int minWords)
        {
            int m = Length(2L * Math.Max(minWords, 8)) / 2;
            return 2L * m <= 1L << MaxLog ? m : 0;
        }

        /// <summary>
        /// r = a·b mod (β^m - 1), m = r.Length from <see cref="CyclicLength"/>, in
        /// [0, β^m - 1) (β^m - 1 written as 0); a and b at most m words. The cyclic convolution of
        /// L = 2m digits wraps at 2^(32L) = β^m, the carry out of the top comes in at the bottom.
        /// </summary>
        public static void MultiplyCyclic(Span<ulong> a, Span<ulong> b, Span<ulong> r) =>
            WrapCarry(r, Convolve(a, b, r, 2 * r.Length, null));

        /// <summary>
        /// A factor used many times (the prepared divisors of ToString): its forward transforms
        /// for the three primes, computed once; a product with it then transforms only the other
        /// factor (two transforms instead of three). Full: products of Words words; cyclic:
        /// modulo β^m - 1, m = Words (from <see cref="CyclicLength"/>).
        /// </summary>
        public sealed class Image
        {
            internal readonly int L, Words;
            internal readonly bool Cyclic;
            internal readonly uint[] Data;   // 3·L values
            internal Image(int l, int words, bool cyclic, uint[] data) { L = l; Words = words; Cyclic = cyclic; Data = data; }
        }

        /// <summary>b's transforms for products of words words (full) or modulo β^words - 1 (cyclic).</summary>
        public static unsafe Image Prepare(Span<ulong> b, int words, bool cyclic)
        {
            int L = cyclic ? 2 * words : Length(2L * words);
            var data = new uint[3 * L];
            bool r3 = (L & (L - 1)) != 0;
            int M = r3 ? L / 3 : L;
            fixed (uint* pd = data)
            fixed (ulong* pb = b)
            {
                for (int k = 0; k < 3; k++)
                {
                    Tables t = Get(k, M);
                    uint[] fk = r3 ? R3Constants(k, t, L, false) : null;
                    fixed (uint* w = t.W)
                    fixed (uint* wq = t.Wq)
                    fixed (uint* pf = fk)
                    {
                        Load(pb, b.Length, pd + k * L, L, t.P);
                        Forward3(pd + k * L, M, r3, pf, w, wq, t.P);
                    }
                }
            }
            return new Image(L, words, cyclic, data);
        }

        /// <summary>r = a·b (full: r.Length = the image's Words) or a·b mod β^m - 1 (cyclic: r.Length = m).</summary>
        public static void MultiplyImage(Span<ulong> a, Image b, Span<ulong> r)
        {
            ulong carry = Convolve(a, Span<ulong>.Empty, r, b.L, b.Data);
            if (b.Cyclic) WrapCarry(r, carry);
        }

        // + carry modulo β^m - 1 (the end-around carry), β^m - 1 written as 0
        private static void WrapCarry(Span<ulong> r, ulong carry)
        {
            int m = r.Length;
            while (carry != 0)                       // + carry (mod β^m - 1): the end-around carry
            {
                ulong c = carry;
                carry = 0;
                for (int i = m - 1; i >= 0; i--)
                {
                    ulong v = r[i] + c;
                    c = v < c ? 1UL : 0UL;
                    r[i] = v;
                    if (c == 0) break;
                }
                carry = c;
            }
            foreach (ulong w in r) if (w != ulong.MaxValue) return;
            r.Clear();                               // β^m - 1: the other zero
        }

        // the cyclic convolution of a and b in L 32-bit digits (L = 2^k or 3·2^k, at least the
        // digits of both), its digits put together into the r.Length <= L/2 words of r; returns
        // the carry out of the top
        private static unsafe ulong Convolve(Span<ulong> a, Span<ulong> b, Span<ulong> r, int L, uint[]? image = null)
        {
            bool square = image == null && a == b;
            int len = r.Length, nd = 2 * len;
            uint[] buf = ArrayPool<uint>.Shared.Rent(4 * L);
            try
            {
                fixed (uint* pb = buf)
                fixed (ulong* pa = a)
                fixed (ulong* pbb = b)
                fixed (ulong* pr = r)
                fixed (ulong* pk = CrtConstants)
                fixed (uint* pimg = image)
                {
                    uint* y = pb + 3 * L;
                    bool r3 = (L & (L - 1)) != 0;            // 3·2^k: a radix-3 level on top
                    int M = r3 ? L / 3 : L;
                    for (int k = 0; k < 3; k++)
                    {
                        Tables t = Get(k, M);
                        uint* x = pb + k * L;
                        uint[] fk = r3 ? R3Constants(k, t, L, false) : null, ik = r3 ? R3Constants(k, t, L, true) : null;
                        fixed (uint* w = t.W)
                        fixed (uint* wq = t.Wq)
                        fixed (uint* iw = t.IW)
                        fixed (uint* iwq = t.IWq)
                        fixed (uint* pf = fk)
                        fixed (uint* pi = ik)
                        {
                            Load(pa, a.Length, x, L, t.P);
                            Forward3(x, M, r3, pf, w, wq, t.P);
                            if (pimg != null)
                            {
                                Point(x, pimg + k * L, L, t.P, t.PInv);   // b's transform kept
                            }
                            else if (square)
                            {
                                Point(x, x, L, t.P, t.PInv);
                            }
                            else
                            {
                                Load(pbb, b.Length, y, L, t.P);
                                Forward3(y, M, r3, pf, w, wq, t.P);
                                Point(x, y, L, t.P, t.PInv);
                            }
                            Inverse3(x, M, r3, pi, iw, iwq, t.P);
                            // × 2^32·L^-1 (a Montgomery product by 2^64·L^-1): the pointwise
                            // products left one 2^-32
                            ulong c = (1UL << 32) % t.P;
                            c = c * c % t.P * PowMod((ulong)L, t.P - 2, t.P) % t.P;
                            Scale(x, (nd + 7) & ~7, t.P, (uint)c, (uint)c * t.PInv);
                        }
                    }
                    return Crt(pb, pb + L, pb + 2 * L, nd, pr, len, pk);
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(buf);
            }
        }

        // a transform of M (r3: 3M) values: the radix-3 level first, then three of M
        private static unsafe void Forward3(uint* x, int M, bool r3, uint* k, uint* w, uint* wq, uint p)
        {
            if (!r3)
            {
                Forward(x, M, w, wq, p);
                return;
            }
            R3Dif(x, M, k, p);
            for (int i = 0; i < 3; i++) Forward(x + i * M, M, w, wq, p);
        }

        private static unsafe void Inverse3(uint* x, int M, bool r3, uint* k, uint* w, uint* wq, uint p)
        {
            if (!r3)
            {
                Inverse(x, M, w, wq, p);
                return;
            }
            for (int i = 0; i < 3; i++) Inverse(x + i * M, M, w, wq, p);
            R3Dit(x, M, k, p);
        }

        // the radix-3 level's constants for N = 3M (NttAvx2.asm): w = w_N (inverse: w^-1),
        // ω = w^M; nine vectors: w^0..7, w^0,2,..,14, w^8 and its q, w^16 and its q, ω and its q,
        // p^-1 (Montgomery form: ·2^32 mod p; q = ·p^-1 mod 2^32)
        private static uint[] R3Constants(int k, Tables t, int n, bool inverse)
        {
            ulong p = t.P, r = (1UL << 32) % p;
            ulong w = PowMod(Generators[k], (p - 1) / (ulong)n, p);
            if (inverse) w = PowMod(w, p - 2, p);
            uint Mont(ulong v) => (uint)(v * r % p);
            var c = new uint[72];
            ulong wj = 1, w2 = w * w % p, w2j = 1;
            for (int j = 0; j < 8; j++)
            {
                c[j] = Mont(wj);
                c[8 + j] = Mont(w2j);
                wj = wj * w % p;
                w2j = w2j * w2 % p;
            }
            uint s1 = Mont(wj), s2 = Mont(w2j), om = Mont(PowMod(w, (ulong)(n / 3), p));
            for (int j = 0; j < 8; j++)
            {
                c[16 + j] = s1; c[24 + j] = s1 * t.PInv;
                c[32 + j] = s2; c[40 + j] = s2 * t.PInv;
                c[48 + j] = om; c[56 + j] = om * t.PInv;
                c[64 + j] = t.PInv;
            }
            return c;
        }

        // decimation in frequency, depth-first
        private static unsafe void Forward(uint* x, int L, uint* w, uint* wq, uint p)
        {
            if (L > Block)
            {
                Dif2(x, L, L / 2, w, wq, p);
                int q = L / 4;
                for (int i = 0; i < 4; i++) Forward(x + i * q, q, w, wq, p);
                return;
            }
            int h = L / 2;
            for (; h >= 16; h /= 4) Dif2(x, L, h, w, wq, p);
            if (h == 8) Dif1(x, L, 8, w, wq, p);
            Dif3(x, L, w, wq, p);
        }

        // decimation in time with the inverse twiddles, Forward backwards (without the L^-1)
        private static unsafe void Inverse(uint* x, int L, uint* w, uint* wq, uint p)
        {
            if (L > Block)
            {
                int q = L / 4;
                for (int i = 0; i < 4; i++) Inverse(x + i * q, q, w, wq, p);
                Dit2(x, L, L / 2, w, wq, p);
                return;
            }
            Dit3(x, L, w, wq, p);
            int h = 8;
            while (h < L)
            {
                if (2 * h < L)
                {
                    Dit2(x, L, 2 * h, w, wq, p);
                    h *= 4;
                }
                else
                {
                    Dit1(x, L, h, w, wq, p);
                    h *= 2;
                }
            }
        }

        // the kernels of the process's library (x64/NttAvx2.asm, x86/NttAvx2.asm: the same functions;
        // AArch64: arm64/NttNeon.s, the same contracts, the twiddles in Shoup's form); reached only
        // through Multiply, which the dispatcher calls for a core with the kernels
        private static readonly bool X64 = Environment.Is64BitProcess;
        private static readonly bool Arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        // ARMv7 (arm32/NttNeon.s, the same contracts, in libFullIntOperations_linux_arm.so: the 32-bit name)
        private static readonly bool Arm32 = RuntimeInformation.ProcessArchitecture == Architecture.Arm;
        // the radix-2 twiddles in Shoup's form on both ARMs (NEON's five-instruction product)
        private static readonly bool Shoup = Arm || Arm32;
        private static unsafe void Dif1(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDif1WIN_neon(x, L, h, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDif1WIN_neon(x, L, h, w, wq, p); else if (X64) Native64.Fullint64NttDif1WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDif1WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dif2(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDif2WIN_neon(x, L, h, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDif2WIN_neon(x, L, h, w, wq, p); else if (X64) Native64.Fullint64NttDif2WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDif2WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dif3(uint* x, long L, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDif3WIN_neon(x, L, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDif3WIN_neon(x, L, w, wq, p); else if (X64) Native64.Fullint64NttDif3WIN_avx2(x, L, w, wq, p); else Native86.Fullint64NttDif3WIN_avx2(x, L, w, wq, p); }
        private static unsafe void Dit1(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDit1WIN_neon(x, L, h, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDit1WIN_neon(x, L, h, w, wq, p); else if (X64) Native64.Fullint64NttDit1WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDit1WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dit2(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDit2WIN_neon(x, L, h, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDit2WIN_neon(x, L, h, w, wq, p); else if (X64) Native64.Fullint64NttDit2WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDit2WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dit3(uint* x, long L, uint* w, uint* wq, uint p) { if (Arm) NativeArm.Fullint64NttDit3WIN_neon(x, L, w, wq, p); else if (Arm32) NativeArm32.Fullint64NttDit3WIN_neon(x, L, w, wq, p); else if (X64) Native64.Fullint64NttDit3WIN_avx2(x, L, w, wq, p); else Native86.Fullint64NttDit3WIN_avx2(x, L, w, wq, p); }
        private static unsafe void R3Dif(uint* x, long M, uint* k, uint p) { if (Arm) NativeArm.Fullint64NttR3DifWIN_neon(x, M, k, p); else if (Arm32) NativeArm32.Fullint64NttR3DifWIN_neon(x, M, k, p); else if (X64) Native64.Fullint64NttR3DifWIN_avx2(x, M, k, p); else Native86.Fullint64NttR3DifWIN_avx2(x, M, k, p); }
        private static unsafe void R3Dit(uint* x, long M, uint* k, uint p) { if (Arm) NativeArm.Fullint64NttR3DitWIN_neon(x, M, k, p); else if (Arm32) NativeArm32.Fullint64NttR3DitWIN_neon(x, M, k, p); else if (X64) Native64.Fullint64NttR3DitWIN_avx2(x, M, k, p); else Native86.Fullint64NttR3DitWIN_avx2(x, M, k, p); }
        private static unsafe void Point(uint* x, uint* y, long n, uint p, uint pinv) { if (Arm) NativeArm.Fullint64NttPointWIN_neon(x, y, n, p, pinv); else if (Arm32) NativeArm32.Fullint64NttPointWIN_neon(x, y, n, p, pinv); else if (X64) Native64.Fullint64NttPointWIN_avx2(x, y, n, p, pinv); else Native86.Fullint64NttPointWIN_avx2(x, y, n, p, pinv); }
        private static unsafe void Scale(uint* x, long n, uint p, uint c, uint cq) { if (Arm) NativeArm.Fullint64NttScaleWIN_neon(x, n, p, c, cq); else if (Arm32) NativeArm32.Fullint64NttScaleWIN_neon(x, n, p, c, cq); else if (X64) Native64.Fullint64NttScaleWIN_avx2(x, n, p, c, cq); else Native86.Fullint64NttScaleWIN_avx2(x, n, p, c, cq); }
        private static unsafe void Load(ulong* a, long la, uint* x, long L, uint p) { if (Arm) NativeArm.Fullint64NttLoadWIN_neon(a, la, x, L, p); else if (Arm32) NativeArm32.Fullint64NttLoadWIN_neon(a, la, x, L, p); else if (X64) Native64.Fullint64NttLoadWIN_avx2(a, la, x, L, p); else Native86.Fullint64NttLoadWIN_avx2(a, la, x, L, p); }
        private static unsafe ulong Crt(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k) { if (Arm) return NativeArm.Fullint64NttCrtWIN_neon(x0, x1, x2, nd, r, len, k); if (Arm32) return NativeArm32.Fullint64NttCrtWIN_neon(x0, x1, x2, nd, r, len, k); if (X64) return Native64.Fullint64NttCrtWIN(x0, x1, x2, nd, r, len, k); else return Native86.Fullint64NttCrtWIN(x0, x1, x2, nd, r, len, k); }

        private static unsafe class Native64
        {
            private const string Dll = NativeNames.Lib64;
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif1WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif2WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif3WIN_avx2(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit1WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit2WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit3WIN_avx2(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DifWIN_avx2(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DitWIN_avx2(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttPointWIN_avx2(uint* x, uint* y, long n, uint p, uint pinv);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttScaleWIN_avx2(uint* x, long n, uint p, uint c, uint cq);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttLoadWIN_avx2(ulong* a, long la, uint* x, long L, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern ulong Fullint64NttCrtWIN(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k);
        }

        // AArch64 (arm64/NttNeon.s in libFullIntOperations_linux_arm64.so, reached under the 64-bit name:
        // NativeLibraryResolver)
        private static unsafe class NativeArm
        {
            private const string Dll = NativeNames.Lib64;
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif1WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif2WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif3WIN_neon(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit1WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit2WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit3WIN_neon(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DifWIN_neon(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DitWIN_neon(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttPointWIN_neon(uint* x, uint* y, long n, uint p, uint pinv);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttScaleWIN_neon(uint* x, long n, uint p, uint c, uint cq);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttLoadWIN_neon(ulong* a, long la, uint* x, long L, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern ulong Fullint64NttCrtWIN_neon(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k);
        }

        // ARMv7 (arm32/NttNeon.s in libFullIntOperations_linux_arm.so, the 32-bit name)
        private static unsafe class NativeArm32
        {
            private const string Dll = NativeNames.Lib32;
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif1WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif2WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif3WIN_neon(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit1WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit2WIN_neon(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit3WIN_neon(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DifWIN_neon(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DitWIN_neon(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttPointWIN_neon(uint* x, uint* y, long n, uint p, uint pinv);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttScaleWIN_neon(uint* x, long n, uint p, uint c, uint cq);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttLoadWIN_neon(ulong* a, long la, uint* x, long L, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern ulong Fullint64NttCrtWIN_neon(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k);
        }

        private static unsafe class Native86
        {
            private const string Dll = NativeNames.Lib32;
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif1WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif2WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDif3WIN_avx2(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit1WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit2WIN_avx2(uint* x, long L, long h, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttDit3WIN_avx2(uint* x, long L, uint* w, uint* wq, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DifWIN_avx2(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttR3DitWIN_avx2(uint* x, long M, uint* k, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttPointWIN_avx2(uint* x, uint* y, long n, uint p, uint pinv);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttScaleWIN_avx2(uint* x, long n, uint p, uint c, uint cq);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttLoadWIN_avx2(ulong* a, long la, uint* x, long L, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern ulong Fullint64NttCrtWIN(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k);
        }
    }
}
