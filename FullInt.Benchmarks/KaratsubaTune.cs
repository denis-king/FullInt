using System.Diagnostics;
using FullNumerics;

namespace FullNumerics.Benchmarks
{
    /// <summary>
    /// Picks the Karatsuba threshold: N×N products on the native core, basecase (the asm column
    /// multiply alone) against Karatsuba with several thresholds. Each cell is the median of
    /// rounds; rounds alternate the variants so drift hits all of them alike.
    ///
    ///   dotnet run -c Release --project FullInt.Benchmarks -- kara [rounds]
    ///
    /// "kara toom [rounds]" does the same for the Toom-3 threshold: the base is Karatsuba alone
    /// (at the current Karatsuba threshold), the variants add Toom-3 levels from T words.
    /// "kara toom4 [rounds]": the base is Karatsuba + Toom-3 (current thresholds) without Toom-4,
    /// the variants add Toom-4 levels from T words.
    /// "kara toom6 [rounds]": the base is the current Toom levels without Toom-6, the variants
    /// add Toom-6 levels from T words (the FFT off).
    /// "kara fft [rounds]": the base is the Toom levels without the FFT, the variants use the
    /// FFT from T words.
    /// </summary>
    internal static class KaratsubaTune
    {
        private static readonly int[] Sizes = { 24, 32, 48, 64, 96, 128, 192, 256, 512, 1024, 2048, 4096 };
        private static readonly int[] Thresholds = { 16, 24, 32, 40, 48, 64, 80, 96 };
        private static readonly int[] ToomSizes = { 128, 192, 256, 384, 512, 768, 1024, 2048, 4096, 8192 };
        private static readonly int[] ToomThresholds = { 96, 128, 192, 256, 384, 512, 768 };
        private static readonly int[] Toom4Sizes = { 256, 384, 512, 768, 1024, 1536, 2048, 4096, 8192 };
        private static readonly int[] Toom4Thresholds = { 192, 256, 384, 512, 768, 1024, 1536 };
        private static readonly int[] Toom6Sizes = { 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192 };
        private static readonly int[] Toom6Thresholds = { 384, 512, 768, 1024, 1536, 2048, 3072 };
        private static readonly int[] FftSizes = { 2048, 4096, 8192, 16384, 32768, 65536, 131072 };
        private static readonly int[] FftThresholds = { 1024, 2048, 4096, 8192, 16384, 32768 };

        /// <summary>
        /// "kara sqr [rounds]": squares a·a (the same array), the core's Sqr alone against Karatsuba
        /// squares from T words; the last column: a full square / a product of two different numbers
        /// of the same size (all levels at their defaults).
        /// </summary>
        public static int RunSqr(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 7;
            var core = FullIntCoreFactory.Create();
            int[] thresholds = { 48, 64, 96, 128, 160, 192, 256 };
            Console.WriteLine($"core={core.GetType().Name} rounds={rounds}; ratio = Karatsuba squares from T / the Sqr kernel alone; sq/mul = square / product");
            Console.Write($"{"N",6} {"Sqr",10}");
            foreach (int t in thresholds) Console.Write($" {"T=" + t,7}");
            Console.WriteLine($" {"sq/mul",7}");
            foreach (int n in new[] { 64, 96, 128, 192, 256, 384, 512, 1024, 2048, 4096, 8192, 16384 })
            {
                ulong[] a = Data.Limbs(n, 1), b = Data.Limbs(n, 2);
                var r = new ulong[2 * n];
                double Best(Action f)
                {
                    f();
                    double best = double.MaxValue;
                    for (int round = 0; round < rounds; round++)
                    {
                        int it = 0;
                        var sw = Stopwatch.StartNew();
                        do { f(); it++; } while (sw.ElapsedMilliseconds < 20);
                        best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / it);
                    }
                    return best;
                }
                double kernel = n <= 1024 ? Best(() => core.Sqr(a, r)) : double.NaN;
                Console.Write($"{n,6} {Format(kernel),10}");
                foreach (int t in thresholds)
                {
                    int th = t;
                    int saved = Karatsuba.SqrThreshold;
                    Karatsuba.SqrThreshold = th;
                    double v = n <= 1024 ? Best(() => Karatsuba.Multiply(core, a, a, r)) : double.NaN;
                    Karatsuba.SqrThreshold = saved;
                    Console.Write($" {v / kernel,7:F3}");
                }
                double sq = Best(() => Karatsuba.Multiply(core, a, a, r));
                double mul = Best(() => Karatsuba.Multiply(core, a, b, r));
                Console.WriteLine($" {sq / mul,7:F3}");
            }
            return 0;
        }

        public static int Run(string[] args)
        {
            bool ntt = args.Length > 0 && args[0] == "ntt";
            if (ntt) return RunNtt(args.Skip(1).ToArray());
            bool fft = args.Length > 0 && args[0] == "fft";
            bool toom4 = args.Length > 0 && args[0] == "toom4";
            bool toom6 = args.Length > 0 && args[0] == "toom6";
            bool toom = fft || toom4 || toom6 || args.Length > 0 && args[0] == "toom";
            if (toom) args = args.Skip(1).ToArray();
            int[] sizes = fft ? FftSizes : toom6 ? Toom6Sizes : toom4 ? Toom4Sizes : toom ? ToomSizes : Sizes;
            int[] thresholds = fft ? FftThresholds : toom6 ? Toom6Thresholds : toom4 ? Toom4Thresholds : toom ? ToomThresholds : Thresholds;
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 9;
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch { /* best effort */ }

            var core = FullIntCoreFactory.Create();
            Console.WriteLine(fft
                ? $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with the FFT / Toom levels alone)"
                : toom6
                ? $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with Toom-6 / the Toom levels up to Toom-4, no FFT)"
                : toom4
                ? $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with Toom-4 / Karatsuba + Toom-3 alone)"
                : toom
                ? $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with Toom-3 / Karatsuba alone at T={Karatsuba.Threshold})"
                : $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = Karatsuba / basecase)");
            Console.Write($"{"N",6} {(toom ? "karatsuba" : "basecase"),12}");
            foreach (var t in thresholds) Console.Write($" {"T=" + t,14}");
            Console.WriteLine();

            foreach (int n in sizes)
            {
                ulong[] a = Data.Limbs(n, 1), b = Data.Limbs(n, 2);
                var r = new ulong[2 * n];

                int karatsuba = Karatsuba.Threshold, toom3 = Karatsuba.Toom3Threshold;
                var variants = new List<Action>
                {
                    fft ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, Karatsuba.Toom4Threshold, int.MaxValue)
                    : toom6 ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, Karatsuba.Toom4Threshold, int.MaxValue, int.MaxValue)
                    : toom4 ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, int.MaxValue, int.MaxValue, int.MaxValue)
                    : toom ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, int.MaxValue, int.MaxValue)
                    : () => core.Mult(a, b, r),
                };
                foreach (int t in thresholds)
                {
                    int threshold = t;
                    variants.Add(fft
                        ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, Karatsuba.Toom4Threshold, threshold)
                        : toom6
                        ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, Karatsuba.Toom4Threshold, threshold, int.MaxValue)
                        : toom4
                        ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, toom3, threshold, int.MaxValue, int.MaxValue)
                        : toom
                        ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, threshold, int.MaxValue)
                        : () => Karatsuba.Multiply(core, a, b, r, threshold, int.MaxValue, int.MaxValue));
                }

                // iterations so one batch takes ~20 ms on the basecase
                int it = 1;
                for (var sw = Stopwatch.StartNew(); ; it *= 2)
                {
                    sw.Restart();
                    for (int i = 0; i < it; i++) variants[0]();
                    if (sw.Elapsed.TotalMilliseconds > 20) break;
                }

                var samples = new List<double>[variants.Count];
                for (int v = 0; v < variants.Count; v++) samples[v] = new List<double>();
                for (int round = 0; round < rounds; round++)
                {
                    for (int k = 0; k < variants.Count; k++)
                    {
                        int v = (k + round) % variants.Count;   // rotate the order every round
                        var sw = Stopwatch.StartNew();
                        for (int i = 0; i < it; i++) variants[v]();
                        samples[v].Add(sw.Elapsed.TotalMilliseconds * 1e6 / it);
                    }
                }

                double basecase = Median(samples[0]);
                Console.Write($"{n,6} {Format(basecase),12}");
                for (int v = 1; v < variants.Count; v++)
                {
                    double m = Median(samples[v]);
                    Console.Write($" {Format(m),8} {m / basecase,5:0.00}");
                }
                Console.WriteLine();
            }
            return 0;
        }

        // kara ntt [rounds] [thresholds] [sizes]: N×N products with the NTT from T words / without it (the Toom and FFT levels)
        private static int RunNtt(string[] args)
        {
            int rounds = args.Length > 0 ? int.Parse(args[0]) : 9;
            int[] sizes = Environment.Is64BitProcess
                ? new[] { 768, 896, 1024, 1100, 1280, 1536, 2048, 2200, 3000, 4096, 4400, 6000, 8192, 16384, 17600, 32768, 65536, 131072 }
                : new[] { 192, 256, 320, 384, 448, 512, 640, 768, 1024, 1100, 1536, 2048, 4096, 8192, 32768 };
            int[] thresholds = Environment.Is64BitProcess ? new[] { 768, 1024 } : new[] { 192, 256, 384, 512 };
            // kara ntt [rounds] [thresholds] [sizes]: comma-separated lists replace the defaults
            if (args.Length > 1) thresholds = args[1].Split(',').Select(int.Parse).ToArray();
            if (args.Length > 2) sizes = args[2].Split(',').Select(int.Parse).ToArray();
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch { /* best effort */ }
            var core = FullIntCoreFactory.Create();
            int saved = Karatsuba.NttThreshold;
            Console.WriteLine($"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with the NTT from T / without it)");
            Console.Write($"{"N",6} {"no ntt",12}");
            foreach (var t in thresholds) Console.Write($" {"T=" + t,14}");
            Console.WriteLine();
            foreach (int n in sizes)
            {
                ulong[] a = Data.Limbs(n, 1), b = Data.Limbs(n, 2);
                var r = new ulong[2 * n];
                var variants = new List<Action> { () => { Karatsuba.NttThreshold = int.MaxValue; Karatsuba.Multiply(core, a, b, r); } };
                foreach (int t in thresholds)
                {
                    int threshold = t;
                    variants.Add(() => { Karatsuba.NttThreshold = threshold; Karatsuba.Multiply(core, a, b, r); });
                }
                int it = 1;
                for (var sw = Stopwatch.StartNew(); ; it *= 2)
                {
                    sw.Restart();
                    for (int i = 0; i < it; i++) variants[0]();
                    if (sw.Elapsed.TotalMilliseconds > 20) break;
                }
                var samples = new List<double>[variants.Count];
                for (int v = 0; v < variants.Count; v++) samples[v] = new List<double>();
                for (int round = 0; round < rounds; round++)
                {
                    for (int k = 0; k < variants.Count; k++)
                    {
                        int v = (k + round) % variants.Count;
                        var sw = Stopwatch.StartNew();
                        for (int i = 0; i < it; i++) variants[v]();
                        samples[v].Add(sw.Elapsed.TotalMilliseconds * 1e6 / it);
                    }
                }
                double baseline = Median(samples[0]);
                Console.Write($"{n,6} {Format(baseline),12}");
                for (int v = 1; v < variants.Count; v++)
                {
                    double m = Median(samples[v]);
                    Console.Write($" {Format(m),8} {m / baseline,5:0.00}");
                }
                Console.WriteLine();
            }
            Karatsuba.NttThreshold = saved;
            return 0;
        }

        private static double Median(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }

        private static string Format(double ns) =>
            ns >= 1e6 ? $"{ns / 1e6:0.00}ms" : ns >= 1e3 ? $"{ns / 1e3:0.0}us" : $"{ns:0}ns";
    }
}
