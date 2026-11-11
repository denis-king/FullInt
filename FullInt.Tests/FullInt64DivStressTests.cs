using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using FullInt.Core;

namespace FullInt.Tests
{
    /// <summary>
    /// Deterministic stress tests for multi-word division.
    /// Every case is checked against System.Numerics.BigInteger (quotient and remainder).
    /// The seed is fixed so a failure can always be replayed.
    /// </summary>
    [TestClass]
    public class FullInt64DivStressTests
    {
        public TestContext TestContext { get; set; } = null!;

        public static IEnumerable<object[]> Cores()
        {
            yield return new object[] { "native" };
            yield return new object[] { "managed" };
        }

        private static IFullIntCore GetCore(string name) =>
            name == "managed" ? new FullInt64CoreDefault() : FullIntCoreFactory.Create();

        private static readonly ulong[] SpecialWords =
        {
            0UL, 1UL, 2UL, ulong.MaxValue, ulong.MaxValue - 1,
            0x8000_0000_0000_0000UL, 0x7FFF_FFFF_FFFF_FFFFUL, 0x8000_0000_0000_0001UL,
            0xFFFF_FFFF_0000_0000UL, 0x0000_0000_FFFF_FFFFUL,
        };

        // ---------------------------------------------------------------- helpers

        /// <summary>MSW-first limbs → BigInteger (unsigned).</summary>
        private static BigInteger ToBig(ReadOnlySpan<ulong> limbs)
        {
            var bytes = new byte[limbs.Length * 8];
            for (int i = 0; i < limbs.Length; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan((limbs.Length - 1 - i) * 8), limbs[i]);
            return new BigInteger(bytes, isUnsigned: true);
        }

        private static ulong NextWord(Random rnd)
        {
            switch (rnd.Next(8))
            {
                case 0: return SpecialWords[rnd.Next(SpecialWords.Length)];
                case 1: return (ulong)rnd.NextInt64() >> rnd.Next(64);          // small
                case 2: return ulong.MaxValue - ((ulong)rnd.NextInt64() >> rnd.Next(64)); // near max
                default: return (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 63);
            }
        }

        private static ulong[] NextNumber(Random rnd, int length, bool nonZeroTop)
        {
            var r = new ulong[length];
            for (int i = 0; i < length; i++) r[i] = NextWord(rnd);
            if (nonZeroTop && r[0] == 0) r[0] = 1UL + ((ulong)rnd.NextInt64() >> rnd.Next(64));
            return r;
        }

        /// <summary>Builds a dividend that forces the q̂ estimate into hard territory:
        /// A = D·Q + R with Q digits near 2^64-1 and R close to D.</summary>
        private static ulong[] HardDividend(Random rnd, ulong[] d, int extraWords)
        {
            var D = ToBig(d);
            var q = BigInteger.Zero;
            for (int i = 0; i <= extraWords; i++)
                q = (q << 64) + new BigInteger(rnd.Next(2) == 0 ? ulong.MaxValue - (ulong)rnd.Next(4) : NextWord(rnd));
            var r = D - 1 - new BigInteger((ulong)rnd.Next(1000));
            if (r.Sign < 0) r = 0;
            var a = D * q + r;
            var bytes = a.ToByteArray(isUnsigned: true, isBigEndian: true);
            int words = (bytes.Length + 7) / 8;
            var padded = new byte[words * 8];
            bytes.CopyTo(padded, padded.Length - bytes.Length);
            var limbs = new ulong[words];
            for (int i = 0; i < words; i++) limbs[i] = BinaryPrimitives.ReadUInt64BigEndian(padded.AsSpan(i * 8));
            return limbs;
        }

        private static string Hex(ReadOnlySpan<ulong> limbs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < limbs.Length; i++) sb.Append(i == 0 ? "" : ", ").Append("0x").Append(limbs[i].ToString("X16"));
            return sb.Append('}').ToString();
        }

        /// <summary>Runs one core division and returns a failure description, or null if correct.</summary>
        private static string? CheckCoreDiv(IFullIntCore core, ulong[] a, ulong[] d)
        {
            int qLen = Math.Max(a.Length - d.Length + 1, 1);
            var q = new ulong[qLen];
            var r = new ulong[d.Length + 2];

            core.Div(a, d, q, r);

            var A = ToBig(a);
            var D = ToBig(d);
            var expQ = BigInteger.DivRem(A, D, out var expR);
            var gotQ = ToBig(q);
            var gotR = ToBig(r);

            if (gotQ == expQ && gotR == expR) return null;

            return $"a={Hex(a)}\n d={Hex(d)}\n expected q={expQ:X} r={expR:X}\n actual   q={gotQ:X} r={gotR:X}\n q diff (expected-actual)={expQ - gotQ}";
        }

        private void RunCoreStress(IFullIntCore core, int seed, int iterations, Func<Random, (ulong[] a, ulong[] d)> generate)
        {
            var rnd = new Random(seed);
            int failures = 0;
            string? first = null;

            for (int i = 0; i < iterations; i++)
            {
                var (a, d) = generate(rnd);
                var failure = CheckCoreDiv(core, a, d);
                if (failure != null)
                {
                    failures++;
                    first ??= $"iteration {i}:\n {failure}";
                }
            }

            TestContext.WriteLine($"seed={seed} iterations={iterations} failures={failures}");
            Assert.AreEqual(0, failures, $"{failures} of {iterations} divisions wrong (seed {seed}). First:\n{first}");
        }

        // ---------------------------------------------------------------- core tests

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        [TestCategory("Stress")]
        public void Div_Core_RandomPatterns_MatchesBigInteger(string coreName)
        {
            RunCoreStress(GetCore(coreName), seed: 20261111, iterations: 50_000, rnd =>
            {
                int n = rnd.Next(1, 9);
                int m = n + rnd.Next(0, 9);
                return (NextNumber(rnd, m, nonZeroTop: false), NextNumber(rnd, n, nonZeroTop: true));
            });
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        [TestCategory("Stress")]
        public void Div_Core_HardQuotientDigits_MatchesBigInteger(string coreName)
        {
            RunCoreStress(GetCore(coreName), seed: 424242, iterations: 50_000, rnd =>
            {
                int n = rnd.Next(2, 9);
                var d = NextNumber(rnd, n, nonZeroTop: true);
                return (HardDividend(rnd, d, rnd.Next(0, 6)), d);
            });
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        [TestCategory("Stress")]
        public void Div_Core_DividendShorterThanDivisor_ReturnsZeroAndDividend(string coreName)
        {
            RunCoreStress(GetCore(coreName), seed: 7, iterations: 5_000, rnd =>
            {
                int n = rnd.Next(2, 9);
                int m = rnd.Next(1, n);
                return (NextNumber(rnd, m, nonZeroTop: true), NextNumber(rnd, n, nonZeroTop: true));
            });
        }

        // ---------------------------------------------------------------- regressions (FullInt.Tests/Fails.txt)

        [DataTestMethod]
        [DataRow(
            "5297008637796872684807718012049388472945157228119195843860303303762468611777089665605347908521928964036413985072299068231110837974825585286535767082110378",
            "169168404980211691586568718211847366102753514476907728969382528648070582312244853114883267240390411269157605683393976790697842000739396220947")]
        [DataRow(
            "-67903283695125838965360252684552717737",
            "879376197509531337395492")]
        public void Div_Engine_KnownFailures_MatchesBigInteger(string a, string b)
        {
            FullInt64 fa = a;
            FullInt64 fb = b;
            var ba = BigInteger.Parse(a);
            var bb = BigInteger.Parse(b);

            Assert.AreEqual((ba / bb).ToString(), (fa / fb).ToString(10), "quotient");
            Assert.AreEqual((ba % bb).ToString(), (fa % fb).ToString(10), "remainder");
        }
    }
}
