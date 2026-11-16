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
        /// Two words by one word in C# (the high word by 64/64, then FullHelper.Div128): boundary
        /// divisors, high word below and above the divisor, every sign, "/" and "%", against
        /// BigInteger. Also FullHelper.Div128 itself on random and boundary words.
        /// </summary>
        [TestMethod]
        public void TwoWordsByOneWord_MatchesBigInteger()
        {
            ulong[] special = { 1, 2, 3, 10, (1UL << 32) - 1, 1UL << 32, (1UL << 32) + 1, (1UL << 63) - 1, 1UL << 63, ulong.MaxValue - 1, ulong.MaxValue };
            var rnd = new Random(1618);
            var divisors = new List<ulong>(special);
            for (int i = 0; i < 30; i++) divisors.Add(((ulong)rnd.NextInt64() >> rnd.Next(64)) | 1);
            var highs = new List<ulong>(special);
            for (int i = 0; i < 20; i++) highs.Add((ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63));

            foreach (var d in divisors)
            foreach (var hi in highs)
            {
                ulong lo = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                BigInteger A = ((BigInteger)hi << 64) + lo, D = d;

                ulong h = hi % d;   // Div128 needs hi < d
                ulong q = FullHelper.Div128(h, lo, d, out ulong r);
                BigInteger Ah = ((BigInteger)h << 64) + lo;
                Assert.AreEqual(Ah / D, (BigInteger)q, $"Div128 q h={h} lo={lo} d={d}");
                Assert.AreEqual(Ah % D, (BigInteger)r, $"Div128 r h={h} lo={lo} d={d}");

                foreach (bool an in new[] { false, true })
                foreach (bool dn in new[] { false, true })
                {
                    var fa = new FullInt64(an, new[] { hi, lo });
                    var fd = new FullInt64(dn, new[] { d });
                    BigInteger SA = an ? -A : A, SD = dn ? -D : D;
                    string ctx = $"a={SA} d={SD}";
                    Assert.AreEqual(SA / SD, BigInteger.Parse((fa / fd).ToString(10)), "div " + ctx);
                    Assert.AreEqual(SA % SD, BigInteger.Parse((fa % fd).ToString(10)), "mod " + ctx);
                    var quo = fa / fd;
                    Assert.IsTrue(quo.value.Length == 1 || quo.value[0] != 0, "div leading zero " + ctx);
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
        /// Compare of equal-length values with the same top word (decided in the engine by the first
        /// differing word: CommonPrefixLength on .NET 8, a loop up to 16 words on netstandard, the
        /// core beyond): one word differs at a random place, or none; lengths across the 16 / 64
        /// word limits; every sign; against BigInteger.
        /// </summary>
        [TestMethod]
        public void Compare_EqualTopWords_MatchBigInteger()
        {
            var rnd = new Random(31337);
            static BigInteger Big(ulong[] w, bool neg)
            {
                BigInteger m = 0;
                foreach (var x in w) m = (m << 64) + x;
                return neg ? -m : m;
            }
            for (int it = 0; it < 3_000; it++)
            {
                int n = rnd.Next(4) == 0 ? rnd.Next(1, 300) : rnd.Next(1, 80);
                var aw = new ulong[n];
                for (int k = 0; k < n; k++) aw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                if (aw[0] == 0) aw[0] = 1;
                var bw = (ulong[])aw.Clone();
                if (n > 1 && rnd.Next(5) != 0)
                {
                    int at = rnd.Next(1, n);   // the top word stays the same
                    bw[at] = rnd.Next(3) switch { 0 => bw[at] + 1, 1 => bw[at] - 1, _ => (ulong)rnd.NextInt64() };
                }
                bool an = rnd.Next(2) == 0, bn = rnd.Next(4) == 0 ? !an : an;
                var fa = new FullInt64(an, aw);
                var fb = new FullInt64(bn, bw);
                BigInteger A = Big(aw, an), B = Big(bw, bn);
                string ctx = $"n={n} it={it}";
                Assert.AreEqual(A < B, fa < fb, "< " + ctx);
                Assert.AreEqual(A > B, fa > fb, "> " + ctx);
                Assert.AreEqual(A == B, fa == fb, "== " + ctx);
                Assert.AreEqual(B > A, fb > fa, "> swapped " + ctx);
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
        /// Decimal text by halves (FullInt64Engine.Decimal.cs): ToString(10) and Parse against
        /// BigInteger, at the default thresholds and at tiny ones (deep recursion). The values
        /// stress the zero padding of the low halves: powers of ten and their neighbours, long
        /// zero blocks inside, and random digits.
        /// </summary>
        [TestMethod]
        public void DecimalByHalves_MatchesBigInteger()
        {
            int savedTo = DecimalConversion.ToStringThreshold, savedParse = DecimalConversion.ParseThreshold;
            try
            {
                var rnd = new Random(4141);
                var cases = new List<BigInteger>();
                // (40000 digits: the prepared powers keep their NTT transforms, from ~20000 digits)
                foreach (int e in new[] { 19, 38, 76, 152, 304, 608, 1216, 2432, 4864, 9728, 20000, 40000 })
                {
                    var p = BigInteger.Pow(10, e);
                    cases.Add(p); cases.Add(p - 1); cases.Add(p + 1); cases.Add(p * 7 + 3);
                }
                for (int i = 0; i < 42; i++)
                {
                    int digits = rnd.Next(20, i < 36 ? 3000 : i < 40 ? 20000 : 45000);   // BigInteger's own text conversion is n² as well
                    var sb = new StringBuilder();
                    sb.Append((char)('1' + rnd.Next(9)));
                    for (int d = 1; d < digits; d++)
                    {
                        // runs of zeros now and then
                        sb.Append(rnd.Next(10) == 0 ? new string('0', rnd.Next(1, 120)) : ((char)('0' + rnd.Next(10))).ToString());
                    }
                    cases.Add(BigInteger.Parse(sb.ToString()));
                }

                foreach (var (to, parse) in new[] { (savedTo, savedParse), (2, 20), (3, 39), (5, 100) })
                {
                    DecimalConversion.ToStringThreshold = to;
                    DecimalConversion.ParseThreshold = parse;
                    bool tiny = to != savedTo;
                    foreach (var big in cases)
                    {
                        if (tiny && big.ToByteArray().Length > 2100) continue;   // deep recursion: up to ~5000 digits
                        foreach (var v in new[] { big, -big })
                        {
                            string text = v.ToString();
                            var parsed = FullInt64.Parse(text);
                            Assert.AreEqual(text, parsed.ToString(10), $"thresholds {to}/{parse}, {text.Length} chars");
                        }
                    }
                }
            }
            finally
            {
                DecimalConversion.ToStringThreshold = savedTo;
                DecimalConversion.ParseThreshold = savedParse;
            }
        }

        /// <summary>
        /// The in-place API (FullInt64.Add(result, a, b) etc.) against BigInteger: result fresh,
        /// reused, and the same object as a or b; signs; sizes across the Karatsuba threshold.
        /// </summary>
        [TestMethod]
        public void InPlaceApi_MatchesBigInteger()
        {
            var rnd = new Random(3131);
            FullInt64 Make(int words)
            {
                var w = new ulong[words];
                for (int k = 0; k < words; k++) w[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                if (rnd.Next(4) == 0) w[0] = 0;   // a leading zero word
                return new FullInt64(rnd.Next(2) == 0, w);
            }
            BigInteger Big(FullInt64 x) => BigInteger.Parse(x.ToString(10));

            var result = new FullInt64(0);
            for (int i = 0; i < 600; i++)
            {
                var a = Make(rnd.Next(1, i % 3 == 0 ? 90 : 12));
                var b = Make(rnd.Next(1, i % 3 == 0 ? 90 : 12));
                BigInteger A = Big(a), B = Big(b);
                int shift = rnd.Next(0, 200);
                string ctx = $"{a.value.Length}/{b.value.Length} words, iteration {i}";

                switch (rnd.Next(3))   // into the shared result, into a, into b
                {
                    case 0:
                        FullInt64.Add(result, a, b); Assert.AreEqual(A + B, Big(result), "add " + ctx);
                        FullInt64.Subtract(result, a, b); Assert.AreEqual(A - B, Big(result), "sub " + ctx);
                        FullInt64.Multiply(result, a, b); Assert.AreEqual(A * B, Big(result), "mul " + ctx);
                        FullInt64.ShiftLeft(result, a, shift); Assert.AreEqual(A << shift, Big(result), "shl " + ctx);
                        FullInt64.ShiftRight(result, a, shift); Assert.AreEqual(BigCompat.ShiftRightFloor(A, shift), Big(result), "shr " + ctx);
                        break;
                    case 1:
                        var x = new FullInt64(a.Sing, (ulong[])a.value.Clone());
                        FullInt64.Add(x, x, b); Assert.AreEqual(A + B, Big(x), "add into a " + ctx);
                        FullInt64.Subtract(x, x, b); Assert.AreEqual(A, Big(x), "sub into a " + ctx);
                        FullInt64.Multiply(x, x, b); Assert.AreEqual(A * B, Big(x), "mul into a " + ctx);
                        FullInt64.ShiftLeft(x, x, shift); Assert.AreEqual((A * B) << shift, Big(x), "shl into a " + ctx);
                        FullInt64.ShiftRight(x, x, shift); Assert.AreEqual(BigCompat.ShiftRightFloor((A * B) << shift, shift), Big(x), "shr into a " + ctx);
                        break;
                    default:
                        var y = new FullInt64(b.Sing, (ulong[])b.value.Clone());
                        FullInt64.Add(y, a, y); Assert.AreEqual(A + B, Big(y), "add into b " + ctx);
                        FullInt64.Subtract(y, a, y); Assert.AreEqual(-B, Big(y), "sub into b " + ctx);
                        FullInt64.Multiply(y, a, y); Assert.AreEqual(-A * B, Big(y), "mul into b " + ctx);
                        break;
                }
            }
        }

        /// <summary>
        /// The in-place API never writes into an array another object can see, and a result
        /// reused in a loop does not grow.
        /// </summary>
        [TestMethod]
        public void InPlaceApi_SharedArraysAndLoops()
        {
            var a = FullInt64.Parse("123456789012345678901234567890");
            var b = FullInt64.Parse("987654321098765432109876543210");

            // an owned result handed to a copy: the next in-place call must not change the copy
            var r = new FullInt64(0);
            FullInt64.Add(r, a, b);
            var copy = new FullInt64(r);
            string before = copy.ToString(10);
            FullInt64.Add(r, r, b);
            Assert.AreEqual(before, copy.ToString(10), "copy constructor shares the array");

            var r2 = new FullInt64(0);
            FullInt64.Multiply(r2, a, b);
            var words = (ulong[])r2;
            var snapshot = (ulong[])words.Clone();
            FullInt64.Add(r2, r2, a);
            CollectionAssert.AreEqual(snapshot, words, "(ulong[]) hands the array out");

            // an operand made by the engine (shares nothing): the result gets its own array
            var sum = a + b;
            var keep = sum.ToString(10);
            var target = new FullInt64(sum);   // shares sum's array: written in place it would change sum
            FullInt64.Add(target, target, b);
            Assert.AreEqual(keep, sum.ToString(10), "an operand's array shared by the result");

            // r = r + 1 a thousand times: the array keeps its size
            var acc = new FullInt64(0);
            FullInt64.Add(acc, a, a);
            int length = 0;
            for (int i = 0; i < 1000; i++)
            {
                FullInt64.Add(acc, acc, 1);
                if (i == 10) length = acc.value.Length;
            }
            Assert.AreEqual(length, acc.value.Length, "the reused array grows");

            // zero in a long reused array prints as "0" (the long-number text path)
            var big = new FullInt64(0);
            FullInt64.Multiply(big, FullInt64.Parse(new string('9', 2000)), FullInt64.Parse(new string('7', 2000)));
            FullInt64.Subtract(big, big, big);
            Assert.AreEqual("0", big.ToString(10), "zero in a long array");
            Assert.AreEqual((BigInteger.Parse("123456789012345678901234567890") * 2 + 1000).ToString(), acc.ToString(10));
        }

        /// <summary>
        /// The operator path into Burnikel–Ziegler (divisor and quotient at or above
        /// BurnikelZiegler.Threshold words): signs, '/' and '%', against BigInteger.
        /// </summary>
        [TestMethod]
        public void DivMod_BurnikelZieglerSizes_MatchBigInteger()
        {
            var rnd = new Random(9292);
            int t = 2 * BurnikelZiegler.Threshold;   // where the engine starts using it
            for (int i = 0; i < 40; i++)
            {
                var bw = new ulong[rnd.Next(t, 500)];
                var aw = new ulong[bw.Length + rnd.Next(t, 700)];
                for (int k = 0; k < aw.Length; k++) aw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                for (int k = 0; k < bw.Length; k++) bw[k] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                aw[0] |= 1; bw[0] |= 1;   // keep the lengths (no leading zero words)
                var fa = new FullInt64(rnd.Next(2) == 0, aw);
                var fb = new FullInt64(rnd.Next(2) == 0, bw);
                var A = BigInteger.Parse(fa.ToString(10));
                var B = BigInteger.Parse(fb.ToString(10));

                Assert.AreEqual(BigInteger.Divide(A, B).ToString(), (fa / fb).ToString(10), $"{aw.Length}/{bw.Length} words /");
                Assert.AreEqual(BigInteger.Remainder(A, B).ToString(), (fa % fb).ToString(10), $"{aw.Length}/{bw.Length} words %");
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
