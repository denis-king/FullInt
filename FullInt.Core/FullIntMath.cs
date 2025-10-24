using System;
using System.Collections;
using System.Collections.Generic;
namespace FullInt.Core
{
    public static class FullIntMath
    {
        public static FullInt add(FullInt value1, FullInt value2)
        {
            toBase Base = toEqualBase(value1, value2);
            
            int b1Sign = 1;
            int b2Sign = 1;

            FullInt result = new FullInt(0 ,Base);
           
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
                    return result;
                }
            }

            int sum = 0;
            byte b1 = 0, b2 = 0;
            int baseMaxValue = (int)Base * (int)Base;
            var bytes = new byte[Math.Max(value1.value.Length, value2.value.Length) + 1];
           
            for (int i = 0; i < value2.value.Length || i < value1.value.Length; i++)
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
            if (sum > 0) bytes[0] = (byte)sum;

            result.value = bytes;
            result.Base = Base;
            return deleteZeros(result);
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

            Stack<byte> bytes = new Stack<byte>();
            FullInt result = null;

            ulong sum = 0;

            for (int i = 0; i < value2.value.Length + value1.value.Length - 1; i++)
            {

                for (int j = 0; j < value2.value.Length; j++)
                {
                    if (value1.value.Length - 1 + j - i >= 0 && value1.value.Length - 1 + j - i < value1.value.Length)
                    {
                        sum += (uint)(value1.value[value1.value.Length - 1 + j - i] * value2.value[value2.value.Length - 1 - j]);

                    }
                }

                bytes.Push((byte)(sum % ((uint)Base * (uint)Base)));
                sum = sum / ((uint)Base * (uint)Base);
            }

            //учёт переносов
            while (sum > 0)
            {
                bytes.Push((byte)(sum % ((uint)Base * (uint)Base)));
                sum = sum / ((uint)Base * (uint)Base);
            }


            result = bytes.ToArray();

            result = bytes.ToArray();
            if (value1.s != value2.s) { result.s = true; }

            result.Base = Base;
            return deleteZeros(result);
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

            var zeros1 = countZeros(value1);
            var zeros2 = countZeros(value2);
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
            //пропуск старших нулей операнда
            int zeros = countZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            var span = value.value.AsSpan();
            value.value = span.Slice(zeros).ToArray();
            return value;
        }

        private static int countZeros(FullInt value)
        {
            int zeros = 0;
            for (zeros = 0; zeros < value.value.Length - 1 && value.value[zeros] == 0; zeros++) { }

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
            int zeros1 = countZeros(value1);

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
        private static FullInt[] div_mod(FullInt value1, FullInt value2)
        {
            toBase Base = toEqualBase(value1, value2);
            List<byte> bytes = new List<byte>();
            FullInt[] result = new FullInt[2];

            FullInt divs = new FullInt(0, Base);

            //пропуск старших нулей операндов
            int zeros1 = countZeros(value1), zeros2 = countZeros(value2);

            while (value1.value.Length - zeros1 > 0)
            {
                //если есть еще байти из делимого числа
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
                if (divs < value2)
                {
                    bytes.Add(0);//!!!!!!!!!!!
                }
                else
                {
                    //находим частичный результат
                    byte b = 128;
                    byte bMax = 255;
                    byte bMin = 0;
                    FullInt temp1;
                    while ((temp1 = divs - mult(abs(value2), b)) >= value2 || temp1.s)
                    {
                        if (temp1.s == false)
                        {
                            //if (temp1 == value2) { b++; break; }
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
                    divs = temp1;
                    bytes.Add(b);
                }
            }

            if (bytes.Count == 0)
                bytes.Add(0);

            //сохранение целой части
            result[0] = deleteZeros(new FullInt(false, bytes.ToArray(), Base));

            //определения знака результата
            if (value1.s != value2.s) { result[0].s = true; }

            //сохранение остатка от деления
            result[1] = deleteZeros(divs);
            result[1].s = false;

            return result;
        }
    }
}