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
            return new BigInteger(bytes, isUnsigned: true);
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
            }
        }
    }
}
