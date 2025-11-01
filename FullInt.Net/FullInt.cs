//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Numerics;
//using System.Text;

//namespace FullInt.Net
//{
//    public enum toBase { dec = 10, hex = 16 }
//    public class FullInt<T> where T : unmanaged, INumber<T>
//    {
//        public T[] value { get; set; }
//        public bool s { get; set; }

//        public toBase Base { get; set; }
//        public FullInt()
//        {
//            value = new []{ T.CreateChecked(0) };
//            s = false;
//            Base = toBase.hex;
//        }

//        public FullInt(bool s, T[] value, toBase Base = toBase.hex)
//        {
//            this.value = value;
//            this.s = s;
//            this.Base = Base;
//        }
//        public FullInt(FullInt<T> value)
//        {
//            this.value = value.value;
//            this.s = value.s;
//            this.Base = value.Base;
//        }

//        public FullInt(T value, toBase Base = toBase.hex)
//        {
//            Stack<T> bytes = new Stack<T>();
//            this.Base = Base;

//            if (value < T.CreateChecked(0))
//            {
//                this.s = true;
//                value = value * T.CreateChecked(-1);
//            }
//            if (value == 0)
//            {
//                bytes.Push(0);
//            }
//            else
//            {
//                while (value > 0)
//                {
//                    bytes.Push((T)(value % ((int)Base * (int)Base)));
//                    value = value / ((int)Base * (int)Base);
//                }
//            }

//            this.value = bytes.ToArray();
//        }
//        private byte GetTypeSize()
//        {
//            if(typeof(T) == typeof(byte))
//            {
//                return 1;
//            }
//            else if (typeof(T) == typeof(ushort))
//            {
//                return 2;
//            }
//            else if (typeof(T) == typeof(uint))
//            {
//                return 4;
//            }
//            else if (typeof(T) == typeof(ulong))
//            {
//                return 8;
//            }
//            else
//            {
//                throw new NotSupportedException("Type not supported");
//            }
//        }

//        private ulong GetMaxDec()
//        {
//            if (typeof(T) == typeof(byte))
//            {
//                return 100;
//            }
//            else if (typeof(T) == typeof(ushort))
//            {
//                return 10_000;
//            }
//            else if (typeof(T) == typeof(uint) || typeof(T) == typeof(int))
//            {
//                return 1_000_000_000;
//            }
//            else if (typeof(T) == typeof(long))
//            {
//                return 1_000_000_000_000_000_000;
//            }
//            else if (typeof(T) == typeof(ulong))
//            {
//                return 10_000_000_000_000_000_000;
//            }
//            else
//            {
//                throw new NotSupportedException("Type not supported");
//            }
//        }
//        public FullInt(long value, toBase Base = toBase.hex)
//        {
//            Stack<T> bytes = new Stack<T>();
//            this.Base = Base;

//            if (value < 0)
//            {
//                this.s = true;
//                value = value * -1;
//            }
//            if (value == 0)
//            {
//                bytes.Push(T.CreateChecked(0));
//            }
//            else
//            {
//                while (value > 0)
//                {
//                    bytes.Push(Base == toBase.dec 
//                        ? T.CreateChecked((ulong)value % GetMaxDec()) 
//                        : T.CreateChecked(value));
//                    value = value / ((int)Base * (int)Base);
//                }
//            }

//            this.value = bytes.ToArray();
//        }
//        public FullInt(T[] value)
//        {
//            this.value = value;
//            this.s = false;
//            this.Base = toBase.hex;
//        }

//        public FullInt(byte value, toBase Base = toBase.hex)
//        {
//            if (GetTypeSize() == 1)
//            {
//                var baseMaxValue = GetMaxDec();
//                this.value = new[] { T.CreateChecked(1), T.CreateChecked(value % 100) };
//            }
//            else
//            {
//                this.value = new[] { T.CreateChecked(value) };
//            }
//        }

//        public override String ToString()
//        {
//            return this.ToString((byte)Base);
//        }

//        public static FullInt invertor(FullInt value)
//        {
//            FullInt result = new FullInt(value);
//            result.s = !value.s;
//            return result;
//        }

//        public override bool Equals(object obj)
//        {
//            if (obj is FullInt || obj is byte || obj is int || obj is long)
//            {
//                return FullIntMath.IsEquals(this, (FullInt)obj);
//            }
//            else
//            {
//                return base.Equals(obj);
//            }
//        }

//        public override int GetHashCode()
//        {
//            return this.value.GetHashCode();
//        }

//        public static FullInt operator +(FullInt a, FullInt b)
//        {
//            return FullIntMath.add(a, b);
//        }
//        public static FullInt operator -(FullInt a, FullInt b)
//        {
//            return FullIntMath.sub(a, b);
//        }
//        public static FullInt operator *(FullInt a, FullInt b)
//        {
//            return FullIntMath.mult(a, b);
//        }
//        public static FullInt operator /(FullInt a, FullInt b)
//        {
//            //if (b == 0)
//            //{
//            //    throw new System.DivideByZeroException();
//            //}
//            return FullIntMath.div(a, b);
//        }
//        public static FullInt operator %(FullInt a, FullInt b)
//        {
//            //if (b == 0)
//            //{
//            //    throw new System.DivideByZeroException();
//            //}
//            return FullIntMath.mod(a, b);
//        }
//        public static FullInt operator ^(FullInt a, FullInt b)
//        {
//            return FullIntMath.pow(a, b);
//        }
//        public static bool operator >(FullInt a, FullInt b)
//        {
//            return FullIntMath.IsABiggerB(a, b);
//        }
//        public static bool operator <(FullInt a, FullInt b)
//        {
//            return FullIntMath.IsABiggerB(b, a);
//        }
//        public static bool operator >=(FullInt a, FullInt b)
//        {
//            return FullIntMath.IsABiggerOrEqualsB(a, b);
//        }
//        public static bool operator <=(FullInt a, FullInt b)
//        {
//            return FullIntMath.IsABiggerOrEqualsB(b, a);
//        }
//        public static bool operator ==(FullInt a, FullInt b)
//        {
//            return FullIntMath.IsEquals(a, b);
//        }
//        public static bool operator !=(FullInt a, FullInt b)
//        {
//            return !FullIntMath.IsEquals(a, b);
//        }
//        public static FullInt operator ++(FullInt a)
//        {
//            return FullIntMath.add(a, 1);
//        }
//        public static FullInt operator --(FullInt a)
//        {
//            return FullIntMath.sub(a, 1);
//        }
//        public static FullInt operator -(FullInt a)
//        {
//            return invertor(a);
//        }

//        public static implicit operator FullInt(byte value)
//        {
//            return new FullInt(value);
//        }
//        public static implicit operator FullInt(int value)
//        {
//            return new FullInt(value);
//        }
//        public static implicit operator FullInt(long value)
//        {
//            return new FullInt(value);
//        }
//        public static implicit operator FullInt(byte[] value)
//        {
//            return new FullInt(value);
//        }
//        public static explicit operator byte[](FullInt value)
//        {
//            return value.value;
//        }
//        public static implicit operator String(FullInt value)
//        {
//            return value.ToString((byte)value.Base);
//        }
//        public static implicit operator FullInt(String value)
//        {
//            return FullIntConvertor.ToFullInt(value);
//        }

//    }
//}