using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
namespace FullInt.Core
{
    public static class FullInt64Math
    {
        [DllImport("128Operations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Mul128(ulong a, ulong b, out ulong high);

        [DllImport("128Operations.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong Div128(ulong hi, ulong lo, ulong divisor, out ulong remainder);

        #region add methods
        public static FullInt64 add(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var result = new FullInt64(false, new ulong[Math.Max(value1.value.Length, value2.value.Length) + 1], Base);
            add(value1, value2, result, Base);
            return deleteZeros(result);
        }
        public static void add(FullInt64 value1, FullInt64 value2, FullInt64 result, toBase Base, bool useCarry = true)
        {
            if(result.value.Length < Math.Max(value1.value.Length, value2.value.Length) + (useCarry ? 1 : 0))
            {
                throw new Exception("the result bufer size is not enought");
            }

            if (value1.Sing == value2.Sing)
            {
                var carry = addInternal(value1.value, value2.value, result.value, Base);
                result.Sing = value1.Sing;

                if (!useCarry && carry > 0)
                {
                    throw new Exception("result buffer is oveflow");
                }
            }
            else
            {
                var compareResult = subInternal(value1.value, value2.value, result.value, Base);

                if (compareResult > 0)
                {
                    subInternal(value1.value, value2.value, result.value, Base);
                    result.Sing = value1.Sing;
                }
                else if (compareResult < 0)
                {
                    subInternal(value2.value, value1.value, result.value, Base);
                    result.Sing = value2.Sing;
                }
                else
                {
                    result.Sing = false;
                    }
                    return;
                }
            }
        }

        public static FullInt64 sub(FullInt64 value1, FullInt64 value2)
        {
            return add(value1, -value2);
        }
        //private static ulong[] add(Span<ulong> value1, Span<ulong> value2, toBase Base)
        //{
        //    Span<ulong> reesult = stackalloc ulong[Math.Max(value1.Length, value2.Length) + 1];
        //    addInternal(value1, value2, reesult, Base);
        //    return reesult.Slice(countZeros(reesult)).ToArray();
        //}
        //private static ulong[] sub(Span<ulong> value1, Span<ulong> value2, toBase Base)
        //{
        //    Span<ulong> reesult = stackalloc ulong[Math.Max(value1.Length, value2.Length) + 1];
        //    subInternal(value1, value2, reesult, Base);
        //    return reesult.Slice(countZeros(reesult)).ToArray();
        //}
        private static ulong addInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base) 
        {
            ulong sum;
            ulong b1, b2;
            //ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var resultLastIndex = result.Length - 1;
            
            ulong carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < value1.Length) { b1 = value1[value1LastIndex - i]; } else { b1 = 0; }
                if (i < value2.Length) { b2 = value2[value2LastIndex - i]; } else { b2 = 0; }

                
                    //save adding two ulongs and carry
                    var sum1 = b1 + b2;
                    bool carry1 = sum1 < b1 || sum1 < b2;
                sum = sum1 + carry;
                carry1 |= sum < sum1;
                carry = carry1 ? 1UL : 0UL;

                    //decimal corrections
                    if (Base == toBase.dec)
                    {
                        if (carry == 1)
                        {
                            sum = sum + (ulong.MaxValue - FullInt64.maxDecValue);
                        }
                        else if (sum > FullInt64.maxDecValue)
                        {
                            sum = sum - FullInt64.maxDecCapasity;
                            carry = 1;
                        }
                    }

                result[resultLastIndex - i] = sum;
            }

            return carry;
            }

        private static int subInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
            Span<ulong> valueG;
            Span<ulong> valueL;
            
            //find the greate and less values
            var compareResult = CompareAbsInternal(value1, value2);

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
            ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            byte borrow = 0;
            var valueGLastIndex = valueG.Length - 1;
            var valueLLastIndex = valueL.Length - 1;
            var resultLastIndex = result.Length - 1;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < valueG.Length) { b1 = valueG[valueGLastIndex - i]; } else { b1 = 0; }
                if (i < valueL.Length) { b2 = valueL[valueLLastIndex - i]; } else { b2 = 0; }

                if (b1 < b2 + borrow)
                {
                    sum = baseMaxValue - b2 - borrow + b1 + 1;
                    borrow = 1;
                }
                else
                {
                    sum = b1 - b2 - borrow;
                    borrow = 0;
                }
                result[resultLastIndex - i] = sum;
            }
            return compareResult;
        }
        #endregion

        public static FullInt64 abs(FullInt64 value)
        {
            var result = new FullInt64(value);
            result.Sing = false;
            return result;
        }


        #region mult methods
        public static FullInt64 mult(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var bytes = new ulong[value1.value.Length + value2.value.Length];

            multInternal(value1.value, value2.value, bytes, Base);
            return new FullInt64(value1.Sing != value2.Sing, deleteZeros(bytes).ToArray(), Base);
        }

        private static void multInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
            if (result.Length < value1.Length + value2.Length)//should we check it here? its for internal use only i should use the correct buffer size
            {
                throw new ArgumentException("Byte array length is not enought");
            }

            Span<ulong> sum = stackalloc ulong[3];
            Span<ulong> mult = stackalloc ulong[2];
            
            ulong multA, multB = 0;
            ulong multHi, multLo = 0;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var value1Index = 0;
            var value1ShiftedIndex = 0;
            var resultLastIndex = result.Length - 1;

            for (int i = 0; i < result.Length; i++)//replaced resultLastIndex with result.Length  just to store the carry on the latest iteration
            {
                value1Index = value1LastIndex - i;
                for (int j = 0; j < value2.Length; j++)
                {
                    value1ShiftedIndex = value1Index + j;
                    if (value1ShiftedIndex >= 0 && value1ShiftedIndex < value1.Length)
                    {
                        multA = value1[value1ShiftedIndex];
                        multB = value2[value2LastIndex - j];
                        if (multA == 0 || multB == 0)
                        {
                            continue;
                        }
                        Multiply(multA, multB, Base == toBase.dec, out multLo, out multHi);
                        mult[0] = multHi;
                        mult[1] = multLo;

                        //sum = sum + mult
                        addInternal(sum, mult, sum, Base);

                    }
                }

                result[resultLastIndex - i] = sum[2];
                //свиг суммы вправо на 1 байт
                sum[2] = sum[1];
                sum[1] = sum[0];
                sum[0] = 0;
                //sum = (int)(sum / baseMaxValue);
            }

            ////check if carry srores correctly in the latest iteration of the loop and remove this
            ////учёт переносов
            //if (sum[2] > 0)
            //{
            //    result[0] = sum[2];
            //}
            //else
            //{
            //    result[0] = 0;
            //}
            }
            else
            {
                result[0] = 0;
            }
        }


        private static bool _nativeAvailable = true;

        public static void Multiply(ulong a, ulong b, bool isDec, out ulong low, out ulong high)
        {
            high = 0;
            low = 0;
            if (_nativeAvailable)
            {
                try
                {
                    low = Mul128(a, b, out high);
                }
                
                catch (Exception)
                {
                    _nativeAvailable = false;
                    Mult(a, b, isDec, out low, out high);
                }
            }

            
            if (isDec)
            {

                if (_nativeAvailable)
                {
                    try
                    {

                        high = Div128(high, low, FullInt64.maxDecCapasity, out low);
                        return;
                    }
                    catch (DllNotFoundException)
                    {
                        _nativeAvailable = false;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _nativeAvailable = false;
                    }
                }
            }
        }
        public static void Mult(ulong x, ulong y, bool isDec, out ulong lo, out ulong hi)
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
        //public static (uint hi, uint lo) Mult(uint x, uint y)
        //{
        //    const ulong mask32 = 0xFFFFFFFF;
        //    ulong result = x * y;
        //    return ((uint)(result >> 32), (uint)(result & mask32));
        //}
        //public static (byte hi, byte lo) Mult(byte x, byte y, bool isDec)
        //{
        //    const ulong mask8 = 0xFF;
        //    uint result = (uint)x * (uint)y;
        //    return isDec 
        //        ? ((byte)(result / 100), (byte)(result % 100)) 
        //        : ((byte)(result >> 8), (byte)(result & mask8));//use base
        //}

        public static FullInt64 pow(FullInt64 value1, FullInt64 value2)
        {
            
            toBase Base = toEqualBase(ref value1, ref value2);
            FullInt64 result = new FullInt64(1, Base);

            FullInt64 value = new FullInt64(value1);
            string point = new FullInt64(value2).ToString(2);

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
        public static bool IsABiggerB(FullInt64 value1, FullInt64 value2)
        {
            var result = Compare(value1, value2);
            return result > 0;
        }
        public static int Compare(FullInt64 value1, FullInt64 value2) 
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
        public static int CompareAbsInternal(Span<ulong> value1, Span<ulong> value2)
        {
            int result = 0;
            //toEqualBase(ref value1, ref value2);

            var zeros1 = countZeros(value1);
            var zeros2 = countZeros(value2);
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

        public static bool IsABiggerOrEqualsB(FullInt64 value1, FullInt64 value2)
        {
            var result = Compare(value1, value2);
            return result >= 0;
        }

        public static bool IsEquals(FullInt64 value1, FullInt64 value2)
        {
            return Compare(value1, value2) == 0;
        }
        #endregion

        #region normalization
        public static toBase toEqualBase(ref FullInt64 value1, ref FullInt64 value2)
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
                    value2 = FullIntConvertor.ConvertToBase(value2, Base);
                }
                else
                {
                    Base = value2.IsDec ? toBase.dec : toBase.hex;
                    value1 = FullIntConvertor.ConvertToBase(value1, Base);
                }
            }
            return Base;
        }
        private static FullInt64 deleteZeros(FullInt64 value)
        {
            value.value = deleteZeros(value.value).ToArray();
            return value;
        }

        public static Span<ulong> deleteZeros(Span<ulong> value)
        {
            //пропуск старших нулей операнда
            int zeros = countZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.Slice(zeros);
        }

        private static int countZeros(ulong[] value)
        {
            return countZeros(value.AsSpan());
            
        }
        private static int countZeros(Span<ulong> value)
        {
            int zeros;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }
            if (zeros == value.Length)
            {
                zeros--;
            }
            return zeros;
        }
        #endregion

        private static FullInt64 Copy(FullInt64 value)
        {
            return Copy(value, (value.IsDec ? toBase.dec : toBase.hex));
        }

        private static FullInt64 Copy(FullInt64 value, toBase toBase)
        {
            var result = FullIntConvertor.ConvertToBase(value, toBase);
            deleteZeros(result);
            return result;
        }

        #region div methods

        public static FullInt64 div(FullInt64 value1, FullInt64 value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            return div(value1, value2, out _);
        }

        public static FullInt64 mod(FullInt64 value1, FullInt64 value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            FullInt64 result;
            div(value1, value2, out result);
            return result;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public static FullInt64 div(FullInt64 value1, ulong value2, out ulong remainder)
        {
            toBase Base = value1.IsDec ? toBase.dec : toBase.hex;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            var bytes = value1.value.Length <= 1024 
                ? stackalloc ulong[value1.value.Length] 
                : new ulong[value1.value.Length];//todo use stackalloc if not big size and use the pool for arrays
            
            var bytes = new ulong[value1.value.Length];
            
            div_internal(value1.value, value2, bytes, out remainder);

            //сохранение целой части
            var result = new FullInt64(value1.Sing, deleteZeros(bytes).ToArray(), Base);
            return result;
        }

        private static ulong FindDivisionCandidate(Span<ulong> partDividend, Span<ulong> divisor)
        {
            ulong hiDividend;
            ulong loDividend;
            
            ulong result;
            ulong resultMin;
            ulong resultMax;
            
            int divisorIndex = countZeros(divisor);
            int dividentIndex = countZeros(partDividend);
            
            //firs step
            if (partDividend.Length - dividentIndex > 1 && partDividend[dividentIndex] < divisor[divisorIndex]) 
            {
                hiDividend = partDividend[dividentIndex];
                loDividend = partDividend[dividentIndex + 1];
            }
            else 
            {
                hiDividend = 0;
                loDividend = partDividend[dividentIndex];
            }

            resultMin = Div128(hiDividend, loDividend, divisor[divisorIndex] + 1, out _);
            resultMax = Div128(hiDividend, loDividend, divisor[divisorIndex], out _);

            result = resultMax;

            if (resultMin == resultMax)
            {
                return result;
            }

            //step 2
            divisorIndex++;
            var range = resultMax - resultMin + 1;
            var step = ulong.MaxValue / range;
            result -= divisor[divisorIndex] / step;

            return result;
        }

        public static void div_internal(Span<ulong> value1, ulong value2, Span<ulong> result, out ulong remainder)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            Span<ulong> value2Buf = stackalloc ulong[] { value2 };
            Span<ulong> divs = stackalloc ulong[2];
            int bytesIndex = 0;
            
            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1);

            while (value1.Length - zeros1 > 0)
            {
                //если есть еще байти из делимого числа
                if (value1.Length - zeros1 >= 1)
                {
                    //move divs left and add new byte
                    divs[0] = divs[1];
                    divs[1] = value1[zeros1];
                    zeros1++;
                }
                else
                {
                    break;
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (CompareAbsInternal(divs, value2Buf) == -1)
                {
                    //skip the zeros befor the result
                    if (bytesIndex > 0)
                    {
                        result[bytesIndex++] = 0;//!!!!!!!!!!!
                    }
                }
                else
                {
                    ulong b;
                    
                    //128long / long div
                    result[bytesIndex++] = Div128(divs[divs.Length - 2], divs[divs.Length - 1], value2, out b);
                    divs[0] = 0;
                    divs[1] = b;
                }
            }

            //move result to the riht buffer end
            if(bytesIndex < result.Length)
            {
                for(int i = 0; i < bytesIndex; i++)
                {
                    result[result.Length - 1 - i] = result[bytesIndex - 1 - i];
                }
                for (int i = 0; i < result.Length - bytesIndex; i++)
                {
                    result[i] = 0;
                }
            }
            remainder = divs[1];
        }

        //метод деления двух больших чисел 
        public static FullInt64 div(FullInt64 value1, FullInt64 value2, out FullInt64 remainder) //propably need to add the internal div method
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            
            //comented this optimalizations to test the general algorithm
            //optimalizations for small numbers
            if (CompareAbsInternal(value1.value, value2.value) == -1)
            {
                remainder = new FullInt64(value1);
                return new FullInt64(false, new ulong[] { 0UL }, Base);
            }

            if (value2.value.Length == 1)
            {
                ulong r = 0UL;
                var res = div(value1, value2.value[0], out r);
                remainder = new FullInt64(value1.Sing, new ulong[] { r }, Base);
                res.Sing = value1.Sing != value2.Sing;

                return res;
            }

            Span<ulong> remainderBuf = value2.value.Length <= 1024
                ? stackalloc ulong[value2.value.Length + 2] // + 2 it uses internal calucation where remainder can be greate than the value 2
                : new ulong[value2.value.Length + 2];//use the buffer pool

            var resultLenght = Math.Max(value1.value.Length - value2.value.Length + 1, 1);
            Span<ulong> resultBuf = resultLenght <= 1024
                ? stackalloc ulong[resultLenght]
                : new ulong[resultLenght];
            
            div_internal(value1.value, value2.value, resultBuf, remainderBuf, Base);
            var sign = value1.Sing != value2.Sing;
            
            //propably ussless if value1 >= value2. remove when uncommented the optimalization above
            if (CompareAbsInternal(resultBuf, stackalloc ulong[] { 0 }) == 0)
            {
                sign = false;
            }
            var result = new FullInt64(sign, deleteZeros(resultBuf).ToArray(), Base);
            remainder = new FullInt64(value1.Sing, deleteZeros(remainderBuf).ToArray(), Base);

            return result;
        }
        public static void div_internal(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> result, Span<ulong> remainder, toBase Base)
        {
            if (CompareAbsInternal(divisor, stackalloc ulong[] {0}) == 0)
            {
                throw new System.DivideByZeroException();
            }

            int bytesIndex = 0;
            bool isInit = true;

            Span<ulong> partDividend = remainder.Slice(1);

            Span<ulong> partResult = stackalloc ulong[] { 0UL };

            Span<ulong> divMult = divisor.Length <= 1024
                ? stackalloc ulong[partDividend.Length]
                : new ulong[partDividend.Length];

            

            var remainderSing = false;
            //пропуск старших нулей операндов
            int zeros1 = 0, zeros2 = 0; //todo use span slice to awoid zeros
            dividend = deleteZeros(dividend);
            divisor = deleteZeros(divisor);

            if (isInit)
            {
                if (dividend.Length >= divisor.Length)
                {
                    partDividend = dividend.Slice(0, divisor.Length);
                    zeros1 += divisor.Length;
                    if (CompareAbsInternal(partDividend, divisor) == -1)
                    {
                        partDividend = dividend.Slice(zeros1 - divisor.Length, divisor.Length + 1);
                    }
                }
                else
                {
                    partDividend = dividend;
                    zeros1 += dividend.Length;
                }
            }


            while (dividend.Length - zeros1 > 0 || isInit)
            {
                //если есть еще байти из делимого числа
                if (!isInit)
                {
                    if (dividend.Length - zeros1 >= 1)
                    {
                    //move divs left and add new byte
                        for (int i = 0; i < partDividend.Length - 1; i++)
                    {
                            partDividend[i] = partDividend[i + 1];
                    }
                        partDividend[partDividend.Length - 1] = dividend[zeros1];

                    zeros1 += 1;
                }
                else
                {

                    break;
                }
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (!isInit && CompareAbsInternal(partDividend, divisor) == -1)
                {
                    //skip the zeros befor the result
                    if (bytesIndex > 0)
                    {
                        result[bytesIndex++] = 0;//!!!!!!!!!!!
                    }
                }
                else
                {
                    //находим частичный результат

                    ulong bMax = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
                    ulong bMin = 0;
                    partResult[0] = bMax / 2;

                    var candidate = FindDivisionCandidate(partDividend, divisor);
                    partResult[0] = candidate;

                    #region remainder = partDividend - (divisor * partResult)
                    //divMult = |value2| * partResult
                    multInternal(divisor, partResult, divMult, Base);
                        //remainder = divs - divMult
                    var compare = subInternal(partDividend, divMult, remainder, Base);
                    remainderSing = compare < 0;
                    #endregion


                    while (remainderSing || (compare = CompareAbsInternal(remainder, divisor)) >= 0)
                        {
                        //candidate correction
                        compare = subInternal(remainder, divisor, remainder, Base);
                        if (remainderSing)
                        {
                            partResult[0]--;
                            remainderSing = compare > 0;
                            if (!remainderSing)
                        {
                            break;
                        }
                        }
                        else
                        {
                            partResult[0]++;
                        }
                    }

                    if (isInit)
                    {
                        isInit = false;
                        partDividend = remainder.Slice(1);
                    }

                    result[bytesIndex++] = partResult[0];
                }
            }

            

            //move result to the riht buffer end
            if (bytesIndex < result.Length)
            {
                result = result.Slice(0, bytesIndex);
            }
        }

        #endregion
    }
}