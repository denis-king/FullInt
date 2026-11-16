using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public static class FullHelper
    {
        // Max buffer size taken from the stack; larger buffers go to the heap.
        // Buffers sized by the input must never be stackalloc'ed unbounded:
        // a stack overflow cannot be caught and kills the process.
        public const int StackAllocLimitWords = 1024;            // 8 KB
        public const int StackAllocLimitBytes = 8 * 1024;        // 8 KB

        /// <summary>
        /// A work buffer above the stack limit: rented from ArrayPool, cut to the exact length and
        /// zeroed (like stackalloc and new). Give it back with Return when done; a buffer that is
        /// not given back (an exception on the way) is just left to the GC.
        /// </summary>
        public static Span<ulong> RentZeroed(int length, out ulong[]? rented)
        {
            rented = ArrayPool<ulong>.Shared.Rent(length);
            var span = rented.AsSpan(0, length);
            span.Clear();
            return span;
        }

        /// <summary>Byte version of RentZeroed.</summary>
        public static Span<byte> RentZeroedBytes(int length, out byte[]? rented)
        {
            rented = ArrayPool<byte>.Shared.Rent(length);
            var span = rented.AsSpan(0, length);
            span.Clear();
            return span;
        }

        public static void Return(ulong[]? rented)
        {
            if (rented != null) ArrayPool<ulong>.Shared.Return(rented);
        }

        public static void Return(byte[]? rented)
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }

        /// <summary>ASCII bytes to a string (netstandard2.0 has no Span overload of GetString).</summary>
        public static string AsciiString(ReadOnlySpan<byte> bytes)
        {
#if NETSTANDARD2_0
            if (bytes.IsEmpty) return string.Empty;
            unsafe
            {
                fixed (byte* p = bytes)
                {
                    return Encoding.ASCII.GetString(p, bytes.Length);
                }
            }
#else
            return Encoding.ASCII.GetString(bytes);
#endif
        }

        public static bool IsZero(ulong[]? value)
        {
            if (value == null) return true;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != 0) return false;
            }
            return true;
        }

        public static int CountZeros(ulong[] value)
        {
            return CountZeros(value.AsSpan());
            
        }
        public static int CountZeros(Span<ulong> value)
        {
            int zeros;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }
            if (zeros == value.Length)
            {
                zeros--;
            }
            return zeros;
        }
		
		// public static FullInt64 DeleteZeros(FullInt64 value)
        // {
            // value.value = DeleteZeros(value.value).ToArray();
            // return value;
        // }

        /// <summary>
        /// The array itself when it has no leading zero words (no allocation, no copy),
        /// otherwise a trimmed copy. Only for arrays the caller owns: the result may share them.
        /// </summary>
        public static ulong[] Trimmed(ulong[] value)
        {
            int zeros = CountZeros(value);
            return zeros == 0 ? value : value.AsSpan(zeros).ToArray();
        }

        /// <summary>
        /// The result array from a work buffer that is either on the stack (heap == null) or the
        /// heap array heap: a stack buffer is copied out once at its trimmed size, a heap buffer is
        /// kept as is unless it has leading zero words.
        /// </summary>
        public static ulong[] ResultArray(Span<ulong> buffer, ulong[]? heap) =>
            heap != null ? Trimmed(heap) : DeleteZeros(buffer).ToArray();

        /// <summary>
        /// Full 64 x 64 -> 128 product, returns the high word (netstandard2.1 has no
        /// Math.BigMul(ulong, ulong, out ulong); same 32-bit split as the managed core).
        /// </summary>
        public static ulong BigMul(ulong x, ulong y, out ulong lo)
        {
#if NET5_0_OR_GREATER
            return Math.BigMul(x, y, out lo);   // one mul on x64 (the split below: ~4x the time)
#else
            const ulong mask32 = 0xFFFFFFFF;
            ulong x0 = x & mask32, x1 = x >> 32, y0 = y & mask32, y1 = y >> 32;
            ulong z0 = x0 * y0;
            ulong mid1 = x0 * y1 + (z0 >> 32);
            ulong mid2 = x1 * y0 + (mid1 & mask32);
            lo = (mid2 << 32) | (z0 & mask32);
            return x1 * y1 + (mid1 >> 32) + (mid2 >> 32);
#endif
        }

        // A two-word divisor (GMP's mpn_divrem_2): one 3-by-2 division per quotient word with the
        // reciprocal of the normalized divisor (Moller, Granlund 2011, Algorithm 5; v as invert_pi1
        // in DivRecip.asm), the dividend shifted on the fly. a: at least 3 words, a[0] != 0;
        // b: 2 words, b[0] != 0; q: a.Length - 1 words; the remainder (rHi, rLo).
        // (fully optimized from the first call: a short-lived run left this loop in a lower JIT tier
        // and the path at 3x the time of the core's Div it replaces)
#if NETCOREAPP3_0_OR_GREATER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public static void DivBy2(Span<ulong> a, Span<ulong> b, Span<ulong> q, out ulong rHi, out ulong rLo)
        {
            int la = a.Length;
            int sh = LeadingZeroCount(b[0]);
            ulong d1 = b[0], d0 = b[1];
            if (sh != 0)
            {
                d1 = d1 << sh | d0 >> (64 - sh);
                d0 <<= sh;
            }
            ulong v = Div128(~d1, ulong.MaxValue, d1, out _);   // floor((b^2 - 1) / d1) - b
            ulong p = d1 * v + d0;
            if (p < d0)
            {
                v--;
                if (p >= d1)
                {
                    v--;
                    p -= d1;
                }
                p -= d1;
            }
            ulong t1 = BigMul(d0, v, out ulong t0);
            p += t1;
            if (p < t1)
            {
                v--;
                if (p > d1 || (p == d1 && t0 >= d0)) v--;
            }

            ulong n2 = sh == 0 ? 0 : a[0] >> (64 - sh);   // u = A << sh: its top two words
            ulong n1 = Shifted(a, 0, sh);
            for (int i = 2; i <= la; i++)
            {
                ulong n0 = Shifted(a, i - 1, sh);
                ulong qq = BigMul(v, n2, out ulong q0);
                q0 += n1;
                qq += n2 + (q0 < n1 ? 1UL : 0UL);
                ulong r1 = n1 - d1 * qq;
                ulong r0 = n0 - d0;
                r1 = r1 - d1 - (n0 < d0 ? 1UL : 0UL);
                t1 = BigMul(d0, qq, out t0);
                r1 = r1 - t1 - (r0 < t0 ? 1UL : 0UL);
                r0 -= t0;
                qq++;
                if (r1 >= q0)
                {
                    qq--;
                    ulong s = r0 + d0;
                    r1 += d1 + (s < r0 ? 1UL : 0UL);
                    r0 = s;
                }
                if (r1 >= d1 && (r1 > d1 || r0 >= d0))   // rare
                {
                    qq++;
                    r1 = r1 - d1 - (r0 < d0 ? 1UL : 0UL);
                    r0 -= d0;
                }
                q[i - 2] = qq;
                n2 = r1;
                n1 = r0;
            }
            rHi = sh != 0 ? n2 >> sh : n2;
            rLo = sh != 0 ? n1 >> sh | n2 << (64 - sh) : n1;
        }

        // word i of A << sh in la + 1 words, counted from word 1 (a[i] with the top bits of a[i + 1])
        private static ulong Shifted(Span<ulong> a, int i, int sh)
        {
            if (sh == 0) return a[i];
            ulong lo = i + 1 < a.Length ? a[i + 1] : 0;
            return a[i] << sh | lo >> (64 - sh);
        }

        /// <summary>
        /// (hi:lo) / d with hi &lt; d (the quotient fits 64 bits), the contract of x64 `div r64`.
        /// Hacker's Delight divlu, as FullInt64CoreDefault.Div128: two 64/32 steps on the normalized
        /// divisor. ~7 ns on x64 (.NET 8 and .NET Framework); UInt128 division measured 36 ns.
        /// </summary>
        public static ulong Div128(ulong hi, ulong lo, ulong d, out ulong remainder)
        {
            const ulong b = 1UL << 32;
            int s = LeadingZeroCount(d);
            ulong v = d << s, vn1 = v >> 32, vn0 = v & 0xFFFFFFFF;
            ulong un32 = s == 0 ? hi : (hi << s) | (lo >> (64 - s));
            ulong un10 = lo << s, un1 = un10 >> 32, un0 = un10 & 0xFFFFFFFF;

            ulong q1 = un32 / vn1, rhat = un32 - q1 * vn1;
            while (q1 >= b || q1 * vn0 > b * rhat + un1)
            {
                q1--;
                rhat += vn1;
                if (rhat >= b) break;
            }

            ulong un21 = un32 * b + un1 - q1 * v;
            ulong q0 = un21 / vn1;
            rhat = un21 - q0 * vn1;
            while (q0 >= b || q0 * vn0 > b * rhat + un0)
            {
                q0--;
                rhat += vn1;
                if (rhat >= b) break;
            }

            remainder = (un21 * b + un0 - q0 * v) >> s;
            return q1 * b + q0;
        }

        public static int LeadingZeroCount(ulong x)
        {
#if NET5_0_OR_GREATER
            return System.Numerics.BitOperations.LeadingZeroCount(x);
#else
            if (x == 0) return 64;
            int n = 0;
            if (x <= 0x0000_0000_FFFF_FFFFUL) { n += 32; x <<= 32; }
            if (x <= 0x0000_FFFF_FFFF_FFFFUL) { n += 16; x <<= 16; }
            if (x <= 0x00FF_FFFF_FFFF_FFFFUL) { n += 8; x <<= 8; }
            if (x <= 0x0FFF_FFFF_FFFF_FFFFUL) { n += 4; x <<= 4; }
            if (x <= 0x3FFF_FFFF_FFFF_FFFFUL) { n += 2; x <<= 2; }
            if (x <= 0x7FFF_FFFF_FFFF_FFFFUL) { n += 1; }
            return n;
#endif
        }

        public static bool IsZeroSpan(Span<ulong> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != 0) return false;
            }
            return true;
        }

        public static Span<ulong> DeleteZeros(Span<ulong> value)
        {
            //пропуск старших нулей операнда
            int zeros = CountZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.Slice(zeros);
        }
    }
}
