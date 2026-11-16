using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using FullInt.Core;

namespace FullInt.Benchmarks
{
    /// <summary>
    /// The .NET half of the cross benchmark (GMP / FullInt C++ / FullInt .NET / BigInteger).
    /// The same operations, sizes and numbers as FullInt.Cpp/bench/api_vs_gmp.cpp: the inputs
    /// come from the same splitmix64 sequence, so both sides work on identical values.
    /// Each cell: a batch of >= 3 ms, the minimum over rounds, the two sides interleaved.
    ///
    ///   dotnet run -c Release --project FullInt.Benchmarks -- cross [--ops add,mul,...] [--max N] [--quick] [--csv file]
    ///   --quick: 1 ms batches and 3 rounds - a fast "did it help" check, noisier than the full run
    ///
    /// Then: fullint_bench_api --net file  prints the four columns side by side.
    /// </summary>
    internal static class CrossBench
    {
        internal static readonly int[] Sizes = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 1024, 4096, 16384 };

        private static ulong _state;
        private static ulong Next()   // splitmix64 (the C++ side has the same)
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // n words, index 0 = most significant, top word nonzero
        private static ulong[] Words(int n)
        {
            var w = new ulong[n];
            for (int i = 0; i < n; i++) w[i] = Next();
            if (w[0] == 0) w[0] = 1;
            return w;
        }

        private static BigInteger Big(ulong[] w)
        {
            var bytes = new byte[w.Length * 8];
            for (int i = 0; i < w.Length; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan((w.Length - 1 - i) * 8, 8), w[i]);   // little-endian
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        }

        private static double Batch(Action f, long iters)
        {
            var sw = Stopwatch.StartNew();
            for (long i = 0; i < iters; i++) f();
            return sw.Elapsed.TotalMilliseconds * 1e6 / iters;
        }

        private static double _batchTargetNs = 3e6;   // --quick: 1e6
        private static int _quickRounds;               // --quick: 3

        private static long Calibrate(Action f)
        {
            double targetNs = _batchTargetNs;
            for (long iters = 1; ; iters *= 4)
            {
                double per = Batch(f, iters);
                if (per * iters >= targetNs / 4 || iters >= (1L << 30)) return Math.Max(1, (long)(targetNs / Math.Max(per, 0.01)));
            }
        }

        // up to 3000 calls, but at most ~50 ms (a large BigInteger division takes 0.3 s per call)
        // The process runs pinned to one core at high priority: the background JIT thread that
        // compiles the tier-1 code only gets the core when this thread sleeps. Without the pauses
        // the small cells were measured partly on tier-0 code (2-4x off, varying run to run).
        private static void Warm(Action f)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 3000 && sw.ElapsedMilliseconds < 50; i++) f();
                Thread.Sleep(pass == 0 ? 150 : 30);   // past the 100 ms call counting delay, then the JIT's turn
            }
        }

        private static (double ours, double big) Run(Action ours, Action big)
        {
            Warm(ours);   // past tier-0 code
            Warm(big);
            long io = Calibrate(ours), ib = Calibrate(big);
            double perCall = Batch(ours, 1);
            int rounds = _quickRounds > 0 ? _quickRounds : perCall > 20e6 ? 3 : perCall > 2e6 ? 5 : 11;
            double bo = double.MaxValue, bb = double.MaxValue;
            for (int r = 0; r < rounds; r++)
            {
                bo = Math.Min(bo, Batch(ours, io));
                bb = Math.Min(bb, Batch(big, ib));
            }
            return (bo, bb);
        }

        public static int Run(string[] args)
        {
            int maxN = 4096;
            string? csvPath = null;
            string? ops = null;   // ",add,mul," or null for all
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--max" && i + 1 < args.Length) maxN = int.Parse(args[++i]);
                else if (args[i] == "--csv" && i + 1 < args.Length) csvPath = args[++i];
                else if (args[i] == "--ops" && i + 1 < args.Length) ops = "," + args[++i] + ",";
                else if (args[i] == "--quick") { _batchTargetNs = 1e6; _quickRounds = 3; }
            }
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                if (Environment.ProcessorCount > 2) proc.ProcessorAffinity = (nint)(1L << 2);
            }
            catch { /* best effort */ }

            using var csv = csvPath != null ? new StreamWriter(csvPath) : null;
            csv?.WriteLine("op,n,fullint_net_ns,biginteger_ns");
            Console.WriteLine($"{"op",-8} {"words",6}  {"FullInt",12}  {"BigInteger",12}");

            // all cells first; every op at every size warmed (both sides) before any is measured:
            // with Dynamic PGO a method compiled while only the 1-word cells ran keeps its other
            // paths cold, so a cell's time depended on the order of the cells
            var cells = new List<(string op, int n, Action ours, Action big)>();
            var keep = new List<object>();
            _state = 42;
            foreach (int n in Sizes)
            {
                // the same draw order as api_vs_gmp.cpp: a (n), b (n), c (2n)
                ulong[] wa = Words(n), wb = Words(n), wc = Words(2 * n);
                if (n > maxN) break;

                var a = new FullInt64(false, wa);
                var b = new FullInt64(false, wb);
                var c = new FullInt64(false, wc);
                var a2 = new FullInt64(false, (ulong[])wa.Clone());
                BigInteger ba = Big(wa), bb = Big(wb), bc = Big(wc), ba2 = Big(wa);
                string text = ba.ToString(CultureInfo.InvariantCulture);
                var sinks = new Sinks();
                keep.Add(sinks);
                var ri = new FullInt64(0);   // into an existing result (FullInt64.Add(result, a, b))

                void Row(string op, Action ours, Action big)
                {
                    if (ops != null && !ops.Contains("," + op + ",")) return;
                    cells.Add((op, n, ours, big));
                }

                Row("add", () => sinks.r = a + b, () => sinks.br = ba + bb);
                Row("sub", () => sinks.r = a - b, () => sinks.br = ba - bb);
                Row("addto", () => FullInt64.Add(ri, a, b), () => sinks.br = ba + bb);
                Row("multo", () => FullInt64.Multiply(ri, a, b), () => sinks.br = ba * bb);
                Row("mul", () => sinks.r = a * b, () => sinks.br = ba * bb);
                Row("div", () => sinks.r = c / b, () => sinks.br = bc / bb);
                Row("mod", () => sinks.r = c % b, () => sinks.br = bc % bb);
                Row("shl", () => sinks.r = a << 13, () => sinks.br = ba << 13);
                Row("shr", () => sinks.r = a >> 13, () => sinks.br = ba >> 13);
                Row("and", () => sinks.r = a & b, () => sinks.br = ba & bb);
                Row("cmp", () => sinks.i += a > a2 ? 1 : 0, () => sinks.i += BigInteger.Compare(ba, ba2));
                Row("todec", () => sinks.s = a.ToString(10), () => sinks.s = ba.ToString(CultureInfo.InvariantCulture));
                Row("parse", () => sinks.r = FullInt64.Parse(text), () => sinks.br = BigInteger.Parse(text, CultureInfo.InvariantCulture));
            }

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var cell in cells)
                {
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 200 && sw.ElapsedMilliseconds < 10; i++) { cell.ours(); cell.big(); }
                }
                Thread.Sleep(pass == 0 ? 200 : 50);
            }

            foreach (var (op, n, ours, big) in cells)
            {
                var (o, g) = Run(ours, big);
                Console.WriteLine($"{op,-8} {n,6}  {o,12:F1}  {g,12:F1}");
                csv?.WriteLine(FormattableString.Invariant($"{op},{n},{o:F2},{g:F2}"));
            }
            GC.KeepAlive(keep);
            return 0;
        }

        private sealed class Sinks
        {
            public FullInt64? r;
            public BigInteger br;
            public string? s;
            public int i;
        }
    }
}
