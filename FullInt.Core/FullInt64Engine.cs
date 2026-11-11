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
    public class FullInt64Engine
    {
		private static readonly Lazy<FullInt64Engine> _instance = new Lazy<FullInt64Engine>(() => new FullInt64Engine());
		public static FullInt64Engine Instance => _instance.Value;
        
		private static IFullIntCore _core;
		
		private FullInt64Engine()
		{
			_core = FullIntCoreFactory.Create();
		}
        #region add methods
        public  FullInt64 add(FullInt64 value1, FullInt64 value2)
        {
            toBase Base = toEqualBase(ref value1, ref value2);

            var result = new FullInt64(false, new ulong[Math.Max(value1.value.Length, value2.value.Length) + 1], Base);
            add(value1, value2, result, Base);
            result.value = FullHelper.DeleteZeros(result.value).ToArray();
            return result;
        }
        public void add(FullInt64 value1, FullInt64 value2, FullInt64 result, toBase Base, bool useCarry = true)
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
            }
        }

        public FullInt64 sub(FullInt64 value1, FullInt64 value2)
        {
            return add(value1, -value2);//possible negative makes a new objext!
        }
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
            var lenth = value1.value.Length + value2.value.Length;
            var bytes = lenth <= 1024 ? stackalloc ulong[lenth] : new ulong[lenth];

            multInternal(value1.value, value2.value, bytes, Base);
            return new FullInt64(value1.Sing != value2.Sing, FullHelper.DeleteZeros(bytes).ToArray(), Base);
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
        public int Compare(FullInt64 value1, FullInt64 value2) 
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

        public FullInt64 div(FullInt64 value1, FullInt64 value2)
        {
            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            return div(value1, value2, out _);
        }

        public FullInt64 mod(FullInt64 value1, FullInt64 value2)
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
        public FullInt64 div(FullInt64 value1, ulong value2, out ulong remainder)
        {
            toBase Base = value1.IsDec ? toBase.dec : toBase.hex;

            if (value2 == 0)
            {
                throw new System.DivideByZeroException();
            }
            var bytes = value1.value.Length <= 1024 
                ? stackalloc ulong[value1.value.Length] 
                : new ulong[value1.value.Length];//todo use the pool for arrays
            
            div_internal(value1.value, value2, bytes, out remainder);

            var result = new FullInt64(value1.Sing, FullHelper.DeleteZeros(bytes).ToArray(), Base);
            return result;
        }

        public void div_internal(Span<ulong> value1, ulong value2, Span<ulong> result, out ulong remainder)
        {
            _core.Div(value1, value2, result, out remainder);
        }

        //метод деления двух больших чисел 
        public FullInt64 div(FullInt64 value1, FullInt64 value2, out FullInt64 remainder) //propably need to add the internal div method
        {
            toBase Base = toEqualBase(ref value1, ref value2);

            //comented this optimalizations to test the general algorithm
            //optimalizations for small numbers
            if (CompareAbsInternal(value1.value, value2.value) == -1)// comparation can be expensive
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
            var result = new FullInt64(sign, FullHelper.DeleteZeros(resultBuf).ToArray(), Base);
            remainder = new FullInt64(value1.Sing, FullHelper.DeleteZeros(remainderBuf).ToArray(), Base);

            return result;
        }
		
        public void div_internal(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> result, Span<ulong> remainder, toBase Base)
        {
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

        public FullInt64 shiftLeft(FullInt64 value, int shift)
        {
            RequireBinary(value, value);
            if (shift < 0) return shiftRight(value, -shift);

            var a = FullHelper.DeleteZeros(value.value);
            var r = new ulong[a.Length + shift / 64 + 1];
            _core.ShiftLeft(a, shift, r);
            return Normalized(value.Sing, r);
        }

        // arithmetic shift: rounds toward negative infinity, like BigInteger
        public FullInt64 shiftRight(FullInt64 value, int shift)
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

        private static FullInt64 invertor(FullInt64 value) => FullInt64.invertor(value);

        private static FullInt64 Normalized(bool negative, Span<ulong> magnitude)
        {
            var m = FullHelper.DeleteZeros(magnitude);
            bool isZero = m.Length == 1 && m[0] == 0;
            return new FullInt64(negative && !isZero, m.ToArray(), toBase.hex);
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
            if (Base == 10)
            {
                Span<ulong> resultBuf = stackalloc ulong[(int)(valueSpan.Length / 19 ) + 1];
                _core.FromDecString(valueSpan, resultBuf, out var startPosition);
                return new FullInt64(s, resultBuf.Slice(startPosition).ToArray(), toBase.hex);
            }
            value = Encoding.ASCII.GetString(valueSpan);
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
            Span<byte> resultBuf = stackalloc byte[(int)(value.value.Length * 1.014 + 1) * 19 + 1];
            
            _core.ToDecString(value.value, resultBuf, out var startPosition);
            
            if(value.Sing && startPosition > 0)
            {
                startPosition--;
                resultBuf[startPosition] = (byte)'-';
            }
            return Encoding.ASCII.GetString(resultBuf.Slice(startPosition));
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