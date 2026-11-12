using System.Numerics;
using BenchmarkDotNet.Attributes;
using FullInt.Core;

namespace FullInt.Benchmarks
{
    /// <summary>
    /// Public API level: FullInt64 operators (allocation, sign handling, normalization included)
    /// against System.Numerics.BigInteger as the reference. Uses the core the library picks
    /// itself (native on win-x64; set FULLINT_FORCE_MANAGED=1 for the fallback).
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class ApiBenchmarks
    {
        [Params(1, 4, 16, 64, 256, 1024)]
        public int N { get; set; }

        private FullInt64 _a = null!, _b = null!, _a2 = null!;
        private BigInteger _ba, _bb, _ba2;
        private string _decString = null!;

        [GlobalSetup]
        public void Setup()
        {
            ulong[] a = Data.Limbs(N, 1), b = Data.Limbs(N, 2), a2 = Data.Limbs(2 * N, 3);
            _a = new FullInt64(false, a);
            _b = new FullInt64(true, b);
            _a2 = new FullInt64(false, a2);
            _ba = Data.ToBig(a);
            _bb = -Data.ToBig(b);
            _ba2 = Data.ToBig(a2);
            _decString = _ba.ToString();
        }

        [Benchmark, BenchmarkCategory("Add")] public FullInt64 Add() => _a + _b;
        [Benchmark(Baseline = true), BenchmarkCategory("Add")] public BigInteger Add_Big() => _ba + _bb;

        [Benchmark, BenchmarkCategory("Mult")] public FullInt64 Mult() => _a * _b;
        [Benchmark(Baseline = true), BenchmarkCategory("Mult")] public BigInteger Mult_Big() => _ba * _bb;

        [Benchmark, BenchmarkCategory("Div")] public FullInt64 Div() => _a2 / _b;
        [Benchmark(Baseline = true), BenchmarkCategory("Div")] public BigInteger Div_Big() => _ba2 / _bb;

        [Benchmark, BenchmarkCategory("ToString")] public string ToDec() => _a.ToString(10);
        [Benchmark(Baseline = true), BenchmarkCategory("ToString")] public string ToDec_Big() => _ba.ToString();

        [Benchmark, BenchmarkCategory("Parse")] public FullInt64 Parse() => FullInt64.Parse(_decString);
        [Benchmark(Baseline = true), BenchmarkCategory("Parse")] public BigInteger Parse_Big() => BigInteger.Parse(_decString);
    }
}
