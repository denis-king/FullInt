using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public class FullIntCoreWindowsX64 : FullIntCoreBase
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

        /// <summary>Bit 0 BMI2, bit 1 ADX, bit 2 AVX2 (OS-enabled); 0 with FULLINT_CPU=baseline.</summary>
        public static readonly int CpuFeatures = DetectCpuFeatures();

        private static int DetectCpuFeatures()
        {
            if (Environment.GetEnvironmentVariable("FULLINT_CPU") == "baseline") return 0;
            try { return Fullint64CpuFeatures(); }
            catch (EntryPointNotFoundException) { return 0; }   // an older native DLL
        }

        // mulx / adcx / adox (MultAdx.asm): BMI2 + ADX
        private static readonly bool MultAdx = (CpuFeatures & 3) == 3;

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Fullint64MultWIN_adx(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

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

        protected override ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64AddWIN_Fast(a, b, r, lenA, lenB, lenR);
#endif
            return Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
        }
		
		protected override int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64SubWIN_Fast(a, b, r, lenA, lenB, lenR);
#endif
            return Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
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
            if (MultAdx) Fullint64MultWIN_adx(a, b, r, lenA, lenB, lenR);
            else Fullint64MultWIN(a, b, r, lenA, lenB, lenR);
        }

        protected override void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
        {
            Fullint64DivWin(a, b, q, r, lenA, lenB);
        }
		
		protected override void Div(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA)
        {
            Fullint64Div2longWin(a, b, q, out r, lenA);
        }

        protected override int Compare(IntPtr a, IntPtr b, int lenA, int lenB)
		{
#if NET5_0_OR_GREATER
			if (Short(lenA, lenB, 0)) return Fullint64CompareWIN_Fast(a, b, lenA, lenB);
#endif
			return Fullint64CompareWIN(a, b, lenA, lenB);
		}
        
        protected override void ToDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            Fullint64ToDecStringWin(value, valueBuf, resultBuf, lenValue, ref lenResult);
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
            if (Short(lenA, lenB, lenR)) return Fullint64AndWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64OrWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, lenB, lenR)) return Fullint64XorWIN_Fast(a, b, r, lenA, lenB, lenR, signs);
#endif
            return Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);
        }

        protected override void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, 0, lenR)) { Fullint64ShlWIN_Fast(a, r, lenA, lenR, shift); return; }
#endif
            Fullint64ShlWIN(a, r, lenA, lenR, shift);
        }

        protected override void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
        {
#if NET5_0_OR_GREATER
            if (Short(lenA, 0, lenR)) { Fullint64ShrWIN_Fast(a, r, lenA, lenR, shift); return; }
#endif
            Fullint64ShrWIN(a, r, lenA, lenR, shift);
        }
    }
}
