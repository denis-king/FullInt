#if NET5_0_OR_GREATER
using System;
using System.Runtime.InteropServices;

namespace FullNumerics
{
    /// <summary>
    /// The ARMv7 core (Linux armhf): the managed core with the ARMv7 kernels where they exist
    /// (FullInt.Asm/Sources/arm32, libFullIntOperations_linux_arm.so) - the equal-length add /
    /// subtract and the schoolbook product (Mult, Sqr, MultLow). The managed core's 64-bit word
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

        // short calls without the GC transition (it costs more than the work there); long ones keep
        // it, so a collection is not held up for their whole length
        [DllImport(dll, EntryPoint = "Fullint64AddNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64AddNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, EntryPoint = "Fullint64SubCmpNWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64SubCmpNWIN_Fast(ulong* a, ulong* b, ulong* r, long n);
        [DllImport(dll, EntryPoint = "Fullint64MultWIN"), SuppressGCTransition]
        private static extern unsafe ulong Fullint64MultWIN_Fast(ulong* a, ulong* b, ulong* r, int lenA, int lenB, int lenR);

        private const int FastWords = 1024;          // add / sub: up to ~1 us
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
            if (lb == 0 || lr < la || lr > la + 1) return base.Add(a, b, r);
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
            if (n == 0 || n != b.Length || r.Length < n) return base.Sub(a, b, r);
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

        public new void Sqr(Span<ulong> a, Span<ulong> r) => Mult(a, a, r);

        // the low r.Length words of a·b: the kernel stops at r's top word
        public new void MultLow(Span<ulong> a, Span<ulong> b, Span<ulong> r) => Mult(a, b, r);
    }
}
#endif
