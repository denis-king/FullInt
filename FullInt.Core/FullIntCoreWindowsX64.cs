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
    }
}
