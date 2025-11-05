using FullInt.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using FullIntCore = FullInt.Core.FullInt64;

namespace FullInt.Tests
{
    [TestClass]
    public class FullIntBigTests
    {
        public TestContext TestContext { get; set; }
        //private void GenerateBigNumber(int bytesCount, out ulong[] longBuf, out byte[] byteBuf) 
        //{
           
        //}

        public static (ulong[] buffer, bool isNegative) ParseBytesWithSingToUnsingUlongs(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return (Array.Empty<ulong>(), false);

            // Определение знака по старшему биту последнего байта (т.к. little-endian)
            bool isNegative = (bytes[^1] & 0x80) != 0;

            byte[] workingBytes = new byte[(bytes.Length / 8 + (bytes.Length % 8 == 0 ? 0 : 1)) * 8];
            Buffer.BlockCopy(bytes.Reverse().ToArray(), 0, workingBytes, workingBytes.Length - bytes.Length, bytes.Length);

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
                .Select(x => BitConverter.ToUInt64(x.Reverse().ToArray()))
                .ToArray();

            
            return (result, isNegative);
        }
        [TestMethod]
        public void BigTestHex_128Bit_Add_with_random_Longs() 
        {
            BigTest_Add_with_random_Longs(16, false);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(32, false);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(64, false);
        }

        [TestMethod]
        public void BigTestDec_128Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(16, true);
        }

        [TestMethod]
        public void BigTestDec_256Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(32, true);
        }

        [TestMethod]
        public void BigTestDec_512Bit_Add_with_random_Longs()
        {
            BigTest_Add_with_random_Longs(64, true);
        }

        public void BigTest_Add_with_random_Longs(int byteLength, bool isDec)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fa = FullIntConvertor.ConvertToBase(fa, toBase.dec);
                    }
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fb = FullIntConvertor.ConvertToBase(fb, toBase.dec);
                    }
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var sumFull = FullInt64Math.add(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba + bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

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
        public void BigTestHec_128Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(16, false);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(32, false);
        }

        [TestMethod]
        public void BigTestHex_512it_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(64, false);
        }

        [TestMethod]
        public void BigTestDec_128Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(16, true);
        }

        [TestMethod]
        public void BigTestDec_256it_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(32, true);
        }

        [TestMethod]
        public void BigTestDec_512Bit_Mult_with_random_Longs()
        {
            BigTest_Mult_with_random_Longs(64, true);
        }
        public void BigTest_Mult_with_random_Longs(int byteLength, bool isDec)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 100;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];
                    
                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fa = FullIntConvertor.ConvertToBase(fa, toBase.dec);
                    }
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fb = FullIntConvertor.ConvertToBase(fb, toBase.dec);
                    }
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var multFull = FullInt64Math.mult(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba * bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();
                   // expectedFull = FullIntConvertor.ConvertToBase(expectedFull, isDec ? toBase.dec : toBase.hex);

                   
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
            BigTest_Div_with_random_Longs(16, false);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(32, false);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(32, false);
        }
        [TestMethod]
        public void BigTestDec_128Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(16, true);
        }

        [TestMethod]
        public void BigTestDec_256Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(32, true);
        }

        [TestMethod]
        public void BigTestDec_512Bit_Div_with_random_Longs()
        {
            BigTest_Div_with_random_Longs(32, true);
        }

        public void BigTest_Div_with_random_Longs(int byteLength, bool isDec)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fa = FullIntConvertor.ConvertToBase(fa, toBase.dec);
                    }
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fb = FullIntConvertor.ConvertToBase(fb, toBase.dec);
                    }
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    //TestContext?.WriteLine($"FullInt a={fa}, b={fb}");
                    fullStopwatch.Start();
                    var divFull = FullInt64Math.div(fa, fb);
                    fullStopwatch.Stop();

                    TestContext?.WriteLine($"BigInt a={ba}, b={bb}");
                    bigStopwatch.Start();
                    BigInteger expected = ba / bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

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

        [TestMethod]
        public void BigTestHex_128Bit_Mod_with_random_Longs() 
        {
            BigTest_Mod_with_random_Longs(16, false);
        }

        [TestMethod]
        public void BigTestHex_256Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(32, false);
        }

        [TestMethod]
        public void BigTestHex_512Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(64, false);
        }
        [TestMethod]
        public void BigTestDec_128Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(16, true);
        }

        [TestMethod]
        public void BigTestDec_256Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(32, true);
        }

        [TestMethod]
        public void BigTestDec_512Bit_Mod_with_random_Longs()
        {
            BigTest_Mod_with_random_Longs(64, true);
        }

        public void BigTest_Mod_with_random_Longs(int byteLength, bool isDec)
        {
            Stopwatch bigStopwatch = new Stopwatch();
            Stopwatch fullStopwatch = new Stopwatch();

            const int iterations = 10;
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < iterations; i++)
                {
                    var buf = new byte[byteLength];

                    rng.GetBytes(buf);
                    var (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fa = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fa = FullIntConvertor.ConvertToBase(fa, toBase.dec);
                    }
                    var ba = new BigInteger(buf);
                    Assert.AreEqual(ba.ToString(), fa.ToString(10), $"Fullint A and Bigint A");

                    rng.GetBytes(buf);
                    (longBuf, sign) = ParseBytesWithSingToUnsingUlongs(buf);
                    var fb = new FullIntCore(sign, longBuf, toBase.hex);
                    if (isDec)
                    {
                        fb = FullIntConvertor.ConvertToBase(fb, toBase.dec);
                    }
                    var bb = new BigInteger(buf);
                    Assert.AreEqual(bb.ToString(), fb.ToString(10), $"Fullint B and Bigint B");

                    fullStopwatch.Start();
                    var divFull = FullInt64Math.mod(fa, fb);
                    fullStopwatch.Stop();

                    bigStopwatch.Start();
                    BigInteger expected = ba % bb;
                    bigStopwatch.Stop();

                    FullIntCore expectedFull = expected.ToString();

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