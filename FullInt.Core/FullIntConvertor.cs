using System;
using System.Collections.Generic;
using System.Linq;
namespace FullInt.Core
{
    public static class FullIntConvertor
    {
        public static FullInt ConvertToBase(FullInt value, toBase Base)
        {
            FullInt result = new FullInt(value.s, new byte[] { }, Base);

            if (value.Base == Base)
            {
                result.value = (byte[])value.value.Clone();
            }
            else
            {
                FullInt a = new FullInt((int)value.Base * (int)value.Base, Base);

                for (int i = 0; i < value.value.Length; i++)
                {
                    result = FullIntMath.mult(result, a);
                    result = FullIntMath.add(result, new FullInt((int)value.value[i], Base));
                }
            }

            return result;
        }
        

        public static String ToString(this FullInt fullint, byte toBase)
        {
            String result = "";
            Stack<byte> bytes = new Stack<byte>();

            FullInt value = new FullInt(fullint);
            FullInt[] cc;

            if (value.s)
            {
                value.s = false;
                result += "-";
            }
            if (toBase == 16)
            {
                result += "0x";
            }

            if (toBase == (byte)fullint.Base)
            {
                for (int i = 0; i < value.value.Length; i++)
                {
                    if (value.value[i] < (int)fullint.Base && i > 0) result += "0";
                    result += Convert.ToString(value.value[i], toBase).ToUpper();
                }
            }
            else
            {
                if (value > 0)
                {
                    while (value > 0)
                    {
                        cc = FullIntMath.div_mod(value, toBase);
                        bytes.Push(cc[1].value[cc[1].value.Length - 1]);
                        value = cc[0];
                    }
                }
                else
                {
                    result = "0";
                }
                for (int i = 0; bytes.Count > 0; i++) result += Convert.ToString(bytes.Pop(), toBase);
            }
            return result;
        }

       

        public static FullInt ToFullInt(String value)
        {
            FullInt result = new FullInt(0);

            bool s = false;
            byte Base = 10;
            result.Base = toBase.dec;

            if (value.Length == 0) value = "0";
            if (value[0] == '-')
            {
                s = true;
                value = value.Remove(0, 1);
            }
            if (value.Length > 1 && value[0] == '0')
            {
                Base = 8;
                value = value.Remove(0, 1);
            }
            if (value.Length > 1 && value[0].ToString().ToLower() == "x")
            {
                Base = 16;
                result.Base = toBase.hex;
                value = value.Remove(0, 1);
            }

            if (Base == (byte)result.Base)
            {
                int index = 0;
                if (value.Length % 2 == 1)
                {
                    result.value = new byte[value.Length / 2 + 1];
                }
                else
                {
                    result.value = new byte[value.Length / 2];
                }
                for (int i = 0; i < value.Length; i++)
                {
                    byte b = Convert.ToByte(value[value.Length - 1 - i].ToString(), (byte)result.Base);
                    i++;
                    if (i < value.Length)
                    {
                        b += (byte)(Convert.ToByte(value[value.Length - 1 - i].ToString(), (byte)result.Base) * (byte)result.Base);
                        result.value[result.value.Length - 1 - index] = b;
                        index++;
                    }
                    else
                    {
                        result.value[result.value.Length - 1 - index] = b;
                        index++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < value.Length; i++)
                {
                    result = FullIntMath.mult(result, Base);
                    result = FullIntMath.add(result, Convert.ToByte(value[i].ToString(), (int)Base));
                }
            }


            result.s = s;
            return result;
        }
        

    }
}