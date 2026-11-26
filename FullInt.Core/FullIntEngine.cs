using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using System.Linq;
using System.Text;

namespace FullNumerics
{
    internal partial class FullIntEngine
    {
		private static readonly Lazy<FullIntEngine> _instance = new Lazy<FullIntEngine>(() => new FullIntEngine());
		public static FullIntEngine Instance => _instance.Value;
        
		// static readonly, set once at type init: the tier-1 JIT then knows its exact type and calls the
		// core directly (devirtualized, its wrappers inlined) - through a plain static field every short
		// operation paid an interface and a virtual dispatch (a + b into r on 2 words: ~5 ns of 14)
		private static readonly IFullIntCore _core = FullIntCoreFactory.Create();
		
		private FullIntEngine()
		{
		}
        #region add methods
        public FullInt add(FullInt value1, FullInt value2)
        {
            if (value1.value.Length == 1 && value2.value.Length == 1)
            {
                return addOneWord(value1.value[0], value1.IsNegative, value2.value[0], value2.IsNegative);
            }
            return addOrSub(value1, value2, negate2: false);
        }

        // a + b, or a - b with negate2 (no FullInt is made for -b). The signs are read once.
        private FullInt addOrSub(FullInt value1, FullInt value2, bool negate2)
        {
            bool neg1 = value1.IsNegative, neg2 = value2.IsNegative != negate2;   // -0 is fine here: a zero result is normalized on read
            ulong[] a = value1.value, b = value2.value;

            if (a.Length == 1 && b.Length == 1)
            {
                return addOneWord(a[0], neg1, b[0], neg2);
            }

            // The two magnitude paths are separate methods: with Dynamic PGO (.NET 8) one shared
            // method gets compiled on the profile of whichever path ran first, and the other one
            // then runs as a cold path (measured ~10-20 ns slower at 2-8 words).
            return neg1 == neg2 ? addMagnitudes(a, b, neg1) : subMagnitudes(a, b, neg1, neg2);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private FullInt addMagnitudes(ulong[] a, ulong[] b, bool negative)
        {
            // Exact size, no word reserved for the carry: one allocation and no copy in the
            // common case. When the top words of equal-length operands overflow, the carry out
            // is certain: the wider result is allocated at once (the core writes the carry into
            // its extra top word). Otherwise a carry out (rare) widens the result by one word.
            int n = Math.Max(a.Length, b.Length);
            bool carryCertain = a.Length == b.Length && a[0] + b[0] < a[0];
            var r = FullHelper.NewWords(carryCertain ? n + 1 : n);   // the core writes every word
            ulong carry = addInternal(a, b, r);
            if (carry != 0)
            {
                var wider = new ulong[r.Length + 1];
                wider[0] = carry;
                r.CopyTo(wider, 1);
                r = wider;
            }
            else
            {
                r = FullHelper.Trimmed(r);
            }
            return new FullInt(negative, r);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private FullInt subMagnitudes(ulong[] a, ulong[] b, bool neg1, bool neg2)
        {
            var r = FullHelper.NewWords(Math.Max(a.Length, b.Length));   // |a - b| always fits; the core writes every word
            int compareResult = subInternal(a, b, r);
            bool sign = compareResult > 0 ? neg1 : compareResult < 0 ? neg2 : false;
            return new FullInt(sign, FullHelper.Trimmed(r));
        }
        // One word and one word: plain 64-bit arithmetic in C#. The native call costs more than
        // the add itself at this size. (A zero result is never negative: IsNegative normalizes on read.)
        private static FullInt addOneWord(ulong a, bool aNegative, ulong b, bool bNegative)
        {
            if (aNegative == bNegative)
            {
                ulong sum = a + b;
                return new FullInt(aNegative, sum < a ? new ulong[] { 1UL, sum } : new ulong[] { sum });
            }
            // opposite signs: the larger magnitude minus the smaller, with its sign
            return a >= b
                ? new FullInt(aNegative, new ulong[] { a - b })
                : new FullInt(bNegative, new ulong[] { b - a });
        }

        public void add(FullInt value1, FullInt value2, FullInt result, bool useCarry = true)
        {
            if(result.value.Length < Math.Max(value1.value.Length, value2.value.Length) + (useCarry ? 1 : 0))
            {
                throw new Exception("the result bufer size is not enought");
            }

            var carry = addSigned(value1, value2, result);
            if (!useCarry && carry > 0)
            {
                throw new Exception("result buffer is oveflow");
            }
        }

        // Signed add into result.value; returns the carry out of the top word (same signs only).
        private ulong addSigned(FullInt value1, FullInt value2, FullInt result)
        {
            if (value1.IsNegative == value2.IsNegative)
            {
                var carry = addInternal(value1.value, value2.value, result.value);
                result.IsNegative = value1.IsNegative;
                return carry;
            }
            else
            {
                var compareResult = subInternal(value1.value, value2.value, result.value);
               
                if (compareResult > 0)
                {
                    result.IsNegative = value1.IsNegative;
                }
                else if (compareResult < 0)
                {
                    result.IsNegative = value2.IsNegative;
                }
                else
                {
                    result.IsNegative = false;
                }
                return 0;   // |a - b| always fits
            }
        }

        public FullInt sub(FullInt value1, FullInt value2)
        {
            if (value1.value.Length == 1 && value2.value.Length == 1)
            {
                return addOneWord(value1.value[0], value1.IsNegative, value2.value[0], !value2.IsNegative);   // -0 is fine: normalized on read
            }
            return addOrSub(value1, value2, negate2: true);
        }
        #region into an existing result (FullInt.Add(result, a, b) etc.)
        // Sizes come from the operands without their leading zero words, so a result reused in a
        // loop (result = result + b) keeps its length instead of growing by a word per call.
        // Negative right shifts and negative shift counts take the ordinary
        // operation and the result object just takes its value.

        public void addInto(FullInt result, FullInt value1, FullInt value2, bool negate2)
        {
            bool neg1 = value1.IsNegative, neg2 = value2.IsNegative != negate2;
            Span<ulong> a = FullHelper.DeleteZeros(value1.value), b = FullHelper.DeleteZeros(value2.value);
            if (a.Length == 1 && b.Length == 1)   // one word: plain 64-bit arithmetic, no core call
            {
                ulong x = a[0], y = b[0], hi = 0, lo;
                bool negative;
                if (neg1 == neg2) { lo = x + y; hi = lo < x ? 1UL : 0UL; negative = neg1; }
                else if (x >= y) { lo = x - y; negative = neg1; }
                else { lo = y - x; negative = neg2; }
                WriteTwoWords(result, hi, lo, negative);
                return;
            }
            int n = Math.Max(a.Length, b.Length);
            if (neg1 == neg2)
            {
                // r may be a's or b's array: the add reads both words before writing, from the
                // low end, and a trimmed operand ends where the array ends
                ulong[] r = result.WritableBuffer(n + 1, mustBeNew: false);
                _core.Add(a, b, r);
                result.SetResult(neg1);
            }
            else
            {
                ulong[] r = result.WritableBuffer(n, mustBeNew: false);
                int compareResult = _core.Sub(a, b, r);
                result.SetResult(compareResult > 0 ? neg1 : compareResult < 0 ? neg2 : false);
            }
        }

        public void multInto(FullInt result, FullInt value1, FullInt value2)
        {
            bool negative = value1.IsNegative != value2.IsNegative;
            Span<ulong> a = FullHelper.DeleteZeros(value1.value), b = FullHelper.DeleteZeros(value2.value);
            if (a.Length == 1 && b.Length == 1)   // one word by one word: FullHelper.BigMul, no core call
            {
                ulong hi = FullHelper.BigMul(a[0], b[0], out ulong lo);
                WriteTwoWords(result, hi, lo, negative);
                return;
            }
            int len = a.Length + b.Length;
            // the multiply reads the operands throughout: never into one of them
            bool aliased = ReferenceEquals(result.value, value1.value) || ReferenceEquals(result.value, value2.value);
            ulong[] r = result.WritableBuffer(len, mustBeNew: aliased);
            Span<ulong> low = r.AsSpan(r.Length - len);
            r.AsSpan(0, r.Length - len).Clear();
            if (FullHelper.MultShort(a, b, low))
            {
                // 2 x 1, 2 x 2: here
            }
            else if (a == b && a.Length < Karatsuba.SqrThreshold)
                _core.Sqr(a, low);                                       // x·x: the square kernel
            else if (Math.Min(a.Length, b.Length) >= Karatsuba.Threshold || a == b)
                Karatsuba.Multiply(_core, a, b, low);
            else
                _core.Mult(a, b, low);
            result.SetResult(negative);
        }

        // a one- or two-word value into result's buffer (the operands were read before)
        private static void WriteTwoWords(FullInt result, ulong hi, ulong lo, bool negative)
        {
            ulong[] r = result.WritableBuffer(2, mustBeNew: false);
            int top = r.Length - 2;
            if (top > 0) r.AsSpan(0, top).Clear();
            r[top] = hi;
            r[top + 1] = lo;
            result.SetResult(negative);
        }

        public void shiftLeftInto(FullInt result, FullInt value, int shift)
        {
            if (shift < 0)
            {
                result.Assign(shiftLeft(value, shift));
                return;
            }
            bool negative = value.IsNegative;
            Span<ulong> a = FullHelper.DeleteZeros(value.value);
            ulong[] r = result.WritableBuffer(a.Length + shift / 64 + 1, mustBeNew: ReferenceEquals(result.value, value.value));
            _core.ShiftLeft(a, shift, r);   // writes the lead zeros of a longer r
            result.SetResult(negative);
        }

        // op: 0 and, 1 or, 2 xor. Both non-negative: word by word into result's buffer;
        // otherwise the ordinary operation
        public void bitwiseInto(FullInt result, int op, FullInt value1, FullInt value2)
        {
            if (value1.IsNegative || value2.IsNegative)
            {
                result.Assign(bitwise(op, value1, value2));
                return;
            }
            Span<ulong> a = FullHelper.DeleteZeros(value1.value), b = FullHelper.DeleteZeros(value2.value);
            if (a.Length < b.Length) { var t = a; a = b; b = t; }
            int la = a.Length, lb = b.Length, d = la - lb, len = op == 0 ? lb : la;
            bool aliased = ReferenceEquals(result.value, value1.value) || ReferenceEquals(result.value, value2.value);
            ulong[] r = result.WritableBuffer(len, mustBeNew: aliased);
            int top = r.Length - len;
            if (top > 0) r.AsSpan(0, top).Clear();
            if (op == 0)
            {
                if (lb < 8) for (int i = 0; i < lb; i++) r[top + i] = a[d + i] & b[i];   // short: in place (a call cost 10%)
                else FullHelper.BitwiseWords(0, a.Slice(d), b, r.AsSpan(top, lb));
            }
            else
            {
                for (int i = 0; i < d; i++) r[top + i] = a[i];
                if (lb >= 8) FullHelper.BitwiseWords(op, a.Slice(d), b, r.AsSpan(top + d, lb));
                else if (op == 1) for (int i = 0; i < lb; i++) r[top + d + i] = a[d + i] | b[i];
                else for (int i = 0; i < lb; i++) r[top + d + i] = a[d + i] ^ b[i];
            }
            result.SetResult(false);
        }

        public void shiftRightInto(FullInt result, FullInt value, int shift)
        {
            if (shift < 0 || value.IsNegative)   // negative: floor rounding, the ordinary path
            {
                result.Assign(shiftRight(value, shift));
                return;
            }
            Span<ulong> a = FullHelper.DeleteZeros(value.value);
            ulong[] r = result.WritableBuffer(Math.Max(a.Length - shift / 64, 1), mustBeNew: ReferenceEquals(result.value, value.value));
            _core.ShiftRight(a, shift, r);   // writes the lead zeros of a longer r
            result.SetResult(false);
        }
        #endregion

        private ulong addInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            return _core.Add(value1, value2, result);
        }
        
        private int subInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
			return _core.Sub(value1, value2, result);
        }
        #endregion

        public FullInt abs(FullInt value)
        {
            var result = new FullInt(value);
            result.IsNegative = false;
            return result;
        }


        #region mult methods
        public FullInt mult(FullInt value1, FullInt value2)
        {

            // one word by one word: a single 64x64 -> 128 multiply in C# (FullHelper.BigMul), no native call
            if (value1.value.Length == 1 && value2.value.Length == 1)
            {
                ulong hi = FullHelper.BigMul(value1.value[0], value2.value[0], out ulong lo);
                return new FullInt(value1.IsNegative != value2.IsNegative, hi == 0 ? new ulong[] { lo } : new ulong[] { hi, lo });
            }

            var lenth = value1.value.Length + value2.value.Length;
            // small: stack buffer, one exact-size copy out; large: the heap buffer becomes the result
            var heap = lenth <= FullHelper.StackAllocLimitWords ? null : new ulong[lenth];
            Span<ulong> bytes = heap ?? stackalloc ulong[lenth];

            if (FullHelper.MultShort(value1.value, value2.value, bytes))
            {
                // 2 x 1, 2 x 2: here
            }
            else if (value1.value == value2.value && value1.value.Length < Karatsuba.SqrThreshold)
            {
                _core.Sqr(value1.value, bytes);                          // x·x: the square kernel
            }
            else if ((Math.Min(value1.value.Length, value2.value.Length) >= Karatsuba.Threshold || value1.value == value2.value))
            {
                Karatsuba.Multiply(_core, value1.value, value2.value, bytes);   // recursion in C#, leaves in the core
            }
            else
            {
                multInternal(value1.value, value2.value, bytes);
            }
            return new FullInt(value1.IsNegative != value2.IsNegative, FullHelper.ResultArray(bytes, heap));
        }

        private void multInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            if (result.Length < value1.Length + value2.Length)//should we check it here? its for internal use only i should use the correct buffer size
            {
                throw new ArgumentException("Byte array length is not enought");
            }
            
			_core.Mult(value1, value2, result);
        }


        public FullInt pow(FullInt value1, FullInt value2)
        {
            // integer power is undefined for negative exponents (same as BigInteger.Pow);
            // before, the '-' of the exponent's base-2 string was silently ignored: a^-n == a^n
            if (value2.IsNegative)
            {
                throw new ArgumentOutOfRangeException(nameof(value2), "The exponent must be non-negative.");
            }

            FullInt result = new FullInt(1);

            FullInt value = new FullInt(value1);
            string point = ToString(new FullInt(value2), 2);

            for (int i = 0; i < point.Length; i++)
            {
                if (point[point.Length - 1 - i] == '1')
                {
                    result = mult(result, value);
                }
                if (i + 1 < point.Length)
                {
                    value = mult(value, value);
                }
            }

            return result;
        }

        #endregion

       
        #region comparation
        public bool IsABiggerB(FullInt value1, FullInt value2)
        {
            var result = Compare(value1, value2);
            return result > 0;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Compare(FullInt value1, FullInt value2)
        {
            {
                bool neg1 = value1.IsNegative, neg2 = value2.IsNegative;
                if (neg1 != neg2) return neg1 ? -1 : 1;
                ulong[] a = value1.value, b = value2.value;
                int abs;
                if (a.Length == b.Length && a.Length <= ShortLoopCompareWords)
                {
                    // equal lengths: leading zero words do not matter, the first differing word
                    // decides; a plain loop over the arrays (no spans in this frame)
                    abs = 0;
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (a[i] != b[i]) { abs = a[i] > b[i] ? 1 : -1; break; }
                    }
                }
                else
                {
                    abs = CompareAbsArrays(a, b);
                }
                return neg1 ? -abs : abs;
            }
        }

        public int CompareAbsInternal(Span<ulong> value1, Span<ulong> value2)
        {
            return _core.Compare(value1, value2);
        }

        // the operators' compare beyond the short loop: separate, so that the caller's frame holds
        // no spans (with them inlined the operator saved 8 registers and zeroed its locals: ~5 ns)
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int CompareAbsArrays(ulong[] value1, ulong[] value2) => CompareAbsQuick(value1, value2);

        private const int ShortLoopCompareWords = 16;

        // Magnitudes almost always differ in the length or the top word: with no leading zero
        // words that decides it here, without the core call. Equal length and top word: the
        // first differing word decides - found here too (.NET 8: CommonPrefixLength, vectorized;
        // netstandard: a loop up to ShortCompareWords). Leading zeros, and long values on
        // netstandard: the core.
        private static int CompareAbsQuick(Span<ulong> value1, Span<ulong> value2)
        {
            if (value1.Length > 0 && value2.Length > 0 && value1[0] != 0 && value2[0] != 0)
            {
                int n = value1.Length;
                if (n != value2.Length) return n > value2.Length ? 1 : -1;
                if (value1[0] != value2[0]) return value1[0] > value2[0] ? 1 : -1;
                if (n == 1) return 0;
#if NET7_0_OR_GREATER
                // 16-byte vectors: half the speed of the x64 AVX2 kernel on long values, which go
                // there (the call costs ~10 ns); on x86 this beats the kernel at every length
                if (n <= LongCompareWords || !Is64Bit)
                {
                    int i = 1 + value1.Slice(1).CommonPrefixLength(value2.Slice(1));
                    if (i == n) return 0;
                    return value1[i] > value2[i] ? 1 : -1;
                }
#else
                if (n <= ShortCompareWords)
                {
                    for (int i = 1; i < n; i++)
                        if (value1[i] != value2[i]) return value1[i] > value2[i] ? 1 : -1;
                    return 0;
                }
#endif
            }
            return _core.Compare(value1, value2);
        }

        // netstandard (.NET Framework): longer equal-topped values go to the core (a native call
        // costs ~10 ns on x64, ~40 ns on x86 .NET Framework)
        private const int ShortCompareWords = 16;
        private const int LongCompareWords = 64;   // x64, measured: 64 words even, 128: the kernel 1.3x ahead
        private static readonly bool Is64Bit = Environment.Is64BitProcess;

        public bool IsABiggerOrEqualsB(FullInt value1, FullInt value2)
        {
            var result = Compare(value1, value2);
            return result >= 0;
        }

        public bool IsEquals(FullInt value1, FullInt value2)
        {
            return Compare(value1, value2) == 0;
        }
        #endregion


        #region div methods

        // a / b: the remainder is not materialized (no allocation for it)
        public FullInt div(FullInt value1, FullInt value2)
        {
            if (FullHelper.IsZero(value2.value))   // not "value2 == 0": that allocates a FullInt for the 0
            {
                throw new System.DivideByZeroException();
            }
            return divRem(value1, value2, wantQuotient: true, wantRemainder: false, out _)!;
        }

        // a % b: the quotient is not materialized (no allocation for it)
        public FullInt mod(FullInt value1, FullInt value2)
        {
            if (FullHelper.IsZero(value2.value))
            {
                throw new System.DivideByZeroException();
            }
            divRem(value1, value2, wantQuotient: false, wantRemainder: true, out var remainder);
            return remainder!;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public FullInt div(FullInt value1, ulong value2, out ulong remainder)
        {

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            // small: stack buffer, one exact-size copy out; large: the heap buffer becomes the result
            var heap = value1.value.Length <= FullHelper.StackAllocLimitWords ? null : new ulong[value1.value.Length];
            Span<ulong> bytes = heap ?? stackalloc ulong[value1.value.Length];

            div_internal(value1.value, value2, bytes, out remainder);

            var result = new FullInt(value1.IsNegative, FullHelper.ResultArray(bytes, heap));
            return result;
        }

        // Two words by one word in C#: the high word by 64/64, then (r:low) / d by Div128, the way
        // Div2long walks the words. No buffers, no native call. Separate (not inlined) so that
        // divRem's general path is compiled as before.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt? divTwoByOne(ulong hi, ulong lo, ulong d, bool negA, bool negB, bool wantQuotient, bool wantRemainder, out FullInt? remainder)
        {
            ulong qh = hi / d;
            ulong ql = FullHelper.Div128(hi - qh * d, lo, d, out ulong r);
            remainder = wantRemainder ? new FullInt(negA, new ulong[] { r }) : null;   // a zero remainder is normalized on read
            return wantQuotient ? new FullInt(negA != negB, qh == 0 ? new ulong[] { ql } : new ulong[] { qh, ql }) : null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt? divByTwoWords(Span<ulong> dividend, Span<ulong> divisor, bool negA, bool negB, bool wantQuotient, bool wantRemainder, out FullInt? remainder)
        {
            var q = new ulong[dividend.Length - 1];
            FullHelper.DivBy2(dividend, divisor, q, out ulong rHi, out ulong rLo);
            remainder = wantRemainder ? new FullInt(negA, rHi == 0 ? new ulong[] { rLo } : new ulong[] { rHi, rLo }) : null;
            return wantQuotient ? new FullInt(negA != negB, FullHelper.DeleteZeros(q).ToArray()) : null;
        }

        public void div_internal(Span<ulong> value1, ulong value2, Span<ulong> result, out ulong remainder)
        {
            _core.Div(value1, value2, result, out remainder);
        }

        //метод деления двух больших чисел 
        public FullInt div(FullInt value1, FullInt value2, out FullInt remainder) //propably need to add the internal div method
        {
            var quotient = divRem(value1, value2, wantQuotient: true, wantRemainder: true, out var r);
            remainder = r!;
            return quotient!;
        }

        // The division itself. Only the requested results are allocated; the other one is
        // computed in a scratch buffer (the kernels produce both) and dropped.
        private FullInt? divRem(FullInt value1, FullInt value2, bool wantQuotient, bool wantRemainder, out FullInt? remainder)
        {

            // the division kernels expect divisor[0] != 0, and the buffer sizes depend on the
            // real lengths, so leading zero words are stripped from both operands first
            var dividend = FullHelper.DeleteZeros(value1.value);
            var divisor = FullHelper.DeleteZeros(value2.value);

            // one word by one word: plain 64-bit division, no native call (before the compare:
            // a < b gives q = 0, r = a here as well)
            if (dividend.Length == 1 && divisor.Length == 1)
            {
                ulong q1 = dividend[0] / divisor[0];
                ulong r1 = dividend[0] - q1 * divisor[0];
                remainder = wantRemainder ? new FullInt(value1.IsNegative, new ulong[] { r1 }) : null;
                return wantQuotient ? new FullInt(value1.IsNegative != value2.IsNegative, new ulong[] { q1 }) : null;
            }

            //optimalizations for small numbers
            if (CompareAbsQuick(dividend, divisor) == -1)
            {
                remainder = wantRemainder ? new FullInt(value1) : null;
                return wantQuotient ? new FullInt(false, new ulong[] { 0UL }) : null;
            }

            // in a 32-bit process 64-bit division is a helper call: there "%" (no quotient to copy
            // out) is faster through the asm kernel (measured 124 vs 140 ns), "/" still gains (172 -> 139)
            if (divisor.Length == 1 && dividend.Length == 2 && (wantQuotient || IntPtr.Size == 8))
            {
                return divTwoByOne(dividend[0], dividend[1], divisor[0], value1.IsNegative, value2.IsNegative, wantQuotient, wantRemainder, out remainder);
            }
            // a two-word divisor (x64): the 3-by-2 steps here (FullHelper.DivBy2), no native call
            // (.NET 8, x64, 2N / N: 3-16 words 0.9-0.97 of the core's Div; 64 words 1.07: the core)
            if (divisor.Length == 2 && dividend.Length <= 32 && IntPtr.Size == 8)
            {
                return divByTwoWords(dividend, divisor, value1.IsNegative, value2.IsNegative, wantQuotient, wantRemainder, out remainder);
            }

            if (divisor.Length == 1)
            {
                ulong r = 0UL;
                FullInt? res = null;
                if (wantQuotient)
                {
                    res = div(value1, divisor[0], out r);
                    res.IsNegative = value1.IsNegative != value2.IsNegative;
                }
                else
                {
                    // quotient into a scratch buffer, only the remainder is kept
                    ulong[]? rented = null;
                    Span<ulong> scratch = value1.value.Length <= FullHelper.StackAllocLimitWords
                        ? stackalloc ulong[value1.value.Length]
                        : FullHelper.RentZeroed(value1.value.Length, out rented);
                    div_internal(value1.value, divisor[0], scratch, out r);
                    FullHelper.Return(rented);
                }
                remainder = wantRemainder ? new FullInt(value1.IsNegative, new ulong[] { r }) : null;

                return res;
            }

            ulong[]? remainderRented = null;
            Span<ulong> remainderBuf = divisor.Length <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[divisor.Length + 2] // + 2 it uses internal calucation where remainder can be greate than the value 2
                : FullHelper.RentZeroed(divisor.Length + 2, out remainderRented);   // a work buffer: copied out below

            var resultLenght = Math.Max(dividend.Length - divisor.Length + 1, 1);
            // small: stack buffer, one exact-size copy out; large: the heap buffer becomes the result
            // ("%" drops the quotient, so there a large one is only a work buffer and comes from the pool)
            ulong[]? resultRented = null;
            var resultHeap = resultLenght <= FullHelper.StackAllocLimitWords || !wantQuotient ? null : new ulong[resultLenght];
            Span<ulong> resultBuf = resultHeap
                ?? (resultLenght <= FullHelper.StackAllocLimitWords
                    ? stackalloc ulong[resultLenght]
                    : FullHelper.RentZeroed(resultLenght, out resultRented));

            div_internal(dividend, divisor, resultBuf, remainderBuf, quotientOnly: !wantRemainder);

            remainder = wantRemainder ? new FullInt(value1.IsNegative, FullHelper.DeleteZeros(remainderBuf).ToArray()) : null;
            FullHelper.Return(remainderRented);
            if (!wantQuotient)
            {
                FullHelper.Return(resultRented);
                return null;
            }

            var sign = value1.IsNegative != value2.IsNegative;

            //propably ussless if value1 >= value2. remove when uncommented the optimalization above
            if (FullHelper.IsZeroSpan(resultBuf))   // in C#: this was a core (native) call comparing with a zero word
            {
                sign = false;
            }
            return new FullInt(sign, FullHelper.ResultArray(resultBuf, resultHeap));
        }
		
        public void div_internal(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> result, Span<ulong> remainder, bool quotientOnly = false)
        {
            // long divisor and long quotient: Burnikel–Ziegler (recursion in C#, leaves in the core);
            // the same contract as the core's Div, so the buffers above stay as they are;
            // longer still: the Newton reciprocal
            if (NewtonDivision.Use(dividend.Length, divisor.Length, quotientOnly))
            {
                if (quotientOnly) NewtonDivision.Quotient(_core, dividend, divisor, result);   // the last block's remainder not computed
                else NewtonDivision.DivRem(_core, dividend, divisor, result, remainder);
                return;
            }
            // the quotient alone (a / b): an approximate quotient with a guard word, no remainder -
            // the core's kernel up to a few hundred words, Burnikel–Ziegler above (a long divisor
            // with a short quotient is cut down to the quotient's length there)
            if (quotientOnly && dividend.Length > divisor.Length)
            {
                if (BurnikelZiegler.QuotientByKernel(dividend.Length, divisor.Length)) _core.Quotient(dividend, divisor, result);
                else BurnikelZiegler.Quotient(_core, dividend, divisor, result);
                return;
            }
            if (divisor.Length >= 2 * BurnikelZiegler.Threshold
                && dividend.Length - divisor.Length >= 2 * BurnikelZiegler.Threshold)
            {
                BurnikelZiegler.DivRem(_core, dividend, divisor, result, remainder);
                return;
            }
            _core.Div(dividend, divisor, result, remainder);
        }

        #endregion
		
        #region bitwise
        // BigInteger semantics: negative numbers behave as infinite two's complement.
        public FullInt and(FullInt value1, FullInt value2) => bitwise(0, value1, value2);
        public FullInt or(FullInt value1, FullInt value2) => bitwise(1, value1, value2);
        public FullInt xor(FullInt value1, FullInt value2) => bitwise(2, value1, value2);

        private FullInt bitwise(int op, FullInt value1, FullInt value2)
        {
            var a = FullHelper.DeleteZeros(value1.value);
            var b = FullHelper.DeleteZeros(value2.value);
            if (!value1.IsNegative && !value2.IsNegative)
            {
                // both non-negative (the common case): word by word here, no kernel call - and:
                // the low min(la, lb) words; or / xor: the longer one's top words as they are
                if (a.Length < b.Length) { var t = a; a = b; b = t; }
                int la = a.Length, lb = b.Length, d = la - lb;
                ulong[] w;
                if (op == 0)
                {
                    w = new ulong[lb];
                    if (lb < 8) for (int i = 0; i < lb; i++) w[i] = a[d + i] & b[i];
                    else FullHelper.BitwiseWords(0, a.Slice(d), b, w);
                }
                else
                {
                    w = new ulong[la];
                    a.Slice(0, d).CopyTo(w);
                    if (lb >= 8) FullHelper.BitwiseWords(op, a.Slice(d), b, w.AsSpan(d));
                    else if (op == 1) for (int i = 0; i < lb; i++) w[d + i] = a[d + i] | b[i];
                    else for (int i = 0; i < lb; i++) w[d + i] = a[d + i] ^ b[i];
                }
                return Normalized(false, w);
            }
            var r = new ulong[Math.Max(a.Length, b.Length) + 1];

            int negative = op == 0 ? _core.And(a, value1.IsNegative, b, value2.IsNegative, r)
                         : op == 1 ? _core.Or(a, value1.IsNegative, b, value2.IsNegative, r)
                         : _core.Xor(a, value1.IsNegative, b, value2.IsNegative, r);

            return Normalized(negative != 0, r);
        }

        // ~x = -x - 1
        public FullInt not(FullInt value)
        {
            return sub(invertor(value), 1);
        }

        // The one-word paths are checked here, in front: a branch inside the general methods
        // made the JIT compile them worse (2-32 words measured 15-50% slower on .NET 8).
        public FullInt shiftLeft(FullInt value, int shift)
        {
            var w = value.value;
            if (w.Length == 1 && (uint)shift < 64) return shiftLeftOneWord(w[0], value.IsNegative, shift);
            return shiftLeftGeneral(value, shift);
        }

        public FullInt shiftRight(FullInt value, int shift)
        {
            var w = value.value;
            if (w.Length == 1 && shift >= 0) return shiftRightOneWord(w[0], value.IsNegative, shift);   // IsNegative is false for 0
            return shiftRightGeneral(value, shift);
        }

        private FullInt shiftLeftGeneral(FullInt value, int shift)
        {
            if (shift < 0) return shiftRight(value, -shift);

            var a = FullHelper.DeleteZeros(value.value);
            var r = new ulong[a.Length + shift / 64 + 1];
            _core.ShiftLeft(a, shift, r);
            return Normalized(value.IsNegative, r);
        }

        // arithmetic shift: rounds toward negative infinity, like BigInteger
        private FullInt shiftRightGeneral(FullInt value, int shift)
        {
            if (shift < 0) return shiftLeft(value, -shift);

            var a = FullHelper.DeleteZeros(value.value);
            bool isZero = a.Length == 1 && a[0] == 0;

            if (!value.IsNegative || isZero)
            {
                var r = new ulong[Math.Max(a.Length - shift / 64, 1)];
                _core.ShiftRight(a, shift, r);
                return Normalized(false, r);
            }

            // -|a| >> k = -(((|a| - 1) >> k) + 1)
            var shifted = shiftRight(sub(abs(value), 1), shift);
            return invertor(add(shifted, 1));
        }

        // One word: in C#, no buffers and no native call. Separate methods keep the general
        // shift paths as they were (frame, locals).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt shiftLeftOneWord(ulong m, bool negative, int shift)   // shift < 64
        {
            ulong lo = m << shift, hi = shift == 0 ? 0 : m >> (64 - shift);
            return new FullInt(negative, hi == 0 ? new ulong[] { lo } : new ulong[] { hi, lo });
        }

        // negative (m >= 1): -m >> k = -(((m - 1) >> k) + 1), rounding toward negative infinity
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt shiftRightOneWord(ulong m, bool negative, int shift)
        {
            return negative
                ? new FullInt(true, new ulong[] { (shift >= 64 ? 0 : (m - 1) >> shift) + 1 })
                : new FullInt(false, new ulong[] { shift >= 64 ? 0 : m >> shift });
        }

        private static FullInt invertor(FullInt value) => FullInt.Negate(value);

        // magnitude is a fresh array owned by the caller: kept as is unless it has leading zeros
        private static FullInt Normalized(bool negative, ulong[] magnitude)
        {
            var m = FullHelper.Trimmed(magnitude);
            bool isZero = m.Length == 1 && m[0] == 0;
            return new FullInt(negative && !isZero, m);
        }

        #endregion

		#region Convertations
		
		//maybe better to use only buffer in the param? and add abylity to suppor internal operation only with the buffers
		public FullInt ToFullInt(String value)
        {
            

            bool s = false;
            byte Base = 10;

            if (value.Length == 0) value = "0";
            
            var valueSpan = Encoding.ASCII.GetBytes(value).AsSpan();
            
            if (valueSpan[0] == 0x2D) //'-'
            {
                s = true;
                valueSpan = valueSpan.Slice(1);
            }
            // "0x" / "0X": hexadecimal; anything else decimal (leading zeros too: no octal)
            if (valueSpan.Length > 2 && valueSpan[0] == 0x30 && (valueSpan[1] == 0x58 || valueSpan[1] == 0x78))
            {
                Base = 16;
                valueSpan = valueSpan.Slice(2);
            }
            if (Base == 10 && valueSpan.Length > DecimalConversion.ParseThreshold)   // long text: by halves
            {
                var magnitude = ParseDecimal(valueSpan);
                return new FullInt(s, magnitude.value);
            }
            if (Base == 10)
            {
                int resultWords = valueSpan.Length / 19 + 1;
                ulong[]? rented = null;
                Span<ulong> resultBuf = resultWords <= FullHelper.StackAllocLimitWords
                    ? stackalloc ulong[resultWords]
                    : FullHelper.RentZeroed(resultWords, out rented);   // a work buffer: copied out below
                _core.FromDecString(valueSpan, resultBuf, out var startPosition);
                var parsed = new FullInt(s, resultBuf.Slice(startPosition).ToArray());
                FullHelper.Return(rented);
                return parsed;
            }
            // hexadecimal: 4 bits a digit, from the low end (the digits checked by FullInt.Parse)
            int words = (valueSpan.Length + 15) / 16;
            var hex = new ulong[words];
            for (int i = 0; i < valueSpan.Length; i++)
            {
                int c = valueSpan[valueSpan.Length - 1 - i];
                int digit = c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
                hex[words - 1 - i / 16] |= (ulong)digit << ((i % 16) * 4);
            }
            return new FullInt(s, FullHelper.Trimmed(hex));
        }
        // a short number by the fraction method (FullIntEngine.Decimal.cs); null: the core has no passes
#if NET5_0_OR_GREATER
        [System.Runtime.CompilerServices.SkipLocalsInit]   // the text is written before it is read
#endif
        private string? ToDecStringFraction(Span<ulong> w, bool negative)
        {
            Span<char> text = stackalloc char[19 * (w.Length + 4) + 2];
            int start = WriteDecimalFraction(w, text, text.Length);
            if (start < 0) return null;
            if (negative) text[--start] = '-';
            return text.Slice(start).ToString();
        }

#if !NETSTANDARD2_0
        // the 2-by-1 reciprocal of 10^19 (Pow10_19, FullIntEngine.Decimal.cs; its top bit set: a
        // normalized divisor): floor((2^128 - 1) / 10^19) - 2^64 (Möller–Granlund; the AArch64
        // ToDecString.s has the same)
        private const ulong Pow10_19Recip = 0xD83C94FB6D2AC34AUL;

        // (u1·2^64 + u0) / 10^19 with u1 < 10^19: the quotient, and the remainder in r
        private static ulong Div10e19(ulong u1, ulong u0, out ulong r)
        {
            ulong q1 = FullHelper.BigMul(Pow10_19Recip, u1, out ulong q0);
            ulong s = q0 + u0;
            q1 += u1 + 1 + (s < q0 ? 1UL : 0UL);
            q0 = s;
            ulong rr = u0 - q1 * Pow10_19;
            if (rr > q0) { q1--; rr += Pow10_19; }
            if (rr >= Pow10_19) { q1++; rr -= Pow10_19; }
            r = rr;
            return q1;
        }

        // A short number (a few words, the top one nonzero) in decimal, in C#: the words divided by
        // 10^19 again and again (one Div10e19 a word), each remainder 19 digits by the BCL's ulong
        // formatting (the top one without its leading zeros).
        [System.Runtime.CompilerServices.SkipLocalsInit]   // every stackalloc here is written before it is read
        private static string ToDecStringShort(ReadOnlySpan<ulong> value, bool negative)
        {
            Span<char> text = stackalloc char[20 * value.Length + 2];
            int start = WriteDecimalShort(value, text, text.Length);
            if (negative) text[--start] = '-';
            return new string(text.Slice(start));
        }

        // value's digits (a few words, the top one nonzero) right-aligned at text[.. end), no
        // leading zeros; returns the first digit's index
        [System.Runtime.CompilerServices.SkipLocalsInit]   // every stackalloc here is written before it is read
        private static int WriteDecimalShort(ReadOnlySpan<ulong> value, Span<char> text, int end)
        {
            int n = value.Length;
            Span<ulong> w = stackalloc ulong[n];
            value.CopyTo(w);
            Span<char> digits = stackalloc char[20];
            int start = 0;
            while (true)
            {
                ulong r = 0;
                for (int i = start; i < n; i++) w[i] = Div10e19(r, w[i], out r);
                while (start < n && w[start] == 0) start++;
                r.TryFormat(digits, out int k, default, System.Globalization.CultureInfo.InvariantCulture);
                digits.Slice(0, k).CopyTo(text.Slice(end - k));
                if (start == n) return end - k;   // the top chunk: no leading zeros
                text.Slice(end - 19, 19 - k).Fill('0');
                end -= 19;
            }
        }
#endif

		private string ToDecString(FullInt value)
        {
            if (value.value.Length == 1)   // one word: the BCL formats a ulong without the buffers and the native call
            {
                var text1 = value.value[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                return value.IsNegative ? "-" + text1 : text1;   // the sign exactly as the general path writes it
            }
#if !NETSTANDARD2_0
            if (value.value.Length <= DecimalConversion.ShortWords)   // short: in C#, no native call
            {
                return ToDecStringShort(FullHelper.DeleteZeros(value.value), value.IsNegative);
            }
#endif
            if (value.value.Length > DecimalConversion.ToStringThreshold)   // long: by halves (FullIntEngine.Decimal.cs)
            {
                return ToDecStringRecursive(value);
            }
            {
                var w = FullHelper.DeleteZeros(value.value);
                if (w.Length >= DecimalConversion.FractionFrom && w.Length <= DecimalConversion.FractionMaxWords)
                {
                    var s = ToDecStringFraction(w, value.IsNegative);
                    if (s != null) return s;
                }
            }

            int resultBytes = (int)(value.value.Length * 1.014 + 1) * 19 + 1;
            byte[]? rented = null;
            Span<byte> resultBuf = resultBytes <= FullHelper.StackAllocLimitBytes
                ? stackalloc byte[resultBytes]
                : FullHelper.RentZeroedBytes(resultBytes, out rented);   // a work buffer: copied into the string

            _core.ToDecString(value.value, resultBuf, out var startPosition);

            if(value.IsNegative && startPosition > 0)
            {
                startPosition--;
                resultBuf[startPosition] = (byte)'-';
            }
            var text = FullHelper.AsciiString(resultBuf.Slice(startPosition));
            FullHelper.Return(rented);
            return text;
        }
        private string ToDHexString(FullInt value)
        {
            throw new NotImplementedException();
        }
		public String ToString(FullInt value, byte toBase)
        {
            if (toBase == 10)
            {
                return ToDecString(value);
            }
            String result = "";
            String prefix = value.IsNegative ? "-" : string.Empty;
            Span<ulong> zero = stackalloc ulong[] { 0 };
            if (toBase == 16)
            {
                prefix += "0x";
                ulong[] words = value.value;
                for (int i = 0; i < words.Length; i++)
                {
                    ulong v = words[i];
                    if (result.Length == 0)
                    {
                        // skip leading zero words, except the last one
                        if (v != 0 || i == words.Length - 1) result += v.ToString("X");
                    }
                    else
                    {
                        result += v.ToString("X16");
                    }
                }
            }
            else
            {
                var buffer = (ulong[])value.value.Clone();
                if (CompareAbsInternal(buffer, zero) == 0)
                {
                    result = "0";
                }
                else
                {
                    while (CompareAbsInternal(buffer, zero) == 1)
                    {
                        div_internal(buffer, toBase, buffer, out ulong remainder);
                        result = remainder.ToString("X") + result;
                    }
                }
            }
            return prefix + result;
        }
		#endregion
	}
}