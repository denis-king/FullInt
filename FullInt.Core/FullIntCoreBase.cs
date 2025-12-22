using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public abstract class FullIntCoreBase : IFullIntCore
    {
        public ulong Add(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
			//todo possible we should return carry in the add sub and mult?  
			ulong carry = 0;
             unsafe 
             {
                 ref ulong rA = ref MemoryMarshal.GetReference(a);
                 ref ulong rB = ref MemoryMarshal.GetReference(b);
                 ref ulong rR = ref MemoryMarshal.GetReference(r);

                 fixed (ulong* pA = &rA)
                 fixed (ulong* pB = &rB)
                 fixed (ulong* pR = &rR)
                 {
                     IntPtr ipA = (IntPtr)pA;
                     IntPtr ipB = (IntPtr)pB;
                     IntPtr ipR = (IntPtr)pR;
                     carry = Add(ipA, ipB, ipR, a.Length, b.Length, r.Length);
                 }
             }
			 return carry;
        }
		
		protected abstract ulong Add(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);
		
		public int Sub(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
             int compareResult;
            unsafe
            {
                ref ulong rA = ref MemoryMarshal.GetReference(a);
                ref ulong rB = ref MemoryMarshal.GetReference(b);
                ref ulong rR = ref MemoryMarshal.GetReference(r);

                fixed (ulong* pA = &rA)
                fixed (ulong* pB = &rB)
                fixed (ulong* pR = &rR)
                {
                    IntPtr ipA = (IntPtr)pA;
                    IntPtr ipB = (IntPtr)pB;
                    IntPtr ipR = (IntPtr)pR;
                    compareResult = Sub(ipA, ipB, ipR, a.Length, b.Length, r.Length);
                }
            }
            return compareResult;
        }

		protected abstract int Sub(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);
		
        public void Mult(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            unsafe
            {
                ref ulong rA = ref MemoryMarshal.GetReference(a);
                ref ulong rB = ref MemoryMarshal.GetReference(b);
                ref ulong rR = ref MemoryMarshal.GetReference(r);

                fixed (ulong* pA = &rA)
                fixed (ulong* pB = &rB)
                fixed (ulong* pR = &rR)
                {
                    IntPtr ipA = (IntPtr)pA;
                    IntPtr ipB = (IntPtr)pB;
                    IntPtr ipR = (IntPtr)pR;
                    Mult(ipA, ipB, ipR, a.Length, b.Length, r.Length);
                }
            }
        }
		
		protected abstract void Mult(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR);
		
		public void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder)
        {
			unsafe
            {
                ref ulong rA = ref MemoryMarshal.GetReference(dividend);
                ref ulong rB = ref MemoryMarshal.GetReference(divisor);
                ref ulong rQ = ref MemoryMarshal.GetReference(quotient);
                ref ulong rR = ref MemoryMarshal.GetReference(remainder);

                fixed (ulong* pA = &rA)
                fixed (ulong* pB = &rB)
                fixed (ulong* pQ = &rQ)
                fixed (ulong* pR = &rR)
                {
                    IntPtr ipA = (IntPtr)pA;
                    IntPtr ipB = (IntPtr)pB;
                    IntPtr ipQ = (IntPtr)pQ;
                    IntPtr ipR = (IntPtr)pR;
                    Div(ipA, ipB, ipQ, ipR, dividend.Length, divisor.Length);
                }
            }
        }
		
		protected abstract void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB);
    }
}
