using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public sealed class FullIntCoreWindowsX64 : FullIntCoreBase
    {
		const string dll = "FullIntOperations_win_x64.dll";
		
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
        private static extern int Fullint64CompareWIN(IntPtr a, IntPtr b, int lenA, int lenB);
        
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ToDecStringWin(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);
        
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64FromDecStringWin(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        // ---- kernel versions for newer CPUs ----
        // The baseline kernels above run on any x86-64 (as on a Core 2). Where the CPU has more,
        // newer versions are called instead, picked once at start-up (Cpu.asm: cpuid / xgetbv).
        // FULLINT_CPU=baseline in the environment forces the baseline kernels.

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CpuFeatures();

        /// <summary>Bit 0 BMI2, bit 1 ADX, bit 2 AVX2 (OS-enabled); bit 3 Recip: not a CPU feature,
        /// the division by reciprocal (DivRecip.asm) runs on any x86-64; 0 with FULLINT_CPU=baseline.</summary>
        public static readonly int CpuFeatures = DetectCpuFeatures();

        private static int DetectCpuFeatures()
        {
            if (Environment.GetEnvironmentVariable("FULLINT_CPU") == "baseline") return 0;
            try { return Fullint64CpuFeatures() | 8; }
            catch (EntryPointNotFoundException) { return 0; }   // an older native DLL
        }

        // mulx / adcx / adox (MultAdx.asm): BMI2 + ADX
        private static readonly bool MultAdx = (CpuFeatures & 3) == 3;

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64SqrWIN_adx(IntPtr a, IntPtr r, int lenA, int lenR);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64SqrWIN(IntPtr a, IntPtr r, int lenA, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64MulLoWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int n);

        // MulLoAdx.asm (BMI2 + ADX), otherwise the full product's low words
        protected override void MultLow(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int n)
        {
            if (MultAdx) Fullint64MulLoWIN_adx(a, b, r, lenA, lenB, n);
            else base.MultLow(a, b, r, lenA, lenB, n);
        }

        // SqrAdx.asm (BMI2 + ADX), otherwise Sqr.asm (mul / adc)
        protected override void Sqr(IntPtr a, IntPtr r, int lenA, int lenR)
        {
            if (MultAdx) Fullint64SqrWIN_adx(a, r, lenA, lenR);
            else Fullint64SqrWIN(a, r, lenA, lenR);
        }

        // the FFT butterfly halves (FftAdx.asm): BMI2 + ADX; Fft.cs uses managed code without them
        private static readonly bool FftKernels = (CpuFeatures & 3) == 3;
        internal bool HasFftKernels => FftKernels;

        // the number-theoretic transforms (NttAvx2.asm): AVX2; AArch64 (the same core over the .so):
        // arm64/NttNeon.s, NEON is always there (FULLINT_CPU=baseline: off, as the baseline set)
        private static readonly bool NttKernels = (CpuFeatures & 4) == 4
            || (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && CpuFeatures != 0);
        internal bool HasNttKernels => NttKernels;


        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64MultWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        // vpand / vpor / vpxor (LogicAvx2.asm): AVX2; negative operands fall back to the baseline inside
        private static readonly bool LogicAvx2 = (CpuFeatures & 4) == 4;

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64AndWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64OrWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64XorWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        // vpsllq / vpsrlq (ShiftAvx2.asm), vpcmpeqq (CompareAvx2.asm): AVX2, the same flag as the logic
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ShlWIN_avx2(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ShrWIN_avx2(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CompareWIN_avx2(IntPtr a, IntPtr b, int lenA, int lenB);

        // division by a precomputed reciprocal (DivRecip.asm): any x86-64. Long division when the
        // dividend is longer than the divisor (equal lengths: the baseline decides by a compare);
        // by one word from RecipWordsMin words (below that its own div does not pay off: measured
        // on Zen 3, 1-3 words slower, 4-48 even, 64 words 1.1x, 1024 1.4x)
        private static readonly bool DivRecip = (CpuFeatures & 8) == 8;
        private const int RecipWordsMin = 64;

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64Div2longWIN_recip(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivExactWIN(IntPtr a, IntPtr q, int len, ulong d);

        protected override ulong DivExact(IntPtr a, IntPtr q, int len, ulong d) => Fullint64DivExactWIN(a, q, len, d);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64ToDecStringWIN_recip(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64DivWIN_recip(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64DivWIN_recipadx(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivQWIN_recip(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64DivQWIN_recipadx(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work);

        // DivRecip.asm's approximate quotient (0: done; 1: saturated)
        protected override bool QuotientKernel(IntPtr a, IntPtr b, IntPtr qa, int lenA, int lenB, IntPtr work)
        {
            if (!DivRecip) return false;
            ulong saturated = MultAdx
                ? Fullint64DivQWIN_recipadx(a, b, qa, IntPtr.Zero, lenA, lenB, work)
                : Fullint64DivQWIN_recip(a, b, qa, IntPtr.Zero, lenA, lenB, work);
            return saturated == 0;
        }

        private static void DivRecipKernel(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB, IntPtr work)
        {
            // the multiply-subtract pass on mulx / adcx / adox: 1.06-1.10x from 64-word divisors,
            // even at 16..32, slower below
            if (MultAdx && lenB >= 16) Fullint64DivWIN_recipadx(a, b, q, r, lenA, lenB, work);
            else Fullint64DivWIN_recip(a, b, q, r, lenA, lenB, work);
        }

        /// <summary>Which kernel versions this process uses, e.g. for a log line.</summary>
        public static string KernelVersions =>
            !DivRecip ? "all: baseline"
            : MultAdx && LogicAvx2 ? "Mult: adx; And/Or/Xor, Shl/Shr, Compare: avx2; Div, ToDecString: recip; other: baseline"
            : MultAdx ? "Mult: adx; Div, ToDecString: recip; other: baseline"
            : LogicAvx2 ? "And/Or/Xor, Shl/Shr, Compare: avx2; Div, ToDecString: recip; other: baseline"
            : "Div, ToDecString: recip; other: baseline";

#if NET5_0_OR_GREATER
        // The same entry points without the GC transition: ~10 ns less per call, which is most
        // of the cost of a small call. Only for calls that finish within a few microseconds:
        // while one runs, the GC cannot suspend this thread. Larger calls take the normal path.
        private const int FastWords = 2048;        // add / sub / compare / logic / shifts: ~1 us at the limit
        private const long FastProducts = 4096;    // mult: lenA * lenB word products, ~3 us at the limit

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64AddWIN"), SuppressGCTransition]
        private static extern ulong Fullint64AddWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64SubWIN"), SuppressGCTransition]
        private static extern int Fullint64SubWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64MultWIN"), SuppressGCTransition]
        private static extern ulong Fullint64MultWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64MultWIN_adx"), SuppressGCTransition]
        private static extern void Fullint64MultWIN_adx_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64AndWIN_avx2"), SuppressGCTransition]
        private static extern int Fullint64AndWIN_avx2_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64OrWIN_avx2"), SuppressGCTransition]
        private static extern int Fullint64OrWIN_avx2_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64XorWIN_avx2"), SuppressGCTransition]
        private static extern int Fullint64XorWIN_avx2_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64ShlWIN_avx2"), SuppressGCTransition]
        private static extern void Fullint64ShlWIN_avx2_Fast(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64ShrWIN_avx2"), SuppressGCTransition]
        private static extern void Fullint64ShrWIN_avx2_Fast(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64CompareWIN_avx2"), SuppressGCTransition]
        private static extern int Fullint64CompareWIN_avx2_Fast(IntPtr a, IntPtr b, int lenA, int lenB);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64CompareWIN"), SuppressGCTransition]
        private static extern int Fullint64CompareWIN_Fast(IntPtr a, IntPtr b, int lenA, int lenB);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64AndWIN"), SuppressGCTransition]
        private static extern int Fullint64AndWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64OrWIN"), SuppressGCTransition]
        private static extern int Fullint64OrWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64XorWIN"), SuppressGCTransition]
        private static extern int Fullint64XorWIN_Fast(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64ShlWIN"), SuppressGCTransition]
        private static extern void Fullint64ShlWIN_Fast(IntPtr a, IntPtr r, int lenA, int lenR, int shift);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64ShrWIN"), SuppressGCTransition]
        private static extern void Fullint64ShrWIN_Fast(IntPtr a, IntPtr r, int lenA, int lenR, int shift);

        private static bool Short(int lenA, int lenB, int lenR) => lenA <= FastWords && lenB <= FastWords && lenR <= FastWords;
#endif

        private ulong AddGeneral(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64AddWIN_Fast(a, b, r, lenA, lenB, lenR);
#endif
            return Slow.Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
        }

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ulong Fullint64AddNWIN(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ulong Fullint64SubCmpNWIN(ulong* a, ulong* b, ulong* r, long n);
#if NET5_0_OR_GREATER
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64AddNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64AddNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64SubCmpNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64SubCmpNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
#endif

        private static unsafe ulong AddN(ulong* a, ulong* b, ulong* r, int n)
        {
#if NET5_0_OR_GREATER
            if (n <= FastWords) return Fullint64AddNWIN_Fast(a, b, r, n);
#endif
            return Slow.Fullint64AddNWIN(a, b, r, n);
        }

        private static unsafe ulong SubCmpN(ulong* a, ulong* b, ulong* r, int n)
        {
#if NET5_0_OR_GREATER
            if (n <= FastWords) return Fullint64SubCmpNWIN_Fast(a, b, r, n);
#endif
            return Slow.Fullint64SubCmpNWIN(a, b, r, n);
        }

        // equal lengths (AddSubN.asm): no length resolution in the kernel; r at least as long as
        // the longer operand and at most one word longer - the shorter operand's words there,
        // the carry through the longer one's top words here (a + b on 1-8 words: the general
        // kernel 4.9-9.1 ns against GMP's 1.4-2.8, AddSubN 3.5-5.5)
        protected override unsafe ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            if (lenA < lenB)
            {
                (a, b) = (b, a);
                (lenA, lenB) = (lenB, lenA);
            }
            if (lenB == 0 || lenR < lenA || lenR > lenA + 1) return AddGeneral(a, b, r, lenA, lenB, lenR);
            ulong* pa = (ulong*)a, pr = (ulong*)r;
            ulong c = AddN(pa + (lenA - lenB), (ulong*)b, pr + (lenR - lenB), lenB);
            for (int i = lenA - lenB - 1; i >= 0; i--)   // a's top words with the carry (r lines up with a at the low end)
            {
                ulong v = pa[i] + c;
                c = v < c ? 1UL : 0UL;
                pr[lenR - lenA + i] = v;
            }
            if (lenR == lenA) return c;
            pr[0] = c;
            return 0;
        }

        // equal lengths: one kernel compares from the top, subtracts the smaller from the larger and
        // counts the zero words above (SubCmpN, AddSubN.asm)
        protected override unsafe int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            int n = lenA;
            if (n != lenB || lenR < n || n == 0) return SubGeneral(a, b, r, lenA, lenB, lenR);
            ulong* pr = (ulong*)r;
            int top = lenR - n;
            ulong rv = SubCmpN((ulong*)a, (ulong*)b, pr + top, n);
            int off = (int)(uint)rv;   // the zero words above the difference (n: a == b, nothing written)
            for (int k = 0; k < top + off; k++) pr[k] = 0;
            if (off == n) return 0;
            return (long)rv < 0 ? -1 : 1;
        }
		
        private int SubGeneral(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64SubWIN_Fast(a, b, r, lenA, lenB, lenR);
#endif
            return Slow.Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
        }
        
        protected override void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
#if NET5_0_OR_GREATER
            if ((long)lenA * lenB <= FastProducts && lenR <= FastWords)
            {
                if (MultAdx) Fullint64MultWIN_adx_Fast(a, b, r, lenA, lenB, lenR);
                else Fullint64MultWIN_Fast(a, b, r, lenA, lenB, lenR);
                return;
            }
#endif
            if (MultAdx) Slow.Fullint64MultWIN_adx(a, b, r, lenA, lenB, lenR);
            else Slow.Fullint64MultWIN(a, b, r, lenA, lenB, lenR);
        }

        protected override void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
        {
            if (DivRecip && lenB >= 2 && lenA > lenB)
            {
                DivRecipLong(a, b, q, r, lenA, lenB);
                return;
            }
            Fullint64DivWin(a, b, q, r, lenA, lenB);
        }

        // the reciprocal kernel works on a copy of the dividend: lenA + 1 words of scratch
        private static unsafe void DivRecipLong(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
        {
            if (lenA < FullHelper.StackAllocLimitWords)
            {
                ulong* work = stackalloc ulong[lenA + 1];
                DivRecipKernel(a, b, q, r, lenA, lenB, (IntPtr)work);
                return;
            }
            var rented = System.Buffers.ArrayPool<ulong>.Shared.Rent(lenA + 1);
            fixed (ulong* work = rented)
                DivRecipKernel(a, b, q, r, lenA, lenB, (IntPtr)work);
            System.Buffers.ArrayPool<ulong>.Shared.Return(rented);
        }
		
		protected override void Div(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA)
        {
            if (DivRecip && lenA >= RecipWordsMin)
            {
                Fullint64Div2longWIN_recip(a, b, q, out r, lenA);
                return;
            }
            Fullint64Div2longWin(a, b, q, out r, lenA);
        }

        protected override int Compare(IntPtr a, IntPtr b, int lenA, int lenB)
		{
#if NET5_0_OR_GREATER
			if (Short(lenA, lenB, 0))
				return LogicAvx2 ? Fullint64CompareWIN_avx2_Fast(a, b, lenA, lenB) : Fullint64CompareWIN_Fast(a, b, lenA, lenB);
#endif
			return LogicAvx2 ? Slow.Fullint64CompareWIN_avx2(a, b, lenA, lenB) : Slow.Fullint64CompareWIN(a, b, lenA, lenB);
		}
        
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void Fullint64ToDecFracWIN(ulong* f, long L, long m, byte* digits);

#if NET5_0_OR_GREATER
        [DllImport(dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Fullint64ToDecFracWIN"), SuppressGCTransition]
        private static extern unsafe void Fullint64ToDecFracWIN_Fast(ulong* f, long L, long m, byte* digits);
#endif

        // the fraction method's passes (ToDecStringFrac.asm, plain x86-64: any CPU); up to 32 chunks
        // (about 1 us) without the GC transition
        public override unsafe bool ToDecFraction(Span<ulong> f, int m, Span<byte> digits)
        {
            fixed (ulong* pf = f)
            fixed (byte* pd = digits)
            {
#if NET5_0_OR_GREATER
                if (m <= 32)
                {
                    Fullint64ToDecFracWIN_Fast(pf, f.Length, m, pd);
                    return true;
                }
#endif
                Fullint64ToDecFracWIN(pf, f.Length, m, pd);
            }
            return true;
        }

        protected override void ToDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            if (DivRecip) Fullint64ToDecStringWIN_recip(value, valueBuf, resultBuf, lenValue, ref lenResult);   // 10^19 by its reciprocal
            else Fullint64ToDecStringWin(value, valueBuf, resultBuf, lenValue, ref lenResult);
        }
        
        protected override void FromDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            Fullint64FromDecStringWin(value, valueBuf, resultBuf, lenValue, ref lenResult);
        }

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

        protected override int And(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR))
                return LogicAvx2 ? Fullint64AndWIN_avx2_Fast(a, b, r, lenA, lenB, lenR, signs) : Fullint64AndWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return LogicAvx2 ? Slow.Fullint64AndWIN_avx2(a, b, r, lenA, lenB, lenR, signs) : Slow.Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR))
                return LogicAvx2 ? Fullint64OrWIN_avx2_Fast(a, b, r, lenA, lenB, lenR, signs) : Fullint64OrWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return LogicAvx2 ? Slow.Fullint64OrWIN_avx2(a, b, r, lenA, lenB, lenR, signs) : Slow.Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR))
                return LogicAvx2 ? Fullint64XorWIN_avx2_Fast(a, b, r, lenA, lenB, lenR, signs) : Fullint64XorWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return LogicAvx2 ? Slow.Fullint64XorWIN_avx2(a, b, r, lenA, lenB, lenR, signs) : Slow.Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, 0, lenR))
            {
                if (LogicAvx2) Fullint64ShlWIN_avx2_Fast(a, r, lenA, lenR, shift); else Fullint64ShlWIN_Fast(a, r, lenA, lenR, shift);
                return;
            }
#endif
            if (LogicAvx2) Slow.Fullint64ShlWIN_avx2(a, r, lenA, lenR, shift); else Slow.Fullint64ShlWIN(a, r, lenA, lenR, shift);
        }

        protected override void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, 0, lenR))
            {
                if (LogicAvx2) Fullint64ShrWIN_avx2_Fast(a, r, lenA, lenR, shift); else Fullint64ShrWIN_Fast(a, r, lenA, lenR, shift);
                return;
            }
#endif
            if (LogicAvx2) Slow.Fullint64ShrWIN_avx2(a, r, lenA, lenR, shift); else Slow.Fullint64ShrWIN(a, r, lenA, lenR, shift);
        }

        // The full P/Invokes (above FastWords) out of line: inlined into the methods that also hold a
        // _Fast call, they made the JIT set up a P/Invoke frame (CORINFO_HELP_INIT_PINVOKE_FRAME, all
        // callee-saved registers) on every call - the short path's whole cost doubled (a + b into r on
        // 2 words: ~5 ns of 11 in core.Add)
        private static class Slow
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static ulong Fullint64AddWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => FullIntCoreWindowsX64.Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64SubWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => FullIntCoreWindowsX64.Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static unsafe ulong Fullint64AddNWIN(ulong* a, ulong* b, ulong* r, long n) => FullIntCoreWindowsX64.Fullint64AddNWIN(a, b, r, n);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static unsafe ulong Fullint64SubCmpNWIN(ulong* a, ulong* b, ulong* r, long n) => FullIntCoreWindowsX64.Fullint64SubCmpNWIN(a, b, r, n);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static ulong Fullint64MultWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => FullIntCoreWindowsX64.Fullint64MultWIN(a, b, r, lenA, lenB, lenR);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Fullint64MultWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => FullIntCoreWindowsX64.Fullint64MultWIN_adx(a, b, r, lenA, lenB, lenR);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64CompareWIN(IntPtr a, IntPtr b, int lenA, int lenB) => FullIntCoreWindowsX64.Fullint64CompareWIN(a, b, lenA, lenB);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64CompareWIN_avx2(IntPtr a, IntPtr b, int lenA, int lenB) => FullIntCoreWindowsX64.Fullint64CompareWIN_avx2(a, b, lenA, lenB);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64AndWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64AndWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64AndWIN_avx2(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64OrWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64OrWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64OrWIN_avx2(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64XorWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Fullint64XorWIN_avx2(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => FullIntCoreWindowsX64.Fullint64XorWIN_avx2(a, b, r, lenA, lenB, lenR, signs);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Fullint64ShlWIN(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => FullIntCoreWindowsX64.Fullint64ShlWIN(a, r, lenA, lenR, shift);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Fullint64ShlWIN_avx2(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => FullIntCoreWindowsX64.Fullint64ShlWIN_avx2(a, r, lenA, lenR, shift);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Fullint64ShrWIN(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => FullIntCoreWindowsX64.Fullint64ShrWIN(a, r, lenA, lenR, shift);
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Fullint64ShrWIN_avx2(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => FullIntCoreWindowsX64.Fullint64ShrWIN_avx2(a, r, lenA, lenR, shift);
        }
    }
}
