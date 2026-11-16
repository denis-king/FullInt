using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace FullInt.Core
{
    /// <summary>
    /// Multiplication through number-theoretic transforms with the AVX2 kernels (NttAvx2.asm, x64 and x86).
    /// The operands are cut into 32-bit digits; their cyclic convolution of length L (a power of
    /// two, at least the product's digits) is computed modulo three primes p &lt; 2^30
    /// (998244353 = 119·2^23 + 1, 985661441 = 235·2^22 + 1, 943718401 = 225·2^22 + 1):
    /// forward transforms of both, pointwise products, the inverse transform, times L^-1. Every
    /// convolution term is below L·2^64 &lt;= 2^86 &lt; p1·p2·p3 (2^89.6), so the Chinese remainder
    /// (Garner) gives it exactly, and the carries put the words together. L &lt;= 2^22 digits: the
    /// products up to 2^21 words.
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

        private static readonly uint[] Primes = { 998244353u, 985661441u, 943718401u };
        private static readonly uint[] Generators = { 3u, 3u, 7u };

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
                ulong cur = (1UL << 32) % p;                 // w^0 in Montgomery form
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
                    Wq[i] = W[i] * inv;
                    IWq[i] = IW[i] * inv;
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

        /// <summary>
        /// Whether the transforms pay off for a product of len words: they fit, and the product
        /// fills at least 0.6 of the transform length unless the operands are large. The length
        /// doubles past every power of two, and just past one (the product half the transform)
        /// the Toom levels are as fast at a few thousand words (C++ x64, Zen 3: 1100 words 1.11 of
        /// them, 1280 words 0.90; from ~4000 words the transforms win at any fill: 4400 0.78).
        /// </summary>
        public static bool Worth(int len, bool large)
        {
            if (!Fits(len)) return false;
            long nd = 2L * len, L = 16;
            while (L < nd) L *= 2;
            return large || 5 * nd >= 3 * L;
        }

        /// <summary>r = a·b, r.Length = a.Length + b.Length (a and b the same span: a square).</summary>
        public static void Multiply(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            int nd = 2 * r.Length, L = 16;
            while (L < nd) L *= 2;
            Convolve(a, b, r, L);
        }

        /// <summary>
        /// The words of a product modulo β^m - 1 that <see cref="MultiplyCyclic"/> takes for at
        /// least minWords words: the power of two from minWords (the transform of 2m digits wraps
        /// exactly at β^m), or 0 when that is too long.
        /// </summary>
        public static int CyclicLength(int minWords)
        {
            int m = 8;
            while (m < minWords) m *= 2;
            return 2L * m <= 1L << MaxLog ? m : 0;
        }

        /// <summary>
        /// r = a·b mod (β^m - 1), m = r.Length a power of two from <see cref="CyclicLength"/>, in
        /// [0, β^m - 1) (β^m - 1 written as 0); a and b at most m words. The cyclic convolution of
        /// L = 2m digits wraps at 2^(32L) = β^m, the carry out of the top comes in at the bottom.
        /// </summary>
        public static void MultiplyCyclic(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            int m = r.Length;
            ulong carry = Convolve(a, b, r, 2 * m);
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

        // the cyclic convolution of a and b in L 32-bit digits (L a power of two, at least the
        // digits of both), its digits put together into the r.Length = L/2 words of r or fewer;
        // returns the carry out of the top
        private static unsafe ulong Convolve(Span<ulong> a, Span<ulong> b, Span<ulong> r, int L)
        {
            bool square = a == b;
            int len = r.Length, nd = 2 * len;
            uint[] buf = ArrayPool<uint>.Shared.Rent(4 * L);
            try
            {
                fixed (uint* pb = buf)
                fixed (ulong* pa = a)
                fixed (ulong* pbb = b)
                fixed (ulong* pr = r)
                fixed (ulong* pk = CrtConstants)
                {
                    uint* y = pb + 3 * L;
                    for (int k = 0; k < 3; k++)
                    {
                        Tables t = Get(k, L);
                        uint* x = pb + k * L;
                        fixed (uint* w = t.W)
                        fixed (uint* wq = t.Wq)
                        fixed (uint* iw = t.IW)
                        fixed (uint* iwq = t.IWq)
                        {
                            Load(pa, a.Length, x, L, t.P);
                            Forward(x, L, w, wq, t.P);
                            if (square)
                            {
                                Point(x, x, L, t.P, t.PInv);
                            }
                            else
                            {
                                Load(pbb, b.Length, y, L, t.P);
                                Forward(y, L, w, wq, t.P);
                                Point(x, y, L, t.P, t.PInv);
                            }
                            Inverse(x, L, iw, iwq, t.P);
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

        // the kernels of the process's DLL (x64/NttAvx2.asm, x86/NttAvx2.asm: the same functions);
        // reached only through Multiply, which the dispatcher calls for a core with the kernels
        private static readonly bool X64 = Environment.Is64BitProcess;
        private static unsafe void Dif1(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDif1WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDif1WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dif2(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDif2WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDif2WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dif3(uint* x, long L, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDif3WIN_avx2(x, L, w, wq, p); else Native86.Fullint64NttDif3WIN_avx2(x, L, w, wq, p); }
        private static unsafe void Dit1(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDit1WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDit1WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dit2(uint* x, long L, long h, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDit2WIN_avx2(x, L, h, w, wq, p); else Native86.Fullint64NttDit2WIN_avx2(x, L, h, w, wq, p); }
        private static unsafe void Dit3(uint* x, long L, uint* w, uint* wq, uint p) { if (X64) Native64.Fullint64NttDit3WIN_avx2(x, L, w, wq, p); else Native86.Fullint64NttDit3WIN_avx2(x, L, w, wq, p); }
        private static unsafe void Point(uint* x, uint* y, long n, uint p, uint pinv) { if (X64) Native64.Fullint64NttPointWIN_avx2(x, y, n, p, pinv); else Native86.Fullint64NttPointWIN_avx2(x, y, n, p, pinv); }
        private static unsafe void Scale(uint* x, long n, uint p, uint c, uint cq) { if (X64) Native64.Fullint64NttScaleWIN_avx2(x, n, p, c, cq); else Native86.Fullint64NttScaleWIN_avx2(x, n, p, c, cq); }
        private static unsafe void Load(ulong* a, long la, uint* x, long L, uint p) { if (X64) Native64.Fullint64NttLoadWIN_avx2(a, la, x, L, p); else Native86.Fullint64NttLoadWIN_avx2(a, la, x, L, p); }
        private static unsafe ulong Crt(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k) { if (X64) return Native64.Fullint64NttCrtWIN(x0, x1, x2, nd, r, len, k); else return Native86.Fullint64NttCrtWIN(x0, x1, x2, nd, r, len, k); }

        private static unsafe class Native64
        {
            private const string Dll = "FullIntOperations_win_x64.dll";
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
            public static extern void Fullint64NttPointWIN_avx2(uint* x, uint* y, long n, uint p, uint pinv);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttScaleWIN_avx2(uint* x, long n, uint p, uint c, uint cq);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Fullint64NttLoadWIN_avx2(ulong* a, long la, uint* x, long L, uint p);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
            public static extern ulong Fullint64NttCrtWIN(uint* x0, uint* x1, uint* x2, long nd, ulong* r, long len, ulong* k);
        }

        private static unsafe class Native86
        {
            private const string Dll = "FullIntOperations_win_x86.dll";
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
