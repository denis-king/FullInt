using System;

namespace FullInt.Core
{
    public class FullInt64CoreDefault : IFullIntCore
    {
        private static toBase Base = toBase.hex;


        // 64 x 64 -> 128: FullHelper.BigMul (.NET 5+: Math.BigMul, one mul / mulx in the JIT's code;
        // netstandard2.0: the 32-bit split)
        protected virtual void Mult128(ulong x, ulong y, bool isDec, out ulong lo, out ulong hi)
        {
            hi = FullHelper.BigMul(x, y, out lo);
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

        private static int LeadingZeroCount(ulong x) => FullHelper.LeadingZeroCount(x);   // lzcnt on .NET 5+

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
        // result = value1 + value2 over result.Length words (the operands' missing top words 0); returns the
        // carry out of result's top word. Word by word from the low end, the carry as 0 / 1.
        public ulong Add(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            int i1 = value1.Length, i2 = value2.Length, ir = result.Length;
            ulong carry = 0;
            while (ir > 0 && i1 > 0 && i2 > 0)
            {
                ulong a = value1[--i1], b = value2[--i2];
                ulong t = a + b;
                ulong c = t < a ? 1UL : 0UL;
                t += carry;
                c += t < carry ? 1UL : 0UL;
                result[--ir] = t;
                carry = c;
            }
            if (i2 > 0)   // the longer one's top words with the carry
            {
                value1 = value2;
                i1 = i2;
            }
            while (ir > 0 && i1 > 0)
            {
                ulong t = value1[--i1] + carry;
                carry = t < carry ? 1UL : 0UL;
                result[--ir] = t;
            }
            if (ir > 0)
            {
                result[--ir] = carry;
                carry = 0;
                result.Slice(0, ir).Clear();
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

            // the divisor's top two words normalized (shifted left until the top bit is set) and the
            // reciprocal of that pair: every digit then starts from one 3-by-2 division of the window's top
            // words (FullHelper.Div3By2)
            int s = LeadingZeroCount(divisor[0]);
            ulong d1 = 0, d0 = 0, v = 0;
            if (n >= 2)
            {
                d1 = Shld(divisor[0], divisor[1], s);
                d0 = Shld(divisor[1], n > 2 ? divisor[2] : 0, s);
                v = FullHelper.Reciprocal3By2(d1, d0);
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
                    digit = DivDigit(R, divisor, n, s, d1, d0, v);
                }
                quotient[qi++] = digit;

                if (pos == m) break;

                // shift the window one word left, bring in the next dividend word
                R[0] = 0;
                R.Slice(2, n).CopyTo(R.Slice(1, n));
                R[n + 1] = dividend[pos++];
            }
        }

        private ulong DivDigit(Span<ulong> R, Span<ulong> D, int n, int s, ulong d1, ulong d0, ulong v)
        {
            ulong uhi = R[1], ulo = R[2];

            if (n == 1)
            {
                ulong digit1 = Div128(uhi, ulo, D[0], out ulong rem);
                R[1] = 0;
                R[2] = rem;
                return digit1;
            }

            // the window R[1 .. n+1] < D * b: its top three words normalized as the divisor's; their 3-by-2
            // quotient is at least the digit (the left-out words only lower the true one) and at most a few
            // above it - the corrections below take that
            ulong u2 = Shld(uhi, ulo, s);
            ulong u1 = Shld(ulo, R[3], s);
            ulong u0 = Shld(R[3], n > 2 ? R[4] : 0, s);
            ulong q = u2 > d1 || (u2 == d1 && u1 >= d0)
                ? ulong.MaxValue                                   // the top words equal the divisor's
                : FullHelper.Div3By2(u2, u1, u0, d1, d0, v, out _, out _);

            // window -= q * D  (D aligned with R[2 .. n+1], borrow runs into R[1], R[0])
            ulong carry = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                ulong hi = FullHelper.BigMul(q, D[i], out ulong lo);
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
            if (dividend.Length < 3)   // the reciprocal costs about one Div128
            {
                for (int i = 0; i < dividend.Length; i++)
                {
                    quotient[i] = Div128(r, dividend[i], divisor, out r);
                }
                remainder = r;
                return;
            }
            // (r : w) / d = ((r : w) << s) / (d << s): the normalized divisor's reciprocal once, then one
            // product a word (FullHelper.Div2By1) instead of a 128 / 64 division
            int s = LeadingZeroCount(divisor);
            ulong dn = divisor << s, v = FullHelper.Reciprocal2By1(dn);
            for (int i = 0; i < dividend.Length; i++)
            {
                ulong w = dividend[i];
                ulong u1 = s == 0 ? r : (r << s) | (w >> (64 - s));
                quotient[i] = FullHelper.Div2By1(u1, w << s, dn, v, out ulong rn);
                r = rn >> s;
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

        private const ulong Base10 = 10000000000000000000UL;   // 10^19: the top bit set (normalized)
        private static readonly ulong Base10Reciprocal = FullHelper.Reciprocal2By1(Base10);

        public bool ToDecFraction(Span<ulong> f, int m, Span<byte> digits) => false;

        public void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex)
        {
            // repeated division by 10^19 (as ToDecString.asm), each word by the reciprocal of 10^19; digits
            // written right-aligned
            ulong[] tmp = value.ToArray();
            int start = 0;
            while (start < tmp.Length && tmp[start] == 0) start++;

            int pos = resultBuf.Length;
            while (true)
            {
                ulong rem = 0;
                for (int i = start; i < tmp.Length; i++)
                {
                    tmp[i] = FullHelper.Div2By1(rem, tmp[i], Base10, Base10Reciprocal, out rem);
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

        // result = value1 * value2: rows (operand scanning), one row per word of the shorter operand, the
        // longer one's words times it added into result from the low end with a carry word. result.Length
        // >= the two lengths: its words above the product 0; shorter: the low words of the product.
        public void Mult(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            int lr = result.Length;
            if (lr < value1.Length + value2.Length)
            {
                var full = new ulong[value1.Length + value2.Length];
                Mult(value1, value2, full);
                full.AsSpan(full.Length - lr).CopyTo(result);
                return;
            }
            if (value1.Length < value2.Length)
            {
                var t = value1;
                value1 = value2;
                value2 = t;
            }
            int la = value1.Length, lb = value2.Length;
            result.Clear();
            for (int j = 0; j < lb; j++)
            {
                ulong bj = value2[lb - 1 - j];
                if (bj == 0) continue;
                ulong carry = 0;
                int k = lr - 1 - j;   // result's word of a_0 * b_j
                for (int i = la - 1; i >= 0; i--, k--)
                {
                    ulong hi = FullHelper.BigMul(value1[i], bj, out ulong lo);
                    lo += carry;
                    hi += lo < carry ? 1UL : 0UL;
                    ulong t = result[k] + lo;
                    hi += t < lo ? 1UL : 0UL;
                    result[k] = t;
                    carry = hi;
                }
                result[k] = carry;   // the row's top word: not written by the rows before
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
            
            // |G| - |L| from the low end, the borrow as 0 / 1
            int ig = valueG.Length, il = valueL.Length, ir = result.Length;
            ulong borrow = 0;
            while (ir > 0 && il > 0)
            {
                ulong a = ig > 0 ? valueG[--ig] : 0, b = valueL[--il];
                ulong d = a - b;
                ulong bw = a < b ? 1UL : 0UL;
                bw |= d < borrow ? 1UL : 0UL;
                result[--ir] = d - borrow;
                borrow = bw;
            }
            while (ir > 0 && ig > 0)
            {
                ulong a = valueG[--ig];
                result[--ir] = a - borrow;
                borrow = a < borrow ? 1UL : 0UL;
            }
            if (ir > 0) result.Slice(0, ir).Clear();
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
            // the digits as base-10^19 blocks, the most significant first
            int len = value.Length;
            int firstBlockSize = len % 19;
            if (firstBlockSize == 0) firstBlockSize = 19;
            ulong[] decBuf = new ulong[(len - firstBlockSize) / 19 + 1];

            int pos = 0, count = 0;
            ulong block = 0;
            for (int i = 0; i < firstBlockSize; i++)
                block = block * 10 + (ulong)(value[pos++] - (byte)'0');
            decBuf[count++] = block;
            while (pos < len)
            {
                block = 0;
                for (int i = 0; i < 19 && pos < len; i++)
                    block = block * 10 + (ulong)(value[pos++] - (byte)'0');
                decBuf[count++] = block;
            }

            // base 10^19 -> base 2^64: each pass divides the blocks by 2^64 (block = remainder * 10^19 + block,
            // the high word the quotient, the low word the next remainder); the last remainder is a binary word
            int hexIndex = resultBuf.Length;
            int start = 0;
            while (start < count)
            {
                ulong remainder = 0;
                for (int i = start; i < count; i++)
                {
                    ulong hi = FullHelper.BigMul(remainder, Base10, out ulong lo);
                    ulong blockLo = lo + decBuf[i];
                    if (blockLo < lo) hi++;
                    decBuf[i] = hi;
                    remainder = blockLo;
                }
                resultBuf[--hexIndex] = remainder;
                while (start < count && decBuf[start] == 0) start++;
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