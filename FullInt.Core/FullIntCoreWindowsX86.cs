using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    /// <summary>
    /// The 32-bit Windows core: FullIntOperations_win_x86.dll (Sources/x86, a port of the x64
    /// kernels to 32-bit registers), cdecl. Operations without an x86 kernel yet run on the
    /// managed core, on the same pinned buffers.
    /// </summary>
    public class FullIntCoreWindowsX86 : FullIntCoreBase
    {
        const string dll = "FullIntOperations_win_x86.dll";

        private static readonly FullInt64CoreDefault Managed = new FullInt64CoreDefault();

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong Fullint64AddWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64SubWIN(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);

        [DllImport(dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int Fullint64CompareWIN(IntPtr a, IntPtr b, int lenA, int lenB);

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

        protected override ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Fullint64AddWIN(a, b, r, lenA, lenB, lenR);

        protected override int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Fullint64SubWIN(a, b, r, lenA, lenB, lenR);

        protected override int Compare(IntPtr a, IntPtr b, int lenA, int lenB)
            => Fullint64CompareWIN(a, b, lenA, lenB);

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

        // ---- not ported to x86 yet: the managed core on the same (already pinned) buffers ----

        protected override unsafe void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Managed.Mult(new Span<ulong>((void*)a, lenA), new Span<ulong>((void*)b, lenB), new Span<ulong>((void*)r, lenR));

        protected override unsafe void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB)
            => Managed.Div(new Span<ulong>((void*)a, lenA), new Span<ulong>((void*)b, lenB),
                           new Span<ulong>((void*)q, Math.Max(lenA - lenB + 1, 1)), new Span<ulong>((void*)r, lenB + 2));

        protected override unsafe void Div(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA)
            => Managed.Div(new Span<ulong>((void*)a, lenA), b, new Span<ulong>((void*)q, lenA), out r);

        protected override unsafe void ToDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            Managed.ToDecString(new Span<ulong>((void*)value, lenValue), new Span<byte>((void*)resultBuf, lenResult), out int start);
            lenResult = start;
        }

        protected override unsafe void FromDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult)
        {
            Managed.FromDecString(new Span<byte>((void*)value, lenValue), new Span<ulong>((void*)resultBuf, lenResult), out int start);
            lenResult = start;
        }
    }
}
