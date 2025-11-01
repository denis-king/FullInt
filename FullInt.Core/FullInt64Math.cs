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

        public static FullInt64 add(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var result = new FullInt64(false, new ulong[Math.Max(value1.value.Length, value2.value.Length) + 1], Base);
            add(value1, value2, result, Base);
            return deleteZeros(result);
        }
        public static void add(FullInt64 value1, FullInt64 value2, FullInt64 result, toBase Base, bool useCarry = true)
        {
            int b1Sign = 1;
            int b2Sign = 1;
            if(result.value.Length < Math.Max(value1.value.Length, value2.value.Length) + (useCarry ? 1 : 0))
            {
                throw new Exception("the result bufer size is not enought");
            }

            if (value1.Sing == value2.Sing)
            {
                result.Sing = value1.Sing;
            }
            else
            {
                var compareResult = Compare(abs(value1), abs(value2));

                if (compareResult > 0)
                {
                    result.Sing = value1.Sing;
                    b2Sign = -1;
                }
                else if (compareResult < 0)
                {
                    result.Sing = value2.Sing;
                    b1Sign = -1;
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

            ulong sum = 0;
            ulong b1 = 0, b2 = 0;
            ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            var bytes = result.value;// new byte[Math.Max(value1.value.Length, value2.value.Length) + 1];
            byte carry = 0;
            byte borrow = 0;
           
            for (int i = 0; i < result.value.Length; i++)
            {
                if (i < value1.value.Length) { b1 = value1.value[value1.value.Length - 1 - i]; } else { b1 = 0; }
                if (i < value2.value.Length) { b2 = value2.value[value2.value.Length - 1 - i]; } else { b2 = 0; }

                if(b1Sign == 1 && b2Sign == 1)
                {
                    //save adding two ulongs and carry
                    var sum1 = b1 + b2;
                    bool carry1 = sum1 < b1 || sum1 < b2;
                    sum = sum1 + (ulong)carry;
                    bool carry2 = sum < sum1;
                    carry = carry1 || carry2 ? (byte)1 : (byte)0;

                    //decimal corrections
                    if (Base == toBase.dec) 
                    {
                        if(carry == 1)
                        {
                            sum = sum + (ulong.MaxValue - FullInt64.maxDecValue);
                        }
                        else if(sum > FullInt64.maxDecValue)
                        {
                            sum = sum - FullInt64.maxDecCapasity;
                            carry = 1;
                        }
                    }

                    
                }
                //todo split into diferent substraction method
                else if(b1Sign == -1 && b2Sign == 1)
                {
                    if(b1 + borrow > b2)
                    {
                        //todo check if we can worrow
                        sum = baseMaxValue - b1 - borrow + b2 + 1;
                        borrow = 1;
                    }
                    else 
                    {
                        sum = b2 - b1 - borrow;
                        borrow = 0;
                    }
                        
                }
                else if(b1Sign == 1 && b2Sign == -1)
                {
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
                }
                bytes[bytes.Length - 1 - i] = sum;
            }
            
            if (!useCarry && carry > 0)
            {
                throw new Exception("result buffer is oveflow");
            }

            result.value = bytes;
        }

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

        public static FullInt64 mult(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var bytes = new ulong[value1.value.Length + value2.value.Length];

            mult(value1, value2, bytes, Base);
            return new FullInt64(value1.Sing != value2.Sing, deleteZeros(bytes), Base);
        }

        public static void mult(FullInt64 value1, FullInt64 value2, ulong[] bytes, toBase Base)
        {
            if (bytes.Length < value1.value.Length + value2.value.Length)
            {
                throw new ArgumentException("Byte array length is not enought");
            }

            var sum = new FullInt64(false, new ulong[3], Base); //todo use pooling
            //int sum = 0;
            var mult = new FullInt64(false, new ulong[2], Base);
            uint baseMaxValue = (uint)Base * (uint)Base;
            ulong multA =0, multB =0;
            ulong multHi=0, multLo = 0;

            for (int i = 0; i < value2.value.Length + value1.value.Length - 1; i++)
            {

                for (int j = 0; j < value2.value.Length; j++)
                {
                    if (value1.value.Length - 1 + j - i >= 0 && value1.value.Length - 1 + j - i < value1.value.Length)
                    {
                        multA = value1.value[value1.value.Length - 1 + j - i];
                        multB = value2.value[value2.value.Length - 1 - j];
                        if(multA == 0 || multB == 0)
                        {
                            continue;
                        }
                        Multiply(multA, multB, Base == toBase.dec, out multLo, out multHi);
                        mult.value[0] = multHi;
                        mult.value[1] = multLo;

                        //it's adding mult to sum in place of sum = sum + mult
                        add(sum, mult, sum, Base, false); //no carry, not anought buffer size

                        //sum += (value1.value[value1.value.Length - 1 + j - i] * value2.value[value2.value.Length - 1 - j]);
                    }
                }

                bytes[value1.value.Length + value2.value.Length - 1 - i] = //(byte)( sum % baseMaxValue); 
                sum.value[2];
                //свиг суммы вправо на 1 байт
                //использовать спаны
                sum.value[2] = sum.value[1];
                sum.value[1] = sum.value[0];
                sum.value[0] = 0;
                //sum = (int)(sum / baseMaxValue);
            }

            //there was a loop
            //учёт переносов
            if (sum.value[2] > 0)
            {
                bytes[0] = sum.value[2];
            }
            else
            {
                bytes[0] = 0;
            }
        }

        //[DllImport("kernel32.dll", EntryPoint = "Multiply128", ExactSpelling = true)]
        //private static extern ulong NativeMultiply128(ulong a, ulong b, out ulong high);

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

        public static FullInt64 sub(FullInt64 value1, FullInt64 value2)
        {
            return add(value1, -value2);
        }

        public static FullInt64 abs(FullInt64 value)
        {
            var result = new FullInt64(value);
            result.Sing = false;
            return result;
        }

        public static bool IsABiggerB(FullInt64 value1, FullInt64 value2)
        {
            var result = Compare(value1, value2);
            return result > 0;
        }
        public static int Compare(FullInt64 value1, FullInt64 value2) 
        {
            int result = 0;
            toEqualBase(ref value1, ref value2);

            var zeros1 = countZeros(value1.value);
            var zeros2 = countZeros(value2.value);
            var value1Length = value1.value.Length - zeros1;
            var value2Length = value2.value.Length - zeros2;

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
                        if (value1.value[i + zeros1] < value2.value[i + zeros2])
                        {
                            result = -1;
                            break;
                        }
                        else if (value1.value[i + zeros1] > value2.value[i + zeros2])
                        {
                            result = 1;
                            break;
                        }
                    }
                }

                //inversion for negative numbers
                if (value1.Sing == true && value2.Sing == true)
                {
                    result = -result;
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
        private static FullInt64 deleteZeros(FullInt64 value)
        {
            value.value = deleteZeros(value.value);
            return value;
        }

        private static ulong[] deleteZeros(ulong[] value)
        {
            //пропуск старших нулей операнда
            int zeros = countZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.AsSpan().Slice(zeros).ToArray();
        }

        private static int countZeros(ulong[] value)
        {
            int zeros = 0;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }

            return zeros;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public static FullInt64[] div_mod(FullInt64 value1, ulong value2)
        {
            toBase Base = value1.IsDec ? toBase.dec : toBase.hex;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            var value2FullInt = new FullInt64(value2, Base);

            var bytes = new List<ulong>(); //todo use the buffer
            var result = new FullInt64[2];

            var divs = new FullInt64(0, Base);

            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1.value);

            while (value1.value.Length - zeros1 > 0)
            {
                if (value1.value.Length - zeros1 >= 1)
                {
                    var newDiv = new List<ulong>(divs.value);
                    newDiv.Add(value1.value[zeros1]);
                    divs.value = newDiv.ToArray();

                    zeros1 += 1;
                }
                else 
                {
                    break;
                }
                
                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (divs < value2FullInt)
                {
                    bytes.Add(0);//!!!!!!!!!!!
                }
                else
                {
                    ulong b;
                    if (divs.value.Length > 1)
                    {
                        //128long / long div
                        // b = divs.value[divs.value.Length - 1] + ((int)Base * (int)Base) * divs.value[divs.value.Length - 2];
                        bytes.Add(Div128(divs.value[divs.value.Length - 2], divs.value[divs.value.Length - 1], value2, out b));
                        divs.value = new ulong[1] { b }; // todo use the same buffer
                    }
                    else
                    {
                        //long / long div
                        b = divs.value[divs.value.Length - 1];
                        bytes.Add((b / value2));
                        divs.value = new ulong[1] { b % value2 }; // todo use the same buffer
                    }
                }
            }

            //сохранение целой части
            result[0] = deleteZeros(new FullInt64( value1.Sing, bytes.ToArray(), Base));

            //сохранение остатка от деления
            result[1] = deleteZeros(new FullInt64(false, divs.value, Base));

            return result;
        }
        
        //метод деления двух больших чисел 
        public static FullInt64[] div_mod(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);
            var result = new FullInt64[2];
            var absValue2 = abs(value2);

            if (abs(value1) < absValue2)
            {
                result[0] = 0;
                result[1] = value1;
                return result;
            }
            var bytes = new ulong[value1.value.Length - value2.value.Length + 1];
            int bytesIndex = 0;
            
            var divs = new FullInt64(false, new ulong[value2.value.Length + 1], Base);
            var partResult = new FullInt64(false, new ulong[] { 0 }, Base);
            var divMult = new FullInt64(false, new ulong[divs.value.Length], Base);
            var remainder = new FullInt64(false, new ulong[divs.value.Length + 1]);

            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1.value), zeros2 = countZeros(value2.value);

            while (value1.value.Length - zeros1 > 0)
            {
                //если есть еще байти из делимого числа
                if (value1.value.Length - zeros1 >= 1)
                {
                    //move divs left and add new byte
                    for (int i= 0; i < divs.value.Length - 1; i++)
                    {
                        divs.value[i] = divs.value[i + 1];
                    }
                    divs.value[divs.value.Length - 1] = value1.value[zeros1];

                    zeros1 += 1;
                }
                else
                {
                        
                    break;
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (divs < absValue2)
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
                    partResult.value[0] = bMax / 2;

                    while (true)
                    {
                        //mult |value2| and partResult into divMult
                        mult(absValue2, partResult, divMult.value, Base);
                        //substract divMult from divs into temp1
                        add(divs, -divMult, remainder, Base);
                        //remainder = divs - (absValue2 * partResult)
                        if (remainder < absValue2 && !remainder.Sing)
                        {
                            break;
                        }

                        if (remainder.Sing == false)
                        {
                            bMin = partResult.value[0];
                            if (partResult.value[0] == bMax) break;
                            if (partResult.value[0] == bMax - 1) partResult.value[0]++;
                            
                            partResult.value[0] = partResult.value[0] / 2 + bMax / 2 + (partResult.value[0] % 2 == 1 && bMax %2 == 1 ? (ulong)1 : 0); //was long overflow
                        }
                        else
                        {
                            bMax = partResult.value[0];
                            if (partResult.value[0] == bMin) break;
                            if (partResult.value[0] == bMin + 1) partResult.value[0]--;
                            partResult.value[0] = partResult.value[0] / 2 + bMin / 2 + (partResult.value[0] % 2 == 1 && bMin % 2 == 1 ? (ulong)1 : 0); //was long overflow
                            //division and wod is a bit owerhead, this the better solution
                        }
                    }

                    //copy temp1 to divs
                    for (int i = 0; i < divs.value.Length; i++)
                    {
                        divs.value[i] = remainder.value[i + 1];
                    }
                    
                    bytes[bytesIndex++] = partResult.value[0];
                }
            }

            //сдвиг результата в начало массива
            if (bytesIndex < bytes.Length)
            {
                for(int i = 0; i < bytesIndex; i++)
                {
                    bytes[bytes.Length - 1 - i] = bytes[bytesIndex - 1 - i];
                    bytes[bytesIndex - 1 - i] = 0;
                }
            }

            //сохранение целой части
            result[0] = deleteZeros(new FullInt64(false, bytes, Base));

            //определения знака результата
            if (value1.Sing != value2.Sing) { result[0].Sing = true; }

            //сохранение остатка от деления
            result[1] = deleteZeros(divs);
            result[1].Sing = value1.Sing;

            return result;
        }
    }
}