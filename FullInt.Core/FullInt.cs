using System;
using System.Globalization;
using System.Numerics;
using System.Text;
#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
#endif

namespace FullNumerics
{
    /// <summary>
    /// The order of the words in <see cref="FullInt.FromWords"/>, <see cref="FullInt.ToWords"/> and
    /// <see cref="FullInt.TryWriteWords"/>. The bytes inside a word are the machine's own (a ulong).
    /// </summary>
    public enum WordOrder
    {
        /// <summary>words[0] is the least significant word (GMP mpz_import / mpz_export order -1).</summary>
        LeastSignificantFirst,
        /// <summary>words[0] is the most significant word (order 1).</summary>
        MostSignificantFirst,
    }

    /// <summary>
    /// An arbitrary-precision signed integer: a sign and a magnitude of 64-bit words. One type on
    /// every platform; the kernels under it (asm for x64 / x86 / ARM64, or managed) are picked by
    /// the process architecture. Operators allocate a new result; the static methods with a result
    /// parameter (Add(result, a, b), ...) reuse the result's storage, GMP style.
    /// </summary>
#if NET5_0_OR_GREATER
    [JsonConverter(typeof(FullIntJsonConverter))]
#endif
    public sealed class FullInt : IEquatable<FullInt>, IComparable<FullInt>, IComparable, IFormattable
    {
        // the magnitude, most significant word first; may carry leading zero words
        private ulong[] _value = null!;
        internal ulong[] value
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
            _negative = negative;
        }

        // result = other, sharing other's array (the in-place API's fallback paths)
        internal void Assign(FullInt other)
        {
            value = other.value;
            _negative = other.IsNegative;
            other._owned = false;
        }

        // Zero is never negative: the sign is normalized on read, so "-0" cannot leak
        // out of any operation (or be constructed by hand).
        private bool _negative;

        /// <summary>True when the value is below zero (zero is never negative).</summary>
        public bool IsNegative
        {
            get => _negative && !FullHelper.IsZero(_value);
            internal set => _negative = value;
        }

        /// <summary>-1, 0 or 1.</summary>
        public int Sign => FullHelper.IsZero(_value) ? 0 : _negative ? -1 : 1;

        public bool IsZero => FullHelper.IsZero(_value);
        public bool IsOne => !_negative && _value[_value.Length - 1] == 1 && Top() == _value.Length - 1;
        public bool IsEven => (_value[_value.Length - 1] & 1) == 0;

        // new objects each time: the in-place API may write into a result object
        public static FullInt Zero => new FullInt();
        public static FullInt One => new FullInt(1);
        public static FullInt MinusOne => new FullInt(-1);

        private static FullIntEngine _engine = FullIntEngine.Instance;

        // ---- construction ----

        public FullInt()
        {
            _value = new ulong[1];
        }

        // takes the array (most significant word first) as it is
        internal FullInt(bool negative, ulong[] magnitude)
        {
            _value = magnitude;
            _negative = negative;
        }

        /// <summary>A copy of <paramref name="value"/> (the storage is shared until either is written in place).</summary>
        public FullInt(FullInt value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            _value = value._value;
            _negative = value._negative;
            value._owned = false;   // the array is shared now
        }

        public FullInt(int value) : this((long)value) { }
        public FullInt(uint value) : this((ulong)value) { }

        public FullInt(long value)
        {
            if (value < 0)
            {
                _negative = true;
                _value = new ulong[] { (ulong)(-(value + 1)) + 1 };   // long.MinValue too
            }
            else
            {
                _value = new ulong[] { (ulong)value };
            }
        }

        public FullInt(ulong value)
        {
            _value = new ulong[] { value };
        }

        /// <summary>
        /// From bytes, as <see cref="BigInteger"/>(ReadOnlySpan&lt;byte&gt;, bool, bool): two's complement
        /// unless <paramref name="isUnsigned"/>, least significant byte first unless
        /// <paramref name="isBigEndian"/>. No bytes: zero.
        /// </summary>
        public FullInt(ReadOnlySpan<byte> value, bool isUnsigned = false, bool isBigEndian = false)
        {
            int n = value.Length;
            if (n == 0)
            {
                _value = new ulong[1];
                return;
            }
            byte top = isBigEndian ? value[0] : value[n - 1];
            bool negative = !isUnsigned && (top & 0x80) != 0;
            int words = (n + 7) / 8;
            var limbs = new ulong[words];   // most significant first
            for (int i = 0; i < n; i++)
            {
                byte b = isBigEndian ? value[n - 1 - i] : value[i];
                limbs[words - 1 - (i >> 3)] |= (ulong)b << ((i & 7) * 8);
            }
            if (negative)
            {
                // the magnitude is 2^(8n) - x: sign-extend to whole words, then negate them
                int extra = words * 8 - n;
                if (extra > 0) limbs[0] |= ulong.MaxValue << (64 - extra * 8);
                ulong carry = 1;
                for (int i = words - 1; i >= 0; i--)
                {
                    ulong v = ~limbs[i] + carry;
                    carry = v == 0 && carry == 1 ? 1UL : 0UL;
                    limbs[i] = v;
                }
            }
            _value = FullHelper.Trimmed(limbs);
            _negative = negative;
        }

        /// <summary>
        /// From the words of the magnitude (64 bits each) in the given order and a sign, as GMP
        /// mpz_import (with the machine's byte order inside a word). No words: zero.
        /// </summary>
        public static FullInt FromWords(ReadOnlySpan<ulong> magnitude, WordOrder order, bool negative = false)
        {
            if (magnitude.Length == 0) return new FullInt();
            var limbs = magnitude.ToArray();
            if (order == WordOrder.LeastSignificantFirst) Array.Reverse(limbs);
            else if (order != WordOrder.MostSignificantFirst) throw new ArgumentOutOfRangeException(nameof(order));
            return new FullInt(negative, FullHelper.Trimmed(limbs));
        }

        // ---- export ----

        // index of the most significant nonzero word (the last one for zero)
        private int Top()
        {
            var w = _value;
            int i = 0;
            while (i < w.Length - 1 && w[i] == 0) i++;
            return i;
        }

        /// <summary>The number of words <see cref="ToWords"/> returns: at least 1 (zero is one zero word).</summary>
        public int GetWordCount() => _value.Length - Top();

        /// <summary>The magnitude's words in the given order (the sign is <see cref="IsNegative"/>); zero is one zero word.</summary>
        public ulong[] ToWords(WordOrder order)
        {
            var result = new ulong[GetWordCount()];
            TryWriteWords(result, out _, order);
            return result;
        }

        /// <summary>
        /// Writes the magnitude's words in the given order; false (nothing written) when
        /// <paramref name="destination"/> is shorter than <see cref="GetWordCount"/>.
        /// </summary>
        public bool TryWriteWords(Span<ulong> destination, out int wordsWritten, WordOrder order)
        {
            if (order != WordOrder.LeastSignificantFirst && order != WordOrder.MostSignificantFirst)
                throw new ArgumentOutOfRangeException(nameof(order));
            int top = Top();
            int count = _value.Length - top;
            if (destination.Length < count)
            {
                wordsWritten = 0;
                return false;
            }
            var src = _value.AsSpan(top, count);
            if (order == WordOrder.MostSignificantFirst)
            {
                src.CopyTo(destination);
            }
            else
            {
                for (int i = 0; i < count; i++) destination[i] = src[count - 1 - i];
            }
            wordsWritten = count;
            return true;
        }

        // byte i of the magnitude (0: the least significant), words from top
        private byte MagnitudeByte(int top, int i)
        {
            return (byte)(_value[_value.Length - 1 - (i >> 3)] >> ((i & 7) * 8));
        }

        // the magnitude's significant bytes (at least 1)
        private int MagnitudeByteCount(int top)
        {
            ulong w = _value[top];
            int bytes = 8;
            while (bytes > 1 && (w >> ((bytes - 1) * 8)) == 0) bytes--;
            return (_value.Length - 1 - top) * 8 + bytes;
        }

        /// <summary>The number of bytes <see cref="ToByteArray"/> returns.</summary>
        public int GetByteCount(bool isUnsigned = false)
        {
            int top = Top();
            int k = MagnitudeByteCount(top);
            bool negative = IsNegative;
            if (isUnsigned)
            {
                if (negative) throw new OverflowException("A negative value has no unsigned bytes.");
                return k;
            }
            byte tb = MagnitudeByte(top, k - 1);
            if (!negative) return tb >= 0x80 ? k + 1 : k;
            // -m fits in k bytes while m <= 2^(8k-1)
            if (tb < 0x80) return k;
            if (tb > 0x80) return k + 1;
            for (int i = 0; i < k - 1; i++)
                if (MagnitudeByte(top, i) != 0) return k + 1;
            return k;
        }

        /// <summary>
        /// The bytes as <see cref="BigInteger.ToByteArray(bool, bool)"/>: two's complement unless
        /// <paramref name="isUnsigned"/> (then a negative value throws OverflowException), least
        /// significant byte first unless <paramref name="isBigEndian"/>; the fewest bytes.
        /// </summary>
        public byte[] ToByteArray(bool isUnsigned = false, bool isBigEndian = false)
        {
            var result = new byte[GetByteCount(isUnsigned)];
            TryWriteBytes(result, out _, isUnsigned, isBigEndian);
            return result;
        }

        /// <summary>
        /// Writes the bytes of <see cref="ToByteArray"/>; false (nothing written) when
        /// <paramref name="destination"/> is too short.
        /// </summary>
        public bool TryWriteBytes(Span<byte> destination, out int bytesWritten, bool isUnsigned = false, bool isBigEndian = false)
        {
            int count = GetByteCount(isUnsigned);
            if (destination.Length < count)
            {
                bytesWritten = 0;
                return false;
            }
            int top = Top();
            int k = MagnitudeByteCount(top);
            var d = destination.Slice(0, count);
            for (int i = 0; i < count; i++) d[i] = i < k ? MagnitudeByte(top, i) : (byte)0;
            if (IsNegative)
            {
                int carry = 1;
                for (int i = 0; i < count; i++)
                {
                    int v = (byte)~d[i] + carry;
                    d[i] = (byte)v;
                    carry = v >> 8;
                }
            }
            if (isBigEndian) d.Reverse();
            bytesWritten = count;
            return true;
        }

        // ---- text ----

        /// <summary>
        /// Decimal digits with an optional sign: "-123", "+5", "  42 " (white space around is
        /// allowed). Also "0x1F" (hex) and a leading 0 as octal.
        /// </summary>
        public static FullInt Parse(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (!TryNormalize(value, out var text)) throw new FormatException("The value could not be parsed as an integer.");
            return _engine.ToFullInt(text);
        }

        public static bool TryParse(string? value, out FullInt result)
        {
            if (value != null && TryNormalize(value, out var text))
            {
                try
                {
                    result = _engine.ToFullInt(text);
                    return true;
                }
                catch (FormatException) { }
                catch (OverflowException) { }
            }
            result = new FullInt();
            return false;
        }

        // trims, drops a '+', checks the digits of the form the engine reads
        private static bool TryNormalize(string value, out string text)
        {
            text = value.Trim();
            int i = 0;
            bool negative = false;
            if (text.Length > 0 && (text[0] == '-' || text[0] == '+'))
            {
                negative = text[0] == '-';
                i = 1;
            }
            bool hex = text.Length >= i + 2 && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X');
            int digitsFrom = hex ? i + 2 : i;
            if (digitsFrom >= text.Length) return false;
            for (int j = digitsFrom; j < text.Length; j++)
            {
                char c = text[j];
                bool ok = hex ? (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')
                              : c >= '0' && c <= '9';
                if (!ok) return false;
            }
            text = (negative ? "-" : "") + text.Substring(i);
            return true;
        }

        /// <summary>Decimal.</summary>
        public override string ToString() => _engine.ToString(this, 10);

        /// <summary>
        /// "D" (or null / empty): decimal; "X" / "x": hexadecimal, upper / lower case; "B": binary.
        /// A number after the letter is the least number of digits (zeros in front). Negative
        /// values get a '-' before the digits of the magnitude (not two's complement).
        /// </summary>
        public string ToString(string? format) => ToString(format, null);

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (string.IsNullOrEmpty(format)) return ToString();
            char kind = format![0];
            int precision = 0;
            if (format.Length > 1 && !int.TryParse(format.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out precision))
                throw new FormatException($"Format '{format}' is not supported.");
            string digits;
            switch (kind)
            {
                case 'D':
                case 'd':
                    digits = _engine.ToString(IsNegative ? Negate(this) : this, 10);
                    break;
                case 'X':
                case 'x':
                    digits = Digits(4, kind == 'x');
                    break;
                case 'B':
                case 'b':
                    digits = Digits(1, false);
                    break;
                default:
                    throw new FormatException($"Format '{format}' is not supported.");
            }
            if (digits.Length < precision) digits = new string('0', precision - digits.Length) + digits;
            return IsNegative ? "-" + digits : digits;
        }

        // the magnitude in base 2^bits (1: binary, 4: hex), no leading zeros
        private string Digits(int bits, bool lower)
        {
            int top = Top();
            int perWord = 64 / bits;
            var sb = new StringBuilder((_value.Length - top) * perWord);
            string alphabet = lower ? "0123456789abcdef" : "0123456789ABCDEF";
            ulong mask = (1UL << bits) - 1;
            for (int i = top; i < _value.Length; i++)
            {
                ulong w = _value[i];
                for (int d = perWord - 1; d >= 0; d--)
                {
                    int digit = (int)((w >> (d * bits)) & mask);
                    if (sb.Length == 0 && digit == 0 && !(i == _value.Length - 1 && d == 0)) continue;
                    sb.Append(alphabet[digit]);
                }
            }
            return sb.ToString();
        }

        // base 2..16 with the engine's prefixes ("0x" for 16); the tests' format
        internal string ToString(byte radix) => _engine.ToString(this, radix);

        // ---- equality, order ----

        public override bool Equals(object? obj)
        {
            // boxed primitives need an explicit conversion: (FullInt)obj on a boxed int is an
            // unboxing cast and throws InvalidCastException
            switch (obj)
            {
                case FullInt f: return Equals(f);
                case int i: return Equals(new FullInt(i));
                case long l: return Equals(new FullInt(l));
                case byte b: return Equals(new FullInt((int)b));
                case sbyte sb: return Equals(new FullInt((int)sb));
                case short s: return Equals(new FullInt((int)s));
                case ushort us: return Equals(new FullInt((int)us));
                case uint ui: return Equals(new FullInt(ui));
                case ulong ul: return Equals(new FullInt(ul));
                case BigInteger bi: return Equals((FullInt)bi);
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
            var words = _value;
            int start = Top();
            var hash = new HashCode();
            hash.Add(IsNegative);
            for (int i = start; i < words.Length; i++) hash.Add(words[i]);
            return hash.ToHashCode();
        }

        public int CompareTo(FullInt? other) => other is null ? 1 : _engine.Compare(this, other);

        public int CompareTo(object? obj)
        {
            if (obj is null) return 1;
            if (obj is FullInt f) return _engine.Compare(this, f);
            throw new ArgumentException("The object is not a FullInt.", nameof(obj));
        }

        public static int Compare(FullInt left, FullInt right) => _engine.Compare(left, right);
        public static FullInt Max(FullInt left, FullInt right) => _engine.Compare(left, right) >= 0 ? left : right;
        public static FullInt Min(FullInt left, FullInt right) => _engine.Compare(left, right) <= 0 ? left : right;

        // ---- arithmetic ----

        public static FullInt Negate(FullInt value)
        {
            var result = new FullInt(value);
            result._negative = !value.IsNegative;
            return result;
        }

        public static FullInt Abs(FullInt value) => value.IsNegative ? Negate(value) : value;

        /// <summary><paramref name="value"/> to the power <paramref name="exponent"/> (at least 0).</summary>
        public static FullInt Pow(FullInt value, int exponent)
        {
            if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent), "The exponent must be non-negative.");
            return _engine.pow(value, new FullInt(exponent));
        }

        /// <summary>The truncated quotient and the remainder (the sign of the dividend), as / and %.</summary>
        public static FullInt DivRem(FullInt dividend, FullInt divisor, out FullInt remainder)
        {
            if (FullHelper.IsZero(divisor._value)) throw new DivideByZeroException();
            return _engine.div(dividend, divisor, out remainder);
        }

        public static FullInt operator +(FullInt a, FullInt b) => _engine.add(a, b);
        public static FullInt operator -(FullInt a, FullInt b) => _engine.sub(a, b);
        public static FullInt operator *(FullInt a, FullInt b) => _engine.mult(a, b);
        /// <summary>Truncated toward zero; DivideByZeroException for b = 0.</summary>
        public static FullInt operator /(FullInt a, FullInt b) => _engine.div(a, b);
        /// <summary>The remainder of the truncated division: the sign of a.</summary>
        public static FullInt operator %(FullInt a, FullInt b) => _engine.mod(a, b);
        public static FullInt operator ++(FullInt a) => _engine.add(a, 1);
        public static FullInt operator --(FullInt a) => _engine.sub(a, 1);
        public static FullInt operator -(FullInt a) => Negate(a);
        public static FullInt operator +(FullInt a) => a;

        public static bool operator >(FullInt a, FullInt b) => _engine.IsABiggerB(a, b);
        public static bool operator <(FullInt a, FullInt b) => _engine.IsABiggerB(b, a);
        public static bool operator >=(FullInt a, FullInt b) => _engine.IsABiggerOrEqualsB(a, b);
        public static bool operator <=(FullInt a, FullInt b) => _engine.IsABiggerOrEqualsB(b, a);

        public static bool operator ==(FullInt? a, FullInt? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return _engine.IsEquals(a, b);
        }
        public static bool operator !=(FullInt? a, FullInt? b) => !(a == b);

        // bitwise, BigInteger semantics (negative numbers as two's complement)
        public static FullInt operator &(FullInt a, FullInt b) => _engine.and(a, b);
        public static FullInt operator |(FullInt a, FullInt b) => _engine.or(a, b);
        /// <summary>Exclusive or, as in C# for int and BigInteger (the power is <see cref="Pow"/>).</summary>
        public static FullInt operator ^(FullInt a, FullInt b) => _engine.xor(a, b);
        public static FullInt operator ~(FullInt a) => _engine.not(a);
        public static FullInt operator <<(FullInt a, int shift) => _engine.shiftLeft(a, shift);
        public static FullInt operator >>(FullInt a, int shift) => _engine.shiftRight(a, shift);

        // ---- conversions ----

        // every integer type, as BigInteger (one per type: no ambiguity between int and uint)
        public static implicit operator FullInt(byte value) => new FullInt((int)value);
        public static implicit operator FullInt(sbyte value) => new FullInt((int)value);
        public static implicit operator FullInt(short value) => new FullInt((int)value);
        public static implicit operator FullInt(ushort value) => new FullInt((int)value);
        public static implicit operator FullInt(int value) => new FullInt(value);
        public static implicit operator FullInt(uint value) => new FullInt(value);
        public static implicit operator FullInt(long value) => new FullInt(value);
        public static implicit operator FullInt(ulong value) => new FullInt(value);

        public static explicit operator long(FullInt value)
        {
            int top = value.Top();
            ulong m = value._value[value._value.Length - 1];
            if (top < value._value.Length - 1) throw new OverflowException("The value does not fit in a long.");
            if (value.IsNegative)
            {
                if (m > 1UL << 63) throw new OverflowException("The value does not fit in a long.");
                return (long)(0 - m);
            }
            if (m > long.MaxValue) throw new OverflowException("The value does not fit in a long.");
            return (long)m;
        }

        public static explicit operator ulong(FullInt value)
        {
            int top = value.Top();
            if (value.IsNegative || top < value._value.Length - 1) throw new OverflowException("The value does not fit in a ulong.");
            return value._value[value._value.Length - 1];
        }

        public static explicit operator int(FullInt value)
        {
            long v = (long)value;
            if (v < int.MinValue || v > int.MaxValue) throw new OverflowException("The value does not fit in an int.");
            return (int)v;
        }

        public static explicit operator uint(FullInt value)
        {
            ulong v = (ulong)value;
            if (v > uint.MaxValue) throw new OverflowException("The value does not fit in a uint.");
            return (uint)v;
        }

        public static explicit operator BigInteger(FullInt value)
        {
#if NET5_0_OR_GREATER
            // the magnitude's words least significant first are its little-endian bytes
            int top = value.Top();
            int count = value._value.Length - top;
            var words = new ulong[count];
            value.TryWriteWords(words, out _, WordOrder.LeastSignificantFirst);
            if (!BitConverter.IsLittleEndian)
                for (int i = 0; i < count; i++) words[i] = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(words[i]);
            var magnitude = new BigInteger(MemoryMarshal.AsBytes(words.AsSpan()), isUnsigned: true, isBigEndian: false);
            return value.IsNegative ? -magnitude : magnitude;
#else
            return new BigInteger(value.ToByteArray());
#endif
        }

        public static explicit operator FullInt(BigInteger value)
        {
#if NET5_0_OR_GREATER
            bool negative = value.Sign < 0;
            var magnitude = BigInteger.Abs(value);
            int bytes = magnitude.GetByteCount(isUnsigned: true);
            var words = new ulong[Math.Max((bytes + 7) / 8, 1)];
            magnitude.TryWriteBytes(MemoryMarshal.AsBytes(words.AsSpan()), out _, isUnsigned: true, isBigEndian: false);
            if (!BitConverter.IsLittleEndian)
                for (int i = 0; i < words.Length; i++) words[i] = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(words[i]);
            return FromWords(words, WordOrder.LeastSignificantFirst, negative);
#else
            return new FullInt(value.ToByteArray());
#endif
        }

        // ---- GMP style: into an existing result ----
        // result = a op b. The result object's word array is reused when this API made it and it
        // is long enough (no allocation in a loop). result may be a or b.

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
    }

#if NET5_0_OR_GREATER
    /// <summary>
    /// System.Text.Json: a FullInt is written as a JSON string of decimal digits ("123"), so that
    /// readers with double numbers (JavaScript) do not round it; a string or a number is read.
    /// </summary>
    public sealed class FullIntJsonConverter : JsonConverter<FullInt>
    {
        public override FullInt Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            string? text;
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                text = reader.GetString();
            }
            else if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
            {
                text = reader.HasValueSequence
                    ? Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence))
                    : Encoding.UTF8.GetString(reader.ValueSpan);
            }
            else
            {
                throw new System.Text.Json.JsonException($"A FullInt is a string or a number, not {reader.TokenType}.");
            }
            if (text is null || !FullInt.TryParse(text, out var result))   // digits only: no fraction, no exponent
                throw new System.Text.Json.JsonException("The value is not an integer.");
            return result;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, FullInt value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
#endif
}
