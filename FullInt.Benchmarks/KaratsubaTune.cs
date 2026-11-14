using System.Diagnostics;
using FullInt.Core;

namespace FullInt.Benchmarks
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
    /// </summary>
    internal static class KaratsubaTune
    {
        private static readonly int[] Sizes = { 24, 32, 48, 64, 96, 128, 192, 256, 512, 1024, 2048, 4096 };
        private static readonly int[] Thresholds = { 16, 24, 32, 40, 48, 64, 80, 96 };
        private static readonly int[] ToomSizes = { 128, 192, 256, 384, 512, 768, 1024, 2048, 4096, 8192 };
        private static readonly int[] ToomThresholds = { 96, 128, 192, 256, 384, 512, 768 };

        public static int Run(string[] args)
        {
            bool toom = args.Length > 0 && args[0] == "toom";
            if (toom) args = args.Skip(1).ToArray();
            int[] sizes = toom ? ToomSizes : Sizes, thresholds = toom ? ToomThresholds : Thresholds;
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
            Console.WriteLine(toom
                ? $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = with Toom-3 / Karatsuba alone at T={Karatsuba.Threshold})"
                : $"core={core.GetType().Name}  rounds={rounds}  (time per N×N product; ratio = Karatsuba / basecase)");
            Console.Write($"{"N",6} {(toom ? "karatsuba" : "basecase"),12}");
            foreach (var t in thresholds) Console.Write($" {"T=" + t,14}");
            Console.WriteLine();

            foreach (int n in sizes)
            {
                ulong[] a = Data.Limbs(n, 1), b = Data.Limbs(n, 2);
                var r = new ulong[2 * n];

                int karatsuba = Karatsuba.Threshold;
                var variants = new List<Action>
                {
                    toom ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, int.MaxValue) : () => core.Mult(a, b, r),
                };
                foreach (int t in thresholds)
                {
                    int threshold = t;
                    variants.Add(toom
                        ? () => Karatsuba.Multiply(core, a, b, r, karatsuba, threshold)
                        : () => Karatsuba.Multiply(core, a, b, r, threshold, int.MaxValue));
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

        private static double Median(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }

        private static string Format(double ns) =>
            ns >= 1e6 ? $"{ns / 1e6:0.00}ms" : ns >= 1e3 ? $"{ns / 1e3:0.0}us" : $"{ns:0}ns";
    }
}
