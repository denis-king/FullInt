using System.Buffers.Binary;
using System.Numerics;
using FullNumerics;

namespace FullNumerics.Tests
{
    /// <summary>
    /// Bitwise operations (&amp;, |, xor, ~, &lt;&lt;, &gt;&gt;) checked against System.Numerics.BigInteger,
    /// for signed values (two's complement semantics). Fixed seeds, so failures replay.
    /// Core-level tests run on both the native (asm) core and the managed fallback.
    /// </summary>
    [TestClass]
    public class FullIntBitwiseTests
    {
        private static readonly ulong[] SpecialWords =
        {
            0UL, 1UL, ulong.MaxValue, ulong.MaxValue - 1, 0x8000_0000_0000_0000UL, 0x7FFF_FFFF_FFFF_FFFFUL,
        };

        // ---------------------------------------------------------------- helpers

        private static ulong[] ToLimbs(BigInteger magnitude)
        {
            var bytes = BigCompat.ToUnsignedBigEndian(magnitude);
            int words = Math.Max((bytes.Length + 7) / 8, 1);
            var padded = new byte[words * 8];
            bytes.CopyTo(padded, padded.Length - bytes.Length);
            var limbs = new ulong[words];
            for (int i = 0; i < words; i++) limbs[i] = BinaryPrimitives.ReadUInt64BigEndian(padded.AsSpan(i * 8));
            return limbs;
        }

        private static BigInteger FromLimbs(ReadOnlySpan<ulong> limbs)
        {
            var bytes = new byte[limbs.Length * 8];
            for (int i = 0; i < limbs.Length; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan((limbs.Length - 1 - i) * 8), limbs[i]);
            return BigCompat.FromUnsignedLittleEndian(bytes);
        }

        private static FullInt ToFull(BigInteger v) => new FullInt(v.Sign < 0, ToLimbs(BigInteger.Abs(v)));

        private static BigInteger NextValue(Random rnd)
        {
            int words = rnd.Next(1, 6);
            var limbs = new ulong[words];
            for (int i = 0; i < words; i++)
                limbs[i] = rnd.Next(3) == 0 ? SpecialWords[rnd.Next(SpecialWords.Length)] : (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 63);
            var v = FromLimbs(limbs);
            return rnd.Next(2) == 0 ? -v : v;
        }

        public static IEnumerable<object[]> Cores()
        {
            yield return new object[] { "native" };
            yield return new object[] { "managed" };
        }

        private static IFullIntCore GetCore(string name) =>
            name == "managed" ? new FullIntCoreManaged() : FullIntCoreFactory.Create();

        // ---------------------------------------------------------------- core level (both cores)

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_AndOrXor_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(1111);
            for (int i = 0; i < 20_000; i++)
            {
                var x = NextValue(rnd);
                var y = NextValue(rnd);
                var a = ToLimbs(BigInteger.Abs(x));
                var b = ToLimbs(BigInteger.Abs(y));

                for (int op = 0; op < 3; op++)
                {
                    var r = new ulong[Math.Max(a.Length, b.Length) + 1];
                    int neg = op == 0 ? core.And(a, x.Sign < 0, b, y.Sign < 0, r)
                            : op == 1 ? core.Or(a, x.Sign < 0, b, y.Sign < 0, r)
                            : core.Xor(a, x.Sign < 0, b, y.Sign < 0, r);
                    var got = neg != 0 ? -FromLimbs(r) : FromLimbs(r);
                    var expected = op == 0 ? x & y : op == 1 ? x | y : x ^ y;
                    Assert.AreEqual(expected, got, $"[{coreName}] op={"&|^"[op]} x={x} y={y} (iteration {i})");
                }
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(Cores), DynamicDataSourceType.Method)]
        public void Core_Shifts_MatchesBigInteger(string coreName)
        {
            var core = GetCore(coreName);
            var rnd = new Random(2222);
            for (int i = 0; i < 20_000; i++)
            {
                var x = BigInteger.Abs(NextValue(rnd));
                int shift = rnd.Next(3) == 0 ? rnd.Next(0, 4) * 64 + rnd.Next(-1, 2) : rnd.Next(0, 400);
                if (shift < 0) shift = 0;
                var a = ToLimbs(x);

                var rl = new ulong[a.Length + shift / 64 + 1 + rnd.Next(0, 2)]; // sometimes one extra lead word
                core.ShiftLeft(a, shift, rl);
                Assert.AreEqual(x << shift, FromLimbs(rl), $"[{coreName}] {x} << {shift}");

                var rr = new ulong[Math.Max(a.Length - shift / 64, 1)];
                core.ShiftRight(a, shift, rr);
                Assert.AreEqual(x >> shift, FromLimbs(rr), $"[{coreName}] {x} >> {shift}");
            }
        }

        [TestMethod]
        public void Core_ShiftRight_InPlace()
        {
            var core = FullIntCoreFactory.Create();
            var x = BigInteger.Parse("123456789012345678901234567890123456789012345678901234567890");
            var a = ToLimbs(x);
            core.ShiftRight(a, 70, a.AsSpan(0, a.Length - 1));
            Assert.AreEqual(x >> 70, FromLimbs(a.AsSpan(0, a.Length - 1)));
        }

        // ---------------------------------------------------------------- operators (engine, native core)

        [TestMethod]
        public void Operators_MatchBigInteger()
        {
            var rnd = new Random(3333);
            for (int i = 0; i < 5_000; i++)
            {
                var x = NextValue(rnd);
                var y = NextValue(rnd);
                int shift = rnd.Next(0, 300);
                FullInt fx = ToFull(x), fy = ToFull(y);

                Assert.AreEqual((x & y).ToString(), (fx & fy).ToString(10), $"{x} & {y}");
                Assert.AreEqual((x | y).ToString(), (fx | fy).ToString(10), $"{x} | {y}");
                Assert.AreEqual((x ^ y).ToString(), (fx ^ fy).ToString(10), $"{x} xor {y}");
                Assert.AreEqual((~x).ToString(), (~fx).ToString(10), $"~{x}");
                Assert.AreEqual((x << shift).ToString(), (fx << shift).ToString(10), $"{x} << {shift}");
                Assert.AreEqual(BigCompat.ShiftRightFloor(x, shift).ToString(), (fx >> shift).ToString(10), $"{x} >> {shift}");
            }
        }

        [TestMethod]
        public void Operators_EdgeCases()
        {
            FullInt zero = 0, one = 1, minusOne = -1;

            Assert.AreEqual("0", (zero & minusOne).ToString(10));
            Assert.AreEqual("-1", (zero | minusOne).ToString(10));
            Assert.AreEqual("0", (minusOne ^ minusOne).ToString(10));
            Assert.AreEqual("-1", (~zero).ToString(10));
            Assert.AreEqual("0", (~minusOne).ToString(10));
            Assert.AreEqual("-1", (minusOne >> 1000).ToString(10), "negative >> large shift is -1");
            Assert.AreEqual("0", (one >> 1000).ToString(10));
            Assert.AreEqual("-2", (minusOne << 1).ToString(10));
            Assert.AreEqual("2", (one << 1).ToString(10));
            Assert.AreEqual("1", (2 >> 1 == 1 ? one : zero).ToString(10));
            Assert.AreEqual((BigInteger.One << 64).ToString(), (one << 64).ToString(10));
            Assert.AreEqual("4", (one << -(-2)).ToString(10));
            Assert.AreEqual("1", ((FullInt)4 << -2).ToString(10), "negative shift = shift the other way");
        }
    }
}
