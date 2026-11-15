using BenchmarkDotNet.Running;
using FullInt.Benchmarks;

// ab <base.dll> <cur.dll> [filter] [rounds]  → precise A/B of two native builds (see AbCompare)
// anything else                              → BenchmarkDotNet (--filter, --list flat, ...)
if (args.Length > 0 && args[0] == "ab")
{
    return AbCompare.Run(args.Skip(1).ToArray());
}
// kara [rounds]                              → Karatsuba threshold tuning (see KaratsubaTune)
if (args.Length > 1 && args[0] == "kara" && args[1] == "sqr")
{
    return KaratsubaTune.RunSqr(args.Skip(2).ToArray());
}
if (args.Length > 0 && args[0] == "kara")
{
    return KaratsubaTune.Run(args.Skip(1).ToArray());
}

// dec [rounds]                               → decimal conversion thresholds (see BzTune.RunDecimal)
if (args.Length > 0 && args[0] == "dec")
{
    return BzTune.RunDecimal(args.Skip(1).ToArray());
}
// newton [rounds] [ratio]                   → Newton division against Burnikel–Ziegler (see BzTune.RunNewton)
if (args.Length > 0 && args[0] == "newton")
{
    return BzTune.RunNewton(args.Skip(1).ToArray());
}

// decprep [rounds]                          → ToString leaf / prepared-division thresholds (see BzTune.RunDecimalPrepared)
if (args.Length > 0 && args[0] == "decprep")
{
    return BzTune.RunDecimalPrepared(args.Skip(1).ToArray());
}

// quot [rounds]                             → the quotient alone against the full division (see BzTune.RunQuotient)
if (args.Length > 0 && args[0] == "quot")
{
    return BzTune.RunQuotient(args.Skip(1).ToArray());
}

// bz [rounds]                                → Burnikel–Ziegler threshold tuning (see BzTune)
if (args.Length > 0 && args[0] == "bz")
{
    return BzTune.Run(args.Skip(1).ToArray());
}
// cross [--max N] [--csv file]               → the .NET half of GMP / C++ / .NET / BigInteger (see CrossBench)
if (args.Length > 0 && args[0] == "cross")
{
    return CrossBench.Run(args.Skip(1).ToArray());
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
