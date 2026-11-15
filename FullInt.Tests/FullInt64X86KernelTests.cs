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

        /// <summary>
        /// The x86 division kernel directly: quotient and remainder against BigInteger, and the
        /// whole quotient / remainder buffers word for word against the managed core (both follow
        /// the same algorithm and buffer contract). Divisors with all-ones top words drive the
        /// q_max overflow path; equal windows the digit-1 path.
        /// </summary>
        [TestMethod]
        public void X86Kernels_Div_MatchesBigIntegerAndManaged()
        {
            if (!IsX86Windows)
            {
                Assert.Inconclusive("x86 kernels run only in a 32-bit Windows process");
                return;
            }

            var rnd = new Random(9393);
            var managed = new FullInt64CoreDefault();
            for (int it = 0; it < 20_000; it++)
            {
                int n = rnd.Next(1, 7), m = rnd.Next(1, 11);
                var d = Next(rnd, n);
                if (d[0] == 0) d[0] = rnd.Next(2) == 0 ? 1UL : ulong.MaxValue;   // normalized divisor
                var a = Next(rnd, m);
                if (rnd.Next(6) == 0 && m >= n) Array.Copy(d, 0, a, 0, n);      // a window equal to D

                var q = new ulong[Math.Max(m - n + 1, 1)];
                var r = new ulong[n + 2];
                var qm = new ulong[q.Length];
                var rm = new ulong[n + 2];
                using (Pinned pa = new Pinned(a), pd = new Pinned(d), pq = new Pinned(q), pr = new Pinned(r))
                {
                    FullIntCoreWindowsX86.Kernels.Div(pa.P, pd.P, pq.P, pr.P, m, n);
                }
                managed.Div(a, d, qm, rm);

                BigInteger A = Big(a), D = Big(d);
                string ctx = $"m={m} n={n} a={A:X} d={D:X}";
                Assert.AreEqual(A / D, Big(q), "q " + ctx);
                Assert.AreEqual(A % D, Big(r), "r " + ctx);
                CollectionAssert.AreEqual(qm, q, "q buffer vs managed " + ctx);
                CollectionAssert.AreEqual(rm, r, "r buffer vs managed " + ctx);
            }
        }

        /// <summary>
        /// The reciprocal division kernels (DivRecip.asm): the digit version (divisors of 2..9
        /// words) and the word version (10 and more), quotients of 1.. words, special words
        /// (0, max, 2^63) and windows equal to the divisor; by one word, also in place.
        /// </summary>
        [TestMethod]
        public void X86Kernels_DivRecip_MatchesBigInteger()
        {
            if (!IsX86Windows)
            {
                Assert.Inconclusive("x86 kernels run only in a 32-bit Windows process");
                return;
            }

            var rnd = new Random(4242);
            for (int it = 0; it < 6_000; it++)
            {
                int n = rnd.Next(2, 16), m = n + rnd.Next(1, 14);
                var d = Next(rnd, n);
                if (d[0] == 0) d[0] = rnd.Next(2) == 0 ? 1UL : ulong.MaxValue;
                var a = Next(rnd, m);
                if (a[0] == 0) a[0] = 1;
                if (rnd.Next(5) == 0) Array.Copy(d, 0, a, rnd.Next(0, m - n + 1), n);

                var q = new ulong[m - n + 1];
                var r = new ulong[n + 2];
                var work = new ulong[m + n + 2];
                using (Pinned pa = new Pinned(a), pd = new Pinned(d), pq = new Pinned(q), pr = new Pinned(r), pw = new Pinned(work))
                {
                    FullIntCoreWindowsX86.Kernels.DivRecip(pa.P, pd.P, pq.P, pr.P, m, n, pw.P);
                }
                BigInteger A = Big(a), D = Big(d);
                string ctx = $"m={m} n={n} a={A:X} d={D:X}";
                Assert.AreEqual(A / D, Big(q), "q " + ctx);
                Assert.AreEqual(A % D, Big(r), "r " + ctx);

                ulong w = rnd.Next(3) == 0 ? (ulong)rnd.Next(1, int.MaxValue) : Next(rnd, 1)[0] | 1;
                var qw = new ulong[m];
                var inPlace = (ulong[])a.Clone();
                using (Pinned pa = new Pinned(a), pq = new Pinned(qw), pi = new Pinned(inPlace))
                {
                    FullIntCoreWindowsX86.Kernels.DivByWordRecip(pa.P, w, pq.P, out ulong rem, m);
                    Assert.AreEqual(A / w, Big(qw), $"div by word d={w:X} " + ctx);
                    Assert.AreEqual(A % w, (BigInteger)rem, $"rem by word d={w:X} " + ctx);
                    FullIntCoreWindowsX86.Kernels.DivByWordRecip(pi.P, w, pi.P, out ulong rem2, m);
                    Assert.AreEqual(A / w, Big(inPlace), $"div by word in place d={w:X} " + ctx);
                    Assert.AreEqual(A % w, (BigInteger)rem2, $"rem by word in place d={w:X} " + ctx);
                }
            }
        }

        /// <summary>
        /// The mulx / adcx / adox multiply (x86/MultAdx.asm), where the CPU has BMI2 + ADX: every
        /// length pair 1..24, special words, a longer result (its extra words must be zero).
        /// </summary>
        [TestMethod]
        public void X86Kernels_MultAdx_MatchesBigInteger()
        {
            if (!IsX86Windows)
            {
                Assert.Inconclusive("x86 kernels run only in a 32-bit Windows process");
                return;
            }
            if ((FullIntCoreWindowsX86.Kernels.CpuFeatures() & 3) != 3)
            {
                Assert.Inconclusive("the CPU has no BMI2 + ADX");
                return;
            }

            var rnd = new Random(6161);
            for (int la = 1; la <= 24; la++)
                for (int lb = 1; lb <= 24; lb++)
                {
                    var a = Next(rnd, la);
                    var b = Next(rnd, lb);
                    int lr = la + lb + rnd.Next(0, 3);
                    var r = new ulong[lr];
                    for (int i = 0; i < lr; i++) r[i] = 0xDEAD_BEEF_DEAD_BEEFUL;   // must be fully overwritten
                    using (Pinned pa = new Pinned(a), pb = new Pinned(b), pr = new Pinned(r))
                    {
                        FullIntCoreWindowsX86.Kernels.MultAdx(pa.P, pb.P, pr.P, la, lb, lr);
                    }
                    Assert.AreEqual(Big(a) * Big(b), Big(r), $"mult adx {la}x{lb} lenR={lr}");
                }
        }

        /// <summary>
        /// The x86 decimal string kernels: ToDecString of random values, FromDecString of their
        /// digits with and without leading zeros (chunk boundaries at multiples of 19 digits),
        /// against BigInteger.
        /// </summary>
        [TestMethod]
        public void X86Kernels_DecimalStrings_MatchBigInteger()
        {
            if (!IsX86Windows)
            {
                Assert.Inconclusive("x86 kernels run only in a 32-bit Windows process");
                return;
            }

            IFullIntCore core = new FullIntCoreWindowsX86();
            var rnd = new Random(1919);
            for (int it = 0; it < 4_000; it++)
            {
                var a = Next(rnd, rnd.Next(1, 40));
                var A = Big(a);
                string expected = A.ToString();

                var buf = new byte[a.Length * 20 + 1];
                core.ToDecString(a, buf, out int start);
                Assert.AreEqual(expected, System.Text.Encoding.ASCII.GetString(buf, start, buf.Length - start), "ToDecString");

                string text = rnd.Next(3) switch
                {
                    0 => new string('0', rnd.Next(1, 40)) + expected,                    // leading zeros
                    1 => expected.PadLeft((expected.Length + 18) / 19 * 19, '0'),          // a multiple of 19 digits
                    _ => expected,
                };
                var bytes = System.Text.Encoding.ASCII.GetBytes(text);
                var limbs = new ulong[bytes.Length / 19 + 1];
                core.FromDecString(bytes, limbs, out int lstart);
                Assert.AreEqual(A, Big(limbs.AsSpan(lstart).ToArray()), $"FromDecString {text}");
            }
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

                    // the SSE2 versions (LogicSse2.asm), where the CPU has SSE2
                    if ((FullIntCoreWindowsX86.Kernels.CpuFeatures() & 16) != 0)
                    {
                        nx = FullIntCoreWindowsX86.Kernels.AndSse2(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                        nm = managed.And(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                        Assert.AreEqual(Big(rm), Big(rx), "and sse2 " + ctx); Assert.AreEqual(nm, nx, "and sse2 sign " + ctx);
                        nx = FullIntCoreWindowsX86.Kernels.OrSse2(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                        nm = managed.Or(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                        Assert.AreEqual(Big(rm), Big(rx), "or sse2 " + ctx); Assert.AreEqual(nm, nx, "or sse2 sign " + ctx);
                        nx = FullIntCoreWindowsX86.Kernels.XorSse2(pa.P, pb.P, px.P, a.Length, b.Length, lr, signs);
                        nm = managed.Xor(a, (signs & 1) != 0, b, (signs & 2) != 0, rm);
                        Assert.AreEqual(Big(rm), Big(rx), "xor sse2 " + ctx); Assert.AreEqual(nm, nx, "xor sse2 sign " + ctx);
                    }
                }

                // division by one 64-bit word: below 2^32 (two div r32 per word), 2^32, full width (fi_div128)
                ulong d = rnd.Next(5) switch
                {
                    0 => (ulong)rnd.Next(1, int.MaxValue),
                    1 => 1UL << 32,
                    2 => ulong.MaxValue - (ulong)rnd.Next(0, 3),
                    3 => (1UL << 63) | 1,
                    _ => Next(rnd, 1)[0] | 1,
                };
                var q = new ulong[a.Length];
                using (Pinned pa = new Pinned(a), pq = new Pinned(q))
                {
                    FullIntCoreWindowsX86.Kernels.DivByWord(pa.P, d, pq.P, out ulong rem, a.Length);
                    Assert.AreEqual(A / d, Big(q), $"div by word d={d:X} " + ctx);
                    Assert.AreEqual(A % d, (BigInteger)rem, $"rem by word d={d:X} " + ctx);
                }
                var inPlace = (ulong[])a.Clone();
                using (Pinned pi = new Pinned(inPlace))
                {
                    FullIntCoreWindowsX86.Kernels.DivByWord(pi.P, d, pi.P, out ulong rem2, a.Length);   // quotient over the dividend
                    Assert.AreEqual(A / d, Big(inPlace), $"div by word in place d={d:X} " + ctx);
                    Assert.AreEqual(A % d, (BigInteger)rem2, $"rem by word in place d={d:X} " + ctx);
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
