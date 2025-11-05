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
        //maybe better to use only buffer in the param? and add abylity to suppor internal operation only with the buffers
        public static FullInt64 ConvertToBase(FullInt64 value, toBase Base) //use the generic or something bettor
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
                
                Span<ulong> remainderBuf = length > 1024
                    ? stackalloc ulong[a.Length + 2] // + 2 it uses internal calucation where remainder can be greate than the value 2
                    : new ulong[a.Length + 2];

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

                while (FullInt64Math.CompareAbsInternal(valueBuf, zoro) == 1)
                {
                    //hex to dec
                    if(a.Length == 1) //possible to use only one general method
                    {
                        FullInt64Math.div_internal(valueBuf, a[0], valueBuf, out remainder);
                        resultBuf[resultBuf.Length - 1 - index] = remainder;
                    }
                    else //dec to hex
                    {
                        FullInt64Math.div_internal(valueBuf, a, valueBuf, remainderBuf, value.IsDec ? toBase.dec : toBase.hex);
                        resultBuf[resultBuf.Length - 1 - index] = remainderBuf[remainderBuf.Length - 2] * FullInt64.maxDecCapasity + remainderBuf[remainderBuf.Length - 1];
                    }
                    index++;
                }

                //todo normilaze the buffer

                return new FullInt64(value.Sing, FullInt64Math.deleteZeros(resultBuf).ToArray(), Base);
            }

            
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
                if (FullInt64Math.CompareAbsInternal(buffer, zero) == 0)
                {
                    result = "0";
                }
                else 
                { 
                    while (FullInt64Math.CompareAbsInternal(buffer, zero) == 1)
                    {
                        //todo 
                        ulong remainder;

                        FullInt64Math.div_internal(buffer, toBase, buffer, out remainder);//check if div in place works
                        result = remainder.ToString((toBase == 10 ? "D" : "X")) + result;
                    }
                }
               
                //for (int i = 0; bytes.Count > 0; i++) result += bytes.Pop().ToString((toBase == 10 ? "D" : "X"));
            }
            return prefix + result;
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