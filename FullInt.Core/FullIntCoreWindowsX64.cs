using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public class FullIntCoreWindowsX64 : FullIntCoreBase
    {
		[DllImport("128Operations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Fullint64AddWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport("128Operations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Fullint64SubWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport("128Operations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Fullint64MultWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport("FullIntOperations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Fullint64DivWin(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenB, int lenR);
		
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

    }
}
