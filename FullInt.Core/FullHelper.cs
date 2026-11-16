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

        // 2 x 1 and 2 x 2 words here (words big-endian), no core call: its entry cost more than the three /
        // four multiplies (C++ FullHelper::Mul2x1 / Mul2x2 / Sqr2). r: a.Length + b.Length words, written
        // whole. false: longer operands, r untouched. x64 .NET 5+ only (one mul a BigMul; x64 .NET 8, 2 x 2:
        // 9.3 ns, the core 15); the 32-bit split of netstandard2.0 lost to the core (.NET Framework 43 / 40).
#if NETCOREAPP3_0_OR_GREATER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
#endif
        public static bool MultShort(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
#if !NET5_0_OR_GREATER
            return false;
#else
            if (IntPtr.Size != 8) return false;   // x86: Math.BigMul is a 32-bit split too
            if (a.Length + b.Length == 3)
            {
                if (a.Length == 1) { var t = a; a = b; b = t; }
                ulong h0 = BigMul(a[1], b[0], out ulong l0);
                ulong h1 = BigMul(a[0], b[0], out ulong l1);
                ulong s = h0 + l1;
                r[0] = h1 + (s < l1 ? 1UL : 0UL);
                r[1] = s;
                r[2] = l0;
                return true;
            }
            if (a.Length != 2 || b.Length != 2) return false;
            ulong h00 = BigMul(a[1], b[1], out ulong l00);
            ulong h01 = BigMul(a[1], b[0], out ulong l01);
            ulong h10 = BigMul(a[0], b[1], out ulong l10);
            ulong h11 = BigMul(a[0], b[0], out ulong l11);
            ulong w1 = h00 + l01;
            ulong c = w1 < l01 ? 1UL : 0UL;
            ulong w2 = h01 + l11;
            ulong c2 = w2 < l11 ? 1UL : 0UL;
            w2 += c;
            c2 += w2 < c ? 1UL : 0UL;
            ulong w3 = h11 + c2;
            w1 += l10;
            c = w1 < l10 ? 1UL : 0UL;
            w2 += h10;
            c2 = w2 < h10 ? 1UL : 0UL;
            w2 += c;
            c2 += w2 < c ? 1UL : 0UL;
            w3 += c2;
            r[0] = w3; r[1] = w2; r[2] = w1; r[3] = l00;
            return true;
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
            ulong v = Reciprocal3By2(d1, d0);

            ulong n2 = sh == 0 ? 0 : a[0] >> (64 - sh);   // u = A << sh: its top two words
            ulong n1 = Shifted(a, 0, sh);
            for (int i = 2; i <= la; i++)
            {
                q[i - 2] = Div3By2(n2, n1, Shifted(a, i - 1, sh), d1, d0, v, out n2, out n1);
            }
            rHi = sh != 0 ? n2 >> sh : n2;
            rLo = sh != 0 ? n1 >> sh | n2 << (64 - sh) : n1;
        }

        // Division through the reciprocal of a normalized divisor (Moller, Granlund 2011): the quotient word
        // from one product and a few corrections instead of a hardware 128 / 64 division.

        /// <summary>v = floor((b^2 - 1) / d) - b for a normalized d (top bit set), b = 2^64.</summary>
        public static ulong Reciprocal2By1(ulong d) => Div128(~d, ulong.MaxValue, d, out _);

        /// <summary>(u1:u0) / d, u1 &lt; d, d normalized, v = Reciprocal2By1(d) (Algorithm 4).</summary>
#if NETCOREAPP3_0_OR_GREATER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public static ulong Div2By1(ulong u1, ulong u0, ulong d, ulong v, out ulong r)
        {
            ulong q1 = BigMul(v, u1, out ulong q0);
            q0 += u0;
            q1 += u1 + (q0 < u0 ? 1UL : 0UL) + 1;
            ulong rr = u0 - q1 * d;
            if (rr > q0)
            {
                q1--;
                rr += d;
            }
            if (rr >= d)   // rare
            {
                q1++;
                rr -= d;
            }
            r = rr;
            return q1;
        }

        /// <summary>
        /// v for the 3-by-2 division by (d1:d0), d1 normalized: floor((b^3 - 1) / (d1:d0)) - b (invert_pi1, as
        /// DivRecip.asm).
        /// </summary>
        public static ulong Reciprocal3By2(ulong d1, ulong d0)
        {
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
            return v;
        }

        /// <summary>
        /// (n2:n1:n0) / (d1:d0) with (n2:n1) &lt; (d1:d0), d1 normalized, v = Reciprocal3By2 (Algorithm 5): the
        /// quotient word, the remainder (r1:r0).
        /// </summary>
#if NETCOREAPP3_0_OR_GREATER
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        public static ulong Div3By2(ulong n2, ulong n1, ulong n0, ulong d1, ulong d0, ulong v, out ulong r1, out ulong r0)
        {
            ulong qq = BigMul(v, n2, out ulong q0);
            q0 += n1;
            qq += n2 + (q0 < n1 ? 1UL : 0UL);
            r1 = n1 - d1 * qq;
            r0 = n0 - d0;
            r1 = r1 - d1 - (n0 < d0 ? 1UL : 0UL);
            ulong t1 = BigMul(d0, qq, out ulong t0);
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
            return qq;
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

        /// <summary>
        /// A word array the caller writes in full: from 2048 words not zeroed on .NET 5+
        /// (GC.AllocateUninitializedArray) - a + b on 4096 words: 0.88-0.90; below it the call is the
        /// slower allocation path (256 words: 1.15, 1024: 1.04), so plain new there.
        /// </summary>
        public static ulong[] NewWords(int n)
        {
#if NET5_0_OR_GREATER
            return n >= 2048 ? GC.AllocateUninitializedArray<ulong>(n) : new ulong[n];
#else
            return new ulong[n];
#endif
        }

        /// <summary>
        /// r = a op b word by word over r.Length words (op: 0 and, 1 or, 2 xor); a and b at least
        /// that long. .NET 7+: 256-bit vectors (4 words a step) - the scalar loop ran at 2-2.5x
        /// GMP's mpz_and from 64 words, the C++ build's auto-vectorized loop at 0.5x.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void BitwiseWords(int op, ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> r)
        {
            // short: the loop inline here (a call cost the 1-4 word cases 11%)
            int n = r.Length;
            if (n >= 8)
            {
                BitwiseWordsLong(op, a, b, r);
                return;
            }
            if (op == 0) for (int i = 0; i < n; i++) r[i] = a[i] & b[i];
            else if (op == 1) for (int i = 0; i < n; i++) r[i] = a[i] | b[i];
            else for (int i = 0; i < n; i++) r[i] = a[i] ^ b[i];
        }

        private static void BitwiseWordsLong(int op, ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> r)
        {
            int n = r.Length, i = 0;
#if NET7_0_OR_GREATER
            if (n >= 8 && System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated)
            {
                ref ulong pa = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(a.Slice(0, n));
                ref ulong pb = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(b.Slice(0, n));
                ref ulong pr = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(r);
                for (; i + 4 <= n; i += 4)
                {
                    var x = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref pa, (nuint)i);
                    var y = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref pb, (nuint)i);
                    var z = op == 0 ? x & y : op == 1 ? x | y : x ^ y;
                    System.Runtime.Intrinsics.Vector256.StoreUnsafe(z, ref pr, (nuint)i);
                }
            }
#endif
            if (op == 0) for (; i < n; i++) r[i] = a[i] & b[i];
            else if (op == 1) for (; i < n; i++) r[i] = a[i] | b[i];
            else for (; i < n; i++) r[i] = a[i] ^ b[i];
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
