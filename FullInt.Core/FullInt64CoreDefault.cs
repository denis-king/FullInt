using System;
using System.Collections.Generic;

namespace FullInt.Core
{
    public class FullInt64CoreDefault : IFullIntCore
    {
        private static toBase Base = toBase.hex;


        protected virtual void Mult128(ulong x, ulong y, bool isDec, out ulong lo, out ulong hi)
        {
            const ulong mask32 = 0xFFFFFFFF;

            ulong x0 = x & mask32;
            ulong x1 = x >> 32;
            ulong y0 = y & mask32;
            ulong y1 = y >> 32;

            ulong z0 = x0 * y0;
            ulong z1 = x0 * y1;
            ulong z2 = x1 * y0;
            ulong z3 = x1 * y1;

            ulong mid1 = z1 + (z0 >> 32);
            ulong mid2 = z2 + (mid1 & mask32);

            hi = z3 + (mid1 >> 32) + (mid2 >> 32);
            lo = (mid2 << 32) | (z0 & mask32);
        }
		
        public void Multiply(ulong a, ulong b, bool isDec, out ulong low, out ulong high)
        {
            high = 0;
            low = 0;
           
			Mult128(a, b, isDec, out low, out high);

            
            if (isDec)
            {
                high = Div128(high, low, FullInt64.maxDecCapasity, out low);
            }
        }

        // (hi:lo) / divisor, requires hi < divisor (quotient fits 64 bits) — same contract as x64 `div r64`.
        // netstandard2.1 has no UInt128 / Math.BigMul, so this is Hacker's Delight `divlu`
        // (two 64/32 steps on a normalized divisor).
        protected virtual ulong Div128(ulong hi, ulong lo, ulong divisor, out ulong remainder)
        {
            if (divisor == 0) throw new DivideByZeroException();
            if (hi >= divisor) throw new OverflowException("Div128: quotient does not fit 64 bits");

            const ulong b = 1UL << 32;
            int s = LeadingZeroCount(divisor);
            ulong v = divisor << s;
            ulong vn1 = v >> 32, vn0 = v & 0xFFFFFFFF;

            ulong un32 = s == 0 ? hi : (hi << s) | (lo >> (64 - s));
            ulong un10 = lo << s;
            ulong un1 = un10 >> 32, un0 = un10 & 0xFFFFFFFF;

            ulong q1 = un32 / vn1;
            ulong rhat = un32 - q1 * vn1;
            while (q1 >= b || q1 * vn0 > b * rhat + un1)
            {
                q1--;
                rhat += vn1;
                if (rhat >= b) break;
            }

            ulong un21 = un32 * b + un1 - q1 * v;
            ulong q0 = un21 / vn1;
            rhat = un21 - q0 * vn1;
            while (q0 >= b || q0 * vn0 > b * rhat + un0)
            {
                q0--;
                rhat += vn1;
                if (rhat >= b) break;
            }

            remainder = (un21 * b + un0 - q0 * v) >> s;
            return q1 * b + q0;
        }

        private static int LeadingZeroCount(ulong x)
        {
            if (x == 0) return 64;
            int n = 0;
            if (x <= 0x0000_0000_FFFF_FFFFUL) { n += 32; x <<= 32; }
            if (x <= 0x0000_FFFF_FFFF_FFFFUL) { n += 16; x <<= 16; }
            if (x <= 0x00FF_FFFF_FFFF_FFFFUL) { n += 8; x <<= 8; }
            if (x <= 0x0FFF_FFFF_FFFF_FFFFUL) { n += 4; x <<= 4; }
            if (x <= 0x3FFF_FFFF_FFFF_FFFFUL) { n += 2; x <<= 2; }
            if (x <= 0x7FFF_FFFF_FFFF_FFFFUL) { n += 1; }
            return n;
        }

        // shld dst, src, s  (s = 0..63)
        private static ulong Shld(ulong dst, ulong src, int s) => s == 0 ? dst : (dst << s) | (src >> (64 - s));
        protected virtual ulong Adc64(ulong a, ulong b, ref byte carry)
        {
            //save adding two ulongs and carry
            var sum1 = a + b;
            bool carry1 = sum1 < a || sum1 < b;
            var sum = sum1 + carry;
            carry1 |= sum < sum1;
            carry = (byte)( carry1 ? 1 : 0);
            return sum;
        }
        protected virtual ulong Sbb64(ulong a, ulong b, ref byte borrow) 
        {
            ulong sum = 0;
            ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            if (a < b || a - b < borrow) // not `a < b + borrow`: b + borrow overflows for b = MaxValue
            {
                sum = baseMaxValue - b - borrow + a + 1;
                borrow = 1;
            }
            else
            {
                sum = a - b - borrow;
                borrow = 0;
            }
            return sum;
        }
        public ulong Add(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            ulong sum;
            ulong b1, b2;
            //ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var resultLastIndex = result.Length - 1;

            byte carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < value1.Length) { b1 = value1[value1LastIndex - i]; } else { b1 = 0; }
                if (i < value2.Length) { b2 = value2[value2LastIndex - i]; } else { b2 = 0; }


                
                sum = Adc64(b1, b2, ref carry);

                //decimal corrections
                if (Base == toBase.dec)
                {
                    carry = (byte)Div128(carry, sum, FullInt64.maxDecCapasity, out sum);
                    //if (carry == 1)
                    //{
                        
                    //}
                    //else if (sum > FullInt64.maxDecValue)
                    //{
                    //    sum = sum - FullInt64.maxDecCapasity;
                    //    carry = 1;
                    //}
                }
                
                result[resultLastIndex - i] = sum;
            }
            return carry;
        }

		

        #region division (managed mirror of Div.asm / Div2Long.asm)

        // Same algorithm and buffer contract as Div.asm, so both cores produce identical results:
        //   remainder: n + 2 words, [0] sign word, [1 .. n+1] working window, result in [2 .. n+1]
        //   quotient:  max(m - n + 1, 1) words, MSW first
        //   divisor[0] != 0 (normalized)
        // Digit estimate: q_min = U/(Dhi+1), q_max = U/Dhi, interpolation by the next divisor word,
        // clamped to [q_min, q_max], then corrected until exact.
        public void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder)
        {
            if (Compare(divisor, stackalloc ulong[] { 0 }) == 0)
            {
                throw new System.DivideByZeroException();
            }

            int m = dividend.Length, n = divisor.Length;
            var R = remainder.Slice(0, n + 2);
            R.Clear();

            if (m < n)
            {
                dividend.CopyTo(R.Slice(n + 2 - m)); // copy first: quotient may alias dividend
                quotient[0] = 0;
                return;
            }

            dividend.Slice(0, n).CopyTo(R.Slice(2));
            int pos = n, qi = 0;

            while (true)
            {
                ulong digit;
                int cmp = R[1] != 0 ? 1 : CompareWindow(R, divisor, n);
                if (cmp < 0)
                {
                    digit = 0;
                }
                else if (cmp == 0)
                {
                    digit = 1;
                    R.Slice(2, n).Clear();
                }
                else
                {
                    digit = DivDigit(R, divisor, n);
                }
                quotient[qi++] = digit;

                if (pos == m) break;

                // shift the window one word left, bring in the next dividend word
                R[0] = 0;
                for (int i = 0; i < n; i++) R[1 + i] = R[2 + i];
                R[n + 1] = dividend[pos++];
            }
        }

        private ulong DivDigit(Span<ulong> R, Span<ulong> D, int n)
        {
            ulong uhi = R[1], ulo = R[2], dhi = D[0];

            if (n == 1)
            {
                ulong digit1 = Div128(uhi, ulo, dhi, out ulong rem);
                R[1] = 0;
                R[2] = rem;
                return digit1;
            }

            // normalization shift (as in Div.asm: by Uhi | Dhi, Uhi <= Dhi, i.e. by Dhi)
            int s = LeadingZeroCount(uhi | dhi);
            ulong u2 = Shld(uhi, ulo, s);
            ulong u1 = Shld(ulo, R[3], s);
            ulong d1 = D[1], d2 = n > 2 ? D[2] : 0;
            ulong vhi = Shld(dhi, d1, s);
            ulong vlo = Shld(d1, d2, s);

            // step 1: max and min candidates
            ulong qmax = u2 >= vhi ? ulong.MaxValue : Div128(u2, u1, vhi, out _);
            ulong qmin = vhi == ulong.MaxValue ? u2 : Div128(u2, u1, vhi + 1, out _);

            // step 2: interpolate by the next divisor word, clamp to [min, max]
            ulong q = qmax;
            if (qmin != qmax)
            {
                ulong range = qmax - qmin + 1;
                if (range != 0)
                {
                    ulong step = ulong.MaxValue / range;
                    ulong offset = vlo / step;
                    q = offset > qmax ? qmin : qmax - offset;
                    if (q < qmin) q = qmin;
                }
            }

            // step 3: window -= q * D  (D aligned with R[2 .. n+1], borrow runs into R[1], R[0])
            ulong carry = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                Mult128(q, D[i], false, out ulong lo, out ulong hi);
                lo += carry;
                if (lo < carry) hi++;
                ulong r = R[i + 2];
                R[i + 2] = r - lo;
                if (r < lo) hi++;
                carry = hi;
            }
            ulong r1 = R[1];
            R[1] = r1 - carry;
            if (r1 < carry) R[0]--;

            // corrections until exact
            while (true)
            {
                if ((long)R[0] < 0)
                {
                    // negative: add D back; once non-negative it is already < D
                    do { AddWindow(R, D, n); q--; } while ((long)R[0] < 0);
                    break;
                }
                if (R[1] != 0 || CompareWindow(R, D, n) >= 0)
                {
                    SubWindow(R, D, n);
                    q++;
                    continue;
                }
                break;
            }
            return q;
        }

        // compare R[2 .. n+1] with D[0 .. n-1]
        private static int CompareWindow(Span<ulong> R, Span<ulong> D, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (R[2 + i] != D[i]) return R[2 + i] < D[i] ? -1 : 1;
            }
            return 0;
        }

        // R[0 .. n+1] += D (aligned at the end), wraps in n + 2 words
        private static void AddWindow(Span<ulong> R, Span<ulong> D, int n)
        {
            ulong carry = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                ulong a = R[i + 2], s1 = a + D[i], s2 = s1 + carry;
                carry = (s1 < a || s2 < s1) ? 1UL : 0UL;
                R[i + 2] = s2;
            }
            for (int i = 1; i >= 0 && carry != 0; i--)
            {
                R[i]++;
                carry = R[i] == 0 ? 1UL : 0UL;
            }
        }

        // R[0 .. n+1] -= D (aligned at the end), wraps in n + 2 words
        private static void SubWindow(Span<ulong> R, Span<ulong> D, int n)
        {
            ulong borrow = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                ulong a = R[i + 2], d1 = a - D[i], d2 = d1 - borrow;
                borrow = (a < D[i] || d1 < borrow) ? 1UL : 0UL;
                R[i + 2] = d2;
            }
            for (int i = 1; i >= 0 && borrow != 0; i--)
            {
                borrow = R[i] == 0 ? 1UL : 0UL;
                R[i]--;
            }
        }

        // Same contract as Div2Long.asm: every dividend word produces a quotient word
        // (leading zeros included), quotient may alias dividend.
        public void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder)
        {
            if (divisor == 0)
            {
                throw new System.DivideByZeroException();
            }
            ulong r = 0;
            for (int i = 0; i < dividend.Length; i++)
            {
                quotient[i] = Div128(r, dividend[i], divisor, out r);
            }
            remainder = r;
        }
        #endregion

        // the exact division: the remainder of the plain division is 0 then
        public ulong DivExact(Span<ulong> a, ulong d, Span<ulong> q)
        {
            Div(a, d, q, out ulong r);
            return r;
        }

        public void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex)
        {
            const ulong BASE10 = 10000000000000000000UL; // 10^19

            // repeated division by 10^19 (as ToDecString.asm); digits written right-aligned
            ulong[] tmp = value.ToArray();
            int start = 0;
            while (start < tmp.Length && tmp[start] == 0) start++;

            int pos = resultBuf.Length;
            while (true)
            {
                ulong rem = 0;
                for (int i = start; i < tmp.Length; i++)
                {
                    tmp[i] = Div128(rem, tmp[i], BASE10, out rem);
                }
                while (start < tmp.Length && tmp[start] == 0) start++;
                bool last = start == tmp.Length;

                // 19 digits per chunk; the most significant chunk without leading zeros
                for (int d = 0; d < 19; d++)
                {
                    resultBuf[--pos] = (byte)('0' + (int)(rem % 10));
                    rem /= 10;
                    if (last && rem == 0) break;
                }
                if (last) break;
            }
            resultStartIndex = pos;
        }

        public void Sqr(Span<ulong> a, Span<ulong> r) => Mult(a, a, r);

        public void Quotient(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient) =>
            Div(dividend, divisor, quotient, new ulong[divisor.Length + 2]);

        public void MultLow(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            var full = new ulong[a.Length + b.Length];
            Mult(a, b, full);
            full.AsSpan(full.Length - r.Length).CopyTo(r);
        }

        public void Mult(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            ulong multA, multB = 0;
            ulong multHi, multLo = 0;
            ulong addHi = 0, addMid = 0, addLo = 0;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var value1Index = 0;
            var value1ShiftedIndex = 0;
            var resultLastIndex = result.Length - 1;
            byte carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                value1Index = value1LastIndex - i;
                value1ShiftedIndex = value1Index;
                for (int j = 0; j < value2.Length; j++)
                {
                   
                    if (value1ShiftedIndex >= 0 && value1ShiftedIndex < value1.Length)
                    {
                        multA = value1[value1ShiftedIndex];
                        multB = value2[value2LastIndex - j];
                        if (multA == 0 || multB == 0)
                        {
                            value1ShiftedIndex++; // `continue` skipped the increment below
                            continue;
                        }
                        Multiply(multA, multB, Base == toBase.dec, out multLo, out multHi);
                        
                        //sum = sum + mult
                        carry = 0;
                        addLo = Adc64(addLo, multLo, ref carry);
                        addMid = Adc64(addMid, multHi, ref carry);
                        addHi += carry;
                    }
                    value1ShiftedIndex++;
                }

                result[resultLastIndex - i] = addLo;
                //свиг суммы вправо на 1 байт
                addLo = addMid;
                addMid = addHi;
                addHi = 0;
            }
        }

        public int Sub(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            Span<ulong> valueG;
            Span<ulong> valueL;

            //find the greate and less values
            var compareResult = Compare(value1, value2);

            if (compareResult > 0)
            {
                valueG = value1;
                valueL = value2;
            }
            else if (compareResult < 0)
            {
                valueG = value2;
                valueL = value1;
            }
            else
            {
                //prepare the zero result
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = 0;
                }
                return compareResult;
            }
            
            ulong sum;
            ulong b1, b2 = 0;
           
            byte borrow = 0;
            var valueGLastIndex = valueG.Length - 1;
            var valueLLastIndex = valueL.Length - 1;
            var resultLastIndex = result.Length - 1;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < valueG.Length) { b1 = valueG[valueGLastIndex - i]; } else { b1 = 0; }
                if (i < valueL.Length) { b2 = valueL[valueLLastIndex - i]; } else { b2 = 0; }

                sum = Sbb64(b1, b2, ref borrow);

                //dec correction
                if(Base == toBase.dec && borrow == 1)
                {
                    sum -= (ulong.MaxValue - FullInt64.maxDecValue);
                }
                //if (b1 < b2 + borrow)
                //{
                //    sum = baseMaxValue - b2 - borrow + b1 + 1 - long + dec);
                //    borrow = 1;
                //}
                //else
                //{
                //    sum = b1 - b2 - borrow;
                //    borrow = 0;
                //}
                result[resultLastIndex - i] = sum;
            }
			return compareResult;
        }
		
		public int Compare(Span<ulong> value1, Span<ulong> value2)
		{
			int result = 0;
            //toEqualBase(ref value1, ref value2);

            var zeros1 = FullHelper.CountZeros(value1);
            var zeros2 = FullHelper.CountZeros(value2);
            var value1Length = value1.Length - zeros1;
            var value2Length = value2.Length - zeros2;

            
            //comparison by length
            if (value1Length < value2Length)
            {
                result = -1;
            }
            else if (value1Length > value2Length)
            {
                result = 1;
            }
            else
            {
                //comparison by value
                for (int i = 0; i < value1Length; i++)
                {
                    if (value1[i + zeros1] < value2[i + zeros2])
                    {
                        result = -1;
                        break;
                    }
                    else if (value1[i + zeros1] > value2[i + zeros2])
                    {
                        result = 1;
                        break;
                    }
                }
            }
                
            return result;
		}
        
        
        public void FromDecString(Span<byte> value, Span<ulong> resultBuf, out int resultStartIndex)
        {
            const ulong BASE10 = 10000000000000000000UL; // 10^19

            // --- Шаг 1. Парсинг строки в массив десятичных слов ---
            int len = value.Length;
            int firstBlockSize = len % 19;
            if (firstBlockSize == 0) firstBlockSize = 19;

            List<ulong> decBuf = new List<ulong>();
            int pos = 0;

            // Первый блок
            ulong block = 0;
            for (int i = 0; i < firstBlockSize; i++)
                block = block * 10 + (ulong)(value[pos++] - (byte)'0');
            decBuf.Add(block);

            // Остальные блоки по 19 цифр
            while (pos < len)
            {
                block = 0;
                for (int i = 0; i < 19 && pos < len; i++)
                    block = block * 10 + (ulong)(value[pos++] - (byte)'0');
                decBuf.Add(block);
            }

            // --- Шаг 2. Многословное деление ---
            int hexIndex = resultBuf.Length;

            while (decBuf.Count > 0)
            {
                ulong remainder = 0;

                for (int i = 0; i < decBuf.Count; i++)
                {
                    // block = remainder * BASE10 + decBuf[i]
                    ulong hi, lo;
                    Mult128(remainder, BASE10, false, out lo, out hi);

                    ulong blockLo = lo + decBuf[i];
                    ulong blockHi = hi;
                    if (blockLo < lo) blockHi++;

                    // Делим на 2^64: hi = частное, lo = остаток
                    decBuf[i] = blockHi;
                    remainder = blockLo;
                }

                // Остаток = hex-слово
                resultBuf[--hexIndex] = remainder;

                // Убираем ведущие нули
                while (decBuf.Count > 0 && decBuf[0] == 0)
                    decBuf.RemoveAt(0);
            }

            resultStartIndex = hexIndex;
        }

        #region bitwise (managed reference of Logic.asm / Shift.asm)
        public int And(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(0, a, negA, b, negB, r);
        public int Or(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(1, a, negA, b, negB, r);
        public int Xor(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r) => Bitwise(2, a, negA, b, negB, r);

        private static ulong Op(int op, ulong x, ulong y) => op == 0 ? x & y : op == 1 ? x | y : x ^ y;

        private static int Bitwise(int op, Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r)
        {
            if (a.Length < 1 || b.Length < 1 || r.Length < Math.Max(a.Length, b.Length) + 1)
            {
                throw new ArgumentException("Bitwise: operands must be non-empty and r.Length >= max(a.Length, b.Length) + 1");
            }

            // two's complement on the fly: t = (word ^ mask) + carry
            ulong maskA = negA ? ulong.MaxValue : 0UL, carryA = negA ? 1UL : 0UL;
            ulong maskB = negB ? ulong.MaxValue : 0UL, carryB = negB ? 1UL : 0UL;
            ulong maskR = Op(op, maskA, maskB), carryR = maskR & 1UL;

            int ia = a.Length, ib = b.Length;
            for (int ir = r.Length - 1; ir >= 0; ir--)
            {
                ulong wa = (ia > 0 ? a[--ia] : 0UL) ^ maskA;
                ulong ta = wa + carryA;
                carryA = ta < wa ? 1UL : 0UL;

                ulong wb = (ib > 0 ? b[--ib] : 0UL) ^ maskB;
                ulong tb = wb + carryB;
                carryB = tb < wb ? 1UL : 0UL;

                ulong w = Op(op, ta, tb) ^ maskR;
                ulong tr = w + carryR;
                carryR = tr < w ? 1UL : 0UL;
                r[ir] = tr;
            }
            return (int)(maskR & 1UL);
        }

        public void ShiftLeft(Span<ulong> a, int shift, Span<ulong> r)
        {
            int ws = shift >> 6, s = shift & 63;
            if (shift < 0 || a.Length < 1 || r.Length < a.Length + ws + 1)
            {
                throw new ArgumentException("ShiftLeft: shift >= 0, a non-empty, r.Length >= a.Length + shift / 64 + 1");
            }
            int lead = r.Length - (a.Length + 1 + ws);
            int k = 0;
            for (; k < lead; k++) r[k] = 0;

            ulong prev = 0;
            for (int i = 0; i < a.Length; i++)
            {
                ulong cur = a[i];
                r[k++] = s == 0 ? prev : (prev << s) | (cur >> (64 - s));
                prev = cur;
            }
            r[k++] = prev << s;
            for (; k < r.Length; k++) r[k] = 0;
        }

        public void ShiftRight(Span<ulong> a, int shift, Span<ulong> r)
        {
            int ws = shift >> 6, s = shift & 63;
            if (shift < 0 || a.Length < 1 || r.Length < Math.Max(a.Length - ws, 1))
            {
                throw new ArgumentException("ShiftRight: shift >= 0, a non-empty, r.Length >= max(a.Length - shift / 64, 1)");
            }
            int count = a.Length - ws;
            if (count <= 0)
            {
                r.Clear();
                return;
            }
            int lead = r.Length - count;
            for (int k = 0; k < lead; k++) r[k] = 0;

            ulong prev = 0;
            for (int i = 0; i < count; i++)
            {
                ulong cur = a[i];
                r[lead + i] = s == 0 ? cur : (cur >> s) | (prev << (64 - s));
                prev = cur;
            }
        }
        #endregion
    }
}