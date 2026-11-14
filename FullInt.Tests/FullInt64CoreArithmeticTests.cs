using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using FullInt.Core;

namespace FullInt.Tests
{
    /// <summary>
    /// Core-level Add / Sub / Mult / Compare / Div-by-word / ToDecString / FromDecString
    /// on both cores (native asm and managed fallback), checked against BigInteger.
    /// Operands include zero and boundary words. Fixed seeds.
    /// </summary>
    [TestClass]
    public class FullInt64CoreArithmeticTests
    {
        private static readonly ulong[] SpecialWords =
        {
            0UL, 1UL, ulong.MaxValue, ulong.MaxValue - 1, 0x8000_0000_0000_0000UL, 0x7FFF_FFFF_FFFF_FFFFUL,
        };

        public static IEnumerable<object[]> Cores()
        {
            yield return new object[] { "native" };
            yield return new object[] { "managed" };
        }

        private static IFullIntCore GetCore(string name) =>
            name == "managed" ? new FullInt64CoreDefault() : FullIntCoreFactory.Create();

        private static BigInteger ToBig(ReadOnlySpan<ulong> limbs)
        {
            var bytes = new byte[limbs.Length * 8];
            for (int i = 0; i < limbs.Length; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan((limbs.Length - 1 - i) * 8), limbs[i]);
            return BigCompat.FromUnsignedLittleEndian(bytes);
        }

        private static ulong[] Next(Random rnd, int length, bool nonZeroTop = false)
        {
            var r = new ulong[length];
            for (int i = 0; i < length; i++)
                r[i] = rnd.Next(3) == 0 ? SpecialWords[rnd.Next(SpecialWords.Length)] : (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 63);
            if (nonZeroTop && r[0] == 0) r[0] = 1;
            return r;
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_AddSubMultCompare_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(5555);
            for (int it = 0; it < 20_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 9));
                var b = Next(rnd, rnd.Next(1, 9));
                BigInteger A = ToBig(a), B = ToBig(b);
                string ctx = $"[{coreName}] a={A:X} b={B:X} (iteration {it})";

                var sum = new ulong[Math.Max(a.Length, b.Length) + 1];
                core.Add(a, b, sum);
                Assert.AreEqual(A + B, ToBig(sum), "add " + ctx);

                var diff = new ulong[Math.Max(a.Length, b.Length) + 1];
                int sign = core.Sub(a, b, diff);
                Assert.AreEqual(A.CompareTo(B), sign, "sub sign " + ctx);
                Assert.AreEqual(BigInteger.Abs(A - B), ToBig(diff), "sub " + ctx);

                Assert.AreEqual(A.CompareTo(B), core.Compare(a, b), "compare " + ctx);

                var prod = new ulong[a.Length + b.Length];
                core.Mult(a, b, prod);
                Assert.AreEqual(A * B, ToBig(prod), "mult " + ctx);
            }
        }

        /// <summary>
        /// Add / Sub with any result length: shorter than the operands (truncated; Add returns
        /// only the carry out of the top word of R), longer (carry, then zeros), A shorter or
        /// longer than B, all-ones words so carries and borrows run through whole tails.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_AddSub_AnyResultLength_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(4321);
            ulong[] Shaped(int n)
            {
                var w = Next(rnd, n);
                if (rnd.Next(3) == 0) for (int k = 0; k < n; k++) if (rnd.Next(4) != 0) w[k] = ulong.MaxValue;
                return w;
            }

            for (int it = 0; it < 20_000; it++)
            {
                var a = Shaped(rnd.Next(1, 13));
                var b = Shaped(rnd.Next(1, 13));
                int lenR = rnd.Next(1, 16);
                BigInteger A = ToBig(a), B = ToBig(b), mod = BigInteger.One << (64 * lenR);
                string ctx = $"[{coreName}] lenA={a.Length} lenB={b.Length} lenR={lenR} a={A:X} b={B:X}";

                var r = new ulong[lenR];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                ulong carry = core.Add(a, b, r);
                Assert.AreEqual((A + B) % mod, ToBig(r), "add " + ctx);
                Assert.AreEqual((ulong)((A % mod + B % mod) / mod), carry, "add carry " + ctx);

                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                int sign = core.Sub(a, b, r);
                Assert.AreEqual(A.CompareTo(B), sign, "sub sign " + ctx);
                Assert.AreEqual(BigInteger.Abs(A - B) % mod, ToBig(r), "sub " + ctx);
            }
        }

        /// <summary>
        /// Mult on longer and very unequal operands, and with a result buffer longer than
        /// lenA + lenB (the extra high words must come out zero).
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_Mult_LongAndUnequal_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(6161);
            for (int it = 0; it < 3_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 101));
                var b = Next(rnd, rnd.Next(1, 101));
                int extra = rnd.Next(4) == 0 ? rnd.Next(1, 4) : 0;
                var prod = new ulong[a.Length + b.Length + extra];
                for (int k = 0; k < prod.Length; k++) prod[k] = 0xDEAD_BEEF_DEAD_BEEFUL;   // must be overwritten

                core.Mult(a, b, prod);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(prod), $"mult {a.Length}x{b.Length} lenR={prod.Length}");
            }
        }

        /// <summary>
        /// Karatsuba and Toom-3 over the core's basecase, against BigInteger. Small thresholds make
        /// the recursion go deep (and mix the three methods) on small numbers. Covers unbalanced
        /// lengths (chunked path), all-ones words (long carries), equal halves (|a1 - a0| = 0,
        /// p(-1) = 0) and zero words.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Karatsuba_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(8181);
            int[] thresholds = { 2, 3, 4, 5, 8, 16, 32 };
            int[] toom3Thresholds = { 3, 4, 6, 9, 16, 40, int.MaxValue };

            ulong[] Shaped(int length)
            {
                switch (rnd.Next(5))
                {
                    case 0:   // all ones: carries run through whole halves
                        var ones = new ulong[length];
                        ones.AsSpan().Fill(ulong.MaxValue);
                        return ones;
                    case 1:   // equal halves: a1 - a0 = 0 at the top level
                        var h = Next(rnd, (length + 1) / 2);
                        var eq = new ulong[length];
                        h.AsSpan(h.Length - length / 2).CopyTo(eq);
                        h.CopyTo(eq.AsSpan(length / 2));
                        return eq;
                    default:
                        return Next(rnd, length);
                }
            }

            for (int it = 0; it < 3_000; it++)
            {
                int threshold = thresholds[rnd.Next(thresholds.Length)];
                int toom3 = toom3Thresholds[rnd.Next(toom3Thresholds.Length)];
                int la = rnd.Next(1, 200);
                int lb = rnd.Next(4) == 0 ? rnd.Next(1, 20) : rnd.Next(1, 200);   // often very unbalanced
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);   // must be fully overwritten

                Karatsuba.Multiply(core, a, b, r, threshold, toom3);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] {la}x{lb} threshold={threshold} toom3={toom3} (iteration {it})");
            }

            // larger sizes, a few, at the default thresholds and at small ones
            for (int it = 0; it < 16; it++)
            {
                int la = rnd.Next(300, 1500), lb = rnd.Next(300, 1500);
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                (int t2, int t3) = (it % 4) switch
                {
                    0 => (Karatsuba.Threshold, Karatsuba.Toom3Threshold),
                    1 => (4, int.MaxValue),
                    2 => (4, 12),
                    _ => (Karatsuba.Threshold, 100),
                };
                Karatsuba.Multiply(core, a, b, r, t2, t3);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] {la}x{lb} large, threshold={t2} toom3={t3}");
            }
        }

        /// <summary>
        /// Burnikel–Ziegler over the core's Div, against BigInteger. Small thresholds make the
        /// recursion go deep on small numbers; the shapes hit the Q̂ = β^k - 1 branch (all-ones
        /// and B1-equal tops), the R̂ &lt; 0 corrections and divisors of any bit alignment.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void BurnikelZiegler_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(9191);
            int[] thresholds = { 2, 3, 4, 5, 8, 16, 33 };

            ulong[] Shaped(int length)
            {
                var w = Next(rnd, length, nonZeroTop: true);
                switch (rnd.Next(5))
                {
                    case 0:   // all ones
                        w.AsSpan().Fill(ulong.MaxValue);
                        break;
                    case 1:   // a small top word: the divisor needs a large bit shift
                        w[0] = (ulong)rnd.Next(1, 16);
                        break;
                }
                return w;
            }

            void Check(ulong[] a, ulong[] b, int threshold, string ctx)
            {
                var q = new ulong[a.Length - b.Length + 1];
                var r = new ulong[b.Length + 2];
                q.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);   // must be fully overwritten
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                BurnikelZiegler.DivRem(core, a, b, q, r, threshold);
                var expectedQ = BigInteger.DivRem(ToBig(a), ToBig(b), out var expectedR);
                Assert.AreEqual(expectedQ, ToBig(q), "quotient " + ctx);
                Assert.AreEqual(expectedR, ToBig(r), "remainder " + ctx);
            }

            for (int it = 0; it < 2_000; it++)
            {
                int threshold = thresholds[rnd.Next(thresholds.Length)];
                int lb = rnd.Next(1, 120);
                int la = lb + rnd.Next(0, 160);
                var b = Shaped(lb);
                var a = Shaped(la);
                if (rnd.Next(4) == 0 && la >= lb)   // top of a equal to b: the Q̂ = β^k - 1 branch
                {
                    b.CopyTo(a, 0);
                }
                Check(a, b, threshold, $"[{coreName}] {la}/{lb} threshold={threshold} (iteration {it})");
            }

            // larger sizes at the default threshold
            for (int it = 0; it < 8; it++)
            {
                int lb = rnd.Next(100, 1200), la = lb + rnd.Next(100, 2000);
                Check(Shaped(la), Shaped(lb), BurnikelZiegler.Threshold, $"[{coreName}] {la}/{lb} large");
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_DivByWord_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(6666);
            for (int it = 0; it < 20_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 9));
                ulong d = Next(rnd, 1, nonZeroTop: true)[0];
                var q = new ulong[a.Length];
                core.Div(a, d, q, out ulong r);

                var expQ = BigInteger.DivRem(ToBig(a), d, out var expR);
                Assert.AreEqual(expQ, ToBig(q), $"[{coreName}] q, a={ToBig(a):X} d={d:X}");
                Assert.AreEqual(expR, (BigInteger)r, $"[{coreName}] r, a={ToBig(a):X} d={d:X}");
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_DecimalStrings_RoundTrip(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(7777);
            for (int it = 0; it < 5_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 9));
                var A = ToBig(a);
                string expected = A.ToString();

                // ToDecString
                var buf = new byte[a.Length * 20 + 1];
                core.ToDecString(a, buf, out int start);
                string got = Encoding.ASCII.GetString(buf, start, buf.Length - start);
                Assert.AreEqual(expected, got, $"[{coreName}] ToDecString");

                // FromDecString
                var text = Encoding.ASCII.GetBytes(expected);
                var limbs = new ulong[text.Length / 19 + 1];
                core.FromDecString(text, limbs, out int lstart);
                Assert.AreEqual(A, ToBig(limbs.AsSpan(lstart)), $"[{coreName}] FromDecString {expected}");
                // the start is the top word: no leading zero words (only "0" is a single zero word)
                Assert.IsTrue(lstart == limbs.Length - 1 || limbs[lstart] != 0, $"[{coreName}] FromDecString leading zero words, start={lstart} of {limbs.Length}: {expected}");
            }
        }
    }
}
