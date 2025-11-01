using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FullInt.Core
{
    
    public class FullInt64
    {
        public bool IsDec { get; private set; } = false;

        public const ulong maxDecCapasity = 10_000_000_000_000_000_000;
        public const ulong maxDecValue = maxDecCapasity - 1;

        public ulong[] value { get; set; }
        public bool Sing { get; set; }

        public FullInt64()
        {
            value = new ulong[1] { 0 };
            Sing = false;
        }

        public FullInt64(bool s, ulong[] value, toBase Base = toBase.hex)
        {
            this.value = value;
            this.Sing = s;
            this.IsDec = Base == toBase.dec;
        }
        public FullInt64(FullInt64 value) : this(value.Sing, value.value)
        {
            this.IsDec = value.IsDec;
        }

        public FullInt64(ulong value, toBase Base = toBase.hex)
        {
            this.IsDec = Base == toBase.dec;
            this.value = new ulong[] { (ulong)value };
        }
        public FullInt64(long value, toBase Base = toBase.hex)
        {
            this.IsDec = Base == toBase.dec;

            if (value < 0)
            {
                this.Sing = true;
                value = value * -1;
            }
            this.value = new ulong[] { (ulong)value };
        }
        public FullInt64(byte value, toBase Base = toBase.hex) : this((long)value, Base)
        {
        }
        public FullInt64(int value, toBase Base = toBase.hex) : this((long)value, Base)
        {
        }
        
        public override String ToString()
        {
            return this.ToString((byte)(IsDec ? 10 : 16));
        }

        public static FullInt64 invertor(FullInt64 value)
        {
            FullInt64 result = new FullInt64(value);//check if need a copy
            result.Sing = !value.Sing;
            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj is FullInt64 || obj is byte || obj is int || obj is long)
            {
                return FullInt64Math.IsEquals(this, (FullInt64)obj);
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

        public static FullInt64 operator +(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.add(a, b);
        }
        public static FullInt64 operator -(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.sub(a, b);
        }
        public static FullInt64 operator *(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.mult(a, b);
        }
        public static FullInt64 operator /(FullInt64 a, FullInt64 b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return FullInt64Math.div(a, b);
        }
        public static FullInt64 operator %(FullInt64 a, FullInt64 b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return FullInt64Math.mod(a, b);
        }
        public static FullInt64 operator ^(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.pow(a, b);
        }
        public static bool operator >(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.IsABiggerB(a, b);
        }
        public static bool operator <(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.IsABiggerB(b, a);
        }
        public static bool operator >=(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.IsABiggerOrEqualsB(a, b);
        }
        public static bool operator <=(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.IsABiggerOrEqualsB(b, a);
        }
        public static bool operator ==(FullInt64 a, FullInt64 b)
        {
            return FullInt64Math.IsEquals(a, b);
        }
        public static bool operator !=(FullInt64 a, FullInt64 b)
        {
            return !FullInt64Math.IsEquals(a, b);
        }
        public static FullInt64 operator ++(FullInt64 a)
        {
            return FullInt64Math.add(a, 1);
        }
        public static FullInt64 operator --(FullInt64 a)
        {
            return FullInt64Math.sub(a, 1);
        }
        public static FullInt64 operator -(FullInt64 a)
        {
            return invertor(a);
        }

        public static implicit operator FullInt64(byte value)
        {
            return new FullInt64(value);
        }
        public static implicit operator FullInt64(int value)
        {
            return new FullInt64(value);
        }
        public static implicit operator FullInt64(long value)
        {
            return new FullInt64(value);
        }
        public static implicit operator FullInt64(byte[] value)
        {
            return new FullInt64(value);
        }
        public static explicit operator ulong[](FullInt64 value)
        {
            return value.value;
        }
        public static implicit operator string(FullInt64 value)
        {
            return value.ToString((byte)(value.IsDec ? 10 : 16));
        }
        public static implicit operator FullInt64(String value)
        {
            return FullIntConvertor.ToFullInt64(value);
        }

    }
}