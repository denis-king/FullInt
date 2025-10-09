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

            Stack<byte> bytes = new Stack<byte>();

            int sum = 0;
            byte b1 = 0, b2 = 0;

            FullInt result = null;


            if (value1.s == value2.s)//++
            {

                for (int i = 0; i < value2.value.Length || i < value1.value.Length; i++)
                {
                    if (i < value1.value.Length) { b1 = value1.value[value1.value.Length - 1 - i]; } else { b1 = 0; }
                    if (i < value2.value.Length) { b2 = value2.value[value2.value.Length - 1 - i]; } else { b2 = 0; }

                    sum += b1 + b2;
                    bytes.Push((byte)(sum % ((int)Base * (int)Base)));
                    sum = sum / ((int)Base * (int)Base);
                }
                if (sum > 0) bytes.Push((byte)(sum % ((int)Base * (int)Base)));

                result = bytes.ToArray();
                result.s = value1.s;

            }

            else
            {
                if (abs(value1) < abs(value2)) // |value1| < |value2|
                {
                    for (int i = 0; i < value2.value.Length || i < value1.value.Length; i++)
                    {
                        if (i < value1.value.Length) { b1 = value1.value[value1.value.Length - 1 - i]; } else { b1 = 0; }
                        if (i < value2.value.Length) { b2 = value2.value[value2.value.Length - 1 - i]; } else { b2 = 0; }

                        sum += b2 - b1; // |value1| < |value2|

                        if (sum >= 0)
                        {
                            bytes.Push((byte)(sum % ((int)Base * (int)Base)));
                            sum = 0;
                        }
                        else
                        {
                            bytes.Push((byte)((sum + ((int)Base * (int)Base)) % ((int)Base * (int)Base)));
                            sum = -1;
                        }
                    }
                    if (sum > 0) bytes.Push((byte)(sum % ((int)Base * (int)Base)));

                    result = bytes.ToArray();
                    result.s = value2.s;

                }
                else // |value1| >= |value2|
                {
                    for (int i = 0; i < value2.value.Length || i < value1.value.Length; i++)
                    {
                        if (i < value1.value.Length) { b1 = value1.value[value1.value.Length - 1 - i]; } else { b1 = 0; }
                        if (i < value2.value.Length) { b2 = value2.value[value2.value.Length - 1 - i]; } else { b2 = 0; }

                        sum += b1 - b2; // |value1| >= |value2|

                        if (sum >= 0)
                        {
                            bytes.Push((byte)(sum % ((int)Base * (int)Base)));
                            sum = 0;
                        }
                        else
                        {
                            bytes.Push((byte)((sum + ((int)Base * (int)Base)) % ((int)Base * (int)Base)));
                            sum = -1;
                        }
                    }
                    if (sum > 0) bytes.Push((byte)(sum % ((int)Base * (int)Base)));

                    result = bytes.ToArray();
                    result.s = value1.s;
                }
            }

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

        public static bool jb(FullInt value1, FullInt value2)
        {
            toEqualBase(value1, value2);
            bool result = true;

            if (value1.s == true && value2.s == false)
            {
                result = false;
            }
            else
            {
                if ((value1.value.Length < value2.value.Length && value1.s == false && value2.s == false) || (value1.value.Length > value2.value.Length && value1.s == true && value2.s == true))
                {
                    result = false;
                }
                else
                {
                    if (value1.value.Length == value2.value.Length)
                    {
                        for (int i = 0; i < value1.value.Length; i++)
                        {
                            if ((value1.value[i] < value2.value[i] && value1.s == false && value2.s == false) || (value1.value[i] > value2.value[i] && value1.s == true && value2.s == true))
                            {
                                result = false;
                                break;
                            }
                            else
                            {
                                if ((value1.value[i] > value2.value[i] && value1.s == false && value2.s == false) || (value1.value[i] < value2.value[i] && value1.s == true && value2.s == true))
                                {
                                    break;
                                }
                            }

                        }
                    }
                }
            }

            return result && !je(value1, value2);
        }

        public static bool jbe(FullInt value1, FullInt value2)
        {
            toEqualBase(value1, value2);
            bool result = true;

            if (value1.s == true && value2.s == false)
            {
                result = false;
            }
            else
            {
                if ((value1.value.Length < value2.value.Length && value1.s == false && value2.s == false) || (value1.value.Length > value2.value.Length && value1.s == true && value2.s == true))
                {
                    result = false;
                }
                else
                {
                    if (value1.value.Length == value2.value.Length)
                    {
                        for (int i = 0; i < value1.value.Length; i++)
                        {
                            if ((value1.value[i] < value2.value[i] && value1.s == false && value2.s == false) || (value1.value[i] > value2.value[i] && value1.s == true && value2.s == true))
                            {
                                result = false;
                                break;
                            }
                            else
                            {
                                if ((value1.value[i] > value2.value[i] && value1.s == false && value2.s == false) || (value1.value[i] < value2.value[i] && value1.s == true && value2.s == true))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static bool je(FullInt value1, FullInt value2)
        {
            toEqualBase(value1, value2);
            bool result = true;


            if (value1.s != value2.s)
            {
                result = false;
            }
            else
            {
                if (value1.value.Length != value2.value.Length)
                {
                    result = false;
                }
                else
                {
                    for (int i = 0; i < value1.value.Length; i++)
                    {
                        if (value1.value[i] != value2.value[i])
                        {
                            result = false;
                            break;
                        }
                    }
                }
            }


            return result;
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

        private static FullInt deleteZeros(FullInt value)
        {

            //пропуск старших нулей операнда
            int zeros = 0;
            for (zeros = 0; zeros < value.value.Length - 1 && value.value[zeros] == 0; zeros++) { }

            if (zeros == 0) return value;

            var span = value.value.AsSpan();

            FullInt result = new FullInt(value.s, new byte[value.value.Length - zeros], value.Base);

            for (int i = zeros; i - zeros < result.value.Length; i++)
                result.value[i - zeros] = value.value[i];

            return result;
        }

        //быстрое деление на байтовое число (для преобразования числа в строку)
        public static FullInt[] div_mod(FullInt value1, byte value2)
        {
            toBase Base = value1.Base;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }

            List<byte> bytes = new List<byte>();
            FullInt[] result = new FullInt[2];

            FullInt divs = new FullInt(0, Base);

            //пропуск старших нулей операндов
            int zeros1 = 0;
            for (zeros1 = 0; zeros1 < value1.value.Length - 1 && value1.value[zeros1] == 0; zeros1++) { }

            FullInt cc = null;

            while (value1.value.Length - zeros1 > 0)
            {
                //получение частичного делимого
                cc = deleteZeros(divs);
                if (cc > 0) //если есть остаток
                {
                    if (value1.value.Length - zeros1 >= 1)
                    {
                        divs.value = new byte[cc.value.Length + 1];
                        for (int i = 0; i < cc.value.Length; i++) divs.value[i] = cc.value[i];
                        for (int i = cc.value.Length; i - cc.value.Length < 1/*value2.Length - 1 - zeros2*/; i++) divs.value[i] = value1.value[i - cc.value.Length + zeros1]; //System.IndexOutOfRangeException: 'Index was outside the bounds of the array.'

                        zeros1 += 1;
                    }
                    else
                    {
                        divs.value = new byte[cc.value.Length + value1.value.Length - zeros1];
                        for (int i = 0; i < cc.value.Length; i++) divs.value[i] = cc.value[i];
                        for (int i = cc.value.Length; i - cc.value.Length < value1.value.Length - zeros1; i++) divs.value[i] = value1.value[i - cc.value.Length + zeros1];
                        break;
                    }
                }
                else //если нет остатка
                {
                    if (value1.value.Length - zeros1 >= 1)
                    {
                        divs.value = new byte[1];

                        for (int i = 0; i < divs.value.Length; i++) divs.value[i] = value1.value[i + zeros1];
                        zeros1 += 1;
                    }
                    else
                    {
                        divs.value = new byte[value1.value.Length - zeros1];
                        for (int i = 0; i < divs.value.Length; i++) divs.value[i] = value1.value[i + zeros1];
                        break;
                    }
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (divs < new FullInt(value2, Base))
                {
                    if (value1.value.Length - zeros1 >= 1)
                    {
                        byte[] temp = divs.value;
                        divs.value = new byte[temp.Length + 1];
                        for (int i = 0; i < temp.Length; i++) divs.value[i] = temp[i];

                        divs.value[temp.Length] = value1.value[zeros1];
                        zeros1++;
                        bytes.Add(0);//!!!!!!!!!!!
                    }

                }

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

            //сохранение целой части
            result[0] = deleteZeros(bytes.ToArray());
            result[0].Base = Base;

            //определения знака результата
            if (value1.s) { result[0].s = true; }


            //сохранение остатка от деления
            result[1] = deleteZeros(divs);
            result[1].s = false;
            result[1].Base = Base;

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
            int zeros1 = 0, zeros2 = 0;
            for (zeros1 = 0; zeros1 < value1.value.Length - 1 && value1.value[zeros1] == 0; zeros1++) { }
            for (zeros2 = 0; zeros2 < value2.value.Length - 1 && value2.value[zeros2] == 0; zeros2++) { }

            FullInt cc = null;


            while (value1.value.Length - zeros1 > 0)
            {
                //получение частичного делимого
                cc = deleteZeros(divs);
                if (cc > 0) //если есть остаток
                {
                    if (value1.value.Length - zeros1 >= 1)
                    {
                        divs.value = new byte[cc.value.Length + 1];
                        for (int i = 0; i < cc.value.Length; i++)
                        {
                            divs.value[i] = cc.value[i];
                        }
                        for (int i = cc.value.Length; i - cc.value.Length < 1/*value2.Length - 1 - zeros2*/; i++)
                        {
                            if (i < divs.value.Length) //added to avodi //System.IndexOutOfRangeException: 'Index was outside the bounds of the array.'
                            {
                                divs.value[i] = value1.value[i - cc.value.Length + zeros1];
                            }
                        }

                        zeros1 += 1;
                    }
                    else
                    {
                        divs.value = new byte[cc.value.Length + value1.value.Length - zeros1];
                        for (int i = 0; i < cc.value.Length; i++)
                        {
                            divs.value[i] = cc.value[i];
                        }
                        for (int i = cc.value.Length; i - cc.value.Length < value1.value.Length - zeros1; i++)
                        {
                            if (i < divs.value.Length) //added to avodi //System.IndexOutOfRangeException: 'Index was outside the bounds of the array.'
                            {
                                divs.value[i] = value1.value[i - cc.value.Length + zeros1];
                            }
                        }
                        break;
                    }
                }
                else //если нет остатка
                {
                    if (value1.value.Length - zeros1 >= value2.value.Length - zeros2)
                    {
                        divs.value = new byte[value2.value.Length - zeros2];
                        for (int i = 0; i < divs.value.Length; i++) divs.value[i] = value1.value[i + zeros1];
                        zeros1 += value2.value.Length - zeros2;
                    }
                    else
                    {
                        divs.value = new byte[value1.value.Length - zeros1];
                        for (int i = 0; i < divs.value.Length; i++) divs.value[i] = value1.value[i + zeros1];
                        break;
                    }
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (divs < value2)
                {
                    if (value1.value.Length - zeros1 >= 1)
                    {
                        byte[] temp = divs.value;
                        divs.value = new byte[temp.Length + 1];
                        for (int i = 0; i < temp.Length; i++) divs.value[i] = temp[i];

                        divs.value[temp.Length] = value1.value[zeros1];
                        zeros1++;
                        bytes.Add(0);//!!!!!!!!!!!
                    }

                }

                //находим частичный результат
                byte b = 0;
                while (divs >= abs(value2))
                {
                    b++;
                    divs = FullIntMath.sub(divs, abs(value2));
                }
                bytes.Add(b);

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