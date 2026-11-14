using System.Numerics;
using System.Runtime.InteropServices;
using FullInt.Core;

namespace FullInt.Tests
{
    /// <summary>
    /// The x86 asm kernels called directly (FullIntCoreWindowsX86.Kernels), bypassing the routing
    /// that sends small sizes and Compare to the managed core: every length 1..12 goes through
    /// the asm here. Runs only in a 32-bit Windows process (net48 with TargetPlatform=x86).
    /// </summary>
    [TestClass]
    public class FullInt64X86KernelTests
    {
        private static bool IsX86Windows =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X86;

        private static ulong[] Next(Random rnd, int n)
        {
            var w = new ulong[n];
            var b = new byte[8];
            for (int i = 0; i < n; i++)
            {
                rnd.NextBytes(b);
                w[i] = rnd.Next(4) switch { 0 => ulong.MaxValue, 1 => 0UL, 2 => 1UL << 63, _ => BitConverter.ToUInt64(b, 0) };
            }
            return w;
        }

        private static BigInteger Big(ulong[] limbs)
        {
            var le = new byte[limbs.Length * 8];
            for (int i = 0; i < limbs.Length; i++)
                BitConverter.GetBytes(limbs[limbs.Length - 1 - i]).CopyTo(le, i * 8);
            return BigCompat.FromUnsignedLittleEndian(le);
        }

        private sealed class Pinned : IDisposable
        {
            private readonly GCHandle _h;
            public Pinned(object array) { _h = GCHandle.Alloc(array, GCHandleType.Pinned); }
            public IntPtr P => _h.AddrOfPinnedObject();
            public void Dispose() => _h.Free();
        }

        [TestMethod]
        public void X86Kernels_AllLengths_MatchBigInteger()
        {
            if (!IsX86Windows)
            {
                Assert.Inconclusive("x86 kernels run only in a 32-bit Windows process");
                return;
            }

            var rnd = new Random(8686);
            for (int it = 0; it < 20_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 13));
                var b = Next(rnd, rnd.Next(1, 13));
                if (rnd.Next(5) == 0) b = (ulong[])a.Clone();
                BigInteger A = Big(a), B = Big(b);
                int lenR = rnd.Next(1, 16);
                BigInteger mod = BigInteger.One << (64 * lenR);
                string ctx = $"lenA={a.Length} lenB={b.Length} lenR={lenR} a={A:X} b={B:X}";

                var r = new ulong[lenR];
                using (Pinned pa = new Pinned(a), pb = new Pinned(b), pr = new Pinned(r))
                {
                    ulong carry = FullIntCoreWindowsX86.Kernels.Add(pa.P, pb.P, pr.P, a.Length, b.Length, lenR);
                    Assert.AreEqual((A + B) % mod, Big(r), "add " + ctx);
                    Assert.AreEqual((ulong)((A % mod + B % mod) / mod), carry, "add carry " + ctx);

                    int sign = FullIntCoreWindowsX86.Kernels.Sub(pa.P, pb.P, pr.P, a.Length, b.Length, lenR);
                    Assert.AreEqual(A.CompareTo(B), sign, "sub sign " + ctx);
                    Assert.AreEqual(BigInteger.Abs(A - B) % mod, Big(r), "sub " + ctx);

                    Assert.AreEqual(A.CompareTo(B), FullIntCoreWindowsX86.Kernels.Compare(pa.P, pb.P, a.Length, b.Length), "compare " + ctx);
                }

                // bitwise with signs (lenR >= max + 1), against the managed core (same semantics, tested elsewhere)
                int signs = rnd.Next(4);
                int lr = Math.Max(a.Length, b.Length) + 1;
                var rx = new ulong[lr];
                var rm = new ulong[lr];
                var managed = new FullInt64CoreDefault();
                using (Pinned pa = new Pinned(a), pb = new Pinned(b), px = new Pinned(rx))
                {
                    int nx = FullIntCoreWindowsX86.Kernels.And(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                    int nm = managed.And(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                    Assert.AreEqual(Big(rm), Big(rx), "and " + ctx); Assert.AreEqual(nm, nx, "and sign " + ctx);
                    nx = FullIntCoreWindowsX86.Kernels.Or(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                    nm = managed.Or(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                    Assert.AreEqual(Big(rm), Big(rx), "or " + ctx); Assert.AreEqual(nm, nx, "or sign " + ctx);
                    nx = FullIntCoreWindowsX86.Kernels.Xor(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                    nm = managed.Xor(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                    Assert.AreEqual(Big(rm), Big(rx), "xor " + ctx); Assert.AreEqual(nm, nx, "xor sign " + ctx);
                }

                // shifts, every shift amount class (0, < 32, 32, > 32, whole words)
                int shift = rnd.Next(0, 300);
                var sl = new ulong[a.Length + shift / 64 + 1 + rnd.Next(0, 2)];
                var sr = new ulong[Math.Max(a.Length - shift / 64, 1)];
                using (Pinned pa = new Pinned(a), pl = new Pinned(sl), ps = new Pinned(sr))
                {
                    FullIntCoreWindowsX86.Kernels.ShiftLeft(pa.P, pl.P, a.Length, sl.Length, shift);
                    Assert.AreEqual(A << shift, Big(sl), $"shl {shift} " + ctx);
                    FullIntCoreWindowsX86.Kernels.ShiftRight(pa.P, ps.P, a.Length, sr.Length, shift);
                    Assert.AreEqual(A >> shift, Big(sr), $"shr {shift} " + ctx);
                }
            }
        }
    }
}
