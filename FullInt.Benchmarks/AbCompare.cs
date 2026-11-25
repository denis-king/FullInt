using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FullNumerics.Benchmarks
{
    /// <summary>
    /// A/B comparison of two builds of the native library (FullIntOperations_win_x64.dll) in one process.
    ///
    /// Why not BenchmarkDotNet for this: the question is "did my asm edit make op X slower", and the
    /// answer is a few percent. Both builds are loaded side by side and called through the same
    /// function-pointer loop on the same buffers; rounds alternate A/B order and the median is taken,
    /// so machine drift (turbo, thermals, background load) hits both equally.
    /// Noise floor measured with A/A runs (same DLL twice): about ±5%.
    ///
    /// Usage: ab &lt;base.dll&gt; &lt;cur.dll&gt; [opFilter] [rounds]
    /// </summary>
    internal static unsafe class AbCompare
    {
        private const double NoiseThreshold = 0.05;
        private const double MinAbsNs = 1.0;
        private static readonly int[] Sizes = { 1, 2, 8, 32, 128, 512 };

        public static int Run(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: ab <base.dll> <cur.dll> [opFilter|*] [rounds]");
                return 2;
            }
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                Console.Error.WriteLine("ab: only win-x64 native builds exist so far");
                return 2;
            }

            // both files usually have the same name: LoadLibrary by full path keeps them apart
            var libs = new[] { NativeLibrary.Load(Path.GetFullPath(args[0])), NativeLibrary.Load(Path.GetFullPath(args[1])) };
            string filter = args.Length > 2 && args[2] != "*" ? args[2] : "";        // '*' = all ops
            int rounds = args.Length > 3 ? int.Parse(args[3]) : 15;

            // less scheduler noise: one core (not core 0, where most interrupts land), high priority
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch (Exception ex) { Console.WriteLine($"(could not raise priority / set affinity: {ex.Message})"); }

            Console.WriteLine($"base: {Path.GetFullPath(args[0])}");
            Console.WriteLine($"cur:  {Path.GetFullPath(args[1])}");
            Console.WriteLine($"rounds: {rounds}, median ns/call; cur/base = median ratio, min c/b = best-of-rounds ratio");
            Console.WriteLine($"'!'/'+' = slower/faster by > {NoiseThreshold:P0} on both ratios and by > {MinAbsNs} ns");
            Console.WriteLine();
            Console.WriteLine($"{"op",-12}{"limbs",6}{"base ns",12}{"cur ns",12}{"cur/base",10}{"min c/b",10}");

            var pins = new List<GCHandle>();
            nint P(Array a) { var h = GCHandle.Alloc(a, GCHandleType.Pinned); pins.Add(h); return h.AddrOfPinnedObject(); }

            int regressions = 0;
            void Bench(string op, int n, string export, Action<nint, long> loop)
            {
                if (filter.Length > 0 && !op.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) return;
                if (!NativeLibrary.TryGetExport(libs[0], export, out var f0) || !NativeLibrary.TryGetExport(libs[1], export, out var f1))
                {
                    Console.WriteLine($"{op,-12}{n,6}   (export {export} missing in one build)");
                    return;
                }
                var fns = new[] { f0, f1 };
                loop(f0, 2000); loop(f1, 2000);                           // warm-up both

                long it = 16;                                              // ~40 ms per sample
                for (var sw = Stopwatch.StartNew(); ; it *= 2)
                {
                    sw.Restart(); loop(f0, it);
                    if (sw.ElapsedMilliseconds >= 40) break;
                }

                var s = new[] { new List<double>(), new List<double>() };
                for (int r = 0; r < rounds; r++)
                    for (int k = 0; k < 2; k++)
                    {
                        int idx = (r + k) & 1;                             // alternate order every round
                        var t = Stopwatch.StartNew();
                        loop(fns[idx], it);
                        s[idx].Add(t.Elapsed.TotalMilliseconds * 1e6 / it);
                    }
                double b = s[0].OrderBy(x => x).ElementAt(rounds / 2), c = s[1].OrderBy(x => x).ElementAt(rounds / 2);
                double ratio = c / b, minRatio = s[1].Min() / s[0].Min();
                // a change counts only if median and best-of-rounds agree and it is more than ~1 ns
                // (sub-nanosecond shifts on 1-2 limb calls are placement/timer noise)
                bool slower = ratio > 1 + NoiseThreshold && minRatio > 1 + NoiseThreshold && c - b > MinAbsNs;
                bool faster = ratio < 1 - NoiseThreshold && minRatio < 1 - NoiseThreshold && b - c > MinAbsNs;
                string mark = slower ? " !" : faster ? " +" : "";
                if (slower) regressions++;
                Console.WriteLine($"{op,-12}{n,6}{b,12:F1}{c,12:F1}{ratio,10:F3}{minRatio,10:F3}{mark}");
            }

            foreach (int n in Sizes)
            {
                ulong[] a = Data.Limbs(n, 1), b = Data.Limbs(n, 2), a2 = Data.Limbs(2 * n, 3);
                ulong[] c = (ulong[])a.Clone(); c[n - 1] ^= 1;
                ulong[] sum = new ulong[n + 1], prod = new ulong[2 * n], quot = new ulong[n + 1], rem = new ulong[n + 2], wide = new ulong[n + 2];
                ulong[] rr = new ulong[1];
                nint pa = P(a), pb = P(b), pa2 = P(a2), pc = P(c), psum = P(sum), pprod = P(prod), pq = P(quot), prem = P(rem), pwide = P(wide), prr = P(rr);

                Bench("Add", n, "Fullint64AddWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, int, int, ulong>)f;
                    for (long i = 0; i < it; i++) fn(pa, pb, psum, n, n, n + 1);
                });
                Bench("Sub", n, "Fullint64SubWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, int, int, int>)f;
                    for (long i = 0; i < it; i++) fn(pa, pb, psum, n, n, n + 1);
                });
                Bench("Mult", n, "Fullint64MultWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, int, int, ulong>)f;
                    for (long i = 0; i < it; i++) fn(pa, pb, pprod, n, n, 2 * n);
                });
                Bench("Compare", n, "Fullint64CompareWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, int, int, int>)f;
                    for (long i = 0; i < it; i++) fn(pa, pc, n, n);
                });
                Bench("DivWord", n, "Fullint64Div2longWin", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, ulong, nint, nint, int, ulong>)f;
                    for (long i = 0; i < it; i++) fn(pa, 0x1234_5678_9ABC_DEF1UL, psum, prr, n);
                });
                Bench("Div 2n/n", n, "Fullint64DivWin", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, nint, int, int, ulong>)f;
                    for (long i = 0; i < it; i++) fn(pa2, pb, pq, prem, 2 * n, n);
                });
                Bench("And", n, "Fullint64AndWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, int, int, int, int>)f;
                    for (long i = 0; i < it; i++) fn(pa, pb, psum, n, n, n + 1, 1);
                });
                Bench("Shl", n, "Fullint64ShlWIN", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                {
                    var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, int, int, int, void>)f;
                    for (long i = 0; i < it; i++) fn(pa, pwide, n, n + 2, 37);
                });

                // decimal strings (quadratic, so only up to 128 limbs ≈ 2.5k digits)
                if (n <= 128)
                {
                    var tmp = new ulong[n];
                    var decBuf = new byte[(int)(n * 1.014 + 1) * 19 + 1];
                    var len = new int[1];
                    nint ptmp = P(tmp), pdec = P(decBuf), plen = P(len);

                    Bench("ToDec", n, "Fullint64ToDecStringWin", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                    {
                        var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, nint, void>)f;
                        for (long i = 0; i < it; i++) { len[0] = decBuf.Length; fn(pa, ptmp, pdec, n, plen); }
                    });

                    // digits for FromDec, produced by ToDec of the current build
                    len[0] = decBuf.Length;
                    ((delegate* unmanaged<nint, nint, nint, int, nint, void>)NativeLibrary.GetExport(libs[1], "Fullint64ToDecStringWin"))(pa, ptmp, pdec, n, plen);
                    var digits = decBuf.AsSpan(len[0]).ToArray();
                    var fromTmp = new ulong[digits.Length / 19 + 1];
                    var fromRes = new ulong[digits.Length / 19 + 1];
                    nint pdig = P(digits), pft = P(fromTmp), pfr = P(fromRes);

                    Bench("FromDec", n, "Fullint64FromDecStringWin", [MethodImpl(MethodImplOptions.AggressiveOptimization)] (f, it) =>
                    {
                        var fn = (delegate* unmanaged[SuppressGCTransition]<nint, nint, nint, int, nint, void>)f;
                        for (long i = 0; i < it; i++) { len[0] = fromRes.Length; fn(pdig, pft, pfr, digits.Length, plen); }
                    });
                }
            }

            foreach (var h in pins) h.Free();
            Console.WriteLine();
            Console.WriteLine(regressions == 0
                ? "no slowdowns above the noise floor"
                : $"{regressions} case(s) slower than base by more than {NoiseThreshold:P0} — rerun with more rounds to confirm");
            return 0;
        }
    }
}
