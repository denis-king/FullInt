using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using FullNumerics;

namespace FullNumerics.Tests
{
    /// <summary>
    /// The conversions to and from double / float / decimal (every framework) and generic math
    /// (.NET 7+): the same generic code run on FullInt and on BigInteger must agree.
    /// </summary>
    [TestClass]
    public class FullIntGenericMathTests
    {
        private static BigInteger RandomBig(Random rnd, int maxBytes)
        {
            var bytes = new byte[rnd.Next(1, maxBytes + 1)];
            rnd.NextBytes(bytes);
            if (rnd.Next(6) == 0) { Array.Clear(bytes, 0, bytes.Length); bytes[bytes.Length - 1] = (byte)(1 << rnd.Next(8)); }
            var v = BigCompat.FromUnsignedLittleEndian(bytes);
            return rnd.Next(2) == 0 ? -v : v;
        }

        [TestMethod]
        public void Double_Float_Decimal_MatchBigInteger()
        {
            var rnd = new Random(21);
            for (int i = 0; i < 5000; i++)
            {
                var v = RandomBig(rnd, i % 10 == 0 ? 140 : 20);
                var f = (FullInt)v;
#if NET5_0_OR_GREATER
                // the reference: parsing the decimal text (correctly rounded on .NET Core 3.0+;
                // BigInteger -> double is not always, e.g. 40949349515825805 -> ...800, not ...808)
                Assert.AreEqual(double.Parse(v.ToString(), CultureInfo.InvariantCulture), (double)f, $"double of {v}");
                Assert.AreEqual(float.Parse(v.ToString(), CultureInfo.InvariantCulture), (float)f, $"float of {v}");
#endif
                if (BigInteger.Abs(v) <= (BigInteger)decimal.MaxValue) Assert.AreEqual((decimal)v, (decimal)f);
                else Assert.ThrowsException<OverflowException>(() => (decimal)f);

                double d = (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-5, 1020));
                Assert.AreEqual(new BigInteger(d), (BigInteger)(FullInt)d, $"from double {d:R}");
                if (Math.Abs(d) < float.MaxValue) Assert.AreEqual(new BigInteger((float)d), (BigInteger)(FullInt)(float)d, $"from float {(float)d:R}");
                decimal m = (decimal)(rnd.NextDouble() - 0.5) * (decimal)Math.Pow(10, rnd.Next(0, 28)) / (decimal)Math.Pow(10, rnd.Next(0, 10));
                Assert.AreEqual(new BigInteger(m), (BigInteger)(FullInt)m, $"from decimal {m}");
            }
            // ties to even and the ends of the range
            var two53 = BigInteger.One << 53;
#if NET5_0_OR_GREATER
            foreach (var v in new[] { two53 + 1, two53 + 3, (two53 + 1) << 900, BigInteger.One << 1023, (BigInteger.One << 1024) - 1, BigInteger.One << 1024 })
                Assert.AreEqual(double.Parse(v.ToString(), CultureInfo.InvariantCulture), (double)(FullInt)v, v.ToString());
#endif
            Assert.AreEqual(9007199254740992.0, (double)(FullInt)(two53 + 1), "a tie: to even");
            Assert.AreEqual(9007199254740996.0, (double)(FullInt)(two53 + 3), "a tie: to even (up)");
            Assert.AreEqual(double.PositiveInfinity, (double)((FullInt)1 << 5000));
            Assert.AreEqual(double.NegativeInfinity, (double)-((FullInt)1 << 5000));
            Assert.AreEqual(float.PositiveInfinity, (float)((FullInt)1 << 200));
            Assert.AreEqual(-1e300, (double)(FullInt)(-1e300));
            Assert.ThrowsException<OverflowException>(() => (FullInt)double.NaN);
            Assert.ThrowsException<OverflowException>(() => (FullInt)double.PositiveInfinity);
            Assert.IsTrue(((FullInt)0.999).IsZero && ((FullInt)(-0.999)).IsZero && ((FullInt)double.Epsilon).IsZero);
            Assert.AreEqual(decimal.MinValue, (decimal)(FullInt)decimal.MinValue);
        }

#if NET5_0_OR_GREATER
        [TestMethod]
        public void GetBitLength_MatchesBigInteger()
        {
            var rnd = new Random(22);
            for (int i = 0; i < 3000; i++)
            {
                var v = i < 300 ? new BigInteger(i - 150) : RandomBig(rnd, 40);
                Assert.AreEqual(v.GetBitLength(), ((FullInt)v).GetBitLength(), v.ToString());
            }
        }
#endif

#if NET7_0_OR_GREATER
        // ---- the same generic code on both types ----

        private static T Gcd<T>(T a, T b) where T : IBinaryInteger<T>
        {
            a = T.Abs(a); b = T.Abs(b);
            while (!T.IsZero(b)) (a, b) = (b, a % b);
            return a;
        }

        private static T Factorial<T>(int n) where T : INumber<T>
        {
            T r = T.One;
            for (int i = 2; i <= n; i++) r *= T.CreateChecked(i);
            return r;
        }

        private static T PowMod<T>(T b, T e, T m) where T : IBinaryInteger<T>
        {
            T r = T.One;
            b %= m;
            while (!T.IsZero(e))
            {
                if (T.IsOddInteger(e)) r = r * b % m;
                b = b * b % m;
                e >>= 1;
            }
            return r;
        }

        private static string Props<T>(T v) where T : IBinaryInteger<T>, ISignedNumber<T>
            => $"{T.IsNegative(v)} {T.IsZero(v)} {T.IsEvenInteger(v)} {T.IsOddInteger(v)} {T.IsPositive(v)} {T.Sign(v)} {T.IsPow2(v)} "
             + $"{T.Abs(v)} {-v} {v + T.One} {v - T.NegativeOne} {T.Max(v, T.Zero)} {T.Min(v, T.Zero)} {T.Clamp(v, -T.CreateChecked(1000), T.CreateChecked(1000))} "
             + $"{T.CopySign(T.CreateChecked(7), v)} {T.MaxMagnitude(v, -v)} {T.MinMagnitude(v, T.One)} {T.TrailingZeroCount(T.Abs(v) + T.One)}";

        [TestMethod]
        public void GenericAlgorithms_AgreeWithBigInteger()
        {
            var rnd = new Random(23);
            Assert.AreEqual(Factorial<BigInteger>(300).ToString(), Factorial<FullInt>(300).ToString());
            for (int i = 0; i < 500; i++)
            {
                var a = RandomBig(rnd, 40);
                var b = RandomBig(rnd, 40);
                var m = BigInteger.Abs(RandomBig(rnd, 30)) + 2;
                var e = BigInteger.Abs(RandomBig(rnd, 8));
                Assert.AreEqual(Gcd(a, b).ToString(), Gcd((FullInt)a, (FullInt)b).ToString());
                Assert.AreEqual(PowMod(BigInteger.Abs(a), e, m).ToString(), PowMod((FullInt)BigInteger.Abs(a), (FullInt)e, (FullInt)m).ToString());
                Assert.AreEqual(Props(a), Props((FullInt)a), a.ToString());
                if (a.Sign > 0)
                {
                    Assert.AreEqual(BigInteger.Log2(a).ToString(), FullInt.Log2((FullInt)a).ToString());
                    Assert.AreEqual(BigInteger.PopCount(a).ToString(), FullInt.PopCount((FullInt)a).ToString());
                }
                var (q, r) = FullInt.DivRem((FullInt)a, (FullInt)(b.IsZero ? 1 : b));
                Assert.AreEqual(BigInteger.DivRem(a, b.IsZero ? 1 : b), ((BigInteger)q, (BigInteger)r));
            }
            Assert.AreEqual("0", Props(BigInteger.Zero).Split(' ')[5]);
            Assert.AreEqual(Props(BigInteger.Zero), Props(FullInt.Zero));
        }

        private static TTo Convert<TFrom, TTo>(TFrom v, int mode) where TFrom : INumberBase<TFrom> where TTo : INumberBase<TTo>
            => mode == 0 ? TTo.CreateChecked(v) : mode == 1 ? TTo.CreateSaturating(v) : TTo.CreateTruncating(v);

        // FullInt -> T and T -> FullInt in the three modes, against BigInteger
        private static void CheckTarget<T>(BigInteger v) where T : INumberBase<T>
        {
            var f = (FullInt)v;
            for (int mode = 0; mode < 3; mode++)
            {
                string expected, actual;
                T expectedValue = default!;
                bool floating = typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half);
                // floating-point targets: the correctly rounded value from the text (BigInteger's
                // conversions truncate in places: 2^63 - 1 -> 2^63 - 1024)
                if (floating)
                {
                    expectedValue = T.Parse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    expected = expectedValue.ToString()!;
                }
                else
                    try { expected = Convert<BigInteger, T>(v, mode)!.ToString()!; } catch (OverflowException) { expected = "overflow"; }
                try { actual = Convert<FullInt, T>(f, mode)!.ToString()!; } catch (OverflowException) { actual = "overflow"; }
                Assert.AreEqual(expected, actual, $"{typeof(T).Name} mode {mode} of {v}");
                if (expected != "overflow")
                {
                    var back = floating ? expectedValue : Convert<BigInteger, T>(v, mode);
                    string e2, a2;
                    try { e2 = Convert<T, BigInteger>(back, 0).ToString(); } catch (OverflowException) { e2 = "overflow"; }   // an infinity
                    try { a2 = Convert<T, FullInt>(back, 0).ToString(); } catch (OverflowException) { a2 = "overflow"; }
                    Assert.AreEqual(e2, a2, $"from {typeof(T).Name} {back}");
                }
            }
        }

        [TestMethod]
        public void CreateCheckedSaturatingTruncating_AgreeWithBigInteger()
        {
            var rnd = new Random(24);
            var values = new BigInteger[] { 0, 1, -1, 127, 128, -128, -129, 255, 256, 65535, 65536, -32768, -32769,
                int.MaxValue, (BigInteger)int.MaxValue + 1, int.MinValue, (BigInteger)int.MinValue - 1, uint.MaxValue,
                long.MaxValue, (BigInteger)long.MaxValue + 1, long.MinValue, (BigInteger)long.MinValue - 1, ulong.MaxValue,
                (BigInteger)ulong.MaxValue + 1, (BigInteger)Int128.MaxValue, (BigInteger)Int128.MinValue, (BigInteger)UInt128.MaxValue,
                (BigInteger)UInt128.MaxValue + 1, -((BigInteger)UInt128.MaxValue) - 5, (BigInteger)decimal.MaxValue, (BigInteger)decimal.MaxValue + 1 };
            foreach (var v in values.Concat(Enumerable.Range(0, 300).Select(_ => RandomBig(rnd, 24))))
            {
                CheckTarget<byte>(v); CheckTarget<sbyte>(v); CheckTarget<short>(v); CheckTarget<ushort>(v); CheckTarget<char>(v);
                CheckTarget<int>(v); CheckTarget<uint>(v); CheckTarget<long>(v); CheckTarget<ulong>(v);
                CheckTarget<nint>(v); CheckTarget<nuint>(v); CheckTarget<Int128>(v); CheckTarget<UInt128>(v);
                CheckTarget<double>(v); CheckTarget<float>(v); CheckTarget<Half>(v); CheckTarget<BigInteger>(v);
                if (BigInteger.Abs(v) <= (BigInteger)decimal.MaxValue) CheckTarget<decimal>(v);
            }
            Assert.AreEqual(decimal.MaxValue, decimal.CreateSaturating((FullInt)((BigInteger)decimal.MaxValue * 2)));
            Assert.ThrowsException<OverflowException>(() => decimal.CreateChecked((FullInt)((BigInteger)decimal.MaxValue * 2)));
            Assert.AreEqual("12", FullInt.CreateTruncating(12.9).ToString());
            Assert.AreEqual("-12", FullInt.CreateChecked(-12.9m).ToString());
        }

        [TestMethod]
        public void Parse_NumberStyles_AndTryFormat()
        {
            var rnd = new Random(25);
            for (int i = 0; i < 500; i++)
            {
                var v = RandomBig(rnd, 30);
                string hex = v.ToString("X");   // two's complement hex, BigInteger's form
                Assert.AreEqual(v, (BigInteger)FullInt.Parse(hex, NumberStyles.HexNumber, null), hex);
                Assert.AreEqual(v, (BigInteger)FullInt.Parse(" " + v.ToString() + " ", NumberStyles.Integer, CultureInfo.InvariantCulture));
                Assert.AreEqual(v, (BigInteger)FullInt.Parse(v.ToString().AsSpan(), CultureInfo.InvariantCulture));
                var f = (FullInt)v;
                var buf = new char[200];
                Assert.IsTrue(f.TryFormat(buf, out int n, "X".AsSpan(), null));
                Assert.AreEqual(f.ToString("X"), new string(buf, 0, n));
                Assert.IsFalse(f.TryFormat(new char[1], out n, default, null) && f.ToString().Length > 1);
            }
            Assert.AreEqual("-1", FullInt.Parse("FF", NumberStyles.HexNumber, null).ToString());
            Assert.AreEqual("255", FullInt.Parse("0FF", NumberStyles.HexNumber, null).ToString());
            Assert.AreEqual("17", FullInt.Parse("017", CultureInfo.InvariantCulture).ToString(), "decimal: no octal in the NumberStyles path");
            Assert.IsFalse(FullInt.TryParse("1,000", NumberStyles.Integer, null, out _));
            Assert.ThrowsException<ArgumentException>(() => FullInt.Parse("1", NumberStyles.Float, null));
            Assert.AreEqual("12", $"{(FullInt)12}");
            Assert.AreEqual("12", string.Create(CultureInfo.InvariantCulture, $"{(FullInt)12}"));
        }

        [TestMethod]
        public void Bits_OfNegativeValues()
        {
            // two's complement in 64-bit words: -1 is one word of ones
            Assert.AreEqual("64", FullInt.PopCount(-1).ToString());
            Assert.AreEqual("0", FullInt.LeadingZeroCount(-5).ToString());
            Assert.AreEqual("63", FullInt.LeadingZeroCount(1).ToString());
            Assert.AreEqual("64", FullInt.TrailingZeroCount(0).ToString());
            Assert.AreEqual("3", FullInt.TrailingZeroCount(-8).ToString());
            Assert.AreEqual(long.MaxValue.ToString(), ((FullInt)(-1) >>> 1).ToString());
            Assert.AreEqual("4", ((FullInt)8 >>> 1).ToString());
            Assert.AreEqual("2", FullInt.RotateLeft(1, 1).ToString());
            Assert.AreEqual(long.MinValue.ToString(), FullInt.RotateRight(1, 1).ToString(), "one word: the top bit is the sign");
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => FullInt.Log2(-1));
            Assert.IsTrue(FullInt.IsPow2((FullInt)1 << 700) && !FullInt.IsPow2(((FullInt)1 << 700) + 1) && !FullInt.IsPow2(-4));
            IBinaryInteger<FullInt> x = (FullInt)(-129);
            Assert.AreEqual(2, x.GetByteCount());
            Assert.AreEqual(9, x.GetShortestBitLength(), "-129 needs 9 bits (8 hold -128..127)");
        }
#endif
    }
}
