using System;
using System.Collections;
using System.Collections.Generic;
namespace FullInt.Net
{
    public static class FullIntMath
    {
        public static FullInt add(FullInt value1, FullInt value2)
        {
            toBase Base = toEqualBase(value1, value2);
            FullInt result = new FullInt(false, new byte[Math.Max(value1.value.Length, value2.value.Length) + 1], Base);
            add(value1, value2, result, Base);
            return deleteZeros(result);
        }
        public static void add(FullInt value1, FullInt value2, FullInt result, toBase Base, bool useCarry = true)
        {
            int b1Sign = 1;
            int b2Sign = 1;
            if(result.value.Length < Math.Max(value1.value.Length, value2.value.Length) + (useCarry ? 1 : 0))
            {
                throw new Exception("the result bufer size is not enought");
            }

            if (value1.s == value2.s)
            {
                result.s = value1.s;
            }
            else
            {
                var compareResult = Compare(abs(value1), abs(value2));

                if (compareResult > 0)
                {
                    result.s = value1.s;
                    b2Sign = -1;
                }
                else if (compareResult < 0)
                {
                    result.s = value2.s;
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

            int sum = 0;
            byte b1 = 0, b2 = 0;
            int baseMaxValue = (int)Base * (int)Base;
            var bytes = result.value;// new byte[Math.Max(value1.value.Length, value2.value.Length) + 1];
           
            for (int i = 0; i < result.value.Length; i++)
            {
                if (i < value1.value.Length) { b1 = value1.value[value1.value.Length - 1 - i]; } else { b1 = 0; }
                if (i < value2.value.Length) { b2 = value2.value[value2.value.Length - 1 - i]; } else { b2 = 0; }

                sum += b1Sign * b1 + b2Sign * b2;
                if(sum >= baseMaxValue)
                {
                    bytes[bytes.Length - 1 - i] = (byte)(sum - baseMaxValue);
                    sum = 1;
                }
                else if (sum < 0)
                {
                    bytes[bytes.Length - 1 - i] = (byte)(sum + baseMaxValue);
                    sum = -1;
                }
                else
                {
                    bytes[bytes.Length - 1 - i] = (byte)sum;
                    sum = 0;
                }
            }
            
            if (!useCarry && sum != 0)
            {
                throw new Exception("result buffer is oveflow");
            }

                result.value = bytes;
            result.Base = Base;
        }

        public static FullInt div(FullInt value1, FullInt value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            return div_mod(value1, value2)[0];
        }

        public static FullInt mod(FullInt value1, FullInt value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            return div_mod(value1, value2)[1];
        }

        public static FullInt mult(FullInt value1, FullInt value2)
        {
            toBase Base = toEqualBase(value1, value2);
            var bytes = new byte[value1.value.Length + value2.value.Length];

            mult(value1, value2, bytes, Base);
            return new FullInt(value1.s != value2.s, deleteZeros(bytes), Base);
        }

        public static void mult(FullInt value1, FullInt value2, byte[] bytes, toBase Base)
        {
            if (bytes.Length < value1.value.Length + value2.value.Length)
            {
                throw new ArgumentException("Byte array length is not enought");
            }

            FullInt sum = new FullInt(false, new byte[3], Base); //todo use pooling
            //int sum = 0;
            FullInt mult = new FullInt(false, new byte[2], Base);
            uint baseMaxValue = (uint)Base * (uint)Base;
            byte multA =0, multB =0;
            byte multHi=0, multLo = 0;

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
                        (multHi, multLo) = Mult(multA, multB, Base == toBase.dec);
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
                bytes[0] = (byte)sum.value[2];
            }
            else
            {
                bytes[0] = 0;
            }
        }

        public static (ulong hi, ulong lo) Mult(ulong x, ulong y)
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

            ulong hi = z3 + (mid1 >> 32) + (mid2 >> 32);
            ulong lo = (mid2 << 32) | (z0 & mask32);

            return (hi, lo);
        }
        public static (uint hi, uint lo) Mult(uint x, uint y)
        {
            const ulong mask32 = 0xFFFFFFFF;
            ulong result = x * y;
            return ((uint)(result >> 32), (uint)(result & mask32));
        }
        public static (byte hi, byte lo) Mult(byte x, byte y, bool isDec)
        {
            const ulong mask8 = 0xFF;
            uint result = (uint)x * (uint)y;
            return isDec 
                ? ((byte)(result / 100), (byte)(result % 100)) 
                : ((byte)(result >> 8), (byte)(result & mask8));//use base
        }

        public static FullInt pow(FullInt value1, FullInt value2)
        {
            FullInt result = 1;
            toBase Base = toEqualBase(value1, value2);
            result.Base = Base;

            FullInt value = new FullInt(value1);
            String point = new FullInt(value2).ToString(2);

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

        public static FullInt sub(FullInt value1, FullInt value2)
        {
            return add(value1, -value2);
        }

        public static FullInt abs(FullInt value)
        {
            FullInt result = new FullInt(value);
            result.s = false;
            return result;
        }

        public static bool IsABiggerB(FullInt value1, FullInt value2)
        {
            var result = Compare(value1, value2);
            return result > 0;
        }
        public static int Compare(FullInt value1, FullInt value2) 
        {
            int result = 0;
            toEqualBase(value1, value2);

            var zeros1 = countZeros(value1.value);
            var zeros2 = countZeros(value2.value);
            var value1Length = value1.value.Length - zeros1;
            var value2Length = value2.value.Length - zeros2;

            //cpmparison by sign
            if (value1.s == true && value2.s == false)
            {
                result = -1;
            }
            else if(value1.s == false && value2.s == true)
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
                if (value1.s == true && value2.s == true)
                {
                    result = -result;
                }
            }
            return result;
        }
        public static bool IsABiggerOrEqualsB(FullInt value1, FullInt value2)
        {
            var result = Compare(value1, value2);
            return result >= 0;
        }

        public static bool IsEquals(FullInt value1, FullInt value2)
        {
            return Compare(value1, value2) == 0;
        }

        public static toBase toEqualBase(FullInt value1, FullInt value2)
        {
            toBase Base;
            if (value1.Base == value2.Base)
            {
                Base = value1.Base;
            }
            else
            {
                if (value1.value.Length >= value2.value.Length)
                {
                    Base = value1.Base;
                    value2.value = FullIntConvertor.ConvertToBase(value2, Base).value;
                    value2.Base = Base;
                }
                else
                {
                    Base = value2.Base;
                    value1.value = FullIntConvertor.ConvertToBase(value1, Base).value;
                    value1.Base = Base;
                }
            }
            return Base;
        }
        private static FullInt Copy(FullInt value) 
        {
            return Copy(value, value.Base);
        }

        private static FullInt Copy(FullInt value, toBase toBase)
        {
            var result = FullIntConvertor.ConvertToBase(value, toBase);
            deleteZeros(result);
            return result;
        }
        private static FullInt deleteZeros(FullInt value)
        {
            value.value = deleteZeros(value.value);
            return value;
        }

        private static byte[] deleteZeros(byte[] value)
        {
            //пропуск старших нулей операнда
            int zeros = countZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.AsSpan().Slice(zeros).ToArray();
        }

        private static int countZeros(byte[] value)
        {
            int zeros = 0;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }

            return zeros;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public static FullInt[] div_mod(FullInt value1, byte value2)
        {
            toBase Base = value1.Base;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            var value2FullInt = new FullInt((int)value2, Base);

            List<byte> bytes = new List<byte>();
            FullInt[] result = new FullInt[2];

            FullInt divs = new FullInt(0, Base);

            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1.value);

            while (value1.value.Length - zeros1 > 0)
            {
                if (value1.value.Length - zeros1 >= 1)
                {
                    var newDiv = new List<byte>(divs.value);
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
                    int b;
                    if (divs.value.Length > 1)
                    {
                        b = divs.value[divs.value.Length - 1] + ((int)Base * (int)Base) * divs.value[divs.value.Length - 2];
                    }
                    else
                    {
                        b = divs.value[divs.value.Length - 1];
                    }
                    bytes.Add((byte)(b / value2));
                    divs.value = new byte[1] { (byte)(b % value2) };
                }
            }

            //сохранение целой части
            result[0] = deleteZeros(new FullInt( value1.s, bytes.ToArray(), Base));

            //сохранение остатка от деления
            result[1] = deleteZeros(new FullInt(false, divs.value, Base));

            return result;
        }
        
        //метод деления двух больших чисел 
        public static FullInt[] div_mod(FullInt value1, FullInt value2)
        {
            toBase Base = toEqualBase(value1, value2);
            FullInt[] result = new FullInt[2];
            if (abs(value1) < abs(value2))
            {
                result[0] = 0;
                result[1] = value1;
                return result;
            }
            var bytes = new byte[value1.value.Length - value2.value.Length + 1];
            int bytesIndex = 0;
            

            FullInt divs = new FullInt(false, new byte[value2.value.Length + 1], Base);
            FullInt divMult = new FullInt(false, new byte[divs.value.Length], Base);
            FullInt temp1 = new FullInt(false, new byte[divs.value.Length + 1]);

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
                if (divs < abs(value2))
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
                    byte b = 128;
                    byte bMax = 255;
                    byte bMin = 0;
                    
                    while (true)
                    {
                        //mult abs(value2) and b into divMult
                        mult(abs(value2), b, divMult.value, Base);
                        //substract divMult from divs into temp1
                        add(divs, -divMult, temp1, Base);
                        //temp1 = divs - (abs(value2) * b)
                        if (temp1 < abs(value2) && !temp1.s)
                        {
                            break;
                        }

                        if (temp1.s == false)
                        {
                            bMin = b;
                            if (b == bMax) break;
                            if (b == bMax - 1) b++;
                            b = (byte)((b + bMax) / 2);
                        }
                        else
                        {
                            bMax = b;
                            if (b == bMin) break;
                            if (b == bMin + 1) b--;
                            b = (byte)((b + bMin) / 2);
                        }
                    }

                    //copy temp1 to divs
                    for (int i = 0; i < divs.value.Length; i++)
                    {
                        divs.value[i] = temp1.value[i + 1];
                    }
                    
                    bytes[bytesIndex++] = b;
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
            result[0] = deleteZeros(new FullInt(false, bytes, Base));

            //определения знака результата
            if (value1.s != value2.s) { result[0].s = true; }

            //сохранение остатка от деления
            result[1] = deleteZeros(divs);
            result[1].s = value1.s;

            return result;
        }
    }
}