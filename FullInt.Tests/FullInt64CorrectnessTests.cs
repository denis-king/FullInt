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
        // ---------------------------------------------------------------- core selection

        /// <summary>
        /// The factory falls back to the managed core when the native library cannot be loaded.
        /// That must not hide a broken native build: in a 64-bit or 32-bit Windows process the
        /// matching asm core is expected (unless FULLINT_FORCE_MANAGED=1); elsewhere the managed one.
        /// </summary>
        [TestMethod]
        public void Factory_PicksTheExpectedCore()
        {
            var core = FullIntCoreFactory.Create();
            bool forced = Environment.GetEnvironmentVariable(FullIntCoreFactory.ForceManagedEnvVar) == "1";
            bool windows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            var expected = forced || !windows ? typeof(FullInt64CoreDefault)
                : arch == System.Runtime.InteropServices.Architecture.X64 ? typeof(FullIntCoreWindowsX64)
                : arch == System.Runtime.InteropServices.Architecture.X86 ? typeof(FullIntCoreWindowsX86)
                : typeof(FullInt64CoreDefault);
            Assert.AreEqual(expected, core.GetType(),
                $"process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        }

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

                // unsigned little-endian magnitude = same layout
                var expected = BigCompat.FromUnsignedLittleEndian(bytes);
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

        // ---------------------------------------------------------------- result sizing (one allocation)

        /// <summary>
        /// Results are allocated at their exact size: Add widens by one word only on a carry out,
        /// leading zero words are trimmed. Operands are biased to all-ones words (long carries),
        /// equal magnitudes (a + (-a) = 0) and leading zero words.
        /// </summary>
        [TestMethod]
        public void Operators_ResultSizing_MatchesBigInteger_AndIsNormalized()
        {
            var rnd = new Random(4242);
            ulong NextWord() => rnd.Next(3) switch { 0 => ulong.MaxValue, 1 => 0UL, _ => (ulong)rnd.NextInt64() };
            ulong[] NextLimbs()
            {
                var w = new ulong[rnd.Next(1, 9)];
                for (int k = 0; k < w.Length; k++) w[k] = NextWord();
                return w;
            }
            static BigInteger Big(FullInt64 x) => BigInteger.Parse(x.ToString(10));
            static void AssertNormalized(FullInt64 x, string what)
            {
                Assert.IsTrue(x.value.Length == 1 || x.value[0] != 0, what + ": leading zero word");
                if (x.value.Length == 1 && x.value[0] == 0) Assert.IsFalse(x.Sing, what + ": negative zero");
            }

            for (int i = 0; i < 5_000; i++)
            {
                var aw = NextLimbs();
                var bw = rnd.Next(4) == 0 ? (ulong[])aw.Clone() : NextLimbs();   // equal magnitudes
                var fa = new FullInt64(rnd.Next(2) == 0, aw);
                var fb = new FullInt64(rnd.Next(2) == 0, bw);
                BigInteger A = Big(fa), B = Big(fb);
                string ctx = $"a={A} b={B}";

                var sum = fa + fb;  Assert.AreEqual(A + B, Big(sum), "add " + ctx);  AssertNormalized(sum, "add " + ctx);
                var dif = fa - fb;  Assert.AreEqual(A - B, Big(dif), "sub " + ctx);  AssertNormalized(dif, "sub " + ctx);
                var prd = fa * fb;  Assert.AreEqual(A * B, Big(prd), "mult " + ctx); AssertNormalized(prd, "mult " + ctx);
                if (!B.IsZero)
                {
                    var quo = fa / fb; Assert.AreEqual(A / B, Big(quo), "div " + ctx); AssertNormalized(quo, "div " + ctx);
                }
                var and = fa & fb;  Assert.AreEqual(A & B, Big(and), "and " + ctx);  AssertNormalized(and, "and " + ctx);
                var shl = fa << 37; Assert.AreEqual(A << 37, Big(shl), "shl " + ctx); AssertNormalized(shl, "shl " + ctx);
            }
        }

        /// <summary>
        /// The one-word fast paths (add/sub/mult/div/mod in plain C#, no native call):
        /// every sign combination, boundary words and random words, against BigInteger.
        /// </summary>
        [TestMethod]
        public void OneWordFastPaths_MatchBigInteger()
        {
            ulong[] special = { 0, 1, 2, ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63, (1UL << 63) - 1, 1UL << 32, uint.MaxValue };
            var words = new List<ulong>(special);
            var rnd = new Random(31337);
            for (int i = 0; i < 40; i++) words.Add((ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63));

            static BigInteger Big(FullInt64 x) => BigInteger.Parse(x.ToString(10));
            static void AssertCanonical(FullInt64 x, string what)
            {
                Assert.IsTrue(x.value.Length == 1 || x.value[0] != 0, what + ": leading zero word");
                if (x.value.Length == 1 && x.value[0] == 0) Assert.IsFalse(x.Sing, what + ": negative zero");
            }

            foreach (var aw in words)
            foreach (var bw in words)
            foreach (bool an in new[] { false, true })
            foreach (bool bn in new[] { false, true })
            {
                var fa = new FullInt64(an, new[] { aw });
                var fb = new FullInt64(bn, new[] { bw });
                BigInteger A = an ? -(BigInteger)aw : aw, B = bn ? -(BigInteger)bw : bw;
                string ctx = $"a={A} b={B}";

                var s = fa + fb; Assert.AreEqual(A + B, Big(s), "add " + ctx); AssertCanonical(s, "add " + ctx);
                var d = fa - fb; Assert.AreEqual(A - B, Big(d), "sub " + ctx); AssertCanonical(d, "sub " + ctx);
                var p = fa * fb; Assert.AreEqual(A * B, Big(p), "mult " + ctx); AssertCanonical(p, "mult " + ctx);
                if (bw != 0)
                {
                    var q = fa / fb; Assert.AreEqual(A / B, Big(q), "div " + ctx); AssertCanonical(q, "div " + ctx);
                    var r = fa % fb; Assert.AreEqual(A % B, Big(r), "mod " + ctx); AssertCanonical(r, "mod " + ctx);
                }
                else
                {
                    Assert.ThrowsException<DivideByZeroException>(() => fa / fb, "div by zero " + ctx);
                    Assert.ThrowsException<DivideByZeroException>(() => fa % fb, "mod by zero " + ctx);
                }
            }
        }

        /// <summary>
        /// The C# fast paths of compare (sign, length, top word; the core only for equal top words
        /// or leading zero words), of one-word shifts and of one-word ToString: every sign, shifts
        /// across 0..130 bits, operands with and without leading zero words, against BigInteger.
        /// </summary>
        [TestMethod]
        public void CompareShiftAndToString_FastPaths_MatchBigInteger()
        {
            ulong[] special = { 0, 1, 2, ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63, (1UL << 63) - 1, 1UL << 32, uint.MaxValue };
            var rnd = new Random(8128);
            var values = new List<(ulong[] w, bool neg)>();
            foreach (var w in special) foreach (bool n in new[] { false, true })
            {
                values.Add((new[] { w }, n));
                values.Add((new[] { 0UL, w }, n));             // a leading zero word
                values.Add((new[] { w, 5UL }, n));             // same top word as the one-word value
                values.Add((new[] { w, w, ulong.MaxValue }, n));
            }
            for (int i = 0; i < 60; i++)
            {
                var w = new ulong[rnd.Next(1, 5)];
                for (int k = 0; k < w.Length; k++) w[k] = rnd.Next(4) == 0 ? special[rnd.Next(special.Length)] : (ulong)rnd.NextInt64();
                values.Add((w, rnd.Next(2) == 0));
            }

            static BigInteger Big(ulong[] w, bool neg)
            {
                BigInteger m = 0;
                foreach (var x in w) m = (m << 64) + x;
                return neg ? -m : m;
            }

            foreach (var (aw, an) in values)
            {
                var fa = new FullInt64(an, aw);
                var A = Big(aw, an);
                Assert.AreEqual(A.ToString(), fa.ToString(10), $"ToString a={A}");

                foreach (int k in new[] { 0, 1, 7, 37, 63, 64, 65, 100, 127, 128, 130 })
                {
                    Assert.AreEqual(A << k, BigInteger.Parse((fa << k).ToString(10)), $"shl a={A} k={k}");
                    Assert.AreEqual(BigCompat.ShiftRightFloor(A, k), BigInteger.Parse((fa >> k).ToString(10)), $"shr a={A} k={k}");
                }

                foreach (var (bw, bn) in values)
                {
                    var fb = new FullInt64(bn, bw);
                    var B = Big(bw, bn);
                    string ctx = $"a={A} b={B}";
                    Assert.AreEqual(A < B, fa < fb, "< " + ctx);
                    Assert.AreEqual(A > B, fa > fb, "> " + ctx);
                    Assert.AreEqual(A <= B, fa <= fb, "<= " + ctx);
                    Assert.AreEqual(A >= B, fa >= fb, ">= " + ctx);
                    Assert.AreEqual(A == B, fa == fb, "== " + ctx);
                }
            }
        }

        /// <summary>
        /// "/" and "%" (which no longer materialize the result they don't return) agree with
        /// the combined div(a, b, out remainder) and with BigInteger, on multi-word operands.
        /// </summary>
        [TestMethod]
        public void DivAndMod_Separately_MatchDivRem()
        {
            var rnd = new Random(2718);
            for (int i = 0; i < 3_000; i++)
            {
                var aw = new ulong[rnd.Next(1, 9)];
                var bw = new ulong[rnd.Next(1, 6)];
                for (int k = 0; k < aw.Length; k++) aw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                for (int k = 0; k < bw.Length; k++) bw[k] = (ulong)rnd.NextInt64() | 1;
                var fa = new FullInt64(rnd.Next(2) == 0, aw);
                var fb = new FullInt64(rnd.Next(2) == 0, bw);
                var A = BigInteger.Parse(fa.ToString(10));
                var B = BigInteger.Parse(fb.ToString(10));

                var q = fa / fb;
                var r = fa % fb;
                var q2 = FullInt64Engine.Instance.div(fa, fb, out var r2);
                Assert.AreEqual((A / B).ToString(), q.ToString(10), $"q: {A} / {B}");
                Assert.AreEqual((A % B).ToString(), r.ToString(10), $"r: {A} % {B}");
                Assert.AreEqual(q2.ToString(10), q.ToString(10), "q vs divRem");
                Assert.AreEqual(r2.ToString(10), r.ToString(10), "r vs divRem");
            }
        }

        /// <summary>
        /// The operator path into Karatsuba (operands at or above Karatsuba.Threshold words):
        /// signs, unequal lengths, against BigInteger.
        /// </summary>
        [TestMethod]
        public void Mult_KaratsubaSizes_MatchesBigInteger()
        {
            var rnd = new Random(9090);
            int t = Karatsuba.Threshold;
            for (int i = 0; i < 60; i++)
            {
                var aw = new ulong[rnd.Next(t, 600)];
                var bw = new ulong[rnd.Next(2) == 0 ? rnd.Next(t, 600) : rnd.Next(t, t + 20)];
                for (int k = 0; k < aw.Length; k++) aw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                for (int k = 0; k < bw.Length; k++) bw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                aw[0] |= 1; bw[0] |= 1;   // keep the lengths (no leading zero words)
                var fa = new FullInt64(rnd.Next(2) == 0, aw);
                var fb = new FullInt64(rnd.Next(2) == 0, bw);
                var A = BigInteger.Parse(fa.ToString(10));
                var B = BigInteger.Parse(fb.ToString(10));

                var p = fa * fb;
                Assert.AreEqual((A * B).ToString(), p.ToString(10), $"{aw.Length}x{bw.Length} words");
                Assert.IsTrue(p.value[0] != 0, "leading zero word");
            }
        }

        /// <summary>
        /// Division with work buffers above the stack limit, which come from ArrayPool: divisor
        /// over 1024 words (remainder buffer), '%' with a quotient over 1024 words (dropped
        /// quotient), '%' by one word with a dividend over 1024 words. Each case runs several
        /// times in a row, so the pool hands back used (dirty) arrays.
        /// </summary>
        [TestMethod]
        public void DivMod_PooledWorkBuffers_MatchBigInteger()
        {
            var rnd = new Random(1717);
            FullInt64 Make(int words, out BigInteger big)
            {
                var w = new ulong[words];
                for (int k = 0; k < words; k++) w[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                w[0] |= 1;
                var f = new FullInt64(rnd.Next(2) == 0, w);
                big = BigInteger.Parse(f.ToString(10));
                return f;
            }

            for (int rep = 0; rep < 3; rep++)
            {
                var a = Make(2600, out var A);
                var bigDivisor = Make(1100, out var D);          // remainder buffer > 1024 words
                var smallDivisor = Make(300, out var d);         // quotient > 1024 words
                var word = Make(1, out var w);

                Assert.AreEqual((A / D).ToString(), (a / bigDivisor).ToString(10), "a / D");
                Assert.AreEqual((A % D).ToString(), (a % bigDivisor).ToString(10), "a % D");
                Assert.AreEqual((A % d).ToString(), (a % smallDivisor).ToString(10), "a % d (dropped quotient)");
                Assert.AreEqual((A / d).ToString(), (a / smallDivisor).ToString(10), "a / d");
                Assert.AreEqual((A % w).ToString(), (a % word).ToString(10), "a % one word");
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
