using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    /// <summary>
    /// The 32-bit Windows core: FullIntOperations_win_x86.dll (Sources/x86, a port of the x64
    /// kernels to 32-bit registers), cdecl. Every operation has an x86 kernel; small sizes and
    /// Compare go to the managed core, where the native call would cost more than the work.
    /// </summary>
    public sealed class FullIntCoreWindowsX86 : FullIntCoreBase, IFullIntCore   // re-implements a few members, see below
    {
        const string dll = "FullIntOperations_win_x86.dll";

        private static readonly FullInt64CoreDefault Managed = new FullInt64CoreDefault();

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64AddWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64SubWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64MultWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivWin(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64Div2longWin(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ToDecStringWin(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64FromDecStringWin(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CompareWIN(IntPtr a, IntPtr b, int lenA, int lenB);

        // Division by a precomputed reciprocal (DivRecip.asm, ToDecStringRecip.asm): plain i386,
        // any CPU; FULLINT_CPU=baseline keeps the original kernels above. The long division works
        // on a copy of the dividend and the divisor: lenA + lenB + 2 words of scratch.
        private static readonly bool DivRecip = Environment.GetEnvironmentVariable("FULLINT_CPU") != "baseline";

        // And / Or / Xor with SSE2 (LogicSse2.asm), checked by cpuid once (x86/Cpu.asm, bit 4);
        // negative operands go on to the baseline inside the kernel
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CpuFeatures();

        private static readonly int CpuFeatures = DetectCpuFeatures();
        private static readonly bool LogicSse2 = (CpuFeatures & 16) != 0;

        // Mult with mulx / adcx / adox in 32-bit digits (x86/MultAdx.asm): BMI2 + ADX
        private static readonly bool MultAdx = (CpuFeatures & 3) == 3;

        // the number-theoretic transforms (x86/NttAvx2.asm): AVX2
        private static readonly bool NttKernels = (CpuFeatures & 4) == 4;
        internal bool HasNttKernels => NttKernels;

        // the same, without a core at hand (the Newton thresholds); false when the x86 DLL is not there
        internal static bool NttAvailable()
        {
            try { return NttKernels; }
            catch (Exception) { return false; }
        }

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64SqrWIN_adx(IntPtr a, IntPtr r, int lenA, int lenR);

        // x86/SqrAdx.asm (BMI2 + ADX, 32-bit digits), otherwise the multiplication a·a
        protected override void Sqr(IntPtr a, IntPtr r, int lenA, int lenR)
        {
            if (MultAdx) Fullint64SqrWIN_adx(a, r, lenA, lenR);
            else Mult(a, a, r, lenA, lenA, lenR);
        }

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64MulLoWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int n);

        // x86/MulLoAdx.asm (BMI2 + ADX, 32-bit digits), otherwise the full product's low words
        protected override void MultLow(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int n)
        {
            if (MultAdx) Fullint64MulLoWIN_adx(a, b, r, lenA, lenB, n);
            else base.MultLow(a, b, r, lenA, lenB, n);
        }

        private static int DetectCpuFeatures()
        {
            if (!DivRecip) return 0;   // FULLINT_CPU=baseline
            try { return Fullint64CpuFeatures(); }
            catch (EntryPointNotFoundException) { return 0; }   // an older native DLL
        }

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64MultWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64AndWIN_sse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64OrWIN_sse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64XorWIN_sse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        // Compare with SSE2 (CompareSse2.asm): equal lengths eight words a pass; different lengths go on
        // to the baseline inside the kernel
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CompareWIN_sse2(IntPtr a, IntPtr b, int lenA, int lenB);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64DivWIN_recip(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivQWIN_recip(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);

        // DivRecip.asm's approximate quotient (the word version; 0: done, 1: saturated)
        protected override bool QuotientKernel(IntPtr a, IntPtr b, IntPtr qa, int lenA, int lenB, IntPtr work)
            => DivRecip && Fullint64DivQWIN_recip(a, b, qa, IntPtr.Zero, lenA, lenB, work) == 0;

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64Div2longWIN_recip(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivExactWIN(IntPtr a, IntPtr q, int len, ulong d);

        protected override ulong DivExact(IntPtr a, IntPtr q, int len, ulong d) => Fullint64DivExactWIN(a, q, len, d);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ToDecStringWIN_recip(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64AndWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64OrWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64XorWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ShlWIN(IntPtr a, IntPtr r, int lenA, int lenR, int shift);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ShrWIN(IntPtr a, IntPtr r, int lenA, int lenR, int shift);

        // On .NET Framework x86 a native call costs ~40-45 ns, more than the work on a few words:
        // up to SmallWords words (and for Compare, which almost always stops at the first word)
        // the managed core is faster. The choice is made here, at the interface, so small calls
        // skip the pinning of the base class too. Measured in a 32-bit process (asm vs managed):
        // Add 4 words 79 vs 67 ns, Compare 44 vs 24 ns; Add 16 words 79 vs 224 ns, Mult 4x4 177 vs 909 ns.
        private const int SmallWords = 4;

        private static bool Small(int lenA, int lenB, int lenR) => lenA <= SmallWords && lenB <= SmallWords && lenR <= SmallWords + 1;

        ulong IFullIntCore.Add(Span<ulong> a, Span<ulong> b, Span<ulong> r)
            => Small(a.Length, b.Length, r.Length) ? Managed.Add(a, b, r) : Add(a, b, r);

        int IFullIntCore.Sub(Span<ulong> a, Span<ulong> b, Span<ulong> r)
            => Small(a.Length, b.Length, r.Length) ? Managed.Sub(a, b, r) : Sub(a, b, r);

        // short values (and the engine decides most compares itself, by length and top word): the
        // managed core; long ones: the kernel, whose loop beats the managed one well before the call
        // cost (4096 equal words: managed 8.5 us, BigInteger 3.7 us)
        int IFullIntCore.Compare(Span<ulong> a, Span<ulong> b)
            => a.Length <= CompareManagedWords && b.Length <= CompareManagedWords ? Managed.Compare(a, b) : Compare(a, b);

        private const int CompareManagedWords = 16;

        int IFullIntCore.And(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
            => Small(a.Length, b.Length, r.Length) ? Managed.And(a, negA, b, negB, r) : And(a, negA, b, negB, r);

        int IFullIntCore.Or(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
            => Small(a.Length, b.Length, r.Length) ? Managed.Or(a, negA, b, negB, r) : Or(a, negA, b, negB, r);

        int IFullIntCore.Xor(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
            => Small(a.Length, b.Length, r.Length) ? Managed.Xor(a, negA, b, negB, r) : Xor(a, negA, b, negB, r);

        void IFullIntCore.ShiftLeft(Span<ulong> a, int shift, Span<ulong> r)
        {
            if (Small(a.Length, 0, r.Length)) Managed.ShiftLeft(a, shift, r);
            else ShiftLeft(a, shift, r);
        }

        void IFullIntCore.ShiftRight(Span<ulong> a, int shift, Span<ulong> r)
        {
            if (Small(a.Length, 0, r.Length)) Managed.ShiftRight(a, shift, r);
            else ShiftRight(a, shift, r);
        }

        // the asm kernels behind the base class (pinned buffers)
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ulong Fullint64AddNWIN(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ulong Fullint64SubCmpNWIN(ulong* a, ulong* b, ulong* r, long n);

        // equal lengths through x86/AddSubN.asm, as the x64 core does (see there)
        protected override unsafe ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            if (lenA < lenB)
            {
                (a, b) = (b, a);
                (lenA, lenB) = (lenB, lenA);
            }
            if (lenB == 0 || lenR < lenA || lenR > lenA + 1) return Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
            ulong* pa = (ulong*)a, pr = (ulong*)r;
            ulong c = Fullint64AddNWIN(pa + (lenA - lenB), (ulong*)b, pr + (lenR - lenB), lenB);
            for (int i = lenA - lenB - 1; i >= 0; i--)
            {
                ulong v = pa[i] + c;
                c = v < c ? 1UL : 0UL;
                pr[lenR - lenA + i] = v;
            }
            if (lenR == lenA) return c;
            pr[0] = c;
            return 0;
        }

        protected override unsafe int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            int n = lenA;
            if (n != lenB || lenR < n || n == 0) return Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
            ulong* pr = (ulong*)r;
            int top = lenR - n;
            ulong rv = Fullint64SubCmpNWIN((ulong*)a, (ulong*)b, pr + top, n);
            int off = (int)(uint)rv;   // the zero words above the difference (n: a == b, nothing written)
            for (int k = 0; k < top + off; k++) pr[k] = 0;
            if (off == n) return 0;
            return (long)rv < 0 ? -1 : 1;
        }

        protected override void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            if (MultAdx) Fullint64MultWIN_adx(a, b, r, lenA, lenB, lenR);   // 1.2-1.3x the baseline
            else Fullint64MultWIN(a, b, r, lenA, lenB, lenR);
        }   // 5x the managed core already at 4 words

        protected override int Compare(IntPtr a, IntPtr b, int lenA, int lenB)
            => LogicSse2 ? Fullint64CompareWIN_sse2(a, b, lenA, lenB) : Fullint64CompareWIN(a, b, lenA, lenB);

        protected override void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
        {
            // equal lengths (a quotient of 0 or 1): the baseline decides those by a compare
            if (DivRecip && lenB >= 2 && lenA > lenB) DivRecipLong(a, b, q, r, lenA, lenB);
            else Fullint64DivWin(a, b, q, r, lenA, lenB);
        }

        private static unsafe void DivRecipLong(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
        {
            int words = lenA + lenB + 2;
            if (words <= FullHelper.StackAllocLimitWords)
            {
                ulong* work = stackalloc ulong[words];
                Fullint64DivWIN_recip(a, b, q, r, lenA, lenB, (IntPtr)work);
                return;
            }
            var rented = System.Buffers.ArrayPool<ulong>.Shared.Rent(words);
            fixed (ulong* work = rented)
                Fullint64DivWIN_recip(a, b, q, r, lenA, lenB, (IntPtr)work);
            System.Buffers.ArrayPool<ulong>.Shared.Return(rented);
        }

        // by one word: the reciprocal kernel takes divisors of 2^32 and more (one 3-by-2 step per
        // 32-bit digit, was fi_div128 per word: 1.1-1.6x), smaller ones go on to the baseline inside
        protected override void Div(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA)
        {
            if (DivRecip) Fullint64Div2longWIN_recip(a, b, q, out r, lenA);
            else Fullint64Div2longWin(a, b, q, out r, lenA);
        }

        protected override int And(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
            => LogicSse2 ? Fullint64AndWIN_sse2(a, b, r, lenA, lenB, lenR, signs) : Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
            => LogicSse2 ? Fullint64OrWIN_sse2(a, b, r, lenA, lenB, lenR, signs) : Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
            => LogicSse2 ? Fullint64XorWIN_sse2(a, b, r, lenA, lenB, lenR, signs) : Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
            => Fullint64ShlWIN(a, r, lenA, lenR, shift);

        protected override void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
            => Fullint64ShrWIN(a, r, lenA, lenR, shift);

        protected override void ToDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            if (DivRecip) Fullint64ToDecStringWIN_recip(value, valueBuf, resultBuf, lenValue, ref lenResult);
            else Fullint64ToDecStringWin(value, valueBuf, resultBuf, lenValue, ref lenResult);
        }

        protected override void FromDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
            => Fullint64FromDecStringWin(value, valueBuf, resultBuf, lenValue, ref lenResult);

        /// <summary>The asm kernels themselves, without the small-size routing (tests, benchmarks).</summary>
        public static class Kernels
        {
            public static ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
            public static int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
            public static int Compare(IntPtr a, IntPtr b, int lenA, int lenB) => Fullint64CompareWIN(a, b, lenA, lenB);
            public static void DivByWord(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA) => Fullint64Div2longWin(a, b, q, out r, lenA);
            public static void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB) => Fullint64DivWin(a, b, q, r, lenA, lenB);
            /// <summary>lenA &gt; lenB &gt;= 2; work: lenA + lenB + 2 words.</summary>
            public static void DivRecip(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work) => Fullint64DivWIN_recip(a, b, q, r, lenA, lenB, work);
            public static void DivByWordRecip(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA) => Fullint64Div2longWIN_recip(a, b, q, out r, lenA);
            public static ulong DivExact(IntPtr a, IntPtr q, int len, ulong d) => Fullint64DivExactWIN(a, q, len, d);
            public static int And(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);
            public static int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);
            public static int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);
            public static int CpuFeatures() => Fullint64CpuFeatures();
            public static void MultAdx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => Fullint64MultWIN_adx(a, b, r, lenA, lenB, lenR);
            public static void SqrAdx(IntPtr a, IntPtr r, int lenA, int lenR) => Fullint64SqrWIN_adx(a, r, lenA, lenR);
            public static int AndSse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64AndWIN_sse2(a, b, r, lenA, lenB, lenR, signs);
            public static int OrSse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64OrWIN_sse2(a, b, r, lenA, lenB, lenR, signs);
            public static int XorSse2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64XorWIN_sse2(a, b, r, lenA, lenB, lenR, signs);
            public static int CompareSse2(IntPtr a, IntPtr b, int lenA, int lenB) => Fullint64CompareWIN_sse2(a, b, lenA, lenB);
            public static void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => Fullint64ShlWIN(a, r, lenA, lenR, shift);
            public static void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => Fullint64ShrWIN(a, r, lenA, lenR, shift);
        }
    }
}
