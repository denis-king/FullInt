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
