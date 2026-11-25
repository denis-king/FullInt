using FullInt.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Linq;
using FullIntCore = FullInt.Core.FullInt64;

namespace FullInt.Tests
{
    [TestClass]
    public class FullIntBigTests
    {
        public TestContext TestContext { get; set; }
        private static FullInt64Engine _engine;
		[ClassInitialize]
		public static void InitClass(TestContext testContext)
		{
			_engine = FullInt64Engine.Instance;
			
			//todo warm up operations
		}
		
        public static (ulong[] buffer, bool isNegative) ParseBytesWithSingToUnsingUlongs(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return (Array.Empty<ulong>(), false);

            // Определение знака по старшему биту последнего байта (т.к. little-endian)
            bool isNegative = (bytes[bytes.Length - 1] & 0x80) != 0;

            byte[] workingBytes = new byte[(bytes.Length / 8 + (bytes.Length % 8 == 0 ? 0 : 1)) * 8];
            Buffer.BlockCopy(Enumerable.Reverse(bytes).ToArray(), 0, workingBytes, workingBytes.Length - bytes.Length, bytes.Length);

            if (isNegative)
            {
                // Инверсия всех байт
                for (int i = 0; i < workingBytes.Length; i++)
                    workingBytes[i] = (byte)~workingBytes[i];

                // Прибавление 1 (two's complement)
                int carry = 1;
                for (int i = 0; i < workingBytes.Length && carry > 0; i++)
                {
                    int sum = workingBytes[workingBytes.Length - 1 - i] + carry;
                    workingBytes[workingBytes.Length - 1 - i] = (byte)sum;
                    carry = sum >> 8;
                }
            }

            // Преобразование в ulong[]

            ulong[] result = workingBytes
                .Chunk(8)
                .Select(x => BitConverter.ToUInt64(Enumerable.Reverse(x).ToArray(), 0))
                .ToArray();

            
            return (result, isNegative);
        }
        [TestMethod]
        public void BigTestHex_128Bit_Add_with_random_Longs() 
        {
            BigTest_Add_with_random_Longs(16);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(32);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(64);
        }

        [TestMethod]
        public void BigTestHex_1024Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(128);
        }

        [TestMethod]
        public void BigTestHex_2048Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(256);
        }

        public void BigTest_Add_with_random_Longs(int byteLength)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = TestRng.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf);
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf);
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var sumFull = _engine.add(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba + bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = (FullIntCore)expected.ToString();

                    Assert.AreEqual(expectedFull.ToString(10), sumFull.ToString(10), $"Mismatch for a={ba}, b={bb}");
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
        public void BigTestHex_128Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(16);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(32);
        }

        [TestMethod]
        public void BigTestHex_512it_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(64);
        }

        [TestMethod]
        public void BigTestHex_1024Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(128);
        }

        [TestMethod]
        public void BigTestHex_2048Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(256);
        }

       
        public void BigTest_Mult_with_random_Longs(int byteLength)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 100;
            using (var rng = TestRng.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];
                    
                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf);
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf);
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var multFull = _engine.mult(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba * bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = (FullIntCore)expected.ToString();

                   
                    Assert.AreEqual(expectedFull.ToString(10), multFull.ToString(10), $"Mismatch for a={ba}, b={bb}");
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
        public void BigTestHex_128Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(16);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(32);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(64);
        }

        [TestMethod]
        public void BigTestHex_1024Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(128);
        }

        [TestMethod]
        public void BigTestHex_2048Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(256);
        }

        public void BigTest_Div_with_random_Longs(int byteLength)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();
            
            const int iterations = 100;
            var fullintA = new FullIntCore[iterations];
            var fullintB = new FullIntCore[iterations];
            var fullintC = new FullIntCore[iterations];

            var bigingA = new BigInteger[iterations];
            var bigingB = new BigInteger[iterations];
            var bigingC = new BigInteger[iterations];

            using (var rng = TestRng.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf);
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    buf[buf.Length - 1] = 0; //to have a less divisor than dividend
                    buf[buf.Length - 2] = 0;
                    buf[buf.Length - 3] = 0;
                    buf[buf.Length - 4] = 0;
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf);
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullintA[i] = fa;
                    fullintB[i] = fb;

                    bigingA[i] = ba;
                    bigingB[i] = bb;

                }
            }
            bigStopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
                bigingC[i] = bigingA[i] / bigingB[i];
            }
            bigStopwatch.Stop();
            Span<ulong> resultBuf = stackalloc ulong[1];
            Span<ulong> remainderBuf = stackalloc ulong[byteLength/8+2];
            Span<ulong> zero = stackalloc ulong[] { 0UL };
            fullStopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
               // Span<ulong> remainderBuf = stackalloc ulong[value2.value.Length + 2]; // + 2 it uses internal calucation where remainder can be greate than the value 2



               
                  

                _engine.div_internal(fullintA[i].value, fullintB[i].value, resultBuf, remainderBuf);
                var sign = fullintA[i].Sing != fullintB[i].Sing;

                //propably ussless if value1 >= value2. remove when uncommented the optimalization above
                if (_engine.CompareAbsInternal(resultBuf, zero) == 0)
                {
                    sign = false;
                }
                fullintC[i] = new FullInt64(sign, FullHelper.DeleteZeros(resultBuf).ToArray());
                for (int j = 0; j < remainderBuf.Length; j++) remainderBuf[j] = 0;
                //remainder = new FullInt64(value1.Sing, deleteZeros(remainderBuf).ToArray(), Base);

                //return result;
                //fullintC[i] = _engine.div(fullintA[i], fullintB[i]);
            }
            fullStopwatch.Stop();

            for (int i = 0; i < iterations; i++)
            {

                FullIntCore expectedFull = (FullIntCore)bigingC[i].ToString();
                Assert.AreEqual(expectedFull.ToString(10), fullintC[i].ToString(10), $"Mismatch for a={bigingA[i]}, b={bigingB[i]}");
            }

            var bigTotal = bigStopwatch.Elapsed;
            var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
            var fullTotal = fullStopwatch.Elapsed;
            var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
            TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
            TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            
        }

        [TestMethod]
        public void BigTestHex_128Bit_Mod_with_random_Longs() 
        {
            BigTest_Mod_with_random_Longs(16);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(32);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(64);
        }

        [TestMethod]
        public void BigTestHex_1024Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(128);
        }

        [TestMethod]
        public void BigTestHex_2048Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(256);
        }

        public void BigTest_Mod_with_random_Longs(int byteLength)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = TestRng.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf);
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf);
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var divFull = _engine.mod(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba % bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = (FullIntCore)expected.ToString();

                    Assert.AreEqual(expectedFull.ToString(10), divFull.ToString(10), $"Mismatch for a={ba}, b={bb}");
                }
                var bigTotal = bigStopwatch.Elapsed;
                var bigAvg = TimeSpan.FromTicks(bigTotal.Ticks / iterations);
                var fullTotal = fullStopwatch.Elapsed;
                var fullAvg = TimeSpan.FromTicks(fullTotal.Ticks / iterations);
                TestContext?.WriteLine($"Speed for BigInt: iterations={iterations}, total={bigTotal}, avg={bigAvg.TotalMilliseconds}");
                TestContext?.WriteLine($"Speed for FullInt: iterations={iterations}, total={fullTotal}, avg={fullAvg.TotalMilliseconds}");
            }
        }

    }
}