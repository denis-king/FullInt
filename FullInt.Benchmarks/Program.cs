using BenchmarkDotNet.Running;
using FullInt.Benchmarks;

// ab <base.dll> <cur.dll> [filter] [rounds]  → precise A/B of two native builds (see AbCompare)
// anything else                              → BenchmarkDotNet (--filter, --list flat, ...)
if (args.Length > 0 && args[0] == "ab")
{
    return AbCompare.Run(args.Skip(1).ToArray());
}
// kara [rounds]                              → Karatsuba threshold tuning (see KaratsubaTune)
if (args.Length > 0 && args[0] == "kara")
{
    return KaratsubaTune.Run(args.Skip(1).ToArray());
}

// cross [--max N] [--csv file]               → the .NET half of GMP / C++ / .NET / BigInteger (see CrossBench)
if (args.Length > 0 && args[0] == "cross")
{
    return CrossBench.Run(args.Skip(1).ToArray());
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
