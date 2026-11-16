using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FullInt.Core
{
    
    public class FullInt64 : IEquatable<FullInt64>
    {
        public bool IsDec { get; private set; } = false;

        public const ulong maxDecCapasity = 10_000_000_000_000_000_000;
        public const ulong maxDecValue = maxDecCapasity - 1;

        private ulong[] _value = null!;
        public ulong[] value
        {
            get => _value;
            set { _value = value; _owned = false; }   // an outside array: never written in place
        }

        // true while _value was made by the in-place API (Add(result, ...) etc.) for this object
        // and has not been handed out since: only then the API writes into it. Arrays are shared
        // between objects elsewhere (copy constructor, operators), so this is the guard.
        private bool _owned;

        // the word array to write a result of at least minLength words into: the own one when it
        // is long enough, otherwise a new one (owned from now on)
        internal ulong[] WritableBuffer(int minLength, bool mustBeNew)
        {
            if (_owned && !mustBeNew && _value.Length >= minLength) return _value;
            var fresh = new ulong[minLength];
            _value = fresh;
            _owned = true;
            return fresh;
        }

        // after a binary-mode result was written into WritableBuffer
        internal void SetResult(bool negative)
        {
            _sing = negative;
            IsDec = false;
        }

        // result = other, sharing other's array (the in-place API's fallback paths)
        internal void Assign(FullInt64 other)
        {
            value = other.value;
            _sing = other.Sing;
            IsDec = other.IsDec;
            other._owned = false;
        }

        // Zero is never negative: the sign is normalized on read, so "-0" cannot leak
        // out of any operation (or be constructed by hand).
        private bool _sing;
        public bool Sing
        {
            get => _sing && !FullHelper.IsZero(value);
            set => _sing = value;
        }
		private static FullInt64Engine _engine = FullInt64Engine.Instance;
		
        public FullInt64()
        {
            _value = new ulong[1] { 0 };
            Sing = false;
        }

        public FullInt64(bool s, ulong[] value, toBase Base = toBase.hex)
        {
            _value = value;   // the field: a new object owns nothing yet (no setter work)
            this.Sing = s;
            this.IsDec = Base == toBase.dec;
        }
        public FullInt64(FullInt64 value) : this(value.Sing, value.value)
        {
            this.IsDec = value.IsDec;
            value._owned = false;   // the array is shared now
        }

        public FullInt64(ulong value, toBase Base = toBase.hex)
        {
            this.IsDec = Base == toBase.dec;
            _value = new ulong[] { (ulong)value };
        }
        public FullInt64(long value, toBase Base = toBase.hex)
        {
            this.IsDec = Base == toBase.dec;

            if (value < 0)
            {
                this.Sing = true;
                value = value * -1;
            }
            _value = new ulong[] { (ulong)value };
        }
        public FullInt64(byte value, toBase Base = toBase.hex) : this((long)value, Base)
        {
        }
        public FullInt64(int value, toBase Base = toBase.hex) : this((long)value, Base)
        {
        }

        /// <summary>
        /// Magnitude from bytes, little-endian: magnitude[0] is the least significant byte.
        /// The sign is a separate flag (the bytes are never two's complement).
        /// </summary>
        public FullInt64(byte[] magnitude, bool negative = false)
        {
            if (magnitude is null) throw new ArgumentNullException(nameof(magnitude));
            int words = Math.Max((magnitude.Length + 7) / 8, 1);
            var limbs = new ulong[words];                 // limbs[0] is the most significant word
            for (int i = 0; i < magnitude.Length; i++)
            {
                limbs[words - 1 - i / 8] |= (ulong)magnitude[i] << ((i % 8) * 8);
            }
            _value = limbs;
            this.Sing = negative;
        }

        public static FullInt64 Parse(string value)
        {
            return _engine.ToFullInt64(value);
        }
        
        public override String ToString()
        {
            return _engine.ToString(this, (byte)(IsDec ? 10 : 16));
        }
		public String ToString(byte Base)
        {
            return _engine.ToString(this, Base);
        }
        public static FullInt64 invertor(FullInt64 value)
        {
            FullInt64 result = new FullInt64(value);//check if need a copy
            result.Sing = !value.Sing;
            return result;
        }

        public override bool Equals(object? obj)
        {
            // boxed primitives need an explicit conversion: (FullInt64)obj on a boxed int is an
            // unboxing cast and throws InvalidCastException
            switch (obj)
            {
                case FullInt64 f: return Equals(f);
                case int i: return Equals(new FullInt64(i));
                case long l: return Equals(new FullInt64(l));
                case byte b: return Equals(new FullInt64(b));
                case sbyte sb: return Equals(new FullInt64((long)sb));
                case short s: return Equals(new FullInt64((long)s));
                case ushort us: return Equals(new FullInt64((long)us));
                case uint ui: return Equals(new FullInt64((long)ui));
                case ulong ul: return Equals(new FullInt64(ul));
                default: return false;
            }
        }

        public bool Equals(FullInt64? other)
        {
            return !(other is null) && _engine.IsEquals(this, other);
        }

        // Consistent with Equals: same numeric value → same hash, regardless of
        // leading zero words or decimal/binary storage.
        public override int GetHashCode()
        {
            var words = IsDec ? _engine.ConvertToBase(this, toBase.hex).value : value;
            int start = 0;
            while (start < words.Length - 1 && words[start] == 0) start++;

            var hash = new HashCode();
            hash.Add(Sing);
            for (int i = start; i < words.Length; i++) hash.Add(words[i]);
            return hash.ToHashCode();
        }

        public static FullInt64 operator +(FullInt64 a, FullInt64 b)
        {
            return _engine.add(a, b);
        }
        public static FullInt64 operator -(FullInt64 a, FullInt64 b)
        {
            return _engine.sub(a, b);
        }
        public static FullInt64 operator *(FullInt64 a, FullInt64 b)
        {
            return _engine.mult(a, b);
        }
        public static FullInt64 operator /(FullInt64 a, FullInt64 b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return _engine.div(a, b);
        }
        public static FullInt64 operator %(FullInt64 a, FullInt64 b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return _engine.mod(a, b);
        }
        public static FullInt64 operator ^(FullInt64 a, FullInt64 b)
        {
            return _engine.pow(a, b);
        }
        public static bool operator >(FullInt64 a, FullInt64 b)
        {
            return _engine.IsABiggerB(a, b);
        }
        public static bool operator <(FullInt64 a, FullInt64 b)
        {
            return _engine.IsABiggerB(b, a);
        }
        public static bool operator >=(FullInt64 a, FullInt64 b)
        {
            return _engine.IsABiggerOrEqualsB(a, b);
        }
        public static bool operator <=(FullInt64 a, FullInt64 b)
        {
            return _engine.IsABiggerOrEqualsB(b, a);
        }
        public static bool operator ==(FullInt64? a, FullInt64? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return _engine.IsEquals(a, b);
        }
        public static bool operator !=(FullInt64? a, FullInt64? b)
        {
            return !(a == b);
        }
        public static FullInt64 operator ++(FullInt64 a)
        {
            return _engine.add(a, 1);
        }
        public static FullInt64 operator --(FullInt64 a)
        {
            return _engine.sub(a, 1);
        }
        public static FullInt64 operator -(FullInt64 a)
        {
            return invertor(a);
        }

        // bitwise, BigInteger semantics (negative numbers as two's complement)
        public static FullInt64 operator &(FullInt64 a, FullInt64 b)
        {
            return _engine.and(a, b);
        }
        public static FullInt64 operator |(FullInt64 a, FullInt64 b)
        {
            return _engine.or(a, b);
        }
        // '^' is the power operator in FullInt64, so XOR is a method
        public static FullInt64 Xor(FullInt64 a, FullInt64 b)
        {
            return _engine.xor(a, b);
        }
        public static FullInt64 operator ~(FullInt64 a)
        {
            return _engine.not(a);
        }
        public static FullInt64 operator <<(FullInt64 a, int shift)
        {
            return _engine.shiftLeft(a, shift);
        }
        public static FullInt64 operator >>(FullInt64 a, int shift)
        {
            return _engine.shiftRight(a, shift);
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
            value._owned = false;   // handed out
            return value.value;
        }

        // ---- GMP style: into an existing result ----
        // result = a op b. The result object's word array is reused when this API made it and it
        // is long enough (no allocation in a loop); it may then carry leading zero words, which
        // every operation accepts. result may be a or b. A word array taken from result.value
        // is overwritten by the next such call on result: copy it to keep it.

        public static void Add(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.addInto(result, a, b, negate2: false);
        public static void Subtract(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.addInto(result, a, b, negate2: true);
        public static void Multiply(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.multInto(result, a, b);
        public static void ShiftLeft(FullInt64 result, FullInt64 a, int shift) => _engine.shiftLeftInto(result, a, shift);
        public static void ShiftRight(FullInt64 result, FullInt64 a, int shift) => _engine.shiftRightInto(result, a, shift);
        public static void And(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.bitwiseInto(result, 0, a, b);
        public static void Or(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.bitwiseInto(result, 1, a, b);
        public static void Xor(FullInt64 result, FullInt64 a, FullInt64 b) => _engine.bitwiseInto(result, 2, a, b);
        // truncating, as / and %
        public static void Divide(FullInt64 result, FullInt64 a, FullInt64 b) => result.Assign(_engine.div(a, b));
        public static void Remainder(FullInt64 result, FullInt64 a, FullInt64 b) => result.Assign(_engine.mod(a, b));
        // explicit: use ToString()/ToString(base); an implicit conversion silently allocated a string
        public static explicit operator string(FullInt64 value)
        {
            return _engine.ToString(value, (byte)(value.IsDec ? 10 : 16));
        }
        // explicit: an implicit string → number conversion made e.g. x.Equals("5") compile and
        // return true, and turns any string into a number silently
        public static explicit operator FullInt64(String value)
        {
            return _engine.ToFullInt64(value);
        }

    }
}