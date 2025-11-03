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
                var compareResult = CompareAbsInternal(value1.value, value2.value);

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
                    //prepare the zero result
                    for (int i = 0; i < result.value.Length; i++)
                    {
                        result.value[i] = 0;
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
        private static byte addInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base) 
        {
            ulong sum = 0;
            ulong b1 = 0, b2 = 0;
            //ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            
            byte carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < value1.Length) { b1 = value1[value1.Length - 1 - i]; } else { b1 = 0; }
                if (i < value2.Length) { b2 = value2[value2.Length - 1 - i]; } else { b2 = 0; }

                
                    //save adding two ulongs and carry
                    var sum1 = b1 + b2;
                    bool carry1 = sum1 < b1 || sum1 < b2;
                    sum = sum1 + (ulong)carry;
                    bool carry2 = sum < sum1;
                    carry = carry1 || carry2 ? (byte)1 : (byte)0;

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


                
                result[result.Length - 1 - i] = sum;
            }

            
            return carry;
        }
        //value1 > value2
        private static void subInternal(Span<ulong> value1, Span<ulong> value2, Span<ulong> result, toBase Base)
        {
            ulong sum = 0;
            ulong b1 = 0, b2 = 0;
            ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            byte borrow = 0;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < value1.Length) { b1 = value1[value1.Length - 1 - i]; } else { b1 = 0; }
                if (i < value2.Length) { b2 = value2[value2.Length - 1 - i]; } else { b2 = 0; }

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
                result[result.Length - 1 - i] = sum;
            }
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
            
            ulong multA = 0, multB = 0;
            ulong multHi = 0, multLo = 0;

            for (int i = 0; i < value2.Length + value1.Length - 1; i++)
            {
                for (int j = 0; j < value2.Length; j++)
                {
                    if (value1.Length - 1 + j - i >= 0 && value1.Length - 1 + j - i < value1.Length)
                    {
                        multA = value1[value1.Length - 1 + j - i];
                        multB = value2[value2.Length - 1 - j];
                        if(multA == 0 || multB == 0)
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

                result[value1.Length + value2.Length - 1 - i] = sum[2];
                //свиг суммы вправо на 1 байт
                sum[2] = sum[1];
                sum[1] = sum[0];
                sum[0] = 0;
                //sum = (int)(sum / baseMaxValue);
            }

            //учёт переносов
            if (sum[2] > 0)
            {
                result[0] = sum[2];
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
            int result = 0;
            toEqualBase(ref value1, ref value2);

            //var zeros1 = countZeros(value1.value);
            //var zeros2 = countZeros(value2.value);
            //var value1Length = value1.value.Length - zeros1;
            //var value2Length = value2.value.Length - zeros2;

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
                ////comparison by length
                //if (value1Length < value2Length)
                //{
                //    result = -1;
                //}
                //else if (value1Length > value2Length)
                //{
                //    result = 1;
                //}
                //else
                //{
                //    //comparison by value
                //    for (int i = 0; i < value1Length; i++)
                //    {
                //        if (value1.value[i + zeros1] < value2.value[i + zeros2])
                //        {
                //            result = -1;
                //            break;
                //        }
                //        else if (value1.value[i + zeros1] > value2.value[i + zeros2])
                //        {
                //            result = 1;
                //            break;
                //        }
                //    }
                //}

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

        private static Span<ulong> deleteZeros(Span<ulong> value)
        {
            //пропуск старших нулей операнда
            int zeros = countZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.Slice(zeros).ToArray();
        }

        private static int countZeros(ulong[] value)
        {
            int zeros = 0;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }

            return zeros;
        }
        private static int countZeros(Span<ulong> value)
        {
            int zeros = 0;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }

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
            return div_mod(value1, value2)[0];
        }

        public static FullInt64 mod(FullInt64 value1, FullInt64 value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            return div_mod(value1, value2)[1];
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public static FullInt64[] div_mod(FullInt64 value1, ulong value2)
        {
            toBase Base = value1.IsDec ? toBase.dec : toBase.hex;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            
            var result = new FullInt64[2];

            var bytes = new ulong[value1.value.Length];
            ulong remainder;
            div_mod_internal(value1.value, value2, bytes, out remainder);

            //сохранение целой части
            result[0] = new FullInt64(value1.Sing, deleteZeros(bytes).ToArray(), Base);

            //сохранение остатка от деления
            result[1] = new FullInt64(value1.Sing, new ulong[] { remainder }, Base);

            return result;
        }

        public static void div_mod_internal(Span<ulong> value1, ulong value2, Span<ulong> result, out ulong remainder)
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
        public static FullInt64[] div_mod(FullInt64 value1, FullInt64 value2) //propably need to add the internal div method
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var result = new FullInt64[2];

            if (CompareAbsInternal(value1.value, value2.value) == -1)
            {
                result[0] = 0;
                result[1] = value1;
                return result;
            }
            Span<ulong> bytes = value1.value.Length - value2.value.Length + 1 <= 1024 
                ? new ulong[value1.value.Length - value2.value.Length + 1]
                : new ulong[value1.value.Length - value2.value.Length + 1];

            int bytesIndex = 0;

            Span<ulong> divs = value2.value.Length <= 1024 //todo use option to use stackalloc or heap
                ? stackalloc ulong[value2.value.Length + 1] 
                : new ulong [value2.value.Length + 1]; //possible a big size! it's better to use heap here

            Span<ulong> partResult = stackalloc ulong[] { 0 };
            
            Span<ulong> divMult = value2.value.Length <= 1024
                ? stackalloc ulong[divs.Length]
                : new ulong[divs.Length];

            Span<ulong> remainder = value2.value.Length <= 1024
                ? stackalloc ulong[divs.Length + 1]
                : new ulong[divs.Length + 1];

            var remainderSing = false;
            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1.value), zeros2 = countZeros(value2.value);

            while (value1.value.Length - zeros1 > 0)
            {
                //если есть еще байти из делимого числа
                if (value1.value.Length - zeros1 >= 1)
                {
                    //move divs left and add new byte
                    for (int i= 0; i < divs.Length - 1; i++)
                    {
                        divs[i] = divs[i + 1];
                    }
                    divs[divs.Length - 1] = value1.value[zeros1];

                    zeros1 += 1;
                }
                else
                {
                        
                    break;
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (CompareAbsInternal(divs, value2.value) == -1)
                {
                    //skip the zeros befor the result
                    if (bytesIndex > 0)
                    {
                        bytes[bytesIndex++] = 0;//!!!!!!!!!!!
                    }
                }
                else
                {
                    //находим частичный результат
                   
                    ulong bMax = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
                    ulong bMin = 0;
                    partResult[0] = bMax / 2;

                    while (true)
                    {
                        #region remainder = divs - (absValue2 * partResult)
                        //divMult = |value2| * partResult
                        multInternal(value2.value, partResult, divMult, Base);

                        //remainder = divs - divMult
                        var compare = CompareAbsInternal(divs, divMult);
                        if (compare < 0)
                        {
                            //divs < divMult
                            //remainder = -(divMult - divs)
                            remainderSing = true;
                            subInternal(divMult, divs, remainder, Base);
                        }
                        else
                        {
                            //divs >= divMult
                            //remainder = divs - divMult
                            remainderSing = false;
                            subInternal(divs, divMult, remainder, Base);
                        }
                        #endregion

                        if (CompareAbsInternal(remainder, value2.value) == -1 && !remainderSing)
                        {
                            break;
                        }

                        if (remainderSing == false)
                        {
                            bMin = partResult[0];
                            if (partResult[0] == bMax) break;
                            if (partResult[0] == bMax - 1) partResult[0]++;
                            
                            partResult[0] = partResult[0] / 2 + bMax / 2 + (partResult[0] % 2 == 1 && bMax %2 == 1 ? (ulong)1 : 0); //was long overflow
                        }
                        else
                        {
                            bMax = partResult[0];
                            if (partResult[0] == bMin) break;
                            if (partResult[0] == bMin + 1) partResult[0]--;
                            partResult[0] = partResult[0] / 2 + bMin / 2 + (partResult[0] % 2 == 1 && bMin % 2 == 1 ? (ulong)1 : 0); //was long overflow
                            //division and wod is a bit owerhead, this the better solution
                        }
                    }

                    //copy remainder to divs
                    for (int i = 0; i < divs.Length; i++)
                    {
                        divs[i] = remainder[i + 1];
                    }
                    
                    bytes[bytesIndex++] = partResult[0];
                }
            }

            
            //move result to the riht buffer end
            if (bytesIndex < bytes.Length)
            {
                for (int i = 0; i < bytesIndex; i++)
                {
                    bytes[bytes.Length - 1 - i] = bytes[bytesIndex - 1 - i];
                }
                for (int i = 0; i < bytes.Length - bytesIndex; i++)
                {
                    bytes[i] = 0;
                }
            }

            //сохранение целой части
            result[0] = new FullInt64(value1.Sing != value2.Sing, deleteZeros(bytes).ToArray(), Base);

            //определения знака результата
            if (value1.Sing != value2.Sing) { result[0].Sing = true; }

            //сохранение остатка от деления
            result[1] = new FullInt64(value1.Sing, deleteZeros(divs).ToArray(), Base);

            return result;
        }
        #endregion
    }
}