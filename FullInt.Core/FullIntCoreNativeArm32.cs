#if NET5_0_OR_GREATER
using System;
using System.Runtime.InteropServices;

namespace FullNumerics
{
    /// <summary>
    /// The ARMv7 core (Linux armhf): the managed core with the ARMv7 kernels where they exist
    /// (FullInt.Asm/Sources/arm32, libFullIntOperations_linux_arm.so) - the equal-length add /
    /// subtract, the schoolbook product (Mult, Sqr, MultLow), the division (long and by a word)
    /// and the decimal conversions. The managed core's 64-bit word
    /// operations are each several instructions on a 32-bit CPU; the kernels work in 32-bit digits
    /// (umaal: one instruction a digit product with both additions).
    /// </summary>
    internal sealed class FullIntCoreNativeArm32 : FullIntCoreManaged, IFullIntCore   // re-implements the members below
    {
        const string dll = NativeNames.Lib32;

        [DllImport(dll)]
        private static extern unsafe ulong Fullint64AddNWIN(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll)]
        private static extern unsafe ulong Fullint64SubCmpNWIN(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll)]
        private static extern unsafe ulong Fullint64MultWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);
        [DllImport(dll)]
        private static extern unsafe ulong Fullint64AddWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);
        [DllImport(dll)]
        private static extern unsafe int Fullint64SubWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);
        [DllImport(dll)]
        private static extern unsafe ulong Fullint64DivExactWIN(ulong* a, ulong* q, int len, ulong d);
        [DllImport(dll, EntryPoint = "Fullint64AddWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64AddWIN_Fast(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);
        [DllImport(dll, EntryPoint = "Fullint64SubWIN"), SuppressGCTransition]
        private static extern unsafe int Fullint64SubWIN_Fast(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);
        [DllImport(dll, EntryPoint = "Fullint64DivExactWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64DivExactWIN_Fast(ulong* a, ulong* q, int len, ulong d);

        // short calls without the GC transition (it costs more than the work there); long ones keep
        // it, so a collection is not held up for their whole length
        [DllImport(dll, EntryPoint = "Fullint64AddNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64AddNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, EntryPoint = "Fullint64SubCmpNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64SubCmpNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, EntryPoint = "Fullint64MultWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64MultWIN_Fast(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);

        private const int FastWords = 1024;          // add / sub: up to ~1 us

        // the number-theoretic transforms (arm32/NttNeon.s): NEON, optional on ARMv7 - HWCAP_NEON in
        // the process's auxiliary vector (FULLINT_CPU=baseline: off)
        private static readonly bool Neon = DetectNeon();
        internal bool HasNttKernels => Neon;
        internal static bool NttAvailable() => Neon;

        private static bool DetectNeon()
        {
            if (Environment.GetEnvironmentVariable("FULLINT_CPU") == "baseline") return false;
            try
            {
                var auxv = System.IO.File.ReadAllBytes("/proc/self/auxv");   // (type, value) pairs of 32 bits
                for (int i = 0; i + 8 <= auxv.Length; i += 8)
                {
                    uint type = BitConverter.ToUInt32(auxv, i), value = BitConverter.ToUInt32(auxv, i + 4);
                    if (type == 0) break;
                    if (type == 16) return (value & (1u << 12)) != 0;   // AT_HWCAP, HWCAP_NEON
                }
            }
            catch (Exception) { }
            return false;
        }
        private const long FastProducts = 64 * 64;   // the product: up to ~8 us (word products)

        // r = a + b over r.Length words (the contract of FullIntCoreManaged.Add): the common case -
        // r one word longer than the longer operand, or as long - through the kernel on the equal
        // part, the longer operand's top words with the carry here
        public new unsafe ulong Add(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            if (a.Length < b.Length)
            {
                var t = a;
                a = b;
                b = t;
            }
            int la = a.Length, lb = b.Length, lr = r.Length;
            if (lb == 0 || lr < la || lr > la + 1)
            {
                fixed (ulong* qa = a, qb = b, qr = r)   // the general case (AddSubN.s's AddWIN)
                    return lr <= FastWords
                        ? Fullint64AddWIN_Fast(qa, qb, qr, la, lb, lr)
                        : Fullint64AddWIN(qa, qb, qr, la, lb, lr);
            }
            fixed (ulong* pa = a, pb = b, pr = r)
            {
                ulong c = lb <= FastWords
                    ? Fullint64AddNWIN_Fast(pa + (la - lb), pb, pr + (lr - lb), lb)
                    : Fullint64AddNWIN(pa + (la - lb), pb, pr + (lr - lb), lb);
                for (int i = la - lb - 1; i >= 0; i--)
                {
                    ulong v = pa[i] + c;
                    c = v < c ? 1UL : 0UL;
                    pr[lr - la + i] = v;
                }
                if (lr == la) return c;
                pr[0] = c;
                return 0;
            }
        }

        // |a - b| and the sign of a - b (the contract of FullIntCoreManaged.Sub): equal lengths
        // through the kernel, which finds the first differing word itself
        public new unsafe int Sub(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            int n = a.Length;
            if (n == 0 || n != b.Length || r.Length < n)
            {
                if (r.Length < Math.Max(a.Length, b.Length)) return base.Sub(a, b, r);
                fixed (ulong* qa = a, qb = b, qr = r)   // different lengths (AddSubN.s's SubWIN)
                    return r.Length <= FastWords
                        ? Fullint64SubWIN_Fast(qa, qb, qr, a.Length, b.Length, r.Length)
                        : Fullint64SubWIN(qa, qb, qr, a.Length, b.Length, r.Length);
            }
            fixed (ulong* pa = a, pb = b, pr = r)
            {
                int top = r.Length - n;
                ulong rv = n <= FastWords
                    ? Fullint64SubCmpNWIN_Fast(pa, pb, pr + top, n)
                    : Fullint64SubCmpNWIN(pa, pb, pr + top, n);
                int off = (int)(uint)rv;   // the zero words above the difference (n: a == b, nothing written)
                for (int k = 0; k < top + off; k++) pr[k] = 0;
                if (off == n) return 0;
                return (long)rv < 0 ? -1 : 1;
            }
        }

        // the kernel takes any r.Length: the product right-aligned with zeros above, or its low words
        public new unsafe void Mult(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            int la = a.Length, lb = b.Length, lr = r.Length;
            fixed (ulong* pa = a, pb = b, pr = r)
            {
                if ((long)la * lb <= FastProducts) Fullint64MultWIN_Fast(pa, pb, pr, la, lb, lr);
                else Fullint64MultWIN(pa, pb, pr, la, lb, lr);
            }
        }

        [DllImport(dll)]
        private static extern unsafe void Fullint64SqrWIN(ulong* a, ulong* r, int lenA, int lenR);
        [DllImport(dll, EntryPoint = "Fullint64SqrWIN"), SuppressGCTransition]
        private static extern unsafe void Fullint64SqrWIN_Fast(ulong* a, ulong* r, int lenA, int lenR);

        // q = a / d for a multiple of d (the Toom-Cook interpolation; ToomOps.s): 0 out when a was one
        public new unsafe ulong DivExact(Span<ulong> a, ulong d, Span<ulong> q)
        {
            fixed (ulong* pa = a, pq = q)
                return a.Length <= FastWords
                    ? Fullint64DivExactWIN_Fast(pa, pq, a.Length, d)
                    : Fullint64DivExactWIN(pa, pq, a.Length, d);
        }

        // r = a² (Sqr.s: the products of different words once, doubled, the squares added): r at least
        // 2·a.Length words, the words above zero
        public new unsafe void Sqr(Span<ulong> a, Span<ulong> r)
        {
            if (r.Length < 2 * a.Length)
            {
                Mult(a, a, r);
                return;
            }
            fixed (ulong* pa = a, pr = r)
            {
                if ((long)a.Length * a.Length <= 2 * FastProducts) Fullint64SqrWIN_Fast(pa, pr, a.Length, r.Length);
                else Fullint64SqrWIN(pa, pr, a.Length, r.Length);
            }
        }

        // the low r.Length words of a·b: the kernel stops at r's top word
        public new void MultLow(Span<ulong> a, Span<ulong> b, Span<ulong> r) => Mult(a, b, r);

        // ---- division and the decimal conversions (Div.s, DivLong.s: 3-by-2 steps by a reciprocal in
        //      32-bit digits) ----

        [DllImport(dll)]
        private static extern unsafe void Fullint64DivWIN_recip(ulong* a, ulong* b, ulong* q, ulong* r, int lenA, int lenB, ulong* work);
        [DllImport(dll)]
        private static extern unsafe void Fullint64Div2longWin(ulong* a, ulong b, ulong* q, ulong* r, int lenA);
        [DllImport(dll)]
        private static extern unsafe void Fullint64ToDecStringWin(ulong* value, ulong* valueBuf, byte* resultBuf, int lenValue, int* lenResult);
        [DllImport(dll)]
        private static extern unsafe void Fullint64FromDecStringWin(byte* value, ulong* valueBuf, ulong* resultBuf, int lenValue, int* lenResult);
        [DllImport(dll, EntryPoint = "Fullint64DivWIN_recip"), SuppressGCTransition]
        private static extern unsafe void Fullint64DivWIN_recip_Fast(ulong* a, ulong* b, ulong* q, ulong* r, int lenA, int lenB, ulong* work);
        [DllImport(dll, EntryPoint = "Fullint64Div2longWin"), SuppressGCTransition]
        private static extern unsafe void Fullint64Div2longWin_Fast(ulong* a, ulong b, ulong* q, ulong* r, int lenA);

        // the Div contract of FullIntCoreManaged (remainder: lenB + 2 words, the value in [2 ..]):
        // lenA > lenB >= 2 through the kernel (work: lenA + 1 words), the rest (a word, equal
        // lengths, a shorter dividend) as the managed core
        public new unsafe void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder)
        {
            int la = dividend.Length, lb = divisor.Length;
            if (lb < 2 || la <= lb)
            {
                base.Div(dividend, divisor, quotient, remainder);
                return;
            }
            int words = la + 1;
            ulong[]? rented = null;
            Span<ulong> work = words <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[words]
                : (rented = System.Buffers.ArrayPool<ulong>.Shared.Rent(words));
            fixed (ulong* pa = dividend, pb = divisor, pq = quotient, pr = remainder, pw = work)
            {
                if ((long)(la - lb + 1) * lb <= FastProducts) Fullint64DivWIN_recip_Fast(pa, pb, pq, pr, la, lb, pw);
                else Fullint64DivWIN_recip(pa, pb, pq, pr, la, lb, pw);
            }
            if (rented != null) System.Buffers.ArrayPool<ulong>.Shared.Return(rented);
        }

        public new unsafe void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder)
        {
            ulong r;
            fixed (ulong* pa = dividend, pq = quotient)
            {
                if (dividend.Length <= FastWords) Fullint64Div2longWin_Fast(pa, divisor, pq, &r, dividend.Length);
                else Fullint64Div2longWin(pa, divisor, pq, &r, dividend.Length);
            }
            remainder = r;
        }

        [DllImport(dll)]
        private static extern unsafe ulong Fullint64DivQWIN_recip(ulong* a, ulong* b, ulong* q, ulong* r, int lenA, int lenB, ulong* work);

        // from this many divisor words the approximate quotient (DivLong.s: the passes on the top
        // digits of B only) and its fix; below, the division (the guard word's extra step costs more)
        private const int QuotientWordsMin = 8;

        // a / b alone: Q' ~ floor(A·β / B) within 1 (the quotient and a guard word); a guard of 0 or
        // β - 1 leaves the quotient it or one less / one more, decided by A - q·B. Saturated (the
        // kernel's left-out error reached B, or B over 256 words): the division.
        public new unsafe void Quotient(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient)
        {
            int la = dividend.Length, lb = divisor.Length, lq = quotient.Length;
            if (lb >= QuotientWordsMin && la > lb && lq == la - lb + 1)
            {
                int words = (lq + 1) + (la + 2) + (la + 1);   // Q', the kernel's work, A - q·B
                ulong[]? bufRented = null;
                Span<ulong> buf = words <= FullHelper.StackAllocLimitWords
                    ? stackalloc ulong[words]
                    : (bufRented = System.Buffers.ArrayPool<ulong>.Shared.Rent(words));
                bool done = false;
                fixed (ulong* pa = dividend, pb = divisor, pq = quotient, pw = buf)
                {
                    ulong* qa = pw, work = pw + lq + 1, p = work + la + 2;
                    if (Fullint64DivQWIN_recip(pa, pb, qa, null, la, lb, work) == 0)
                    {
                        for (int i = 0; i < lq; i++) pq[i] = qa[i];
                        done = true;
                        if (qa[lq] + 1 <= 1) Fix(pa, pb, pq, p, la, lb, lq);
                    }
                }
                if (bufRented != null) System.Buffers.ArrayPool<ulong>.Shared.Return(bufRented);
                if (done) return;
            }
            int rn = divisor.Length + 2;
            ulong[]? rented = null;
            Span<ulong> r = rn <= FullHelper.StackAllocLimitWords ? stackalloc ulong[rn] : FullHelper.RentZeroed(rn, out rented);
            Div(dividend, divisor, quotient, r);
            FullHelper.Return(rented);
        }

        // q within 1 of floor(A / B): D = A - q·B (p: la + 1 words) decides q - 1 / q / q + 1
        private unsafe void Fix(ulong* a, ulong* b, ulong* q, ulong* p, int la, int lb, int lq)
        {
            Mult(new Span<ulong>(q, lq), new Span<ulong>(b, lb), new Span<ulong>(p, la + 1));
            ulong borrow = 0;
            for (int i = la; i >= 0; i--)   // p = [0, A] - p
            {
                ulong x = i > 0 ? a[i - 1] : 0, y = p[i];
                ulong d = x - y;
                ulong b1 = x < y ? 1UL : 0UL;
                p[i] = d - borrow;
                borrow = b1 | (d < borrow ? 1UL : 0UL);
            }
            if (borrow != 0)   // q·B > A: q - 1
            {
                for (int i = lq - 1; i >= 0; i--)
                    if (q[i]-- != 0) break;
                return;
            }
            int s = 0;
            while (s <= la && p[s] == 0) s++;
            if (s <= la && Compare(new Span<ulong>(p + s, la + 1 - s), new Span<ulong>(b, lb)) >= 0)   // D >= B: q + 1
                for (int i = lq - 1; i >= 0; i--)
                    if (++q[i] != 0) break;
        }

        // ---- compare, And / Or / Xor, the shifts (Compare.s, Logic.s, Shift.s) ----

        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe int Fullint64CompareWIN(ulong* a, ulong* b, int lenA, int lenB);
        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe int Fullint64AndWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe int Fullint64OrWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe int Fullint64XorWIN(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe void Fullint64ShlWIN(ulong* a, ulong* r, int lenA, int lenR, int shift);
        [DllImport(dll), SuppressGCTransition]
        private static extern unsafe void Fullint64ShrWIN(ulong* a, ulong* r, int lenA, int lenR, int shift);

        // (these are linear: past LinearFastWords words the call keeps its GC transition)
        [DllImport(dll, EntryPoint = "Fullint64CompareWIN")]
        private static extern unsafe int Fullint64CompareWIN_Slow(ulong* a, ulong* b, int lenA, int lenB);
        [DllImport(dll, EntryPoint = "Fullint64AndWIN")]
        private static extern unsafe int Fullint64AndWIN_Slow(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, EntryPoint = "Fullint64OrWIN")]
        private static extern unsafe int Fullint64OrWIN_Slow(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, EntryPoint = "Fullint64XorWIN")]
        private static extern unsafe int Fullint64XorWIN_Slow(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, EntryPoint = "Fullint64ShlWIN")]
        private static extern unsafe void Fullint64ShlWIN_Slow(ulong* a, ulong* r, int lenA, int lenR, int shift);
        [DllImport(dll, EntryPoint = "Fullint64ShrWIN")]
        private static extern unsafe void Fullint64ShrWIN_Slow(ulong* a, ulong* r, int lenA, int lenR, int shift);

        public new unsafe int Compare(Span<ulong> a, Span<ulong> b)
        {
            fixed (ulong* pa = a, pb = b)
                return Math.Max(a.Length, b.Length) <= FastWords
                    ? Fullint64CompareWIN(pa, pb, a.Length, b.Length)
                    : Fullint64CompareWIN_Slow(pa, pb, a.Length, b.Length);
        }

        public new int And(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(0, a, negA, b, negB, r);
        public new int Or(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(1, a, negA, b, negB, r);
        public new int Xor(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(2, a, negA, b, negB, r);

        // the contract of FullIntCoreManaged's And / Or / Xor: r one word longer than the longer operand
        private unsafe int Bitwise(int op, Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
        {
            if (a.Length < 1 || b.Length < 1 || r.Length < Math.Max(a.Length, b.Length) + 1)
                return op == 0 ? base.And(a, negA, b, negB, r) : op == 1 ? base.Or(a, negA, b, negB, r) : base.Xor(a, negA, b, negB, r);
            int signs = (negA ? 1 : 0) | (negB ? 2 : 0);
            bool fast = r.Length <= FastWords;
            fixed (ulong* pa = a, pb = b, pr = r)
                return op switch
                {
                    0 => fast ? Fullint64AndWIN(pa, pb, pr, a.Length, b.Length, r.Length, signs) : Fullint64AndWIN_Slow(pa, pb, pr, a.Length, b.Length, r.Length, signs),
                    1 => fast ? Fullint64OrWIN(pa, pb, pr, a.Length, b.Length, r.Length, signs) : Fullint64OrWIN_Slow(pa, pb, pr, a.Length, b.Length, r.Length, signs),
                    _ => fast ? Fullint64XorWIN(pa, pb, pr, a.Length, b.Length, r.Length, signs) : Fullint64XorWIN_Slow(pa, pb, pr, a.Length, b.Length, r.Length, signs),
                };
        }

        public new unsafe void ShiftLeft(Span<ulong> a, int shift, Span<ulong> r)
        {
            fixed (ulong* pa = a, pr = r)
            {
                if (r.Length <= FastWords) Fullint64ShlWIN(pa, pr, a.Length, r.Length, shift);
                else Fullint64ShlWIN_Slow(pa, pr, a.Length, r.Length, shift);
            }
        }

        public new unsafe void ShiftRight(Span<ulong> a, int shift, Span<ulong> r)
        {
            fixed (ulong* pa = a, pr = r)
            {
                if (r.Length <= FastWords) Fullint64ShrWIN(pa, pr, a.Length, r.Length, shift);
                else Fullint64ShrWIN_Slow(pa, pr, a.Length, r.Length, shift);
            }
        }

        // repeated division by 10^19 (resultBuf: the digits right-aligned, the first one's index out)
        public new unsafe void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex)
        {
            ulong[]? rented = null;
            Span<ulong> valueBuf = value.Length <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[value.Length]
                : FullHelper.RentZeroed(value.Length, out rented);
            int start = resultBuf.Length;
            fixed (ulong* pv = value, pvb = valueBuf)
            fixed (byte* pr = resultBuf)
                Fullint64ToDecStringWin(pv, pvb, pr, value.Length, &start);
            FullHelper.Return(rented);
            resultStartIndex = start;
        }

        // the digits as base-10^19 blocks, then base 2^64 (resultBuf: the words right-aligned)
        public new unsafe void FromDecString(Span<byte> value, Span<ulong> resultBuf, out int resultStartIndex)
        {
            int valueWords = value.Length / 19 + 1;
            ulong[]? rented = null;
            Span<ulong> valueBuf = valueWords <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[valueWords]
                : FullHelper.RentZeroed(valueWords, out rented);
            int start = resultBuf.Length;
            fixed (byte* pv = value)
            fixed (ulong* pvb = valueBuf, pr = resultBuf)
                Fullint64FromDecStringWin(pv, pvb, pr, value.Length, &start);
            FullHelper.Return(rented);
            resultStartIndex = start;
        }
    }
}
#endif
