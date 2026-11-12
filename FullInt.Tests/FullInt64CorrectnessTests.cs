using System.Numerics;
using System.Text;
using FullInt.Core;

namespace FullInt.Tests
{
    /// <summary>
    /// Regression tests for the C#-side correctness issues listed in docs/ANALYSIS.md (C3–C8, C2):
    /// negative zero, hashing/equality, negative exponents, unnormalized divisors,
    /// and stack usage on very large numbers.
    /// </summary>
    [TestClass]
    public class FullInt64CorrectnessTests
    {
        // ---------------------------------------------------------------- negative zero (C7)

        [TestMethod]
        public void NegativeZero_IsNeverProduced_AndEqualsZero()
        {
            FullInt64 zero = 0, minusFive = -5, five = 5;

            var products = new[] { zero * minusFive, minusFive * zero };
            foreach (var p in products)
            {
                Assert.IsFalse(p.Sing, "0 * -5 must not be negative");
                Assert.IsTrue(p == zero, "0 * -5 == 0");
                Assert.AreEqual("0", p.ToString(10));
            }

            Assert.IsFalse((-zero).Sing, "-0 must not be negative");
            Assert.IsTrue(-zero == zero);
            Assert.IsFalse((zero / minusFive).Sing, "0 / -5");
            Assert.IsFalse(((FullInt64)(-10) % five).Sing, "-10 % 5 == 0, not -0");
            Assert.AreEqual("0", ((FullInt64)(-10) % five).ToString(10));
        }

        [TestMethod]
        public void NegativeZero_ConstructedExplicitly_ComparesEqualToZero()
        {
            var negZero = new FullInt64(true, new ulong[] { 0 });
            FullInt64 zero = 0;
            Assert.IsTrue(negZero == zero);
            Assert.IsFalse(negZero < zero);
            Assert.IsFalse(negZero > zero);
            Assert.AreEqual(zero.GetHashCode(), negZero.GetHashCode());
        }

        // ---------------------------------------------------------------- hashing / equality (C3, C4)

        [TestMethod]
        public void GetHashCode_EqualValues_HaveEqualHashes()
        {
            FullInt64 a = FullInt64.Parse("123456789012345678901234567890");
            FullInt64 b = FullInt64.Parse("123456789012345678901234567890");
            var c = new FullInt64(false, new ulong[] { 0, 0 }.Concat(a.value).ToArray()); // leading zero words

            Assert.IsTrue(a == b && a == c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(a.GetHashCode(), c.GetHashCode());

            var set = new HashSet<FullInt64> { a };
            Assert.IsTrue(set.Contains(b), "HashSet must find an equal value");
            Assert.IsTrue(set.Contains(c), "HashSet must find an equal value with leading zero words");

            var dict = new Dictionary<FullInt64, string> { [a] = "x" };
            Assert.AreEqual("x", dict[b]);
        }

        [TestMethod]
        public void GetHashCode_DecimalAndBinaryRepresentations_Match()
        {
            var bin = new FullInt64(123456789L);
            var dec = new FullInt64(123456789L, toBase.dec);
            Assert.IsTrue(bin == dec);
            Assert.AreEqual(bin.GetHashCode(), dec.GetHashCode());
        }

        [TestMethod]
        public void Equals_WithBoxedPrimitives_DoesNotThrow()
        {
            FullInt64 five = 5;
            Assert.IsTrue(five.Equals((object)5));
            Assert.IsTrue(five.Equals((object)5L));
            Assert.IsTrue(five.Equals((object)(byte)5));
            Assert.IsTrue(five.Equals((object)5UL));
            Assert.IsFalse(five.Equals((object)6));
            // a string is not a number (string → FullInt64 is explicit, so this binds to Equals(object))
            Assert.IsFalse(five.Equals("5"));
            Assert.IsFalse(five.Equals(null));
        }

        [TestMethod]
        public void EqualityOperators_HandleNull()
        {
            FullInt64? n = null;
            FullInt64 five = 5;
            Assert.IsTrue(n == null);
            Assert.IsFalse(five == n);
            Assert.IsFalse(n == five);
            Assert.IsTrue(five != n);
        }

        // ---------------------------------------------------------------- byte[] (C5)

        [TestMethod]
        public void FromBytes_LittleEndianMagnitude_SignSeparate()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 2_000; i++)
            {
                var bytes = new byte[rnd.Next(0, 40)];
                rnd.NextBytes(bytes);
                bool neg = rnd.Next(2) == 0;

                // BigInteger(ReadOnlySpan<byte>, isUnsigned: true, isBigEndian: false) = same layout
                var expected = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
                if (neg) expected = -expected;

                Assert.AreEqual(expected.ToString(), new FullInt64(bytes, neg).ToString(10));
                if (!neg)
                {
                    FullInt64 implicitConv = bytes;
                    Assert.AreEqual(expected.ToString(), implicitConv.ToString(10));
                }
            }

            FullInt64 x = new byte[] { 0x01, 0x02 };       // 0x0201
            Assert.AreEqual("513", x.ToString(10));
            FullInt64 empty = new byte[0];
            Assert.IsTrue(empty == 0);
        }

        // ---------------------------------------------------------------- pow (C8)

        [TestMethod]
        public void Pow_NegativeExponent_Throws()
        {
            FullInt64 two = 2;
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => two ^ -3);
        }

        [TestMethod]
        public void Pow_MatchesBigInteger()
        {
            var rnd = new Random(42);
            for (int i = 0; i < 300; i++)
            {
                long b = rnd.Next(-1000, 1000);
                int e = rnd.Next(0, 60);
                FullInt64 fb = b;
                Assert.AreEqual(BigInteger.Pow(b, e).ToString(), (fb ^ e).ToString(10), $"{b}^{e}");
            }
        }

        // ---------------------------------------------------------------- unnormalized divisor (engine)

        [TestMethod]
        public void Div_DivisorWithLeadingZeroWords_MatchesBigInteger()
        {
            var rnd = new Random(77);
            for (int i = 0; i < 2_000; i++)
            {
                var a = new ulong[rnd.Next(1, 7)];
                for (int k = 0; k < a.Length; k++) a[k] = (ulong)rnd.NextInt64();
                var dWords = new ulong[rnd.Next(1, 4)];
                for (int k = 0; k < dWords.Length; k++) dWords[k] = (ulong)rnd.NextInt64() | 1;
                var dPadded = new ulong[rnd.Next(1, 3)].Concat(dWords).ToArray(); // leading zero words

                var fa = new FullInt64(rnd.Next(2) == 0, a);
                var fd = new FullInt64(rnd.Next(2) == 0, dPadded);
                var A = BigInteger.Parse(fa.ToString(10));
                var D = BigInteger.Parse(fd.ToString(10));

                Assert.AreEqual((A / D).ToString(), (fa / fd).ToString(10), $"q: {A} / {D}");
                Assert.AreEqual((A % D).ToString(), (fa % fd).ToString(10), $"r: {A} % {D}");
            }
        }

        // ---------------------------------------------------------------- stack usage (C2)

        /// <summary>
        /// Parsing and printing a ~200k-digit number on a thread with a 256 KB stack.
        /// Before the fix the stackalloc'ed buffers (sized by the input) overflowed the stack,
        /// which kills the process.
        /// </summary>
        [TestMethod]
        public void HugeNumber_ParseAndToString_DoNotOverflowSmallStack()
        {
            var sb = new StringBuilder("9");
            var rnd = new Random(1);
            for (int i = 0; i < 200_000; i++) sb.Append((char)('0' + rnd.Next(10)));
            string digits = sb.ToString();

            string? result = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    FullInt64 big = FullInt64.Parse(digits);
                    result = big.ToString(10);
                }
                catch (Exception ex) { error = ex; }
            }, maxStackSize: 256 * 1024);
            t.Start();
            t.Join();

            Assert.IsNull(error, error?.ToString());
            Assert.AreEqual(digits, result);
        }
    }
}
