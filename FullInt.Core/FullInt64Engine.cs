using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using System.Linq;
using System.Text;

namespace FullInt.Core
{
    public partial class FullInt64Engine
    {
		private static readonly Lazy<FullInt64Engine> _instance = new Lazy<FullInt64Engine>(() => new FullInt64Engine());
		public static FullInt64Engine Instance => _instance.Value;
        
		private static IFullIntCore _core;
		
		private FullInt64Engine()
		{
			_core = FullIntCoreFactory.Create();
		}
        #region add methods
        public FullInt64 add(FullInt64 value1, FullInt64 value2)
        {
            if (!value1.IsDec && !value2.IsDec && value1.value.Length == 1 && value2.value.Length == 1)
            {
                return addOneWord(value1.value[0], value1.Sing, value2.value[0], value2.Sing);
            }
            return addOrSub(value1, value2, negate2: false);
        }

        // a + b, or a - b with negate2 (no FullInt64 is made for -b). The signs are read once.
        private FullInt64 addOrSub(FullInt64 value1, FullInt64 value2, bool negate2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            bool neg1 = value1.Sing, neg2 = value2.Sing != negate2;   // -0 is fine here: a zero result is normalized on read
            ulong[] a = value1.value, b = value2.value;

            if (Base == toBase.hex && a.Length == 1 && b.Length == 1)   // after a base conversion (add / sub check it first)
            {
                return addOneWord(a[0], neg1, b[0], neg2);
            }

            // The two magnitude paths are separate methods: with Dynamic PGO (.NET 8) one shared
            // method gets compiled on the profile of whichever path ran first, and the other one
            // then runs as a cold path (measured ~10-20 ns slower at 2-8 words).
            return neg1 == neg2 ? addMagnitudes(a, b, neg1, Base) : subMagnitudes(a, b, neg1, neg2, Base);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private FullInt64 addMagnitudes(ulong[] a, ulong[] b, bool negative, toBase Base)
        {
            // Exact size, no word reserved for the carry: one allocation and no copy in the
            // common case. When the top words of equal-length operands overflow, the carry out
            // is certain: the wider result is allocated at once (the core writes the carry into
            // its extra top word). Otherwise a carry out (rare) widens the result by one word.
            int n = Math.Max(a.Length, b.Length);
            bool carryCertain = Base == toBase.hex && a.Length == b.Length && a[0] + b[0] < a[0];
            var r = new ulong[carryCertain ? n + 1 : n];
            ulong carry = addInternal(a, b, r, Base);
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
            return new FullInt64(negative, r, Base);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private FullInt64 subMagnitudes(ulong[] a, ulong[] b, bool neg1, bool neg2, toBase Base)
        {
            var r = new ulong[Math.Max(a.Length, b.Length)];   // |a - b| always fits
            int compareResult = subInternal(a, b, r, Base);
            bool sign = compareResult > 0 ? neg1 : compareResult < 0 ? neg2 : false;
            return new FullInt64(sign, FullHelper.Trimmed(r), Base);
        }
        // One word and one word: plain 64-bit arithmetic in C#. The native call costs more than
        // the add itself at this size. (A zero result is never negative: Sing normalizes on read.)
        private static FullInt64 addOneWord(ulong a, bool aNegative, ulong b, bool bNegative)
        {
            if (aNegative == bNegative)
            {
                ulong sum = a + b;
                return new FullInt64(aNegative, sum < a ? new ulong[] { 1UL, sum } : new ulong[] { sum });
            }
            // opposite signs: the larger magnitude minus the smaller, with its sign
            return a >= b
                ? new FullInt64(aNegative, new ulong[] { a - b })
                : new FullInt64(bNegative, new ulong[] { b - a });
        }

        public void add(FullInt64 value1, FullInt64 value2, FullInt64 result, toBase Base, bool useCarry = true)
        {
            if(result.value.Length < Math.Max(value1.value.Length, value2.value.Length) + (useCarry ? 1 : 0))
            {
                throw new Exception("the result bufer size is not enought");
            }

            var carry = addSigned(value1, value2, result, Base);
            if (!useCarry && carry > 0)
            {
                throw new Exception("result buffer is oveflow");
            }
        }

        // Signed add into result.value; returns the carry out of the top word (same signs only).
        private ulong addSigned(FullInt64 value1, FullInt64 value2, FullInt64 result, toBase Base)
        {
            if (value1.Sing == value2.Sing)
            {
                var carry = addInternal(value1.value, value2.value, result.value, Base);
                result.Sing = value1.Sing;
                return carry;
            }
            else
            {
                var compareResult = subInternal(value1.value, value2.value, result.value, Base);
               
                if (compareResult > 0)
                {
                    result.Sing = value1.Sing;
                }
                else if (compareResult < 0)
                {
                    result.Sing = value2.Sing;
                }
                else
                {
                    result.Sing = false;
                }
                return 0;   // |a - b| always fits
            }
        }

        public FullInt64 sub(FullInt64 value1, FullInt64 value2)
        {
            if (!value1.IsDec && !value2.IsDec && value1.value.Length == 1 && value2.value.Length == 1)
            {
                return addOneWord(value1.value[0], value1.Sing, value2.value[0], !value2.Sing);   // -0 is fine: normalized on read
            }
            return addOrSub(value1, value2, negate2: true);
        }
        #region into an existing result (FullInt64.Add(result, a, b) etc.)
        // Sizes come from the operands without their leading zero words, so a result reused in a
        // loop (result = result + b) keeps its length instead of growing by a word per call.
        // Decimal operands, negative right shifts and negative shift counts take the ordinary
        // operation and the result object just takes its value.

        public void addInto(FullInt64 result, FullInt64 value1, FullInt64 value2, bool negate2)
        {
            if (value1.IsDec || value2.IsDec)
            {
                result.Assign(negate2 ? sub(value1, value2) : add(value1, value2));
                return;
            }
            bool neg1 = value1.Sing, neg2 = value2.Sing != negate2;
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

        public void multInto(FullInt64 result, FullInt64 value1, FullInt64 value2)
        {
            if (value1.IsDec || value2.IsDec)
            {
                result.Assign(mult(value1, value2));
                return;
            }
            bool negative = value1.Sing != value2.Sing;
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
            if (Math.Min(a.Length, b.Length) >= Karatsuba.Threshold)
                Karatsuba.Multiply(_core, a, b, low);
            else
                _core.Mult(a, b, low);
            result.SetResult(negative);
        }

        // a one- or two-word value into result's buffer (the operands were read before)
        private static void WriteTwoWords(FullInt64 result, ulong hi, ulong lo, bool negative)
        {
            ulong[] r = result.WritableBuffer(2, mustBeNew: false);
            int top = r.Length - 2;
            if (top > 0) r.AsSpan(0, top).Clear();
            r[top] = hi;
            r[top + 1] = lo;
            result.SetResult(negative);
        }

        public void shiftLeftInto(FullInt64 result, FullInt64 value, int shift)
        {
            if (value.IsDec || shift < 0)
            {
                result.Assign(shiftLeft(value, shift));
                return;
            }
            bool negative = value.Sing;
            Span<ulong> a = FullHelper.DeleteZeros(value.value);
            ulong[] r = result.WritableBuffer(a.Length + shift / 64 + 1, mustBeNew: ReferenceEquals(result.value, value.value));
            _core.ShiftLeft(a, shift, r);   // writes the lead zeros of a longer r
            result.SetResult(negative);
        }

        public void shiftRightInto(FullInt64 result, FullInt64 value, int shift)
        {
            if (value.IsDec || shift < 0 || value.Sing)   // negative: floor rounding, the ordinary path
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

        private ulong addInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
            return _core.Add(value1, value2, result);
        }
        
        private int subInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
			return _core.Sub(value1, value2, result);
        }
        #endregion

        public FullInt64 abs(FullInt64 value)
        {
            var result = new FullInt64(value);
            result.Sing = false;
            return result;
        }


        #region mult methods
        public FullInt64 mult(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);

            // one word by one word: a single 64x64 -> 128 multiply in C# (FullHelper.BigMul), no native call
            if (Base == toBase.hex && value1.value.Length == 1 && value2.value.Length == 1)
            {
                ulong hi = FullHelper.BigMul(value1.value[0], value2.value[0], out ulong lo);
                return new FullInt64(value1.Sing != value2.Sing, hi == 0 ? new ulong[] { lo } : new ulong[] { hi, lo }, Base);
            }

            var lenth = value1.value.Length + value2.value.Length;
            // small: stack buffer, one exact-size copy out; large: the heap buffer becomes the result
            var heap = lenth <= FullHelper.StackAllocLimitWords ? null : new ulong[lenth];
            Span<ulong> bytes = heap ?? stackalloc ulong[lenth];

            if (Base == toBase.hex && Math.Min(value1.value.Length, value2.value.Length) >= Karatsuba.Threshold)
            {
                Karatsuba.Multiply(_core, value1.value, value2.value, bytes);   // recursion in C#, leaves in the core
            }
            else
            {
                multInternal(value1.value, value2.value, bytes, Base);
            }
            return new FullInt64(value1.Sing != value2.Sing, FullHelper.ResultArray(bytes, heap), Base);
        }

        private void multInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
            if (result.Length < value1.Length + value2.Length)//should we check it here? its for internal use only i should use the correct buffer size
            {
                throw new ArgumentException("Byte array length is not enought");
            }
            
			_core.Mult(value1, value2, result);
        }


        public FullInt64 pow(FullInt64 value1, FullInt64 value2)
        {
            // integer power is undefined for negative exponents (same as BigInteger.Pow);
            // before, the '-' of the exponent's base-2 string was silently ignored: a^-n == a^n
            if (value2.Sing)
            {
                throw new ArgumentOutOfRangeException(nameof(value2), "The exponent must be non-negative.");
            }

            toBase Base = toEqualBase(ref value1, ref value2);
            FullInt64 result = new FullInt64(1, Base);

            FullInt64 value = new FullInt64(value1);
            string point = ToString(new FullInt64(value2), 2);

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
        public bool IsABiggerB(FullInt64 value1, FullInt64 value2)
        {
            var result = Compare(value1, value2);
            return result > 0;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Compare(FullInt64 value1, FullInt64 value2)
        {
            if (value1.IsDec == value2.IsDec)   // the common case: no base conversion, each sign read once
            {
                bool neg1 = value1.Sing, neg2 = value2.Sing;
                if (neg1 != neg2) return neg1 ? -1 : 1;
                int abs = CompareAbsQuick(value1.value, value2.value);
                return neg1 ? -abs : abs;
            }
            return CompareMixedBase(value1, value2);
        }

        private int CompareMixedBase(FullInt64 value1, FullInt64 value2)
        {
            int result;
            toEqualBase(ref value1, ref value2);
           
            //cpmparison by sign
            if (value1.Sing == true && value2.Sing == false)
            {
                result = -1;
            }
            else if(value1.Sing == false && value2.Sing == true)
            {
                result = 1;
            }
            else
            {
                //compare absolute values
                result = CompareAbsInternal(value1.value, value2.value);

                //inversion for negative numbers
                if (value1.Sing == true && value2.Sing == true)
                {
                    result = -result;
                }
            }
            return result;
        }
        public int CompareAbsInternal(Span<ulong> value1, Span<ulong> value2)
        {
            return _core.Compare(value1, value2);
        }

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

        public bool IsABiggerOrEqualsB(FullInt64 value1, FullInt64 value2)
        {
            var result = Compare(value1, value2);
            return result >= 0;
        }

        public bool IsEquals(FullInt64 value1, FullInt64 value2)
        {
            return Compare(value1, value2) == 0;
        }
        #endregion

        #region normalization
        public toBase toEqualBase(ref FullInt64 value1, ref FullInt64 value2)
        {
            toBase Base;
            if (value1.IsDec == value2.IsDec)
            {
                Base = value1.IsDec ? toBase.dec : toBase.hex;
            }
            else
            {
                if (value1.value.Length >= value2.value.Length)
                {
                    Base = value1.IsDec ? toBase.dec : toBase.hex;
                    value2 = ConvertToBase(value2, Base);
                }
                else
                {
                    Base = value2.IsDec ? toBase.dec : toBase.hex;
                    value1 = ConvertToBase(value1, Base);
                }
            }
            return Base;
        }
        

        
        #endregion

        private FullInt64 Copy(FullInt64 value)
        {
            return Copy(value, (value.IsDec ? toBase.dec : toBase.hex));
        }

        private FullInt64 Copy(FullInt64 value, toBase toBase)
        {
            var result = ConvertToBase(value, toBase);
            result.value = FullHelper.DeleteZeros(result.value).ToArray();
            return result;
        }

        #region div methods

        // a / b: the remainder is not materialized (no allocation for it)
        public FullInt64 div(FullInt64 value1, FullInt64 value2)
        {
            if (FullHelper.IsZero(value2.value))   // not "value2 == 0": that allocates a FullInt64 for the 0
            {
                throw new System.DivideByZeroException();
            }
            return divRem(value1, value2, wantQuotient: true, wantRemainder: false, out _)!;
        }

        // a % b: the quotient is not materialized (no allocation for it)
        public FullInt64 mod(FullInt64 value1, FullInt64 value2)
        {
            if (FullHelper.IsZero(value2.value))
            {
                throw new System.DivideByZeroException();
            }
            divRem(value1, value2, wantQuotient: false, wantRemainder: true, out var remainder);
            return remainder!;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public FullInt64 div(FullInt64 value1, ulong value2, out ulong remainder)
        {
            toBase Base = value1.IsDec ? toBase.dec : toBase.hex;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            // small: stack buffer, one exact-size copy out; large: the heap buffer becomes the result
            var heap = value1.value.Length <= FullHelper.StackAllocLimitWords ? null : new ulong[value1.value.Length];
            Span<ulong> bytes = heap ?? stackalloc ulong[value1.value.Length];

            div_internal(value1.value, value2, bytes, out remainder);

            var result = new FullInt64(value1.Sing, FullHelper.ResultArray(bytes, heap), Base);
            return result;
        }

        // Two words by one word in C#: the high word by 64/64, then (r:low) / d by Div128, the way
        // Div2long walks the words. No buffers, no native call. Separate (not inlined) so that
        // divRem's general path is compiled as before.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt64? divTwoByOne(ulong hi, ulong lo, ulong d, bool negA, bool negB, bool wantQuotient, bool wantRemainder, out FullInt64? remainder)
        {
            ulong qh = hi / d;
            ulong ql = FullHelper.Div128(hi - qh * d, lo, d, out ulong r);
            remainder = wantRemainder ? new FullInt64(negA, new ulong[] { r }) : null;   // a zero remainder is normalized on read
            return wantQuotient ? new FullInt64(negA != negB, qh == 0 ? new ulong[] { ql } : new ulong[] { qh, ql }) : null;
        }

        public void div_internal(Span<ulong> value1, ulong value2, Span<ulong> result, out ulong remainder)
        {
            _core.Div(value1, value2, result, out remainder);
        }

        //метод деления двух больших чисел 
        public FullInt64 div(FullInt64 value1, FullInt64 value2, out FullInt64 remainder) //propably need to add the internal div method
        {
            var quotient = divRem(value1, value2, wantQuotient: true, wantRemainder: true, out var r);
            remainder = r!;
            return quotient!;
        }

        // The division itself. Only the requested results are allocated; the other one is
        // computed in a scratch buffer (the kernels produce both) and dropped.
        private FullInt64? divRem(FullInt64 value1, FullInt64 value2, bool wantQuotient, bool wantRemainder, out FullInt64? remainder)
        {
            toBase Base = toEqualBase(ref value1, ref value2);

            // the division kernels expect divisor[0] != 0, and the buffer sizes depend on the
            // real lengths, so leading zero words are stripped from both operands first
            var dividend = FullHelper.DeleteZeros(value1.value);
            var divisor = FullHelper.DeleteZeros(value2.value);

            // one word by one word: plain 64-bit division, no native call (before the compare:
            // a < b gives q = 0, r = a here as well)
            if (dividend.Length == 1 && divisor.Length == 1 && Base == toBase.hex)
            {
                ulong q1 = dividend[0] / divisor[0];
                ulong r1 = dividend[0] - q1 * divisor[0];
                remainder = wantRemainder ? new FullInt64(value1.Sing, new ulong[] { r1 }, Base) : null;
                return wantQuotient ? new FullInt64(value1.Sing != value2.Sing, new ulong[] { q1 }, Base) : null;
            }

            //optimalizations for small numbers
            if (CompareAbsQuick(dividend, divisor) == -1)
            {
                remainder = wantRemainder ? new FullInt64(value1) : null;
                return wantQuotient ? new FullInt64(false, new ulong[] { 0UL }, Base) : null;
            }

            // in a 32-bit process 64-bit division is a helper call: there "%" (no quotient to copy
            // out) is faster through the asm kernel (measured 124 vs 140 ns), "/" still gains (172 -> 139)
            if (divisor.Length == 1 && dividend.Length == 2 && Base == toBase.hex && (wantQuotient || IntPtr.Size == 8))
            {
                return divTwoByOne(dividend[0], dividend[1], divisor[0], value1.Sing, value2.Sing, wantQuotient, wantRemainder, out remainder);
            }

            if (divisor.Length == 1)
            {
                ulong r = 0UL;
                FullInt64? res = null;
                if (wantQuotient)
                {
                    res = div(value1, divisor[0], out r);
                    res.Sing = value1.Sing != value2.Sing;
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
                remainder = wantRemainder ? new FullInt64(value1.Sing, new ulong[] { r }, Base) : null;

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

            div_internal(dividend, divisor, resultBuf, remainderBuf, Base);

            remainder = wantRemainder ? new FullInt64(value1.Sing, FullHelper.DeleteZeros(remainderBuf).ToArray(), Base) : null;
            FullHelper.Return(remainderRented);
            if (!wantQuotient)
            {
                FullHelper.Return(resultRented);
                return null;
            }

            var sign = value1.Sing != value2.Sing;

            //propably ussless if value1 >= value2. remove when uncommented the optimalization above
            if (FullHelper.IsZeroSpan(resultBuf))   // in C#: this was a core (native) call comparing with a zero word
            {
                sign = false;
            }
            return new FullInt64(sign, FullHelper.ResultArray(resultBuf, resultHeap), Base);
        }
		
        public void div_internal(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> result, Span<ulong> remainder, toBase Base)
        {
            // long divisor and long quotient: Burnikel–Ziegler (recursion in C#, leaves in the core);
            // the same contract as the core's Div, so the buffers above stay as they are
            if (Base == toBase.hex && divisor.Length >= 2 * BurnikelZiegler.Threshold
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
        public FullInt64 and(FullInt64 value1, FullInt64 value2) => bitwise(0, value1, value2);
        public FullInt64 or(FullInt64 value1, FullInt64 value2) => bitwise(1, value1, value2);
        public FullInt64 xor(FullInt64 value1, FullInt64 value2) => bitwise(2, value1, value2);

        private FullInt64 bitwise(int op, FullInt64 value1, FullInt64 value2)
        {
            RequireBinary(value1, value2);
            var a = FullHelper.DeleteZeros(value1.value);
            var b = FullHelper.DeleteZeros(value2.value);
            var r = new ulong[Math.Max(a.Length, b.Length) + 1];

            int negative = op == 0 ? _core.And(a, value1.Sing, b, value2.Sing, r)
                         : op == 1 ? _core.Or(a, value1.Sing, b, value2.Sing, r)
                         : _core.Xor(a, value1.Sing, b, value2.Sing, r);

            return Normalized(negative != 0, r);
        }

        // ~x = -x - 1
        public FullInt64 not(FullInt64 value)
        {
            RequireBinary(value, value);
            return sub(invertor(value), 1);
        }

        // The one-word paths are checked here, in front: a branch inside the general methods
        // made the JIT compile them worse (2-32 words measured 15-50% slower on .NET 8).
        public FullInt64 shiftLeft(FullInt64 value, int shift)
        {
            var w = value.value;
            if (w.Length == 1 && (uint)shift < 64 && !value.IsDec) return shiftLeftOneWord(w[0], value.Sing, shift);
            return shiftLeftGeneral(value, shift);
        }

        public FullInt64 shiftRight(FullInt64 value, int shift)
        {
            var w = value.value;
            if (w.Length == 1 && shift >= 0 && !value.IsDec) return shiftRightOneWord(w[0], value.Sing, shift);   // Sing is false for 0
            return shiftRightGeneral(value, shift);
        }

        private FullInt64 shiftLeftGeneral(FullInt64 value, int shift)
        {
            RequireBinary(value, value);
            if (shift < 0) return shiftRight(value, -shift);

            var a = FullHelper.DeleteZeros(value.value);
            var r = new ulong[a.Length + shift / 64 + 1];
            _core.ShiftLeft(a, shift, r);
            return Normalized(value.Sing, r);
        }

        // arithmetic shift: rounds toward negative infinity, like BigInteger
        private FullInt64 shiftRightGeneral(FullInt64 value, int shift)
        {
            RequireBinary(value, value);
            if (shift < 0) return shiftLeft(value, -shift);

            var a = FullHelper.DeleteZeros(value.value);
            bool isZero = a.Length == 1 && a[0] == 0;

            if (!value.Sing || isZero)
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
        private static FullInt64 shiftLeftOneWord(ulong m, bool negative, int shift)   // shift < 64
        {
            ulong lo = m << shift, hi = shift == 0 ? 0 : m >> (64 - shift);
            return new FullInt64(negative, hi == 0 ? new ulong[] { lo } : new ulong[] { hi, lo });
        }

        // negative (m >= 1): -m >> k = -(((m - 1) >> k) + 1), rounding toward negative infinity
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static FullInt64 shiftRightOneWord(ulong m, bool negative, int shift)
        {
            return negative
                ? new FullInt64(true, new ulong[] { (shift >= 64 ? 0 : (m - 1) >> shift) + 1 })
                : new FullInt64(false, new ulong[] { shift >= 64 ? 0 : m >> shift });
        }

        private static FullInt64 invertor(FullInt64 value) => FullInt64.invertor(value);

        // magnitude is a fresh array owned by the caller: kept as is unless it has leading zeros
        private static FullInt64 Normalized(bool negative, ulong[] magnitude)
        {
            var m = FullHelper.Trimmed(magnitude);
            bool isZero = m.Length == 1 && m[0] == 0;
            return new FullInt64(negative && !isZero, m, toBase.hex);
        }

        private static void RequireBinary(FullInt64 value1, FullInt64 value2)
        {
            if (value1.IsDec || value2.IsDec)
            {
                throw new NotSupportedException("Bitwise operations require binary (hex) values; convert with ConvertToBase(value, toBase.hex).");
            }
        }
        #endregion

		#region Convertations
		
		//maybe better to use only buffer in the param? and add abylity to suppor internal operation only with the buffers
        public FullInt64 ConvertToBase(FullInt64 value, toBase Base) //use the generic or something bettor
        {
            var isDec = Base == toBase.dec;
            Span<ulong> zoro = stackalloc ulong[] { 0 };

            if (value.IsDec == isDec)
            {
                return value;// result.value = (ulong[])value.value.Clone(); //should we clone?
            }
            else
            {
                Span<ulong> a = value.IsDec
                    ? stackalloc ulong[] { 1, ulong.MaxValue - FullInt64.maxDecCapasity + 1 }
                    : stackalloc ulong[] { FullInt64.maxDecCapasity };

                //calc the buf length
                int length =  value.IsDec 
                    ? value.value.Length //dec to hex
                    : (int)(value.value.Length * 1.014 + 1); //hex to dec
                
                Span<ulong> resultBuf = length > 1024 
                    ? new ulong[length] 
                    : stackalloc ulong[length];
                
                Span<ulong> remainderBuf = stackalloc ulong[a.Length + 2]; // + 2 it uses internal calucation where remainder can be greate than the value 2 (a is 1-2 words)

                Span<ulong> valueBuf = value.value.Length > 1024 
                    ? new ulong[value.value.Length] 
                    : stackalloc ulong[value.value.Length];

                //coppy value to the buffer
                for(int i=0; i<value.value.Length; i++)
                {
                    valueBuf[i] = value.value[i];
                }
                
                ulong remainder;
                int index = 0;

                while (CompareAbsInternal(valueBuf, zoro) == 1)
                {
                    //hex to dec
                    if(a.Length == 1) //possible to use only one general method
                    {
                        div_internal(valueBuf, a[0], valueBuf, out remainder);//throw new Exception("--test--"+remainder);
                        resultBuf[resultBuf.Length - 1 - index] = remainder;
                    }
                    else //dec to hex
                    {
                        div_internal(valueBuf, a, valueBuf, remainderBuf, value.IsDec ? toBase.dec : toBase.hex);
                        resultBuf[resultBuf.Length - 1 - index] = remainderBuf[remainderBuf.Length - 2] * FullInt64.maxDecCapasity + remainderBuf[remainderBuf.Length - 1];
                    }
                    index++;
                }

                //todo normilaze the buffer

                return new FullInt64(value.Sing, FullHelper.DeleteZeros(resultBuf).ToArray(), Base);
            }

            
        }
		
		public FullInt64 ToFullInt64(String value)
        {
            

            bool s = false;
            byte Base = 10;
            //result.IsDec = true;

            if (value.Length == 0) value = "0";
            
            var valueSpan = Encoding.ASCII.GetBytes(value).AsSpan();
            
            if (valueSpan[0] == 0x2D) //'-'
            {
                s = true;
                valueSpan = valueSpan.Slice(1);
            }
            if (valueSpan.Length > 1 && valueSpan[0] == 0x30)
            {
                Base = 8;
                valueSpan = valueSpan.Slice(1);
            }
            if (valueSpan.Length > 1 && (valueSpan[0] == 0x58 || valueSpan[0] == 0x78)) //'x' or 'X'
            {
                Base = 16;
                //result.Base = toBase.hex;
                valueSpan = valueSpan.Slice(1);
            }
            if (Base == 10 && valueSpan.Length > DecimalConversion.ParseThreshold)   // long text: by halves
            {
                var magnitude = ParseDecimal(valueSpan);
                return new FullInt64(s, magnitude.value, toBase.hex);
            }
            if (Base == 10)
            {
                int resultWords = valueSpan.Length / 19 + 1;
                ulong[]? rented = null;
                Span<ulong> resultBuf = resultWords <= FullHelper.StackAllocLimitWords
                    ? stackalloc ulong[resultWords]
                    : FullHelper.RentZeroed(resultWords, out rented);   // a work buffer: copied out below
                _core.FromDecString(valueSpan, resultBuf, out var startPosition);
                var parsed = new FullInt64(s, resultBuf.Slice(startPosition).ToArray(), toBase.hex);
                FullHelper.Return(rented);
                return parsed;
            }
            value = FullHelper.AsciiString(valueSpan);
            FullInt64 result = new FullInt64(s, new ulong[0], (toBase)Base);
            int chunkSize = Base == 10 ? 19 : 16;
            if (Base == (byte)(result.IsDec ? 10 :16))
            {
               
                result.value = new ulong[value.Length / chunkSize + (value.Length % chunkSize > 0 ? 1 : 0)];
               
                int index = 0;
                
                var reversedChunks = Enumerable
                .Range(0, (value.Length + chunkSize - 1) / chunkSize)
                .Select(i => value
                    .Reverse()
                    .Skip(i * chunkSize)
                    .Take(chunkSize)
                    .Reverse()
                    .ToArray()
                )
                .Reverse()
                .Select(c => new string(c));

                foreach (var chunk in reversedChunks)
                {
                    ulong b = Convert.ToUInt64(chunk, (byte)Base);
                    result.value[index] = b;
                    index++;
                }
            }
            else
            {
                for (int i = 0; i < value.Length; i++)
                {
                    result = mult(result, Base);
                    result = add(result, Convert.ToByte(value[i].ToString(), (int)Base));
                }
            }

            return result;
        }
		private string ToDecString(FullInt64 value)
        {
            if (value.value.Length == 1)   // one word: the BCL formats a ulong without the buffers and the native call
            {
                var text1 = value.value[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                return value.Sing ? "-" + text1 : text1;   // the sign exactly as the general path writes it
            }
            if (value.value.Length > DecimalConversion.ToStringThreshold)   // long: by halves (FullInt64Engine.Decimal.cs)
            {
                return ToDecStringRecursive(value);
            }

            int resultBytes = (int)(value.value.Length * 1.014 + 1) * 19 + 1;
            byte[]? rented = null;
            Span<byte> resultBuf = resultBytes <= FullHelper.StackAllocLimitBytes
                ? stackalloc byte[resultBytes]
                : FullHelper.RentZeroedBytes(resultBytes, out rented);   // a work buffer: copied into the string

            _core.ToDecString(value.value, resultBuf, out var startPosition);

            if(value.Sing && startPosition > 0)
            {
                startPosition--;
                resultBuf[startPosition] = (byte)'-';
            }
            var text = FullHelper.AsciiString(resultBuf.Slice(startPosition));
            FullHelper.Return(rented);
            return text;
        }
        private string ToDHexString(FullInt64 value)
        {
            throw new NotImplementedException();
        }
		public String ToString(FullInt64 value, byte toBase)
        {
            if (value.IsDec == false && toBase == 10)
            {
                return ToDecString(value);
            }
            // else if (value.IsDec == false && toBase == 16)
            // {
                // return ToDHexString(value);
            // }
            else
            {
                String result = "";
                //var bytes = new Stack<byte>();
                String prefix = string.Empty;
                //FullInt64 value = new FullInt64(fullint);
                
                FullInt64 convertedValue = value;
                Span<ulong> zero = stackalloc ulong[] { 0 };

                if (value.Sing)
                {
                    //value.Sing = false;
                    prefix += "-";
                }
                if (toBase == 16)
                {
                    prefix += "0x";
                }

                //try to conver the base
                if(toBase != (byte)(value.IsDec ? 10 : 16) && (toBase == 10 || toBase == 16))
                {
                    convertedValue = ConvertToBase(value, (toBase)toBase);
                }

                if (toBase == (byte)(convertedValue.IsDec ? 10 : 16))
                {
                    ulong v;
                    for (int i = 0; i < convertedValue.value.Length; i++)
                    {
                        v = convertedValue.value[i];

                        if (result.Length == 0)
                        {
                            //skip zeros exept the last iteration
                            if (v != 0 || i == convertedValue.value.Length - 1)
                            {
                                result += toBase == 10 ? v.ToString() : v.ToString("X");
                            }
                        }
                        else
                        {
                            result += toBase == 10 ? v.ToString("D19") : v.ToString("X16");
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
                            //todo 
                            ulong remainder;

                            div_internal(buffer, toBase, buffer, out remainder);//check if div in place works
                            result = remainder.ToString((toBase == 10 ? "D" : "X")) + result;
                        }
                    }
                   
                    //for (int i = 0; bytes.Count > 0; i++) result += bytes.Pop().ToString((toBase == 10 ? "D" : "X"));
                }
                return prefix + result;
            }
        }
		#endregion
	}
}