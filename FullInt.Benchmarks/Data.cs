using System.Numerics;
using FullNumerics;

namespace FullNumerics.Benchmarks
{
    /// <summary>Deterministic operands, so runs are comparable between builds and machines.</summary>
    internal static class Data
    {
        /// <summary>
        /// n random limbs (index 0 = most significant), top limb kept large so the length is exact
        /// and division takes the general path.
        /// </summary>
        public static ulong[] Limbs(int n, int seed)
        {
            var rnd = new Random(seed * 7919 + n);
            var a = new ulong[n];
            for (int i = 0; i < n; i++) a[i] = (ulong)rnd.NextInt64() | ((ulong)rnd.Next(2) << 63);
            a[0] |= 1UL << 62;
            return a;
        }

        public static BigInteger ToBig(ulong[] limbs)
        {
            var bytes = new byte[limbs.Length * 8];
            for (int i = 0; i < limbs.Length; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan((limbs.Length - 1 - i) * 8, 8), limbs[i]);   // little-endian
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        }

        public static IFullIntCore CreateCore(string name) => name switch
        {
            "native" => new FullIntCoreNative64(),
            "managed" => new FullIntCoreManaged(),
            _ => throw new ArgumentException(name),
        };

        /// <summary>Cores available on this machine: native only where the asm library is built.</summary>
        public static IEnumerable<string> Cores()
        {
            if (OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
                yield return "native";
            yield return "managed";
        }
    }
}
