#if NET7_0_OR_GREATER
using System;
using System.Globalization;
using System.Numerics;

namespace FullNumerics
{
    // Generic math (.NET 7+): FullInt is an IBinaryInteger and an ISignedNumber, as BigInteger, so
    // code written for T : INumber<T> / IBinaryInteger<T> takes it. The bit operations of an
    // unbounded integer work on the shortest two's complement form in 64-bit words (BigInteger:
    // 32-bit words, so LeadingZeroCount, PopCount of a negative value, >>> and the rotations can
    // differ from BigInteger's; the values of every arithmetic operation are the same).
    public sealed partial class FullInt : IBinaryInteger<FullInt>, ISignedNumber<FullInt>
    {
        // ---- identities, classification ----

        static FullInt ISignedNumber<FullInt>.NegativeOne => MinusOne;
        static int INumberBase<FullInt>.Radix => 2;
        static FullInt IAdditiveIdentity<FullInt, FullInt>.AdditiveIdentity => Zero;
        static FullInt IMultiplicativeIdentity<FullInt, FullInt>.MultiplicativeIdentity => One;
        static FullInt IBinaryNumber<FullInt>.AllBitsSet => MinusOne;

        static bool INumberBase<FullInt>.IsCanonical(FullInt value) => true;
        static bool INumberBase<FullInt>.IsComplexNumber(FullInt value) => false;
        static bool INumberBase<FullInt>.IsEvenInteger(FullInt value) => value.IsEven;
        static bool INumberBase<FullInt>.IsFinite(FullInt value) => true;
        static bool INumberBase<FullInt>.IsImaginaryNumber(FullInt value) => false;
        static bool INumberBase<FullInt>.IsInfinity(FullInt value) => false;
        static bool INumberBase<FullInt>.IsInteger(FullInt value) => true;
        static bool INumberBase<FullInt>.IsNaN(FullInt value) => false;
        static bool INumberBase<FullInt>.IsNegative(FullInt value) => value.IsNegative;
        static bool INumberBase<FullInt>.IsNegativeInfinity(FullInt value) => false;
        static bool INumberBase<FullInt>.IsNormal(FullInt value) => !value.IsZero;
        static bool INumberBase<FullInt>.IsOddInteger(FullInt value) => !value.IsEven;
        static bool INumberBase<FullInt>.IsPositive(FullInt value) => !value.IsNegative;
        static bool INumberBase<FullInt>.IsPositiveInfinity(FullInt value) => false;
        static bool INumberBase<FullInt>.IsRealNumber(FullInt value) => true;
        static bool INumberBase<FullInt>.IsSubnormal(FullInt value) => false;
        static bool INumberBase<FullInt>.IsZero(FullInt value) => value.IsZero;
        static int INumber<FullInt>.Sign(FullInt value) => value.Sign;

        static FullInt INumberBase<FullInt>.MaxMagnitude(FullInt x, FullInt y) => CompareMagnitude(x, y) >= 0 ? x : y;
        static FullInt INumberBase<FullInt>.MaxMagnitudeNumber(FullInt x, FullInt y) => CompareMagnitude(x, y) >= 0 ? x : y;
        static FullInt INumberBase<FullInt>.MinMagnitude(FullInt x, FullInt y) => CompareMagnitude(x, y) <= 0 ? x : y;
        static FullInt INumberBase<FullInt>.MinMagnitudeNumber(FullInt x, FullInt y) => CompareMagnitude(x, y) <= 0 ? x : y;

        // |x| vs |y|; equal magnitudes: the positive one is the larger (as Math.MaxMagnitude)
        private static int CompareMagnitude(FullInt x, FullInt y)
        {
            int c = _engine.Compare(Abs(x), Abs(y));
            if (c != 0) return c;
            return x.IsNegative == y.IsNegative ? 0 : x.IsNegative ? -1 : 1;
        }

        // ---- bits ----

        /// <summary>True for a positive power of two.</summary>
        public static bool IsPow2(FullInt value)
        {
            if (value.IsNegative || value.IsZero) return false;
            int top = value.Top();
            if (BitOperations.PopCount(value._value[top]) != 1) return false;
            for (int i = top + 1; i < value._value.Length; i++)
                if (value._value[i] != 0) return false;
            return true;
        }

        /// <summary>floor(log2(value)); ArgumentOutOfRangeException for a negative value, 0 for 0.</summary>
        public static FullInt Log2(FullInt value)
        {
            if (value.IsNegative) throw new ArgumentOutOfRangeException(nameof(value), "Log2 of a negative value.");
            long bits = value.MagnitudeBitLength();
            return bits == 0 ? Zero : new FullInt(bits - 1);
        }

        // the shortest two's complement form, least significant word first: the magnitude for a
        // non-negative value; for a negative one 2^(64n) - m over the fewest words with the top bit set
        private ulong[] TwosComplementWords()
        {
            var lsf = ToWords(WordOrder.LeastSignificantFirst);
            if (!IsNegative)
            {
                if ((lsf[lsf.Length - 1] >> 63) == 0) return lsf;
                Array.Resize(ref lsf, lsf.Length + 1);   // a zero word keeps it non-negative
                return lsf;
            }
            ulong carry = 1;
            for (int i = 0; i < lsf.Length; i++)
            {
                ulong v = ~lsf[i] + carry;
                carry = carry == 1 && v == 0 ? 1UL : 0UL;
                lsf[i] = v;
            }
            if ((lsf[lsf.Length - 1] >> 63) == 0)
            {
                Array.Resize(ref lsf, lsf.Length + 1);
                lsf[lsf.Length - 1] = ulong.MaxValue;   // the sign extension
            }
            return lsf;
        }

        /// <summary>The number of set bits; of the shortest two's complement form (64-bit words) when negative.</summary>
        public static FullInt PopCount(FullInt value)
        {
            long count = 0;
            if (!value.IsNegative)
            {
                foreach (var w in value._value) count += BitOperations.PopCount(w);
            }
            else
            {
                foreach (var w in value.TwosComplementWords()) count += BitOperations.PopCount(w);
            }
            return new FullInt(count);
        }

        /// <summary>The zero bits below the lowest set bit (the same for -x); 64 for 0.</summary>
        public static FullInt TrailingZeroCount(FullInt value)
        {
            var w = value._value;
            long count = 0;
            for (int i = w.Length - 1; i >= 0; i--)
            {
                if (w[i] != 0) return new FullInt(count + BitOperations.TrailingZeroCount(w[i]));
                count += 64;
            }
            return new FullInt(64);
        }

        /// <summary>The zero bits above the highest set bit in its 64-bit word; 0 for a negative value, 64 for 0.</summary>
        public static FullInt LeadingZeroCount(FullInt value)
        {
            if (value.IsNegative) return Zero;
            return new FullInt(BitOperations.LeadingZeroCount(value._value[value.Top()]));
        }

        /// <summary>A rotation of the shortest two's complement form in 64-bit words, read back as signed.</summary>
        public static FullInt RotateLeft(FullInt value, int rotateAmount) => Rotate(value, rotateAmount);
        public static FullInt RotateRight(FullInt value, int rotateAmount) => Rotate(value, -rotateAmount);

        // a rotation of the shortest two's complement form in 64-bit words, read back as signed
        private static FullInt Rotate(FullInt value, int amount)
        {
            var w = value.TwosComplementWords();
            int bits = w.Length * 64;
            amount %= bits;
            if (amount < 0) amount += bits;
            if (amount == 0) return value;
            var bytes = new byte[w.Length * 8];
            var r = new ulong[w.Length];
            for (int i = 0; i < bits; i++)
            {
                ulong bit = (w[i >> 6] >> (i & 63)) & 1;
                int j = (i + amount) % bits;
                r[j >> 6] |= bit << (j & 63);
            }
            for (int i = 0; i < r.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), r[i]);
            if (!BitConverter.IsLittleEndian)
                for (int i = 0; i < r.Length; i++) bytes.AsSpan(i * 8, 8).Reverse();
            return new FullInt(bytes);
        }

        /// <summary>A logical shift of the shortest two's complement form (64-bit words); &gt;&gt; for a non-negative value.</summary>
        public static FullInt operator >>>(FullInt value, int shiftAmount)
        {
            if (!value.IsNegative || shiftAmount <= 0) return value >> shiftAmount;
            var w = value.TwosComplementWords();
            var bytes = new byte[w.Length * 8 + 1];   // a zero byte on top: read back as unsigned
            for (int i = 0; i < w.Length; i++)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), w[i]);
                if (!BitConverter.IsLittleEndian) bytes.AsSpan(i * 8, 8).Reverse();
            }
            return new FullInt(bytes, isUnsigned: true) >> shiftAmount;
        }

        // ---- bytes (IBinaryInteger) ----

        int IBinaryInteger<FullInt>.GetByteCount() => GetByteCount();
        int IBinaryInteger<FullInt>.GetShortestBitLength() => (int)Math.Min(int.MaxValue, GetBitLength() + (IsNegative ? 1 : 0));
        bool IBinaryInteger<FullInt>.TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) => TryWriteBytes(destination, out bytesWritten);
        bool IBinaryInteger<FullInt>.TryWriteBigEndian(Span<byte> destination, out int bytesWritten) => TryWriteBytes(destination, out bytesWritten, isBigEndian: true);

        static bool IBinaryInteger<FullInt>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out FullInt value)
        {
            value = new FullInt(source, isUnsigned);
            return true;
        }

        static bool IBinaryInteger<FullInt>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out FullInt value)
        {
            value = new FullInt(source, isUnsigned, isBigEndian: true);
            return true;
        }

        /// <summary>The truncated quotient and the remainder (the sign of the dividend).</summary>
        public static (FullInt Quotient, FullInt Remainder) DivRem(FullInt left, FullInt right)
        {
            var q = DivRem(left, right, out var r);
            return (q, r);
        }

        // ---- the 128-bit integers ----

        public static implicit operator FullInt(Int128 value)
        {
            bool negative = value < 0;
            UInt128 m = negative ? (UInt128)(-(value + 1)) + 1 : (UInt128)value;
            return new FullInt(negative, new[] { (ulong)(m >> 64), (ulong)m });
        }

        public static implicit operator FullInt(UInt128 value) => new FullInt(false, new[] { (ulong)(value >> 64), (ulong)value });

        // the low 128 bits of the two's complement form (a wrap, as a cast of a wider integer)
        private UInt128 Low128()
        {
            var w = _value;
            ulong lo = w[w.Length - 1];
            ulong hi = w.Length > 1 ? w[w.Length - 2] : 0;
            var m = new UInt128(hi, lo);
            return IsNegative ? 0 - m : m;
        }

        public static explicit operator Int128(FullInt value)
        {
            if (value < Int128.MinValue || value > Int128.MaxValue) throw new OverflowException("The value does not fit in an Int128.");
            return (Int128)value.Low128();
        }

        public static explicit operator UInt128(FullInt value)
        {
            if (value.IsNegative || value > UInt128.MaxValue) throw new OverflowException("The value does not fit in a UInt128.");
            return value.Low128();
        }

        public static explicit operator Half(FullInt value)
        {
            // Half ends below 2^16: a longer value is an infinity; a shorter one is exact in an
            // int and a float, so float -> Half is the one rounding
            if (value.MagnitudeBitLength() > 17) return value.IsNegative ? Half.NegativeInfinity : Half.PositiveInfinity;
            return (Half)(float)(long)value;
        }
        public static explicit operator FullInt(Half value) => (FullInt)(double)value;

        // ---- conversions of generic math (CreateChecked / CreateSaturating / CreateTruncating) ----

        private enum Mode { Checked, Saturating, Truncating }

        // public, as BigInteger's; they implement INumberBase's too: our conversions first, then
        // the other type's conversion to FullInt (a type of a library that knows FullInt)
        public static FullInt CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther>
        {
            if (TryConvertFrom(value, out var result) || TOther.TryConvertToChecked(value, out result)) return result;
            throw new NotSupportedException($"No conversion from {typeof(TOther)} to FullInt.");
        }

        public static FullInt CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther>
        {
            if (TryConvertFrom(value, out var result) || TOther.TryConvertToSaturating(value, out result)) return result;
            throw new NotSupportedException($"No conversion from {typeof(TOther)} to FullInt.");
        }

        public static FullInt CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther>
        {
            if (TryConvertFrom(value, out var result) || TOther.TryConvertToTruncating(value, out result)) return result;
            throw new NotSupportedException($"No conversion from {typeof(TOther)} to FullInt.");
        }

        static bool INumberBase<FullInt>.TryConvertFromChecked<TOther>(TOther value, out FullInt result) => TryConvertFrom(value, out result);
        static bool INumberBase<FullInt>.TryConvertFromSaturating<TOther>(TOther value, out FullInt result) => TryConvertFrom(value, out result);
        static bool INumberBase<FullInt>.TryConvertFromTruncating<TOther>(TOther value, out FullInt result) => TryConvertFrom(value, out result);

        // every integer fits; a floating-point value is truncated toward zero (NaN and the
        // infinities throw OverflowException in each mode: an unbounded integer has no end to saturate to)
        private static bool TryConvertFrom<TOther>(TOther value, out FullInt result) where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte)) result = (byte)(object)value;
            else if (typeof(TOther) == typeof(sbyte)) result = (sbyte)(object)value;
            else if (typeof(TOther) == typeof(short)) result = (short)(object)value;
            else if (typeof(TOther) == typeof(ushort)) result = (ushort)(object)value;
            else if (typeof(TOther) == typeof(char)) result = (ushort)(char)(object)value;
            else if (typeof(TOther) == typeof(int)) result = (int)(object)value;
            else if (typeof(TOther) == typeof(uint)) result = (uint)(object)value;
            else if (typeof(TOther) == typeof(long)) result = (long)(object)value;
            else if (typeof(TOther) == typeof(ulong)) result = (ulong)(object)value;
            else if (typeof(TOther) == typeof(nint)) result = (long)(nint)(object)value;
            else if (typeof(TOther) == typeof(nuint)) result = (ulong)(nuint)(object)value;
            else if (typeof(TOther) == typeof(Int128)) result = (Int128)(object)value;
            else if (typeof(TOther) == typeof(UInt128)) result = (UInt128)(object)value;
            else if (typeof(TOther) == typeof(BigInteger)) result = (FullInt)(BigInteger)(object)value;
            else if (typeof(TOther) == typeof(double)) result = (FullInt)(double)(object)value;
            else if (typeof(TOther) == typeof(float)) result = (FullInt)(float)(object)value;
            else if (typeof(TOther) == typeof(Half)) result = (FullInt)(Half)(object)value;
            else if (typeof(TOther) == typeof(decimal)) result = (FullInt)(decimal)(object)value;
            else
            {
                result = null!;
                return false;
            }
            return true;
        }

        static bool INumberBase<FullInt>.TryConvertToChecked<TOther>(FullInt value, out TOther result) => TryConvertTo(value, Mode.Checked, out result);
        static bool INumberBase<FullInt>.TryConvertToSaturating<TOther>(FullInt value, out TOther result) => TryConvertTo(value, Mode.Saturating, out result);
        static bool INumberBase<FullInt>.TryConvertToTruncating<TOther>(FullInt value, out TOther result) => TryConvertTo(value, Mode.Truncating, out result);

        // value brought into [min, max] by the mode: checked throws, saturating clamps,
        // truncating keeps it (the cast of Low128 then wraps)
        private static FullInt Fit(FullInt value, FullInt min, FullInt max, Mode mode)
        {
            if (value >= min && value <= max) return value;
            if (mode == Mode.Checked) throw new OverflowException("The value is out of the range of the target type.");
            if (mode == Mode.Saturating) return value < min ? min : max;
            return value;
        }

        private static bool TryConvertTo<TOther>(FullInt value, Mode mode, out TOther result) where TOther : INumberBase<TOther>
        {
            object r;
            if (typeof(TOther) == typeof(byte)) r = (byte)Fit(value, byte.MinValue, byte.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(sbyte)) r = (sbyte)Fit(value, sbyte.MinValue, sbyte.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(short)) r = (short)Fit(value, short.MinValue, short.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(ushort)) r = (ushort)Fit(value, ushort.MinValue, ushort.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(char)) r = (char)(ushort)Fit(value, ushort.MinValue, ushort.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(int)) r = (int)Fit(value, int.MinValue, int.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(uint)) r = (uint)Fit(value, uint.MinValue, uint.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(long)) r = (long)Fit(value, long.MinValue, long.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(ulong)) r = (ulong)Fit(value, ulong.MinValue, ulong.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(nint)) r = (nint)Fit(value, (long)nint.MinValue, (long)nint.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(nuint)) r = (nuint)Fit(value, (ulong)nuint.MinValue, (ulong)nuint.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(Int128)) r = (Int128)Fit(value, Int128.MinValue, Int128.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(UInt128)) r = Fit(value, UInt128.MinValue, UInt128.MaxValue, mode).Low128();
            else if (typeof(TOther) == typeof(BigInteger)) r = (BigInteger)value;
            else if (typeof(TOther) == typeof(double)) r = (double)value;
            else if (typeof(TOther) == typeof(float)) r = (float)value;
            else if (typeof(TOther) == typeof(Half)) r = (Half)value;
            else if (typeof(TOther) == typeof(decimal))
            {
                var max = (FullInt)decimal.MaxValue;
                r = (decimal)(mode == Mode.Checked ? value : Fit(value, -max, max, Mode.Saturating));
            }
            else
            {
                result = default!;
                return false;
            }
            result = (TOther)r;
            return true;
        }

        // ---- text: parsing with NumberStyles, span formatting ----

        public static FullInt Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), NumberStyles.Integer, provider);
        public static FullInt Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s.AsSpan(), style, provider);
        public static FullInt Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static FullInt Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
        {
            if (!TryParse(s, style, provider, out var result)) throw new FormatException("The value could not be parsed as an integer.");
            return result;
        }

        public static bool TryParse(string? s, IFormatProvider? provider, out FullInt result) => TryParse(s.AsSpan(), NumberStyles.Integer, provider, out result);
        public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out FullInt result) => TryParse(s.AsSpan(), style, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out FullInt result) => TryParse(s, NumberStyles.Integer, provider, out result);

        private const NumberStyles SupportedStyles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign | NumberStyles.AllowHexSpecifier;

        /// <summary>
        /// NumberStyles.Integer (white space, a sign from the provider, decimal digits) and
        /// NumberStyles.HexNumber (hex digits as two's complement, as BigInteger: "FF" is -1,
        /// "0FF" is 255). Other styles: ArgumentException.
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out FullInt result)
        {
            if ((style & ~SupportedStyles) != 0)
                throw new ArgumentException("Supported: NumberStyles.Integer, NumberStyles.HexNumber and their parts.", nameof(style));
            result = new FullInt();
            if ((style & NumberStyles.AllowLeadingWhite) != 0) s = s.TrimStart();
            if ((style & NumberStyles.AllowTrailingWhite) != 0) s = s.TrimEnd();
            bool hex = (style & NumberStyles.AllowHexSpecifier) != 0;
            bool negative = false;
            if (!hex && (style & NumberStyles.AllowLeadingSign) != 0)
            {
                var nfi = NumberFormatInfo.GetInstance(provider);
                if (s.StartsWith(nfi.NegativeSign.AsSpan(), StringComparison.Ordinal)) { negative = true; s = s.Slice(nfi.NegativeSign.Length); }
                else if (s.StartsWith(nfi.PositiveSign.AsSpan(), StringComparison.Ordinal)) s = s.Slice(nfi.PositiveSign.Length);
            }
            if (s.IsEmpty) return false;
            foreach (char c in s)
            {
                bool ok = hex ? char.IsAsciiHexDigit(c) : char.IsAsciiDigit(c);
                if (!ok) return false;
            }
            if (!hex)
            {
                // decimal: the engine's text form (its "0x" prefix cannot occur: digits only)
                var digits = s.TrimStart('0');
                if (digits.IsEmpty) return true;
                result = _engine.ToFullInt((negative ? "-" : "") + digits.ToString());
                return true;
            }
            // hex: two's complement of 4 bits a digit, as BigInteger
            var magnitude = _engine.ToFullInt("0x" + s.ToString());
            if (HexValue(s[0]) >= 8)   // the top digit's high bit: the sign
                magnitude -= One << (4 * s.Length);
            result = magnitude;
            return true;
        }

        private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            string text = ToString(format.IsEmpty ? null : format.ToString(), provider);
            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }
    }
}
#endif
