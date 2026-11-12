using BenchmarkDotNet.Attributes;
using FullInt.Core;

namespace FullInt.Benchmarks
{
    /// <summary>
    /// Kernel level: IFullIntCore calls on preallocated buffers (no allocation, no sign handling),
    /// native asm core vs managed fallback. This is what to watch when changing the asm.
    /// </summary>
    [MemoryDiagnoser(false)]
    [GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
    public class CoreBenchmarks
    {
        [ParamsSource(nameof(CoreNames))]
        public string Core { get; set; } = "native";

        [Params(1, 4, 16, 64, 256, 1024)]
        public int N { get; set; }

        public static IEnumerable<string> CoreNames() => Data.Cores();

        private IFullIntCore _core = null!;
        private ulong[] _a = null!, _b = null!, _aNear = null!, _a2 = null!;
        private ulong[] _sum = null!, _prod = null!, _quot = null!, _rem = null!, _wide = null!;
        private byte[] _dec = null!, _decBuf = null!;
        private ulong[] _fromDecBuf = null!;

        [GlobalSetup]
        public void Setup()
        {
            _core = Data.CreateCore(Core);
            _a = Data.Limbs(N, 1);
            _b = Data.Limbs(N, 2);
            _aNear = (ulong[])_a.Clone();
            _aNear[N - 1] ^= 1;                        // compare has to scan to the last limb
            _a2 = Data.Limbs(2 * N, 3);

            _sum = new ulong[N + 1];
            _prod = new ulong[2 * N];
            _quot = new ulong[N + 1];
            _rem = new ulong[N + 2];
            _wide = new ulong[N + 2];

            _decBuf = new byte[(int)(N * 1.014 + 1) * 19 + 1];
            _core.ToDecString(_a, _decBuf, out int start);
            _dec = _decBuf.AsSpan(start).ToArray();
            _fromDecBuf = new ulong[_dec.Length / 19 + 1];
        }

        [Benchmark, BenchmarkCategory("Add")]
        public ulong Add() => _core.Add(_a, _b, _sum);

        [Benchmark, BenchmarkCategory("Sub")]
        public int Sub() => _core.Sub(_a, _b, _sum);

        [Benchmark, BenchmarkCategory("Mult")]
        public void Mult() => _core.Mult(_a, _b, _prod);

        [Benchmark, BenchmarkCategory("Compare")]
        public int Compare() => _core.Compare(_a, _aNear);

        [Benchmark, BenchmarkCategory("DivWord")]
        public ulong DivByWord()
        {
            _core.Div(_a, 0x1234_5678_9ABC_DEF1UL, _sum, out ulong r);
            return r;
        }

        /// <summary>2N / N limbs: N+1 quotient limbs, the general multi-word path.</summary>
        [Benchmark, BenchmarkCategory("Div")]
        public void Div() => _core.Div(_a2, _b, _quot, _rem);

        [Benchmark, BenchmarkCategory("And")]
        public int AndNegative() => _core.And(_a, true, _b, false, _sum);

        [Benchmark, BenchmarkCategory("Shl")]
        public void ShiftLeft() => _core.ShiftLeft(_a, 37, _wide);

        [Benchmark, BenchmarkCategory("ToDec")]
        public int ToDecString()
        {
            _core.ToDecString(_a, _decBuf, out int start);
            return start;
        }

        [Benchmark, BenchmarkCategory("FromDec")]
        public int FromDecString()
        {
            _core.FromDecString(_dec, _fromDecBuf, out int start);
            return start;
        }
    }
}
