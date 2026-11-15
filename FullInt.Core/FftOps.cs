using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    /// <summary>
    /// The two halves of a Schönhage–Strassen butterfly modulo p = 2^N + 1, N = 64·(n1 - 1),
    /// on values of n1 words (big-endian) in [0, 2^N]. The native kernels (x64 FftAdx.asm, BMI2
    /// + ADX) when the x64 core runs on such a CPU, otherwise the same steps in managed code.
    /// </summary>
    internal static class FftOps
    {
        /// <summary>u, v = u + v, u - v (mod p), in place.</summary>
        public static void AddSub(bool native, Span<ulong> u, Span<ulong> v)
        {
            if (native) { Native.AddSub(u, v); return; }
            int n1 = u.Length, w = n1 - 1;
            ulong carry = 0, borrow = 0;
            for (int k = w; k >= 0; k--)
            {
                ulong a = u[k], b = v[k];
                ulong s = a + b;
                ulong c = s < a ? 1UL : 0UL;
                ulong s2 = s + carry;
                c |= s2 < s ? 1UL : 0UL;
                ulong d = a - b;
                ulong br = a < b ? 1UL : 0UL;
                ulong d2 = d - borrow;
                br |= d < borrow ? 1UL : 0UL;
                u[k] = s2;
                v[k] = d2;
                carry = c;
                borrow = br;
            }
            // the sum: top word t > 0 -> low - t (+ p on a borrow)
            ulong t = u[0];
            if (t != 0)
            {
                u[0] = 0;
                ulong bw = t;
                for (int k = w; k >= 1 && bw != 0; k--)
                {
                    ulong x = u[k];
                    u[k] = x - bw;
                    bw = x < bw ? 1UL : 0UL;
                }
                if (bw != 0) LowPlusOne(u);
            }
            if (borrow != 0) LowPlusOne(v);   // a negative difference: + p
        }

        /// <summary>dst = x·2^s mod p, 0 &lt;= s &lt; 2N; dst must not be x.</summary>
        public static void ShlMod(bool native, Span<ulong> x, int s, Span<ulong> dst)
        {
            if (native) { Native.ShlMod(x, dst, s); return; }
            int n1 = x.Length, w = n1 - 1, nBits = 64 * w;
            bool negate = false;
            if (s >= nBits) { s -= nBits; negate = true; }
            int ws = s >> 6, bs = s & 63;
            ulong borrow = 0;
            for (int i = 0; i < w; i++)
            {
                ulong l = i < ws ? 0 : Sh(x, w, i - ws, bs);
                ulong h = i < ws ? Sh(x, w, i + w - ws, bs) : i == ws ? Sh(x, w, w, bs) : 0;
                if (negate) { ulong tmp = l; l = h; h = tmp; }
                ulong d = l - h;
                ulong br = l < h ? 1UL : 0UL;
                ulong d2 = d - borrow;
                br |= d < borrow ? 1UL : 0UL;
                dst[w - i] = d2;
                borrow = br;
            }
            dst[0] = 0;
            if (borrow != 0) LowPlusOne(dst);
        }

        // the x64 kernels (FftAdx.asm), in a class of their own and behind calls that are not
        // inlined: compiling AddSub / ShlMod must not bind the x64 DLL in a 32-bit process.
        private static class Native
        {
            private const string Dll = "FullIntOperations_win_x64.dll";

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
#if NET5_0_OR_GREATER
            [SuppressGCTransition]   // one pass over a vector of at most a few thousand words
#endif
            private static extern void Fullint64FftAddSubWIN_adx(IntPtr u, IntPtr v, int n1);

            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
#if NET5_0_OR_GREATER
            [SuppressGCTransition]
#endif
            private static extern void Fullint64FftShlModWIN_bmi2(IntPtr x, IntPtr dst, int n1, int s);

            // not inlined: a SuppressGCTransition call is bound when its caller is compiled
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static unsafe void AddSub(Span<ulong> u, Span<ulong> v)
            {
                fixed (ulong* pu = u)
                fixed (ulong* pv = v)
                    Fullint64FftAddSubWIN_adx((IntPtr)pu, (IntPtr)pv, u.Length);
            }

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static unsafe void ShlMod(Span<ulong> x, Span<ulong> dst, int s)
            {
                fixed (ulong* px = x)
                fixed (ulong* pd = dst)
                    Fullint64FftShlModWIN_bmi2((IntPtr)px, (IntPtr)pd, x.Length, s);
            }
        }

        // word j (from the low end) of x << bs; x word j is x[w - j]
        private static ulong Sh(Span<ulong> x, int w, int j, int bs)
        {
            ulong lo = x[w - j] << bs;
            if (bs != 0 && j > 0) lo |= x[w - j + 1] >> (64 - bs);
            return lo;
        }

        // the low part + 1, the top word = 0 or the carry out
        private static void LowPlusOne(Span<ulong> v)
        {
            v[0] = 0;
            for (int k = v.Length - 1; k >= 1; k--)
            {
                if (++v[k] != 0) return;
            }
            v[0] = 1;
        }
    }
}
