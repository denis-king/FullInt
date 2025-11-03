using System;
using System.Collections.Generic;
using System.Linq;
namespace FullInt.Core
{
    internal static class FullIntConvertor
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
        //maybe better to use only buffer in the param? and add abylity to suppor internal operation only with the buffers
        public static FullInt64 ConvertToBase(FullInt64 value, toBase Base) //use the generic or something bettor
        {
            var result = new FullInt64(value.Sing, new ulong[] { 0 }, Base); //todo optimaze to reuse the save buffer to avoid allocations
            var isDec = Base == toBase.dec;

            if (value.IsDec == isDec)
            {
                result.value = (ulong[])value.value.Clone(); //should we clone?
            }
            else
            {
               //todo check the logic
                var a = new FullInt64(false, new ulong[] {1,0}, Base);//avoid creation new objects

                for (int i = 0; i < value.value.Length; i++)
                {
                    result = FullInt64Math.mult(result, a);
                    result = FullInt64Math.add(result, new FullInt64(value.Sing, new ulong[] { value.value[i] }, Base)); //avoid creation new arreys
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

        public static String ToString(this FullInt64 value, byte toBase)
        {
            String result = "";
            //var bytes = new Stack<byte>();

            //FullInt64 value = new FullInt64(fullint);
            var buffer = (ulong[])value.value.Clone();
            FullInt64[] cc;

            if (value.Sing)
            {
                value.Sing = false;
                result += "-";
            }
            if (toBase == 16)
            {
                result += "0x";
            }

            if (toBase == (byte)(value.IsDec ? 10 : 16))
            {
                int i = 0;
                foreach (var v in value.value)
                {
                    if (i == 0)
                    {
                        result += toBase == 10 ? v.ToString() : v.ToString("X");
                    }
                    else
                    {
                        result += toBase == 10 ? v.ToString("D19") : v.ToString("X16");
                    }
                        i++;
                }
            }
            else
            {
                if (value > 0)
                {
                    while (FullInt64Math.CompareAbsInternal(buffer, stackalloc ulong[] { 0 }) == 1)
                    {
                        //todo 
                        ulong remainder;

                        FullInt64Math.div_mod_internal(buffer, toBase, buffer, out remainder);//check if div in place works
                        result = remainder.ToString((toBase == 10 ? "D" : "X")) + result;
                    }
                }
                else
                {
                    result = "0";
                }
                //for (int i = 0; bytes.Count > 0; i++) result += bytes.Pop().ToString((toBase == 10 ? "D" : "X"));
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
        public static FullInt64 ToFullInt64(String value)
        {
           

            bool s = false;
            byte Base = 10;
            //result.IsDec = true;

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
                //result.Base = toBase.hex;
                value = value.Remove(0, 1);
            }
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
                    result = FullInt64Math.mult(result, Base);
                    result = FullInt64Math.add(result, Convert.ToByte(value[i].ToString(), (int)Base));
                }
            }

            return result;
        }

    }
}