using System.Diagnostics;
using FullInt.Core;

namespace FullInt.Benchmarks
{
    /// <summary>
    /// Picks the Burnikel–Ziegler threshold: 2N / N word divisions, the core's Div alone (the asm
    /// Knuth division) against Burnikel–Ziegler with several thresholds. Minimum of rounds; the
    /// variants interleaved. Ratio = variant time / core Div time (&lt; 1: faster).
    ///
    ///   dotnet run -c Release --project FullInt.Benchmarks -- bz [rounds]
    /// </summary>
    internal static class BzTune
    {
        private static readonly int[] Sizes = { 64, 96, 128, 192, 256, 512, 1024, 2048, 4096 };
        private static readonly int[] Thresholds = { 24, 32, 48, 64, 80, 96, 128, 192 };

        /// <summary>
        /// "dec": the thresholds of the decimal conversion by halves (DecimalConversion): ToString
        /// and Parse of N-word numbers, time in us per threshold (words; Parse: words·19 digits).
        /// </summary>
        public static int RunDecimal(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 3;
            int[] sizes = { 32, 64, 128, 256, 512, 1024, 4096 };
            int[] thresholds = { 16, 24, 32, 48, 64, 96, 128, 100000 };   // 100000: the kernels alone
            var rnd = new Random(2);
            Console.WriteLine($"rounds={rounds}; us per call; columns: threshold in words (100000 = kernels alone)");
            foreach (string what in new[] { "ToString", "Parse" })
            {
                Console.Write($"{what,-9}{"N",6}");
                foreach (int t in thresholds) Console.Write($" {t,9}");
                Console.WriteLine();
                foreach (int n in sizes)
                {
                    var w = new ulong[n];
                    for (int i = 0; i < n; i++) w[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                    w[0] |= 1;
                    var x = new FullInt64(false, w);
                    string text = x.ToString(10);
                    Console.Write($"{"",-9}{n,6}");
                    foreach (int t in thresholds)
                    {
                        DecimalConversion.ToStringThreshold = t;
                        DecimalConversion.ParseThreshold = t * 19;
                        Action f = what == "ToString" ? () => x.ToString(10) : () => FullInt64.Parse(text);
                        f();
                        double best = double.MaxValue;
                        for (int r = 0; r < rounds; r++)
                        {
                            int it = 0;
                            var sw = Stopwatch.StartNew();
                            do { f(); it++; } while (sw.ElapsedMilliseconds < 20);
                            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / it);
                        }
                        Console.Write($" {best,9:F1}");
                    }
                    Console.WriteLine();
                }
            }
            return 0;
        }

        /// <summary>
        /// "newton [rounds] [ratio]": ratio·N / N word divisions (ratio 2 by default), Burnikel–Ziegler
        /// against the Newton reciprocal with several block splits and reciprocal bases.
        /// </summary>
        public static int RunNewton(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 5;
            int ratio = args.Length > 1 ? int.Parse(args[1]) : 2;
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
            }
            catch { /* best effort */ }

            var core = FullIntCoreFactory.Create();
            var rnd = new Random(1);
            int fft = Karatsuba.FftThreshold;
            var configs = new (int split, int inverseBase, int cyclic)[] { (1, 64, fft), (2, 64, fft), (1, 64, 4096), (2, 64, 4096), (1, 64, 2048), (2, 64, 2048), (1, 64, 1024), (2, 64, 1024) };
            Console.WriteLine($"core={core.GetType().Name} rounds={rounds}; {ratio}N / N; ratio = Newton (split/cyclic from) / Burnikel–Ziegler");
            Console.Write($"{"N",6} {"BZ",12}");
            foreach (var c in configs) Console.Write($" {c.split + "/" + (c.cyclic == fft ? "fft" : c.cyclic.ToString()),7}");
            Console.WriteLine();

            foreach (int n in new[] { 1024, 2048, 4096, 8192, 16384, 32768, 65536 })
            {
                var a = new ulong[ratio * n];
                var b = new ulong[n];
                for (int i = 0; i < a.Length; i++) a[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                for (int i = 0; i < b.Length; i++) b[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                a[0] |= 1; b[0] |= 1;
                var q = new ulong[a.Length - n + 1];
                var r = new ulong[n + 2];

                var variants = new List<Action> { () => BurnikelZiegler.DivRem(core, a, b, q, r) };
                foreach (var c in configs) variants.Add(() => NewtonDivision.DivRem(core, a, b, q, r, c.split, c.inverseBase, c.cyclic));

                var best = new double[variants.Count];
                Array.Fill(best, double.MaxValue);
                foreach (var v in variants) v();
                for (int round = 0; round < rounds; round++)
                {
                    for (int i = 0; i < variants.Count; i++)
                    {
                        int it = 0;
                        var sw = Stopwatch.StartNew();
                        do { variants[i](); it++; } while (sw.ElapsedMilliseconds < 30);
                        best[i] = Math.Min(best[i], sw.Elapsed.TotalMilliseconds * 1e3 / it);
                    }
                }

                Console.Write($"{n,6} {best[0],9:F0} us");
                for (int i = 1; i < variants.Count; i++) Console.Write($" {best[i] / best[0],7:F3}");
                Console.WriteLine();
            }
            return 0;
        }

        /// <summary>
        /// "quot [rounds]": the quotient alone (BurnikelZiegler.Quotient) against the full division
        /// the engine used before: 2N / N against DivRem, (N + 40) / N against the core Div.
        /// </summary>
        public static int RunQuotient(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 5;
            var core = FullIntCoreFactory.Create();
            var rnd = new Random(1);
            Console.WriteLine($"core={core.GetType().Name} rounds={rounds}; us; ratio = Quotient / before");
            Console.WriteLine($"{"N",6} {"2N/N DivRem",12} {"Quotient",9} {"ratio",6} | {"(N+40)/N Div",13} {"Quotient",9} {"ratio",6}");
            foreach (int n in new[] { 256, 512, 1024, 2048, 4096, 8192 })
            {
                ulong[] Rand(int len) { var w = new ulong[len]; for (int i = 0; i < len; i++) w[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63); w[0] |= 1; return w; }
                var a = Rand(2 * n); var b = Rand(n); var a2 = Rand(n + 40);
                var q = new ulong[n + 1]; var r = new ulong[n + 2]; var q2 = new ulong[41];
                double Best(Action f)
                {
                    f();
                    double best = double.MaxValue;
                    for (int round = 0; round < rounds; round++)
                    {
                        int it = 0;
                        var sw = Stopwatch.StartNew();
                        do { f(); it++; } while (sw.ElapsedMilliseconds < 30);
                        best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e3 / it);
                    }
                    return best;
                }
                double full = Best(() => BurnikelZiegler.DivRem(core, a, b, q, r));
                double quot = Best(() => BurnikelZiegler.Quotient(core, a, b, q));
                double kern = Best(() => { q2.AsSpan().Clear(); r.AsSpan().Clear(); core.Div(a2, b, q2, r); });
                double quot2 = Best(() => BurnikelZiegler.Quotient(core, a2, b, q2));
                Console.WriteLine($"{n,6} {full,12:F1} {quot,9:F1} {quot / full,6:F3} | {kern,13:F1} {quot2,9:F1} {quot2 / kern,6:F3}");
            }
            return 0;
        }

        public static int Run(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 5;
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
            }
            catch { /* best effort */ }

            var core = FullIntCoreFactory.Create();
            var rnd = new Random(1);
            Console.WriteLine($"core={core.GetType().Name} rounds={rounds}; ratio = Burnikel–Ziegler from T / core Div alone");
            Console.Write($"{"N",6} {"core Div",12}");
            foreach (int t in Thresholds) Console.Write($" {t,6}");
            Console.WriteLine();

            foreach (int n in Sizes)
            {
                var a = new ulong[2 * n];
                var b = new ulong[n];
                for (int i = 0; i < a.Length; i++) a[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                for (int i = 0; i < b.Length; i++) b[i] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next(2) << 63);
                a[0] |= 1; b[0] |= 1;
                var q = new ulong[n + 1];
                var r = new ulong[n + 2];

                var variants = new List<Action> { () => { r.AsSpan().Clear(); core.Div(a, b, q, r); } };
                foreach (int t in Thresholds) variants.Add(() => BurnikelZiegler.DivRem(core, a, b, q, r, t));

                var iters = variants.Select(v =>
                {
                    for (int w = 0; w < 3; w++) v();
                    var sw = Stopwatch.StartNew();
                    v();
                    return Math.Max(1, (int)(2e6 / Math.Max(sw.Elapsed.TotalMilliseconds * 1e6, 1)));
                }).ToArray();
                var best = new double[variants.Count];
                Array.Fill(best, double.MaxValue);
                for (int round = 0; round < rounds; round++)
                {
                    for (int i = 0; i < variants.Count; i++)
                    {
                        var sw = Stopwatch.StartNew();
                        for (int k = 0; k < iters[i]; k++) variants[i]();
                        best[i] = Math.Min(best[i], sw.Elapsed.TotalMilliseconds * 1e6 / iters[i]);
                    }
                }

                Console.Write($"{n,6} {best[0] / 1000,9:F1} us");
                for (int i = 1; i < variants.Count; i++) Console.Write($" {best[i] / best[0],6:F3}");
                Console.WriteLine();
            }
            return 0;
        }
    }
}
