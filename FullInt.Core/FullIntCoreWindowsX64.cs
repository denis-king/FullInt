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
        
        protected override ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            return Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
        }
		
		protected override int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            return Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
        }
        
        protected override void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
        {
            Fullint64MultWIN(a, b, r, lenA, lenB, lenR);
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
            => Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
            => Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs)
            => Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);

        protected override void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
            => Fullint64ShlWIN(a, r, lenA, lenR, shift);

        protected override void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift)
            => Fullint64ShrWIN(a, r, lenA, lenR, shift);
    }
}
