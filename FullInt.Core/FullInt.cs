using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FullInt.Core
{
    public enum toBase { dec = 10, hex = 16 }
    public class FullInt
    {
        public byte[] value { get; set; }
        public bool s { get; set; }

        public toBase Base { get; set; }
        public FullInt()
        {
            value = new byte[1] { 0 };
            s = false;
            Base = toBase.hex;
        }

        public FullInt(bool s, byte[] value, toBase Base = toBase.hex)
        {
            this.value = value;
            this.s = s;
            this.Base = Base;
        }
        public FullInt(FullInt value)
        {
            this.value = value.value;
            this.s = value.s;
            this.Base = value.Base;
        }

        public FullInt(int value, toBase Base = toBase.hex)
        {
            Stack<byte> bytes = new Stack<byte>();
            this.Base = Base;

            if (value < 0)
            {
                this.s = true;
                value = value * -1;
            }
            if (value == 0)
            {
                bytes.Push(0);
            }
            else
            {
                while (value > 0)
                {
                    bytes.Push((byte)(value % ((int)Base * (int)Base)));
                    value = value / ((int)Base * (int)Base);
                }
            }

            this.value = bytes.ToArray();
        }
        public FullInt(long value, toBase Base = toBase.hex)
        {
            Stack<byte> bytes = new Stack<byte>();
            this.Base = Base;

            if (value < 0)
            {
                this.s = true;
                value = value * -1;
            }
            if (value == 0)
            {
                bytes.Push(0);
            }
            else
            {
                while (value > 0)
                {
                    bytes.Push((byte)(value % ((int)Base * (int)Base)));
                    value = value / ((int)Base * (int)Base);
                }
            }

            this.value = bytes.ToArray();
        }
        public FullInt(byte[] value)
        {
            this.value = value;
            this.s = false;
            this.Base = toBase.hex;
        }
        public FullInt(byte value, toBase Base = toBase.hex)
        {
            Stack<byte> bytes = new Stack<byte>();
            this.Base = Base;

            if (value == 0)
            {
                bytes.Push(0);
            }
            else
            {
                while (value > 0)
                {
                    bytes.Push((byte)(value % ((int)Base * (int)Base)));
                    value = (byte)(value / ((int)Base * (int)Base));
                }
            }

            this.value = bytes.ToArray();
        }



        public override String ToString()
        {
            return this.ToString((byte)Base);
        }





        public static FullInt invertor(FullInt value)
        {
            FullInt result = new FullInt(value);
            result.s = !value.s;
            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj is FullInt || obj is byte || obj is int || obj is long)
            {
                return FullIntMath.IsEquals(this, (FullInt)obj);
            }
            else
            {
                return base.Equals(obj);
            }
        }

        public override int GetHashCode()
        {
            return this.value.GetHashCode();
        }

        public static FullInt operator +(FullInt a, FullInt b)
        {
            return FullIntMath.add(a, b);
        }
        public static FullInt operator -(FullInt a, FullInt b)
        {
            return FullIntMath.sub(a, b);
        }
        public static FullInt operator *(FullInt a, FullInt b)
        {
            return FullIntMath.mult(a, b);
        }
        public static FullInt operator /(FullInt a, FullInt b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return FullIntMath.div(a, b);
        }
        public static FullInt operator %(FullInt a, FullInt b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return FullIntMath.mod(a, b);
        }
        public static FullInt operator ^(FullInt a, FullInt b)
        {
            return FullIntMath.pow(a, b);
        }
        public static bool operator >(FullInt a, FullInt b)
        {
            return FullIntMath.IsABiggerB(a, b);
        }
        public static bool operator <(FullInt a, FullInt b)
        {
            return FullIntMath.IsABiggerB(b, a);
        }
        public static bool operator >=(FullInt a, FullInt b)
        {
            return FullIntMath.IsABiggerOrEqualsB(a, b);
        }
        public static bool operator <=(FullInt a, FullInt b)
        {
            return FullIntMath.IsABiggerOrEqualsB(b, a);
        }
        public static bool operator ==(FullInt a, FullInt b)
        {
            return FullIntMath.IsEquals(a, b);
        }
        public static bool operator !=(FullInt a, FullInt b)
        {
            return !FullIntMath.IsEquals(a, b);
        }
        public static FullInt operator ++(FullInt a)
        {
            return FullIntMath.add(a, 1);
        }
        public static FullInt operator --(FullInt a)
        {
            return FullIntMath.sub(a, 1);
        }
        public static FullInt operator -(FullInt a)
        {
            return invertor(a);
        }

        public static implicit operator FullInt(byte value)
        {
            return new FullInt(value);
        }
        public static implicit operator FullInt(int value)
        {
            return new FullInt(value);
        }
        public static implicit operator FullInt(long value)
        {
            return new FullInt(value);
        }
        public static implicit operator FullInt(byte[] value)
        {
            return new FullInt(value);
        }
        public static explicit operator byte[](FullInt value)
        {
            return value.value;
        }
        public static implicit operator String(FullInt value)
        {
            return value.ToString((byte)value.Base);
        }
        public static implicit operator FullInt(String value)
        {
            return FullIntConvertor.ToFullInt(value);
        }

    }
}