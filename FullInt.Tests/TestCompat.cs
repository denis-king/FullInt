using System.Numerics;

namespace FullNumerics.Tests
{
    /// <summary>
    /// BigInteger byte conversions that exist on every target the tests run on
    /// (.NET 8 and .NET Framework 4.8, which has no isUnsigned / isBigEndian overloads).
    /// </summary>
    internal static class BigCompat
    {
        /// <summary>Unsigned magnitude from little-endian bytes (index 0 = least significant).</summary>
        public static BigInteger FromUnsignedLittleEndian(byte[] littleEndian)
        {
            var withSign = new byte[littleEndian.Length + 1];   // a zero top byte keeps it positive
            Array.Copy(littleEndian, withSign, littleEndian.Length);
            return new BigInteger(withSign);
        }

        /// <summary>
        /// x >> k rounded toward negative infinity (BigInteger semantics on .NET Core). Shifts only a
        /// non-negative value: .NET Framework's BigInteger gets some negative right shifts wrong
        /// (e.g. -(2^64 - 2) >> 32 gives 0 instead of -2^32).
        /// </summary>
        public static BigInteger ShiftRightFloor(BigInteger x, int k) =>
            x.Sign >= 0 ? x >> k : -((-x - 1) >> k) - 1;

        /// <summary>Big-endian bytes of a non-negative value, without the sign byte (at least one byte).</summary>
        public static byte[] ToUnsignedBigEndian(BigInteger value)
        {
            var le = value.ToByteArray();                        // little-endian two's complement
            int length = le.Length;
            while (length > 1 && le[length - 1] == 0) length--;  // drop the sign byte(s)
            var be = new byte[length];
            for (int i = 0; i < length; i++) be[i] = le[length - 1 - i];
            return be;
        }
    }

#if NETFRAMEWORK
    /// <summary>.NET 6+ helpers used by the tests, for the .NET Framework 4.8 test run.</summary>
    internal static class FrameworkCompat
    {
        public static long NextInt64(this Random random)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToInt64(bytes, 0) & long.MaxValue;   // [0, long.MaxValue], like .NET's
        }

        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source, int size)
        {
            var chunk = new List<T>(size);
            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count == size)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                }
            }
            if (chunk.Count > 0) yield return chunk.ToArray();
        }
    }
#endif
}
