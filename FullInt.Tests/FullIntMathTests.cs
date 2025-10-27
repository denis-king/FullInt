using FullInt.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using FullIntCore = FullInt.Core.FullInt;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;

namespace FullInt.Tests
{
    [TestClass]
    public class FullIntMathTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void Add_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(456L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(579L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_zore_make_negative_ReturnsSum()
        {
            var a = new FullIntCore(false, new byte[] { 0, 0 });
            var b = new FullIntCore(128, toBase.dec);
            var result = FullIntMath.sub(a, b);
            Assert.AreEqual(new FullIntCore(-128, toBase.dec), result);
        }

        [TestMethod]
        public void Add_NegativeNumbers_ReturnsSum()
        {
            var a = new FullIntCore(-100L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(-150L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_PositiveAndNegative_ReturnsDifference()
        {
            var a = new FullIntCore(200L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(150L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_SubtractsCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(40L, toBase.dec);
            var result = FullIntMath.sub(a, b);
            Assert.AreEqual(new FullIntCore(60L, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_MultipliesCorrectly()
        {
            var a = new FullIntCore(12L, toBase.dec);
            var b = new FullIntCore(11L, toBase.dec);
            var result = FullIntMath.mult(a, b);
            Assert.AreEqual(new FullIntCore(132L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_DividesCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(4L, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(25L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Div_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            FullIntMath.div(a, b);
        }

        [TestMethod]
        public void Mod_ReturnsRemainder()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(7L, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(2L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Mod_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            FullIntMath.mod(a, b);
        }

        [TestMethod]
        public void Pow_RaisesToPower()
        {
            var a = new FullIntCore(2L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            var result = FullIntMath.pow(a, b);
            Assert.AreEqual(new FullIntCore(1024L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Positive_ReturnsSame()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var result = FullIntMath.abs(a);
            Assert.AreEqual(new FullIntCore(123L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Negative_ReturnsPositive()
        {
            var a = new FullIntCore(-123L, toBase.dec);
            var result = FullIntMath.abs(a);
            Assert.AreEqual(new FullIntCore(123L, toBase.dec), result);
        }

        [TestMethod]
        public void IsABiggerB_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(10L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(FullIntMath.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerB_A_NotLessThan_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerB_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.IsABiggerB(a, b));

            a = new FullIntCore(10L, toBase.dec);
            b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerOrEqualsB_LessOrEqual_ReturnsTrue()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(FullIntMath.IsABiggerOrEqualsB(a, b));
            Assert.IsTrue(FullIntMath.IsABiggerOrEqualsB(a, new FullIntCore(4L, toBase.dec)));
        }

        [TestMethod]
        public void Je_Equal_ReturnsTrue()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(123L, toBase.dec);
            Assert.IsTrue(FullIntMath.IsEquals(a, b));
        }

        [TestMethod]
        public void Je_NotEqual_ReturnsFalse()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(124L, toBase.dec);
            Assert.IsFalse(FullIntMath.IsEquals(a, b));
        }

        [TestMethod]
        public void ToEqualBase_ConvertsBases()
        {
            var a = new FullIntCore(255L, toBase.hex);
            var b = new FullIntCore(255L, toBase.dec);
            var resultBase = FullIntMath.toEqualBase(a, b);
            Assert.AreEqual(a.Base, b.Base);
            Assert.AreEqual(resultBase, a.Base);
        }

        [TestMethod]
        public void DivMod_Byte_DividesAndReturnsRemainder()
        {
            var a = new FullIntCore(123L, toBase.dec);
            byte b = 10;
            var result = FullIntMath.div_mod(a, b);
            Assert.AreEqual(new FullIntCore(12L, toBase.dec), result[0]);
            Assert.AreEqual(new FullIntCore(3L, toBase.dec), result[1]);
        }

        #region bigger data tests
        [TestMethod]
        public void Jb_Long_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(long.MaxValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue - 1, toBase.dec);
            Assert.IsTrue(FullIntMath.IsABiggerB(a, b));
        }

        [TestMethod]
        public void Jb_Long_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(long.MinValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(FullIntMath.IsABiggerB(a, b));

            a = new FullIntCore(long.MaxValue, toBase.dec);
            b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(FullIntMath.IsABiggerB(a, b));
        }

        [TestMethod]
        public void Add_Long_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(9223372036854775800L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(9223372036854775805L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_into_operand_Long_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(9223372036854775800L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            FullIntMath.add(a, b, a, toBase.dec, false);
            Assert.AreEqual(new FullIntCore(9223372036854775805L, toBase.dec), a);
        }

        [TestMethod]
        public void Test_Add_with_random_Longs()
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[8];
                    rng.GetBytes(buf);
                    long a = BitConverter.ToInt64(buf, 0);

                    rng.GetBytes(buf);
                    long b = BitConverter.ToInt64(buf, 0);

                    var fa = new FullIntCore(a, toBase.dec);
                    var fb = new FullIntCore(b, toBase.dec);

                    fullStopwatch.Start();
                    var sumFull = FullIntMath.add(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = (BigInteger)a + (BigInteger)b;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

                    Assert.AreEqual(expectedFull, sumFull, $"Mismatch for a={a}, b={b}");
                }
                var bigTotal = bigStopwatch.Elapsed;
                var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
                var fullTotal = fullStopwatch.Elapsed;
                var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
                TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
                TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            }
        }

        [TestMethod]
        public void Test_Mult_with_random_Longs()
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();
            
            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[8];
                    rng.GetBytes(buf);
                    long a = BitConverter.ToInt64(buf, 0);

                    rng.GetBytes(buf);
                    long b = BitConverter.ToInt64(buf, 0);

                    var fa = new FullIntCore(a, toBase.dec);
                    var fb = new FullIntCore(b, toBase.dec);
                    
                    fullStopwatch.Start();
                    var multFull = FullIntMath.mult(fa, fb);
                    fullStopwatch.Stop();
                    
                    bigStopwatch.Start();
                    BigInteger expected = (BigInteger)a * (BigInteger)b;
                    bigStopwatch.Stop();
                    
                    FullIntCore expectedFull = expected.ToString();

                    Assert.AreEqual(expectedFull, multFull, $"Mismatch for a={a}, b={b}");
                }
                var bigTotal = bigStopwatch.Elapsed;
                var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
                var fullTotal = fullStopwatch.Elapsed;
                var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
                TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
                TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            }
        }

        [TestMethod]
        public void Test_Div_with_random_Longs()
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();
            
            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[8];
                    rng.GetBytes(buf);
                    long a = BitConverter.ToInt64(buf, 0);

                    rng.GetBytes(buf);
                    long b = BitConverter.ToInt64(buf, 0);

                    var fa = new FullIntCore(a, toBase.dec);
                    var fb = new FullIntCore(b, toBase.dec);
                    TestContext?.WriteLine($"FullInt a={a}, b={b}");
                    fullStopwatch.Start();
                    var divFull = FullIntMath.div(fa, fb);
                    fullStopwatch.Stop();

                    TestContext?.WriteLine($"BigInt a={a}, b={b}");
                    bigStopwatch.Start();
                    BigInteger expected = (BigInteger)a / (BigInteger)b;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

                    Assert.AreEqual(expectedFull, divFull, $"Mismatch for a={a}, b={b}");
                }
                var bigTotal = bigStopwatch.Elapsed;
                var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
                var fullTotal = fullStopwatch.Elapsed;
                var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
                TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
                TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            }
        }

        [TestMethod]
        public void Test_Mod_with_random_Longs()
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[8];
                    rng.GetBytes(buf);
                    long a = BitConverter.ToInt64(buf, 0);

                    rng.GetBytes(buf);
                    long b = BitConverter.ToInt64(buf, 0);

                    var fa = new FullIntCore(a, toBase.dec);
                    var fb = new FullIntCore(b, toBase.dec);

                    fullStopwatch.Start();
                    var divFull = FullIntMath.mod(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = (BigInteger)a % (BigInteger)b;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

                    Assert.AreEqual(expectedFull, divFull, $"Mismatch for a={a}, b={b}");
                }
                var bigTotal = bigStopwatch.Elapsed;
                var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
                var fullTotal = fullStopwatch.Elapsed;
                var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
                TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
                TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            }
        }

        [TestMethod]
        public void Sub_Long_SubtractsCorrectly()
        {
            var a = new FullIntCore(long.MaxValue, toBase.dec);
            var b = new FullIntCore(1L, toBase.dec);
            var result = FullIntMath.sub(a, b);
            Assert.AreEqual(new FullIntCore(long.MaxValue - 1, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_Long_MultipliesCorrectly()
        {
            var a = new FullIntCore(1234567890L, toBase.dec);
            var b = new FullIntCore(987654321L, toBase.dec);
            var result = FullIntMath.mult(a, b);
            Assert.AreEqual(new FullIntCore(1219326311126352690L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_DividesCorrectly()
        {
            long a0 = 9223372036854775806L;
            long b0 = 2L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_mod_Long_DividesCorrectly()
        {
            long a0 = 9223372036854775806L;
            long b0 = 2L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.div_mod(a, (byte)2);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result[0]);
        }

        [TestMethod]
        public void Mod_Long_ReturnsRemainder()
        {
            long a0 = 9223372036854775806L;
            long b0 = 123456789L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_negative_ReturnsRemainder()
        {
            long a0 = -6163651401547877321L;
            long b0 = -8251442412018563718L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_2_ReturnsRemainder()
        {
            long a0 = 5774879304267866471L;
            long b0 = -838346427744435866L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_2_ReturnsRemainder()
        {
            long a0 = 4040031482854394222L;
            long b0 = 711194979896964872L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_3_ReturnsRemainder()
        {
            long a0 =  3951019230310331208L;
            long b0 = -5188172996314720727L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void add_Long_2_ReturnsRemainder()
        {
            long a0 = 5774879304267866471L;
            long b0 = -3353385710977743464L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(a0 + b0, toBase.dec), result);
        }

        #endregion

        #region Speed test
        [TestMethod]
        public void Speed_Pow_2Pow100_LogDuration()
        {
            // Measure performance of FullIntMath.pow for 2^100
            var baseValue = new FullIntCore(2L, toBase.dec);
            var exponent = new FullIntCore(10000L, toBase.dec);

            const int iterations = 100;
            var sw = new Stopwatch();
            FullIntCore lastResult = null;

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = FullIntMath.pow(baseValue, exponent);
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Pow_2Pow100: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Pow_2Pow100: iterations = 100, total = 8851,9205, avg = 88,5192
            Assert.IsNotNull(lastResult);
        }

        [TestMethod]
        public void Speed_Div_bigvalue_LogDuration()
        {
            // Measure performance of FullIntMath.div for division involving long.MaxValue
            FullIntCore lastResult = FullIntMath.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = FullIntMath.div(lastResult, 2);
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(FullIntMath.pow(2, 9900), lastResult);
        }

        [TestMethod]
        public void Speed_Div_bigvalue_to_byte_LogDuration()
        {
            // Measure performance of FullIntMath.div for division involving long.MaxValue
            FullIntCore lastResult = FullIntMath.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = FullIntMath.div_mod(lastResult, (byte)2)[0];
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(FullIntMath.pow(2, 9900), lastResult);
        }

        [TestMethod]
        public void Speed_Div_bigvalue_to_bigger_one_LogDuration()
        {
            // Measure performance of FullIntMath.div for division involving long.MaxValue
            FullIntCore lastResult = FullIntMath.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = FullIntMath.div(lastResult, 1024);
                sw.Stop();
                var expResult = FullIntMath.pow(2, 10000 - ((i + 1) * 10));
                Assert.AreEqual(expResult, lastResult);
                sw.Start();
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(FullIntMath.pow(2, 9000), lastResult);
        }
        #endregion
    }
}