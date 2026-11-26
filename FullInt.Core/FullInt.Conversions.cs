using System;

namespace FullNumerics
{
    // Conversions to and from the floating-point types and decimal, and the bit length: every
    // target framework (generic math builds on them on .NET 7+, FullInt.GenericMath.cs).
    public sealed partial class FullInt
    {
        // the magnitude's bit length (0 for zero)
        private long MagnitudeBitLength()
        {
            int top = Top();
            ulong w = _value[top];
            if (w == 0) return 0;
            return (long)(_value.Length - top) * 64 - FullHelper.LeadingZeroCount(w);
        }

        // the magnitude as top · 2^scale with top below 2^63: its top 63 bits, bit 0 set when any
        // bit below them is set (a sticky bit: the one rounding to 53 or 24 bits, in the long ->
        // double / float conversion, is then the rounding of the whole value). 63, not 64: the
        // ulong -> double conversion of a value from 2^63 rounds twice on some runtimes.
        private long MagnitudeTop63Sticky(out long scale)
        {
            var w = _value;
            int n = w.Length;
            long bits = MagnitudeBitLength();
            scale = 0;
            if (bits <= 63) return (long)w[n - 1];
            long shift = bits - 63;
            scale = shift;
            int wi = (int)(shift / 64), off = (int)(shift % 64);
            ulong lo = w[n - 1 - wi];
            ulong hi = n - 2 - wi >= 0 ? w[n - 2 - wi] : 0;
            ulong top = off == 0 ? lo : (lo >> off) | (hi << (64 - off));
            bool sticky = off != 0 && (lo << (64 - off)) != 0;
            for (int i = n - 1; i > n - 1 - wi && !sticky; i--) sticky = w[i] != 0;
            return (long)(sticky ? top | 1 : top);
        }

        private static double ScaleB(double x, int n)
        {
#if NET5_0_OR_GREATER
            return Math.ScaleB(x, n);
#else
            return x * Math.Pow(2, n);   // n <= 960 here: an exact power of two
#endif
        }

        /// <summary>
        /// The number of bits of the shortest two's complement form without the sign bit, as
        /// BigInteger.GetBitLength: 0 for 0 and -1, 8 for 255, 7 for -128.
        /// </summary>
        public long GetBitLength()
        {
            if (!IsNegative) return MagnitudeBitLength();
            // -m needs the bits of m - 1
            return (Negate(this) - 1).MagnitudeBitLength();
        }

        /// <summary>Rounded to the nearest double (ties to even); beyond its range: an infinity.</summary>
        public static explicit operator double(FullInt value)
        {
            long top = value.MagnitudeTop63Sticky(out long scale);
            double d = ScaleB(top, (int)Math.Min(scale, 2000));   // rounded in (double)top; beyond the range: an infinity
            return value.IsNegative ? -d : d;
        }

        /// <summary>Rounded to the nearest float (ties to even); beyond its range: an infinity.</summary>
        public static explicit operator float(FullInt value)
        {
            long top = value.MagnitudeTop63Sticky(out long scale);
            // rounded in (float)top; the scaling is exact in double, then only the range is left
            float f = (float)ScaleB((float)top, (int)Math.Min(scale, 300));
            return value.IsNegative ? -f : f;
        }

        /// <summary>OverflowException beyond decimal's 96 bits.</summary>
        public static explicit operator decimal(FullInt value)
        {
            if (value.MagnitudeBitLength() > 96) throw new OverflowException("The value does not fit in a decimal.");
            var w = value._value;
            ulong lo = w[w.Length - 1];
            ulong hi = w.Length > 1 ? w[w.Length - 2] : 0;
            return new decimal((int)(uint)lo, (int)(uint)(lo >> 32), (int)(uint)hi, value.IsNegative, 0);
        }

        /// <summary>Truncated toward zero; OverflowException for NaN and the infinities.</summary>
        public static explicit operator FullInt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new OverflowException("NaN and the infinities have no integer value.");
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exp = (int)((bits >> 52) & 0x7FF);
            if (exp < 1023) return new FullInt();   // |value| < 1 (subnormals and zeros too)
            ulong mantissa = ((ulong)bits & 0xF_FFFF_FFFF_FFFFUL) | 1UL << 52;
            int shift = exp - 1075;   // value = mantissa · 2^shift
            FullInt magnitude = shift <= 0 ? new FullInt(mantissa >> -shift) : new FullInt(mantissa) << shift;
            return bits < 0 ? Negate(magnitude) : magnitude;
        }

        /// <summary>Truncated toward zero; OverflowException for NaN and the infinities.</summary>
        public static explicit operator FullInt(float value) => (FullInt)(double)value;

        /// <summary>Truncated toward zero.</summary>
        public static explicit operator FullInt(decimal value)
        {
            int[] b = decimal.GetBits(value);
            int scale = (b[3] >> 16) & 0xFF;
            ulong lo = (uint)b[0] | (ulong)(uint)b[1] << 32;
            ulong hi = (uint)b[2];
            var magnitude = new FullInt(false, new[] { hi, lo });
            if (scale != 0) magnitude /= Pow(10, scale);
            return (b[3] & int.MinValue) != 0 ? Negate(magnitude) : magnitude;
        }
    }
}
