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

		public unsafe void Sqr(Span<ulong> a, Span<ulong> r)
		{
			fixed (ulong* pA = a)
			fixed (ulong* pR = r)
			{
				Sqr((IntPtr)pA, (IntPtr)pR, a.Length, r.Length);
			}
		}

		public unsafe void MultLow(Span<ulong> a, Span<ulong> b, Span<ulong> r)
		{
			fixed (ulong* pA = a)
			fixed (ulong* pB = b)
			fixed (ulong* pR = r)
			{
				MultLow((IntPtr)pA, (IntPtr)pB, (IntPtr)pR, a.Length, b.Length, r.Length);
			}
		}

		// a low-half kernel where there is one; otherwise the full product, its low words kept
		protected virtual unsafe void MultLow(IntPtr a, IntPtr b, IntPtr r, int lenA, int lenB, int n)
		{
			ulong[] full = new ulong[lenA + lenB];
			fixed (ulong* pF = full)
			{
				Mult(a, b, (IntPtr)pF, lenA, lenB, lenA + lenB);
			}
			new Span<ulong>(full, lenA + lenB - n, n).CopyTo(new Span<ulong>((void*)r, n));
		}

		// a square kernel where there is one; the multiplication a·a otherwise
		protected virtual void Sqr(IntPtr a, IntPtr r, int lenA, int lenR) => Mult(a, a, r, lenA, lenA, lenR);
		
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

		public unsafe void Quotient(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient)
		{
			int la = dividend.Length, lb = divisor.Length, lq = quotient.Length;
			if (lb >= QuotientWordsMin && la > lb)
			{
				int words = lq + 1 + la + 3;   // Q' (lq + 1), the kernel's work (la + 3)
				ulong[]? rented = null;
				Span<ulong> buf = words <= FullHelper.StackAllocLimitWords
					? stackalloc ulong[words]
					: (rented = System.Buffers.ArrayPool<ulong>.Shared.Rent(words));
				bool done;
				fixed (ulong* pA = dividend)
				fixed (ulong* pB = divisor)
				fixed (ulong* pQ = quotient)
				fixed (ulong* pW = buf)
				{
					done = QuotientKernel((IntPtr)pA, (IntPtr)pB, (IntPtr)pW, la, lb, (IntPtr)(pW + lq + 1))
						&& QuotientFinish(pA, pB, pQ, pW, la, lb, lq, pW + lq + 1);
				}
				if (rented != null) System.Buffers.ArrayPool<ulong>.Shared.Return(rented);
				if (done) return;
			}
			int rn = lb + 2;
			ulong[]? rRented = null;
			Span<ulong> r = rn <= FullHelper.StackAllocLimitWords ? stackalloc ulong[rn] : FullHelper.RentZeroed(rn, out rRented);
			Div(dividend, divisor, quotient, r);
			FullHelper.Return(rRented);
		}

		// below: the guard word's extra step costs more than the shorter passes save (x64, Zen 3,
		// 2N / N against Div: 3-6 words 1.15-1.38, 8-12 0.9-1.1, 16 0.84, 32 0.74, 64 0.63)
		protected const int QuotientWordsMin = 16;

		// the approximate quotient: Q' = lenA - lenB + 2 words into qa (floor(A·β / B) within 1),
		// work: lenA + 3 words; false: no such kernel here, or it saturated (divide exactly)
		protected virtual bool QuotientKernel(IntPtr a, IntPtr b, IntPtr qa, int lenA, int lenB, IntPtr work) => false;

		// Q' -> q: the words above the guard; a guard of 0 or β - 1 (an exact multiple: 0) leaves
		// the quotient they or one less / one more, decided by D = A - q·B (p: la + 1 words)
		private unsafe bool QuotientFinish(ulong* a, ulong* b, ulong* q, ulong* qa, int la, int lb, int lq, ulong* p)
		{
			for (int i = 0; i < lq; i++) q[i] = qa[i];
			if (qa[lq] + 1 > 1) return true;
			Mult((IntPtr)q, (IntPtr)b, (IntPtr)p, lq, lb, la + 1);
			ulong borrow = 0;
			for (int i = la; i >= 0; i--)   // p = [0, A] - p
			{
				ulong x = i > 0 ? a[i - 1] : 0, y = p[i];
				ulong d = x - y;
				ulong b1 = x < y ? 1UL : 0UL;
				p[i] = d - borrow;
				borrow = b1 | (d < borrow ? 1UL : 0UL);
			}
			if (borrow != 0)   // q·B > A: q - 1
			{
				for (int i = lq - 1; i >= 0; i--)
					if (q[i]-- != 0) break;
				return true;
			}
			int s = 0;
			while (s <= la && p[s] == 0) s++;
			if (s <= la && Compare((IntPtr)(p + s), (IntPtr)b, la + 1 - s, lb) >= 0)   // D >= B: q + 1
				for (int i = lq - 1; i >= 0; i--)
					if (++q[i] != 0) break;
			return true;
		}
		
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
