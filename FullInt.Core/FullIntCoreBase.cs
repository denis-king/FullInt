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
		
		public void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder)
        {
			unsafe
            {
                ref ulong rA = ref MemoryMarshal.GetReference(dividend);
                ref ulong rQ = ref MemoryMarshal.GetReference(quotient);

                fixed (ulong* pA = &rA)
                fixed (ulong* pQ = &rQ)
                {
                    IntPtr ipA = (IntPtr)pA;
                    IntPtr ipQ = (IntPtr)pQ;
                    Div(ipA, divisor, ipQ, out remainder, dividend.Length);
                }
            }
        }
		
		public unsafe ulong DivExact(Span<ulong> a, ulong d, Span<ulong> q)
		{
			fixed (ulong* pA = a)
			fixed (ulong* pQ = q)
			{
				return DivExact((IntPtr)pA, (IntPtr)pQ, a.Length, d);
			}
		}

		protected abstract ulong DivExact(IntPtr a, IntPtr q, int len, ulong d);

		protected abstract void Div(IntPtr a, IntPtr b, IntPtr q, IntPtr r, int lenA, int lenB);
		protected abstract void Div(IntPtr a, ulong b, IntPtr q, out ulong r, int lenA);
		
		public int Compare(Span<ulong> a, Span<ulong> b)
		{
			int compareResult;
            unsafe
            {
                ref ulong rA = ref MemoryMarshal.GetReference(a);
                ref ulong rB = ref MemoryMarshal.GetReference(b);

                fixed (ulong* pA = &rA)
                fixed (ulong* pB = &rB)
                {
                    IntPtr ipA = (IntPtr)pA;
                    IntPtr ipB = (IntPtr)pB;
                    compareResult = Compare(ipA, ipB, a.Length, b.Length);
                }
            }
            return compareResult;
		}
		
		protected abstract int Compare(IntPtr a, IntPtr b, int lenA, int lenB);
        
        public void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex)
        {
            ulong[]? rented = null;
            Span<ulong> valueBuf = value.Length <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[value.Length]
                : FullHelper.RentZeroed(value.Length, out rented);
            resultStartIndex = resultBuf.Length; 
            unsafe
            {
                ref ulong rValue = ref MemoryMarshal.GetReference(value);
                ref ulong rValueBuf = ref MemoryMarshal.GetReference(valueBuf);
                ref byte rResultBuf = ref MemoryMarshal.GetReference(resultBuf);

                fixed (ulong* pValue = &rValue)
                fixed (ulong* pValueBuf = &rValueBuf)
                fixed (byte* pResultBuf = &rResultBuf)
                {
                    IntPtr ipValue = (IntPtr)pValue;
                    IntPtr ipValueBuf = (IntPtr)pValueBuf;
                    IntPtr ipResultBuf = (IntPtr)pResultBuf;
                    ToDecString(ipValue, ipValueBuf, ipResultBuf, value.Length, ref resultStartIndex);
                }
            }
            FullHelper.Return(rented);
        }
        
        protected abstract void ToDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        public void FromDecString(Span<byte> value, Span<ulong> resultBuf, out int resultStartIndex)
        {
            resultStartIndex = resultBuf.Length; 
            int valueWords = value.Length / 19 + 1;
            ulong[]? rented = null;
            Span<ulong> valueBuf = valueWords <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[valueWords]
                : FullHelper.RentZeroed(valueWords, out rented);
            unsafe
            {
                ref byte rValue = ref MemoryMarshal.GetReference(value);
                ref ulong rValueBuf = ref MemoryMarshal.GetReference(valueBuf);
                ref ulong rResultBuf = ref MemoryMarshal.GetReference(resultBuf);

                fixed (byte* pValue = &rValue)
                fixed (ulong* pValueBuf = &rValueBuf)
                fixed (ulong* pResultBuf = &rResultBuf)
                {
                    IntPtr ipValue = (IntPtr)pValue;
                    IntPtr ipValueBuf = (IntPtr)pValueBuf;
                    IntPtr ipResultBuf = (IntPtr)pResultBuf;
                    FromDecString(ipValue, ipValueBuf, ipResultBuf, value.Length, ref resultStartIndex);
                }
            }
            FullHelper.Return(rented);
        }
        
        protected abstract void FromDecString(IntPtr value, IntPtr valueBuf, IntPtr resultBuf, int lenValue, ref int lenResult);

        #region bitwise
        public int And(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(0, a, negA, b, negB, r);
        public int Or(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(1, a, negA, b, negB, r);
        public int Xor(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(2, a, negA, b, negB, r);

        private unsafe int Bitwise(int op, Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
        {
            if (a.Length < 1 || b.Length < 1 || r.Length < Math.Max(a.Length, b.Length) + 1)
            {
                throw new ArgumentException("Bitwise: operands must be non-empty and r.Length >= max(a.Length, b.Length) + 1");
            }
            int signs = (negA ? 1 : 0) | (negB ? 2 : 0);

            fixed (ulong* pA = a)
            fixed (ulong* pB = b)
            fixed (ulong* pR = r)
            {
                switch (op)
                {
                    case 0: return And((IntPtr)pA, (IntPtr)pB, (IntPtr)pR, a.Length, b.Length, r.Length, signs);
                    case 1: return Or((IntPtr)pA, (IntPtr)pB, (IntPtr)pR, a.Length, b.Length, r.Length, signs);
                    default: return Xor((IntPtr)pA, (IntPtr)pB, (IntPtr)pR, a.Length, b.Length, r.Length, signs);
                }
            }
        }

        protected abstract int And(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        protected abstract int Or(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);
        protected abstract int Xor(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int lenR, int signs);

        public unsafe void ShiftLeft(Span<ulong> a, int shift, Span<ulong> r)
        {
            if (shift < 0 || a.Length < 1 || r.Length < a.Length + shift / 64 + 1)
            {
                throw new ArgumentException("ShiftLeft: shift >= 0, a non-empty, r.Length >= a.Length + shift / 64 + 1");
            }
            fixed (ulong* pA = a)
            fixed (ulong* pR = r)
            {
                ShiftLeft((IntPtr)pA, (IntPtr)pR, a.Length, r.Length, shift);
            }
        }

        public unsafe void ShiftRight(Span<ulong> a, int shift, Span<ulong> r)
        {
            if (shift < 0 || a.Length < 1 || r.Length < Math.Max(a.Length - shift / 64, 1))
            {
                throw new ArgumentException("ShiftRight: shift >= 0, a non-empty, r.Length >= max(a.Length - shift / 64, 1)");
            }
            fixed (ulong* pA = a)
            fixed (ulong* pR = r)
            {
                ShiftRight((IntPtr)pA, (IntPtr)pR, a.Length, r.Length, shift);
            }
        }

        protected abstract void ShiftLeft(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        protected abstract void ShiftRight(IntPtr a, IntPtr r, int lenA, int lenR, int shift);
        #endregion

    }
}
