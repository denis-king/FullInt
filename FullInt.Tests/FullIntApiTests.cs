using System;
using System.Linq;
using System.Numerics;
using FullNumerics;

namespace FullNumerics.Tests
{
    /// <summary>
    /// The public surface: bytes and words in both orders, BigInteger, the primitive types, the
    /// text formats, Parse, JSON, the operators that changed meaning (^ is XOR, ToString() is
    /// decimal). BigInteger is the reference; fixed seeds.
    /// </summary>
    [TestClass]
    public class FullIntApiTests
    {
        private static BigInteger RandomBig(Random rnd, int maxBytes)
        {
            var bytes = new byte[rnd.Next(1, maxBytes + 1)];
            rnd.NextBytes(bytes);
            int shape = rnd.Next(8);
            if (shape == 0) for (int i = 0; i < bytes.Length; i++) bytes[i] = 0xFF;
            else if (shape == 1) { Array.Clear(bytes, 0, bytes.Length); bytes[bytes.Length - 1] = 0x80; }
            else if (shape == 2) { Array.Clear(bytes, 0, bytes.Length); bytes[bytes.Length - 1] = (byte)rnd.Next(256); }
            var v = BigCompat.FromUnsignedLittleEndian(bytes);
            return rnd.Next(2) == 0 ? -v : v;
        }

        // BigInteger.ToByteArray(isUnsigned, isBigEndian) for net48 as well
        private static byte[] Expected(BigInteger v, bool isUnsigned, bool isBigEndian)
        {
            var b = v.ToByteArray();   // signed, little-endian, the fewest bytes
            if (isUnsigned && b.Length > 1 && b[b.Length - 1] == 0) b = b.Take(b.Length - 1).ToArray();
            if (isBigEndian) Array.Reverse(b);
            return b;
        }

        [TestMethod]
        public void Bytes_MatchBigInteger_AllFlags()
        {
            var rnd = new Random(11);
            for (int i = 0; i < 4000; i++)
            {
                var v = i < 3 ? new BigInteger(i - 1) : RandomBig(rnd, i % 10 == 0 ? 300 : 24);
                var f = (FullInt)v;
                foreach (bool be in new[] { false, true })
                {
                    var signed = f.ToByteArray(isBigEndian: be);
                    CollectionAssert.AreEqual(Expected(v, false, be), signed, $"{v} signed be={be}");
                    Assert.AreEqual(signed.Length, f.GetByteCount());
                    Assert.AreEqual(v.ToString(), new FullInt(signed, isBigEndian: be).ToString(), $"{v} back be={be}");

                    if (v.Sign >= 0)
                    {
                        var unsigned = f.ToByteArray(isUnsigned: true, isBigEndian: be);
                        CollectionAssert.AreEqual(Expected(v, true, be), unsigned, $"{v} unsigned be={be}");
                        Assert.AreEqual(v.ToString(), new FullInt(unsigned, isUnsigned: true, isBigEndian: be).ToString());
                    }
                    else
                    {
                        Assert.ThrowsException<OverflowException>(() => f.ToByteArray(isUnsigned: true, isBigEndian: be));
                    }

                    var dest = new byte[signed.Length + 3];
                    Assert.IsTrue(f.TryWriteBytes(dest, out int written, isBigEndian: be));
                    Assert.AreEqual(signed.Length, written);
                    CollectionAssert.AreEqual(signed, dest.Take(written).ToArray());
                    Assert.IsFalse(f.TryWriteBytes(new byte[signed.Length - 1], out written, isBigEndian: be));
                    Assert.AreEqual(0, written);
                }
            }
            Assert.IsTrue(new FullInt(ReadOnlySpan<byte>.Empty).IsZero);
            Assert.AreEqual("-1", new FullInt(new byte[] { 0xFF, 0xFF, 0xFF }).ToString());
            Assert.AreEqual("16777215", new FullInt(new byte[] { 0xFF, 0xFF, 0xFF }, isUnsigned: true).ToString());
            Assert.AreEqual("258", new FullInt(new byte[] { 0x01, 0x02 }, isBigEndian: true).ToString());
        }

        [TestMethod]
        public void Words_BothOrders_RoundTrip()
        {
            var rnd = new Random(12);
            for (int i = 0; i < 2000; i++)
            {
                var v = RandomBig(rnd, 200);
                var f = (FullInt)v;
                var lsf = f.ToWords(WordOrder.LeastSignificantFirst);
                var msf = f.ToWords(WordOrder.MostSignificantFirst);
                CollectionAssert.AreEqual(Enumerable.Reverse(lsf).ToArray(), msf);
                Assert.AreEqual(lsf.Length, f.GetWordCount());
                Assert.IsTrue(lsf.Length == 1 || lsf[lsf.Length - 1] != 0, "no leading zero words");

                // the words are the magnitude: Σ w[i]·2^(64i)
                BigInteger m = 0;
                for (int k = lsf.Length - 1; k >= 0; k--) m = (m << 64) + lsf[k];
                Assert.AreEqual(BigInteger.Abs(v), m);

                Assert.AreEqual(v, (BigInteger)FullInt.FromWords(lsf, WordOrder.LeastSignificantFirst, v.Sign < 0));
                Assert.AreEqual(v, (BigInteger)FullInt.FromWords(msf, WordOrder.MostSignificantFirst, v.Sign < 0));

                var dest = new ulong[lsf.Length];
                Assert.IsTrue(f.TryWriteWords(dest, out int n, WordOrder.MostSignificantFirst));
                CollectionAssert.AreEqual(msf, dest);
                Assert.IsFalse(f.TryWriteWords(new ulong[lsf.Length - 1], out n, WordOrder.LeastSignificantFirst));
            }
            Assert.IsTrue(FullInt.FromWords(ReadOnlySpan<ulong>.Empty, WordOrder.LeastSignificantFirst, true).IsZero);
            Assert.IsFalse(FullInt.FromWords(new ulong[] { 0, 0 }, WordOrder.LeastSignificantFirst, true).IsNegative, "-0 is 0");
            CollectionAssert.AreEqual(new ulong[] { 0 }, new FullInt().ToWords(WordOrder.LeastSignificantFirst));
            CollectionAssert.AreEqual(new ulong[] { 5, 1 },
                FullInt.FromWords(new ulong[] { 0, 0, 1, 5 }, WordOrder.MostSignificantFirst).ToWords(WordOrder.LeastSignificantFirst));
        }

        [TestMethod]
        public void BigInteger_BothWays()
        {
            var rnd = new Random(13);
            for (int i = 0; i < 3000; i++)
            {
                var v = RandomBig(rnd, i % 20 == 0 ? 2000 : 40);
                var f = (FullInt)v;
                Assert.AreEqual(v.ToString(), f.ToString());
                Assert.AreEqual(v, (BigInteger)f);
                Assert.IsTrue(f.Equals((object)v));
            }
            Assert.AreEqual(BigInteger.Zero, (BigInteger)new FullInt());
            Assert.IsTrue(((FullInt)BigInteger.Zero).IsZero);
        }

        [TestMethod]
        public void Primitives_ConvertAndOverflow()
        {
            foreach (long x in new[] { 0L, 1L, -1L, long.MaxValue, long.MinValue, int.MaxValue, int.MinValue, 1234567890123L })
            {
                FullInt f = x;
                Assert.AreEqual(x.ToString(), f.ToString());
                Assert.AreEqual(x, (long)f);
            }
            FullInt big = ulong.MaxValue;
            Assert.AreEqual(ulong.MaxValue, (ulong)big);
            Assert.ThrowsException<OverflowException>(() => (long)big);
            Assert.ThrowsException<OverflowException>(() => (ulong)new FullInt(-1));
            Assert.ThrowsException<OverflowException>(() => (long)(big + 1));
            Assert.ThrowsException<OverflowException>(() => (long)-(new FullInt(long.MaxValue) + 2));
            Assert.AreEqual(long.MinValue, (long)-(new FullInt(long.MaxValue) + 1));
            Assert.AreEqual(int.MinValue, (int)new FullInt(int.MinValue));
            Assert.ThrowsException<OverflowException>(() => (int)new FullInt(int.MaxValue + 1L));
            Assert.AreEqual(uint.MaxValue, (uint)new FullInt(uint.MaxValue));
            Assert.ThrowsException<OverflowException>(() => (uint)new FullInt(uint.MaxValue + 1L));

            byte b = 200; sbyte sb = -5; short s = -300; ushort us = 60000; uint ui = 4000000000;
            FullInt fb = b, fsb = sb, fs = s, fus = us, fui = ui;
            Assert.AreEqual("200 -5 -300 60000 4000000000", $"{fb} {fsb} {fs} {fus} {fui}");
        }

        [TestMethod]
        public void Formats()
        {
            var rnd = new Random(14);
            for (int i = 0; i < 500; i++)
            {
                var v = RandomBig(rnd, 64);
                var f = (FullInt)v;
                var abs = BigInteger.Abs(v);
                string sign = v.Sign < 0 ? "-" : "";
                Assert.AreEqual(v.ToString(), f.ToString());
                Assert.AreEqual(v.ToString(), f.ToString("D"));
                Assert.AreEqual(v.ToString(), f.ToString(null));
                Assert.AreEqual(sign + abs.ToString("D40"), f.ToString("D40"));

                // BigInteger "X" writes a leading 0 for a positive with the top bit set: trim it
                string hex = abs.ToString("X").TrimStart('0');
                if (hex.Length == 0) hex = "0";
                Assert.AreEqual(sign + hex, f.ToString("X"));
                Assert.AreEqual(sign + hex.ToLowerInvariant(), f.ToString("x"));
                Assert.AreEqual(sign + hex.PadLeft(70, '0'), f.ToString("X70"));

                string bin = string.Concat(hex.Select(c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0'))).TrimStart('0');
                if (bin.Length == 0) bin = "0";
                Assert.AreEqual(sign + bin, f.ToString("B"));
            }
            Assert.AreEqual("0", new FullInt().ToString("X"));
            Assert.AreEqual("0000", new FullInt().ToString("D4"));
            Assert.AreEqual("-00FF", new FullInt(-255).ToString("X4"));
            Assert.ThrowsException<FormatException>(() => new FullInt(1).ToString("Q"));
            Assert.ThrowsException<FormatException>(() => new FullInt(1).ToString("Dx"));
            Assert.AreEqual("-12", string.Format("{0}", new FullInt(-12)));
            Assert.AreEqual("ff", $"{new FullInt(255):x}");
        }

        [TestMethod]
        public void Parse_ValidAndInvalid()
        {
            Assert.AreEqual("123", FullInt.Parse("123").ToString());
            Assert.AreEqual("-123", FullInt.Parse("-123").ToString());
            Assert.AreEqual("123", FullInt.Parse("+123").ToString());
            Assert.AreEqual("42", FullInt.Parse("  42 ").ToString());
            Assert.AreEqual("255", FullInt.Parse("0xFF").ToString());
            Assert.AreEqual("-255", FullInt.Parse("-0xff").ToString());
            Assert.AreEqual("17", FullInt.Parse("017").ToString(), "leading zeros: decimal, no octal");
            Assert.AreEqual("-8", FullInt.Parse("-008").ToString());
            Assert.AreEqual("0", FullInt.Parse("000").ToString());
            string longText = "9" + new string('0', 5000);
            Assert.AreEqual(longText, FullInt.Parse(longText).ToString());

            foreach (var bad in new[] { "", " ", "-", "+", "12a", "1 2", "0x", "0xG", "--1", "1.5", "1e5", "٣" })
            {
                Assert.ThrowsException<FormatException>(() => FullInt.Parse(bad), $"'{bad}'");
                Assert.IsFalse(FullInt.TryParse(bad, out var r), $"'{bad}'");
                Assert.IsTrue(r.IsZero);
            }
            Assert.IsFalse(FullInt.TryParse(null, out _));
            Assert.ThrowsException<ArgumentNullException>(() => FullInt.Parse(null!));
            Assert.IsTrue(FullInt.TryParse("-77", out var ok));
            Assert.AreEqual(-77L, (long)ok);
        }

        [TestMethod]
        public void Operators_XorPowDivRemCompare()
        {
            var rnd = new Random(15);
            for (int i = 0; i < 1000; i++)
            {
                var x = RandomBig(rnd, 40);
                var y = RandomBig(rnd, 40);
                FullInt fx = (FullInt)x, fy = (FullInt)y;
                Assert.AreEqual(x ^ y, (BigInteger)(fx ^ fy), "^ is XOR");
                Assert.AreEqual(x.CompareTo(y), Math.Sign(fx.CompareTo(fy)));
                Assert.AreEqual(x.Sign, fx.Sign);
                Assert.AreEqual(x.IsEven, fx.IsEven);
                if (!y.IsZero)
                {
                    var q = FullInt.DivRem(fx, fy, out var r);
                    Assert.AreEqual(BigInteger.DivRem(x, y, out var br), (BigInteger)q);
                    Assert.AreEqual(br, (BigInteger)r);
                }
            }
            Assert.AreEqual(BigInteger.Pow(-7, 33), (BigInteger)FullInt.Pow(-7, 33));
            Assert.IsTrue(FullInt.Pow(5, 0).IsOne);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => FullInt.Pow(2, -1));
            Assert.ThrowsException<DivideByZeroException>(() => FullInt.DivRem(1, 0, out _));
            Assert.AreEqual("5", FullInt.Abs(-5).ToString());
            Assert.AreEqual("-5", FullInt.Negate(5).ToString());
            Assert.AreEqual("9", FullInt.Max(3, 9).ToString());
            Assert.AreEqual("3", FullInt.Min(3, 9).ToString());
            Assert.AreEqual(1, new FullInt(1).CompareTo((object?)null));
            Assert.ThrowsException<ArgumentException>(() => new FullInt(1).CompareTo("1"));
            Assert.AreNotSame(FullInt.Zero, FullInt.Zero, "fresh objects: the in-place API may write into a result");
            Assert.IsTrue(FullInt.MinusOne.IsNegative && FullInt.One.IsOne && FullInt.Zero.IsZero);
            var sorted = new[] { 5, -3, 0, 100, -100 }.Select(v => new FullInt(v)).OrderBy(v => v).Select(v => v.ToString());
            Assert.AreEqual("-100 -3 0 5 100", string.Join(" ", sorted));
        }

#if NET5_0_OR_GREATER
        private sealed class Holder
        {
            public FullInt? Value { get; set; }
        }

        [TestMethod]
        public void Json_StringOut_StringOrNumberIn()
        {
            var big = FullInt.Parse("-123456789012345678901234567890");
            string json = System.Text.Json.JsonSerializer.Serialize(new Holder { Value = big });
            Assert.AreEqual("{\"Value\":\"-123456789012345678901234567890\"}", json);
            Assert.AreEqual(big, System.Text.Json.JsonSerializer.Deserialize<Holder>(json)!.Value);
            Assert.AreEqual(big, System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"Value\":-123456789012345678901234567890}")!.Value);
            Assert.IsNull(System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"Value\":null}")!.Value);
            Assert.ThrowsException<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"Value\":1.5}"));
            Assert.ThrowsException<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"Value\":\"abc\"}"));
            Assert.ThrowsException<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"Value\":true}"));
        }
#endif
    }
}
