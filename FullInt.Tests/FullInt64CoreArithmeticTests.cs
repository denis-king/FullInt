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

        // big-endian words of a non-negative value, without leading zero words
        private static ulong[] FromBig(BigInteger v)
        {
            var words = new System.Collections.Generic.List<ulong>();
            var mask = (BigInteger)ulong.MaxValue;
            do
            {
                words.Add((ulong)(v & mask));
                v >>= 64;
            } while (v > 0);
            words.Reverse();
            return words.ToArray();
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
        /// The Schönhage–Strassen FFT product, against BigInteger: forced K = 2^k (1..9) on small
        /// and unbalanced numbers (pieces of one word up to hundreds), all-ones and zero words;
        /// and the dispatcher with a small FFT threshold, so the pointwise products recurse into
        /// the FFT and the Toom levels too.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Fft_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(1717);
            ulong[] Shaped(int length) => rnd.Next(6) switch
            {
                0 => Enumerable.Repeat(ulong.MaxValue, length).ToArray(),
                1 => Enumerable.Range(0, length).Select(i => i % 3 == 0 ? 0UL : ulong.MaxValue).ToArray(),
                _ => Next(rnd, length),
            };
            for (int it = 0; it < 600; it++)
            {
                int k = rnd.Next(1, 10);
                int la = rnd.Next(1, it % 10 == 0 ? 400 : 60), lb = rnd.Next(1, it % 10 == 0 ? 400 : 60);
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                Karatsuba.FftMultiply(core, a, b, r, k);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] fft {la}x{lb} k={k} (iteration {it})");
            }
            for (int it = 0; it < 12; it++)
            {
                int la = rnd.Next(100, 700), lb = rnd.Next(100, 700);
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                int fft = new[] { 24, 48, 100 }[it % 3];
                Karatsuba.Multiply(core, a, b, r, 8, 24, 48, fft);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] {la}x{lb} fft threshold {fft}");
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
            int[] toom4Thresholds = { 4, 5, 8, 12, 24, 60, int.MaxValue };
            int[] toom6Thresholds = { 6, 8, 12, 36, 50, 90, int.MaxValue, int.MaxValue };

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
                int toom4 = toom4Thresholds[rnd.Next(toom4Thresholds.Length)];
                int toom6 = toom6Thresholds[rnd.Next(toom6Thresholds.Length)];
                int la = rnd.Next(1, 200);
                int lb = rnd.Next(4) == 0 ? rnd.Next(1, 20) : rnd.Next(1, 200);   // often very unbalanced
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);   // must be fully overwritten

                Karatsuba.Multiply(core, a, b, r, threshold, toom3, toom4, toom6, Karatsuba.FftThreshold);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] {la}x{lb} threshold={threshold} toom3={toom3} toom4={toom4} toom6={toom6} (iteration {it})");
            }

            // larger sizes, a few, at the default thresholds and at small ones
            for (int it = 0; it < 30; it++)
            {
                int la = rnd.Next(300, 1500), lb = rnd.Next(300, 1500);
                var a = Shaped(la);
                var b = Shaped(lb);
                var r = new ulong[la + lb];
                (int t2, int t3, int t4, int t6) = (it % 8) switch
                {
                    0 => (Karatsuba.Threshold, Karatsuba.Toom3Threshold, Karatsuba.Toom4Threshold, Karatsuba.Toom6Threshold),
                    1 => (4, int.MaxValue, int.MaxValue, int.MaxValue),
                    2 => (4, 12, int.MaxValue, int.MaxValue),
                    3 => (4, 12, 16, int.MaxValue),
                    4 => (Karatsuba.Threshold, 100, 200, int.MaxValue),
                    5 => (Karatsuba.Threshold, int.MaxValue, 100, int.MaxValue),
                    6 => (4, 12, 16, 40),
                    _ => (Karatsuba.Threshold, Karatsuba.Toom3Threshold, Karatsuba.Toom4Threshold, 200),
                };
                Karatsuba.Multiply(core, a, b, r, t2, t3, t4, t6, Karatsuba.FftThreshold);
                Assert.AreEqual(ToBig(a) * ToBig(b), ToBig(r), $"[{coreName}] {la}x{lb} large, threshold={t2} toom3={t3} toom4={t4} toom6={t6}");
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

        /// <summary>
        /// Products modulo β^m - 1 (the cyclic FFT without zero padding, and the folded full
        /// product), against BigInteger: the cyclic path forced from small sizes, operands up to
        /// m words, all-ones operands (the β^m - 1 = 0 representation), m of any K·M shape.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void MultiplyMod_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(4343);
            for (int it = 0; it < 300; it++)
            {
                int minWords = rnd.Next(8, 400);
                int cyclicFrom = rnd.Next(3) == 0 ? int.MaxValue : 8;
                int m = Karatsuba.MulModLength(core, minWords, cyclicFrom);
                Assert.IsTrue(m >= minWords);
                var a = Next(rnd, rnd.Next(1, m + 1));
                var b = Next(rnd, rnd.Next(1, m + 1));
                if (rnd.Next(5) == 0) a.AsSpan().Fill(ulong.MaxValue);
                if (rnd.Next(5) == 0) b.AsSpan().Fill(ulong.MaxValue);
                var r = new ulong[m];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                Karatsuba.MultiplyMod(core, a, b, r, cyclicFrom);
                var mod = (BigInteger.One << (64 * m)) - 1;
                Assert.AreEqual(ToBig(a) * ToBig(b) % mod, ToBig(r), $"[{coreName}] {a.Length}x{b.Length} mod β^{m} - 1 (min {minWords}, cyclic from {cyclicFrom}, iteration {it})");
            }
        }

        /// <summary>
        /// The quotient alone (an approximate quotient with a guard word), against BigInteger:
        /// small thresholds make the approximation recurse on small numbers; long divisors with
        /// short quotients (cut down); exact multiples A = B·Q put the guard word at the boundary
        /// (the fallback to the full division).
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void BurnikelZieglerQuotient_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(5151);
            int[] thresholds = { 2, 3, 4, 5, 8, 16, 33, 96 };

            ulong[] Shaped(int length)
            {
                var w = Next(rnd, length, nonZeroTop: true);
                switch (rnd.Next(5))
                {
                    case 0: w.AsSpan().Fill(ulong.MaxValue); break;
                    case 1: w[0] = (ulong)rnd.Next(1, 16); break;
                }
                return w;
            }

            for (int it = 0; it < 3_000; it++)
            {
                int threshold = thresholds[rnd.Next(thresholds.Length)];
                int lb = rnd.Next(1, 150);
                int la = lb + (rnd.Next(3) == 0 ? rnd.Next(0, 8) : rnd.Next(0, 250));
                var b = Shaped(lb);
                ulong[] a;
                int kind = rnd.Next(6);
                if (kind == 0)
                {
                    // A = B·Q (+ a small remainder): the guard word at the boundary
                    var qv = Shaped(Math.Max(1, la - lb));
                    var prod = ToBig(b) * ToBig(qv) + (rnd.Next(2) == 0 ? BigInteger.Zero : BigInteger.One);
                    a = FromBig(prod);
                }
                else
                {
                    a = Shaped(la);
                    if (kind == 1) b.CopyTo(a, 0);
                }
                if (a.Length < b.Length) continue;
                var q = new ulong[a.Length - b.Length + 1];
                q.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                BurnikelZiegler.Quotient(core, a, b, q, threshold);
                Assert.AreEqual(ToBig(a) / ToBig(b), ToBig(q), $"[{coreName}] {a.Length}/{lb} threshold={threshold} kind={kind} (iteration {it})");
            }
        }

        /// <summary>
        /// Division through the Newton reciprocal, against BigInteger: small reciprocal bases make
        /// the Newton recursion go deep on small numbers; splits give blocks shorter than the
        /// divisor and a short first block. Also the reciprocal alone against the exact floor.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void NewtonDivision_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(7272);
            int[] bases = { 2, 3, 4, 5, 8, 16, 64 };

            ulong[] Shaped(int length)
            {
                var w = Next(rnd, length, nonZeroTop: true);
                switch (rnd.Next(6))
                {
                    case 0:   // all ones
                        w.AsSpan().Fill(ulong.MaxValue);
                        break;
                    case 1:   // a small top word: the divisor needs a large bit shift
                        w[0] = (ulong)rnd.Next(1, 16);
                        break;
                    case 2:   // a power of two times something: the reciprocal near β^s exactly
                        w.AsSpan(1).Clear();
                        w[0] = 1UL << rnd.Next(64);
                        break;
                }
                return w;
            }

            void Check(ulong[] a, ulong[] b, int split, int inverseBase, string ctx, int cyclicFrom = 0)
            {
                var q = new ulong[a.Length - b.Length + 1];
                var r = new ulong[b.Length + 2];
                q.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);   // must be fully overwritten
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                NewtonDivision.DivRem(core, a, b, q, r, split, inverseBase, cyclicFrom);
                var expectedQ = BigInteger.DivRem(ToBig(a), ToBig(b), out var expectedR);
                Assert.AreEqual(expectedQ, ToBig(q), "quotient " + ctx);
                Assert.AreEqual(expectedR, ToBig(r), "remainder " + ctx);
            }

            for (int it = 0; it < 2_000; it++)
            {
                int inverseBase = bases[rnd.Next(bases.Length)];
                int split = rnd.Next(1, 4);
                int lb = rnd.Next(1, 120);
                int la = lb + rnd.Next(0, 300);
                var b = Shaped(lb);
                var a = Shaped(la);
                if (rnd.Next(4) == 0) b.CopyTo(a, 0);   // top of a equal to b
                int cyclicFrom = rnd.Next(2) == 0 ? 0 : rnd.Next(8, 64);   // the products modulo β^m - 1 on small numbers
                Check(a, b, split, inverseBase, $"[{coreName}] {la}/{lb} split={split} base={inverseBase} cyclic={cyclicFrom} (iteration {it})", cyclicFrom);
            }

            for (int it = 0; it < 8; it++)
            {
                int lb = rnd.Next(100, 1500), la = lb + rnd.Next(100, 3000);
                Check(Shaped(la), Shaped(lb), 1 + it % 2, 16, $"[{coreName}] {la}/{lb} large");
            }

            // the sizes the engine sends here, at the default split (automatic) and base
            foreach (var (la, lb) in new[] { (20000, 9000), (30000, 8200) })
            {
                Check(Shaped(la), Shaped(lb), NewtonDivision.Split, NewtonDivision.InverseBase, $"[{coreName}] {la}/{lb} engine sizes");
            }

            // the reciprocal: within a few units of floor(β^(2s) / D)
            for (int it = 0; it < 500; it++)
            {
                int s = rnd.Next(1, 200);
                var d = Shaped(s);
                d[0] |= 1UL << 63;
                var inv = NewtonDivision.Inverse(core, d, bases[rnd.Next(bases.Length)], rnd.Next(2) == 0 ? int.MaxValue : rnd.Next(8, 64));
                var exact = (BigInteger.One << (128 * s)) / ToBig(d);
                var diff = BigInteger.Abs(ToBig(inv) - exact);
                Assert.IsTrue(diff <= 4, $"[{coreName}] inverse s={s}: off by {diff}");
            }
        }

        /// <summary>
        /// Squares (the same span as both operands): the square kernel, Karatsuba / Toom-3 / 4 / 6
        /// levels with one evaluation and the FFT with one transform, against BigInteger. Small
        /// thresholds put every level on small numbers; all-ones and equal halves (a1 - a0 = 0).
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Squares_MatchBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(6262);
            ulong[] Shaped(int length)
            {
                var w = Next(rnd, length);
                switch (rnd.Next(5))
                {
                    case 0: w.AsSpan().Fill(ulong.MaxValue); break;
                    case 1:
                        var h = Next(rnd, (length + 1) / 2);
                        var eq = new ulong[length];
                        h.AsSpan(h.Length - length / 2).CopyTo(eq);
                        h.CopyTo(eq.AsSpan(length / 2));
                        return eq;
                }
                return w;
            }
            for (int it = 0; it < 2_000; it++)
            {
                int n = rnd.Next(1, 260);
                var a = Shaped(n);
                var r = new ulong[2 * n];
                r.AsSpan().Fill(0xDEAD_BEEF_DEAD_BEEFUL);
                int t2 = rnd.Next(2, 20), t3 = rnd.Next(3, 60), t4 = rnd.Next(4, 90), t6 = rnd.Next(6, 120);
                if (rnd.Next(3) == 0) { t3 = t4 = t6 = int.MaxValue; }
                int fft = rnd.Next(4) == 0 ? rnd.Next(8, 64) : Karatsuba.FftThreshold;
                Karatsuba.Multiply(core, a, a, r, t2, t3, t4, t6, fft);
                Assert.AreEqual(ToBig(a) * ToBig(a), ToBig(r), $"[{coreName}] {n}² thresholds {t2}/{t3}/{t4}/{t6}/{fft} (iteration {it})");
            }
            for (int it = 0; it < 60; it++)
            {
                int n = rnd.Next(1, 200);
                var a = Shaped(n);
                var r = new ulong[2 * n];
                Karatsuba.FftMultiply(core, a, a, r, rnd.Next(1, 6));
                Assert.AreEqual(ToBig(a) * ToBig(a), ToBig(r), $"[{coreName}] FFT {n}²");
                core.Sqr(a, r);
                Assert.AreEqual(ToBig(a) * ToBig(a), ToBig(r), $"[{coreName}] Sqr {n}²");
            }
            // the engine: x * x of the same object
            for (int it = 0; it < 40; it++)
            {
                var w = Shaped(rnd.Next(1, 600));
                w[0] |= 1;
                var x = new FullInt64(rnd.Next(2) == 0, w);
                var big = BigInteger.Parse(x.ToString(10));
                Assert.AreEqual((big * big).ToString(), (x * x).ToString(10), $"engine x*x {w.Length} words");
            }
        }

        /// <summary>
        /// ToString of long numbers (the halves divide by the powers of ten through the prepared
        /// reciprocal from DecimalConversion.PreparedFrom words), against BigInteger: random,
        /// all-ones and 10^k ± 1 (long runs of 9 and 0 across the halves).
        /// </summary>
        [TestMethod]
        public void ToString_LongNumbers_MatchBigInteger()
        {
            var rnd = new Random(1919);
            var cases = new List<BigInteger>();
            foreach (int words in new[] { 260, 600, 1500, 3100 })
            {
                cases.Add(ToBig(Next(rnd, words, nonZeroTop: true)));
                var ones = new ulong[words];
                ones.AsSpan().Fill(ulong.MaxValue);
                cases.Add(ToBig(ones));
            }
            foreach (int digits in new[] { 19 * 256, 19 * 512 + 7, 19 * 2048 })
            {
                var p = BigInteger.Pow(10, digits);
                cases.Add(p - 1);
                cases.Add(p);
                cases.Add(p + 1);
            }
            foreach (var big in cases)
            {
                var x = FullInt64.Parse(big.ToString());
                Assert.AreEqual(big.ToString(), x.ToString(10), $"{big.ToString().Length} digits");
            }
        }

        /// <summary>
        /// The exact division (Toom-Cook interpolation): multiples of odd and even d (d = d'·2^t,
        /// d' below 2^32), into another buffer and in place; a non-multiple returns nonzero.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_DivExact_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(3131);
            for (int it = 0; it < 20_000; it++)
            {
                int len = rnd.Next(1, 40);
                ulong d = ((ulong)rnd.Next(1, int.MaxValue) * 2 + 1) >> rnd.Next(0, 20);
                d |= 1;
                d <<= rnd.Next(3) == 0 ? rnd.Next(0, 31) : 0;
                var qv = Next(rnd, len);
                qv[0] >>= 63;                                    // q·d fits len words (d < 2^63)
                var a = new ulong[len];
                var prod = FromBig(ToBig(qv) * d);
                prod.CopyTo(a, len - prod.Length);
                bool inPlace = rnd.Next(2) == 0;
                var q = inPlace ? a : new ulong[len];
                ulong c = core.DivExact(a, d, q);
                Assert.AreEqual(0UL, c, $"[{coreName}] len={len} d={d} carry");
                Assert.AreEqual(ToBig(qv), ToBig(q), $"[{coreName}] len={len} d={d} inPlace={inPlace}");
            }
            // not a multiple: reported
            var odd = new ulong[] { 5, 8 };                     // 5·2^64 + 8 = 1 mod 3 (2^64 = 1 mod 3)
            Assert.AreNotEqual(0UL, core.DivExact(odd, 3, new ulong[2]), $"[{coreName}] non-multiple");
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
