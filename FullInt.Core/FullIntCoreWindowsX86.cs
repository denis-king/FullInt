using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    /// <summary>
    /// The 32-bit Windows core: FullIntOperations_win_x86.dll (Sources/x86, a port of the x64
    /// kernels to 32-bit registers), cdecl. Operations without an x86 kernel yet run on the
    /// managed core, on the same pinned buffers.
    /// </summary>
    public class FullIntCoreWindowsX86 : FullIntCoreBase, IFullIntCore   // re-implements a few members, see below
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

        int IFullIntCore.Compare(Span<ulong> a, Span<ulong> b)
            => Managed.Compare(a, b);   // Fullint64CompareWIN stays available through the base class and Kernels

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
        protected override ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Fullint64AddWIN(a, b, r, lenA, lenB, lenR);

        protected override int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Fullint64SubWIN(a, b, r, lenA, lenB, lenR);

        protected override void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR)
            => Fullint64MultWIN(a, b, r, lenA, lenB, lenR);   // 5x the managed core already at 4 words

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

        /// <summary>The asm kernels themselves, without the small-size routing (tests, benchmarks).</summary>
        public static class Kernels
        {
            public static ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => Fullint64AddWIN(a, b, r, lenA, lenB, lenR);
            public static int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR) => Fullint64SubWIN(a, b, r, lenA, lenB, lenR);
            public static int Compare(IntPtr a, IntPtr b, int lenA, int lenB) => Fullint64CompareWIN(a, b, lenA, lenB);
            public static int And(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64AndWIN(a, b, r, lenA, lenB, lenR, signs);
            public static int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64OrWIN(a, b, r, lenA, lenB, lenR, signs);
            public static int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs) => Fullint64XorWIN(a, b, r, lenA, lenB, lenR, signs);
            public static void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => Fullint64ShlWIN(a, r, lenA, lenR, shift);
            public static void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift) => Fullint64ShrWIN(a, r, lenA, lenR, shift);
        }

        // ---- not ported to x86 yet: the managed core on the same (already pinned) buffers ----

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
