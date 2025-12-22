using FullInt.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using FullIntCore = FullInt.Core.FullInt64;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;

namespace FullInt.Tests
{
    [TestClass]
    public class FullInt64MathTests
    {
        public TestContext TestContext { get; set; }
		private static FullInt64Engine _engine;
		[ClassInitialize]
		public static void InitClass(TestContext testContext)
		{
			_engine = FullInt64Engine.Instance;
			
			//todo warm up operations
		}
        [TestMethod]
        public void Add_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(456L, toBase.dec);
            var result = _engine.add(a, b);
            Assert.AreEqual(new FullIntCore(579L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_zore_make_negative_ReturnsSum()
        {
            var a = new FullIntCore(false, new ulong[] { 0, 0 });
            var b = new FullIntCore(128, toBase.dec);
            var result = _engine.sub(a, b);
            Assert.AreEqual(new FullIntCore(-128, toBase.dec), result);
        }

        [TestMethod]
        public void Add_NegativeNumbers_ReturnsSum()
        {
            var a = new FullIntCore(-100L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = _engine.add(a, b);
            Assert.AreEqual(new FullIntCore(-150L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_PositiveAndNegative_ReturnsDifference()
        {
            var a = new FullIntCore(200L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = _engine.add(a, b);
            Assert.AreEqual(new FullIntCore(150L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_SubtractsCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(40L, toBase.dec);
            var result = _engine.sub(a, b);
            Assert.AreEqual(new FullIntCore(60L, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_MultipliesCorrectly()
        {
            var a = new FullIntCore(12L, toBase.dec);
            var b = new FullIntCore(11L, toBase.dec);
            var result = _engine.mult(a, b);
            Assert.AreEqual(new FullIntCore(132L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_DividesCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(4L, toBase.dec);
            var result = _engine.div(a, b);
            Assert.AreEqual(new FullIntCore(25L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Div_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            _engine.div(a, b);
        }

        [TestMethod]
        public void Mod_ReturnsRemainder()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(7L, toBase.dec);
            var result = _engine.mod(a, b);
            Assert.AreEqual(new FullIntCore(2L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Mod_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            _engine.mod(a, b);
        }

        [TestMethod]
        public void Pow_RaisesToPower()
        {
            var a = new FullIntCore(2L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            var result = _engine.pow(a, b);
            Assert.AreEqual(new FullIntCore(1024L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Positive_ReturnsSame()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var result = _engine.abs(a);
            Assert.AreEqual(new FullIntCore(123L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Negative_ReturnsPositive()
        {
            var a = new FullIntCore(-123L, toBase.dec);
            var result = _engine.abs(a);
            var expected = new FullIntCore(123L, toBase.dec);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void IsABiggerB_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(10L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(_engine.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerB_A_NotLessThan_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(_engine.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerB_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(_engine.IsABiggerB(a, b));

            a = new FullIntCore(10L, toBase.dec);
            b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(_engine.IsABiggerB(a, b));
        }

        [TestMethod]
        public void IsABiggerOrEqualsB_LessOrEqual_ReturnsTrue()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(_engine.IsABiggerOrEqualsB(a, b));
            Assert.IsTrue(_engine.IsABiggerOrEqualsB(a, new FullIntCore(4L, toBase.dec)));
        }

        [TestMethod]
        public void Je_Equal_ReturnsTrue()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(123L, toBase.dec);
            Assert.IsTrue(_engine.IsEquals(a, b));
        }

        [TestMethod]
        public void Je_NotEqual_ReturnsFalse()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(124L, toBase.dec);
            Assert.IsFalse(_engine.IsEquals(a, b));
        }

        [TestMethod]
        public void ToEqualBase_ConvertsBases()
        {
            var a = new FullIntCore(255L, toBase.hex);
            var b = new FullIntCore(255L, toBase.dec);
            var resultBase = _engine.toEqualBase(ref a, ref b);
            Assert.AreEqual(a.IsDec, b.IsDec);
            Assert.AreEqual(resultBase == toBase.dec, a.IsDec);
        }

        [TestMethod]
        public void DivMod_Byte_DividesAndReturnsRemainder()
        {
            var a = new FullIntCore(123L, toBase.dec);
            ulong b = 10;
            ulong remainder;
            var result = _engine.div(a, b, out remainder);
            Assert.AreEqual(new FullIntCore(12L, toBase.dec), result);
            Assert.AreEqual(new FullIntCore(3L, toBase.dec), new FullIntCore( remainder, toBase.dec));
        }

        #region bigger data tests
        [TestMethod]
        public void Jb_Long_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(long.MaxValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue - 1, toBase.dec);
            Assert.IsTrue(_engine.IsABiggerB(a, b));
        }

        [TestMethod]
        public void Jb_Long_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(long.MinValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(_engine.IsABiggerB(a, b));

            a = new FullIntCore(long.MaxValue, toBase.dec);
            b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(_engine.IsABiggerB(a, b));
        }

        [TestMethod]
        public void Add_Long_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(9223372036854775800L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            var result = _engine.add(a, b);
            Assert.AreEqual(new FullIntCore(9223372036854775805L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_into_operand_Long_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(9223372036854775800L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            _engine.add(a, b, a, toBase.dec, false);
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
                    var sumFull = _engine.add(fa, fb);
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
            
            const int iterations = 100;
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
                    var multFull = _engine.mult(fa, fb);
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
                    var divFull = _engine.div(fa, fb);
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
                    var divFull = _engine.mod(fa, fb);
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
            var result = _engine.sub(a, b);
            Assert.AreEqual(new FullIntCore(long.MaxValue - 1, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_Long_MultipliesCorrectly()
        {
            var a = new FullIntCore(1234567890L, toBase.dec);
            var b = new FullIntCore(987654321L, toBase.dec);
            var result = _engine.mult(a, b);
            Assert.AreEqual(new FullIntCore(1219326311126352690L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_DividesCorrectly()
        {
            long a0 = 9223372036854775806L;
            long b0 = 2L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_mod_Long_DividesCorrectly()
        {
            long a0 = 9223372036854775806L;
            long b0 = 2L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.div(a, (ulong)2, out _);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_ReturnsRemainder()
        {
            long a0 = 9223372036854775806L;
            long b0 = 123456789L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_negative_ReturnsRemainder()
        {
            long a0 = -6163651401547877321L;
            long b0 = -8251442412018563718L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_2_ReturnsRemainder()
        {
            long a0 = 5774879304267866471L;
            long b0 = -838346427744435866L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.div(a, b);
            Assert.AreEqual(new FullIntCore(a0 / b0, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_2_ReturnsRemainder()
        {
            long a0 = 4040031482854394222L;
            long b0 = 711194979896964872L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_3_ReturnsRemainder()
        {
            long a0 =  3951019230310331208L;
            long b0 = -5188172996314720727L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.mod(a, b);
            Assert.AreEqual(new FullIntCore(a0 % b0, toBase.dec), result);
        }

        [TestMethod]
        public void add_Long_2_ReturnsRemainder()
        {
            long a0 = 5774879304267866471L;
            long b0 = -3353385710977743464L;
            var a = new FullIntCore(a0, toBase.dec);
            var b = new FullIntCore(b0, toBase.dec);
            var result = _engine.add(a, b);
            Assert.AreEqual(new FullIntCore(a0 + b0, toBase.dec), result);
        }

        #endregion

        #region Speed test
        [TestMethod]
        public void Speed_Pow_2Pow100_LogDuration()
        {
            // Measure performance of FullInt64Engine.pow for 2^100
            var baseValue = new FullIntCore(2L, toBase.dec);
            var exponent = new FullIntCore(10000L, toBase.dec);

            const int iterations = 100;
            var sw = new Stopwatch();
            FullIntCore lastResult = null;

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = _engine.pow(baseValue, exponent);
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
            // Measure performance of FullInt64Engine.div for division involving long.MaxValue
            FullIntCore lastResult = _engine.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = _engine.div(lastResult, 2);
                sw.Stop();
                Assert.AreEqual(_engine.pow(2, 10000 - (i + 1)), lastResult, $" 2^ {10000 - (i + 1)}");
                sw.Start();
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(_engine.pow(2, 9900), lastResult);
        }

        [TestMethod]
        public void Speed_Div_bigvalue_to_byte_LogDuration()
        {
            // Measure performance of FullInt64Engine.div for division involving long.MaxValue
            FullIntCore lastResult = _engine.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = _engine.div(lastResult, (ulong)2, out _);
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(_engine.pow(2, 9900), lastResult);
        }

        [TestMethod]
        public void Speed_Div_bigvalue_to_bigger_one_LogDuration()
        {
            // Measure performance of FullInt64Engine.div for division involving long.MaxValue
            FullIntCore lastResult = _engine.pow(2, 10000);

            const int iterations = 100;
            var sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                lastResult = _engine.div(lastResult, 1024);
                sw.Stop();
                var expResult = _engine.pow(2, 10000 - ((i + 1) * 10));
                Assert.AreEqual(expResult, lastResult);
                sw.Start();
            }
            sw.Stop();

            var total = sw.Elapsed;
            var avg = TimeSpan.FromTicks(total.Ticks / iterations);

            TestContext?.WriteLine($"Speed_Div_MaxLong: iterations={iterations}, total={total.TotalMilliseconds}, avg={avg.TotalMilliseconds}");
            //Speed_Div_MaxLong: iterations = 100, total = 883,7553, avg = 8,8375
            Assert.AreEqual(_engine.pow(2, 9000), lastResult);
        }
        #endregion

        // Assert.AreEqual failed.Expected:<-1465223086307108019104641070397715380>. Actual:<-179104641070397715380>. Mismatch for a= 414846724483320771, b= -3531962529370334780
        [TestMethod]
        public void Div_Mult_bigvalue2_to_bigger_one_LogDuration()
        {
            //var buf = new byte[8];
            //rng.GetBytes(buf);
            long a = 414846724483320771;

            //rng.GetBytes(buf);
            long b = -3531962529370334780;

            var fa = new FullIntCore(a, toBase.hex);
            var fb = new FullIntCore(b, toBase.hex);

            //fullStopwatch.Start();
            var multFull = _engine.mult(fa, fb);
           // fullStopwatch.Stop();

            //bigStopwatch.Start();
            BigInteger expected = (BigInteger)a * (BigInteger)b;
            //bigStopwatch.Stop();

            FullIntCore expectedFull = expected.ToString();

            Assert.AreEqual(expectedFull, multFull, $"Mismatch for a={a}, b={b}");
        }
    }
}