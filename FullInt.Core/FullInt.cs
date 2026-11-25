using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FullNumerics
{
    
    public class FullInt : IEquatable<FullInt>
    {
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

        // after a result was written into WritableBuffer
        internal void SetResult(bool negative)
        {
            _sing = negative;
        }

        // result = other, sharing other's array (the in-place API's fallback paths)
        internal void Assign(FullInt other)
        {
            value = other.value;
            _sing = other.Sing;
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
		private static FullIntEngine _engine = FullIntEngine.Instance;
		
        public FullInt()
        {
            _value = new ulong[1] { 0 };
            Sing = false;
        }

        public FullInt(bool s, ulong[] value)
        {
            _value = value;   // the field: a new object owns nothing yet (no setter work)
            this.Sing = s;
        }
        public FullInt(FullInt value) : this(value.Sing, value.value)
        {
            value._owned = false;   // the array is shared now
        }

        public FullInt(ulong value)
        {
            _value = new ulong[] { (ulong)value };
        }
        public FullInt(long value)
        {
            if (value < 0)
            {
                this.Sing = true;
                value = value * -1;
            }
            _value = new ulong[] { (ulong)value };
        }
        public FullInt(byte value) : this((long)value)
        {
        }
        public FullInt(int value) : this((long)value)
        {
        }

        /// <summary>
        /// Magnitude from bytes, little-endian: magnitude[0] is the least significant byte.
        /// The sign is a separate flag (the bytes are never two's complement).
        /// </summary>
        public FullInt(byte[] magnitude, bool negative = false)
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

        public static FullInt Parse(string value)
        {
            return _engine.ToFullInt(value);
        }
        
        public override String ToString()
        {
            return _engine.ToString(this, 16);
        }
		public String ToString(byte Base)
        {
            return _engine.ToString(this, Base);
        }
        public static FullInt invertor(FullInt value)
        {
            FullInt result = new FullInt(value);//check if need a copy
            result.Sing = !value.Sing;
            return result;
        }

        public override bool Equals(object? obj)
        {
            // boxed primitives need an explicit conversion: (FullInt)obj on a boxed int is an
            // unboxing cast and throws InvalidCastException
            switch (obj)
            {
                case FullInt f: return Equals(f);
                case int i: return Equals(new FullInt(i));
                case long l: return Equals(new FullInt(l));
                case byte b: return Equals(new FullInt(b));
                case sbyte sb: return Equals(new FullInt((long)sb));
                case short s: return Equals(new FullInt((long)s));
                case ushort us: return Equals(new FullInt((long)us));
                case uint ui: return Equals(new FullInt((long)ui));
                case ulong ul: return Equals(new FullInt(ul));
                default: return false;
            }
        }

        public bool Equals(FullInt? other)
        {
            return !(other is null) && _engine.IsEquals(this, other);
        }

        // Consistent with Equals: same numeric value → same hash, regardless of
        // leading zero words.
        public override int GetHashCode()
        {
            var words = value;
            int start = 0;
            while (start < words.Length - 1 && words[start] == 0) start++;

            var hash = new HashCode();
            hash.Add(Sing);
            for (int i = start; i < words.Length; i++) hash.Add(words[i]);
            return hash.ToHashCode();
        }

        public static FullInt operator +(FullInt a, FullInt b)
        {
            return _engine.add(a, b);
        }
        public static FullInt operator -(FullInt a, FullInt b)
        {
            return _engine.sub(a, b);
        }
        public static FullInt operator *(FullInt a, FullInt b)
        {
            return _engine.mult(a, b);
        }
        public static FullInt operator /(FullInt a, FullInt b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return _engine.div(a, b);
        }
        public static FullInt operator %(FullInt a, FullInt b)
        {
            //if (b == 0)
            //{
            //    throw new System.DivideByZeroException();
            //}
            return _engine.mod(a, b);
        }
        public static FullInt operator ^(FullInt a, FullInt b)
        {
            return _engine.pow(a, b);
        }
        public static bool operator >(FullInt a, FullInt b)
        {
            return _engine.IsABiggerB(a, b);
        }
        public static bool operator <(FullInt a, FullInt b)
        {
            return _engine.IsABiggerB(b, a);
        }
        public static bool operator >=(FullInt a, FullInt b)
        {
            return _engine.IsABiggerOrEqualsB(a, b);
        }
        public static bool operator <=(FullInt a, FullInt b)
        {
            return _engine.IsABiggerOrEqualsB(b, a);
        }
        public static bool operator ==(FullInt? a, FullInt? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return _engine.IsEquals(a, b);
        }
        public static bool operator !=(FullInt? a, FullInt? b)
        {
            return !(a == b);
        }
        public static FullInt operator ++(FullInt a)
        {
            return _engine.add(a, 1);
        }
        public static FullInt operator --(FullInt a)
        {
            return _engine.sub(a, 1);
        }
        public static FullInt operator -(FullInt a)
        {
            return invertor(a);
        }

        // bitwise, BigInteger semantics (negative numbers as two's complement)
        public static FullInt operator &(FullInt a, FullInt b)
        {
            return _engine.and(a, b);
        }
        public static FullInt operator |(FullInt a, FullInt b)
        {
            return _engine.or(a, b);
        }
        // '^' is the power operator in FullInt, so XOR is a method
        public static FullInt Xor(FullInt a, FullInt b)
        {
            return _engine.xor(a, b);
        }
        public static FullInt operator ~(FullInt a)
        {
            return _engine.not(a);
        }
        public static FullInt operator <<(FullInt a, int shift)
        {
            return _engine.shiftLeft(a, shift);
        }
        public static FullInt operator >>(FullInt a, int shift)
        {
            return _engine.shiftRight(a, shift);
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
        public static explicit operator ulong[](FullInt value)
        {
            value._owned = false;   // handed out
            return value.value;
        }

        // ---- GMP style: into an existing result ----
        // result = a op b. The result object's word array is reused when this API made it and it
        // is long enough (no allocation in a loop); it may then carry leading zero words, which
        // every operation accepts. result may be a or b. A word array taken from result.value
        // is overwritten by the next such call on result: copy it to keep it.

        public static void Add(FullInt result, FullInt a, FullInt b) => _engine.addInto(result, a, b, negate2: false);
        public static void Subtract(FullInt result, FullInt a, FullInt b) => _engine.addInto(result, a, b, negate2: true);
        public static void Multiply(FullInt result, FullInt a, FullInt b) => _engine.multInto(result, a, b);
        public static void ShiftLeft(FullInt result, FullInt a, int shift) => _engine.shiftLeftInto(result, a, shift);
        public static void ShiftRight(FullInt result, FullInt a, int shift) => _engine.shiftRightInto(result, a, shift);
        public static void And(FullInt result, FullInt a, FullInt b) => _engine.bitwiseInto(result, 0, a, b);
        public static void Or(FullInt result, FullInt a, FullInt b) => _engine.bitwiseInto(result, 1, a, b);
        public static void Xor(FullInt result, FullInt a, FullInt b) => _engine.bitwiseInto(result, 2, a, b);
        // truncating, as / and %
        public static void Divide(FullInt result, FullInt a, FullInt b) => result.Assign(_engine.div(a, b));
        public static void Remainder(FullInt result, FullInt a, FullInt b) => result.Assign(_engine.mod(a, b));
        // explicit: use ToString()/ToString(base); an implicit conversion silently allocated a string
        public static explicit operator string(FullInt value)
        {
            return _engine.ToString(value, 16);
        }
        // explicit: an implicit string → number conversion made e.g. x.Equals("5") compile and
        // return true, and turns any string into a number silently
        public static explicit operator FullInt(String value)
        {
            return _engine.ToFullInt(value);
        }

    }
}