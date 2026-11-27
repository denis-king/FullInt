using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using FullNumerics;

// The package's consumer: which core runs (the native one is expected unless EXPECT_CORE says
// otherwise), a few results against BigInteger, and a coarse speed floor: on 4096-word numbers the
// native core must beat BigInteger by MIN_SPEEDUP (default 3; measured 9-30x) - wide enough for a
// shared CI machine's noise, narrow enough to catch a wrong library or a silent fallback. Exit
// code 0: all good.
int failed = 0;
void Check(string what, bool ok)
{
    if (!ok) { failed++; Console.WriteLine("FAIL: " + what); }
}

var engine = typeof(FullInt).Assembly.GetType("FullNumerics.FullIntEngine", throwOnError: true)!;
var core = engine.GetField("_core", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
string coreName = core.GetType().Name;
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription.Trim()}, " +
                  $"process {RuntimeInformation.ProcessArchitecture}: core {coreName}");

string? expectEnv = Environment.GetEnvironmentVariable("EXPECT_CORE");   // (empty: not set - a CI matrix leaves it so)
// (iOS: no native library in the package - the managed core; the app gets no environment there)
bool ios = RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));
string expected = !string.IsNullOrEmpty(expectEnv) ? expectEnv!
                  : ios ? "FullIntCoreManaged"
                  : (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "FullIntCoreNative32"
                    : RuntimeInformation.ProcessArchitecture == Architecture.Arm ? "FullIntCoreNativeArm32"
                    : "FullIntCoreNative64");
Check($"core {coreName}, expected {expected}", coreName == expected);

var rnd = new Random(12345);
for (int bits = 64; bits <= 200_000; bits *= 3)
{
    var bytes = new byte[bits / 8 + 1];
    rnd.NextBytes(bytes);
    var ba = new BigInteger(bytes);
    rnd.NextBytes(bytes);
    var bb = new BigInteger(bytes) / 7 + 1;
    var a = (FullInt)ba;
    var b = (FullInt)bb;
    Check($"{bits}: +", (BigInteger)(a + b) == ba + bb);
    Check($"{bits}: *", (BigInteger)(a * b) == ba * bb);
    Check($"{bits}: /", (BigInteger)(a / b) == ba / bb);
    Check($"{bits}: %", (BigInteger)(a % b) == ba % bb);
    Check($"{bits}: ToString", a.ToString() == ba.ToString());
    Check($"{bits}: Parse", FullInt.Parse(ba.ToString()) == a);
}
Check("Pow", FullInt.Pow(3, 5000).ToString() == BigInteger.Pow(3, 5000).ToString());

// the speed floor (the native cores only: the managed one is slower by design)
if (coreName != "FullIntCoreManaged")
{
    double min = double.TryParse(Environment.GetEnvironmentVariable("MIN_SPEEDUP"), out var m) ? m : 3;
    var x = new byte[4096 * 8 + 1];
    var y = new byte[2048 * 8 + 1];
    rnd.NextBytes(x);
    rnd.NextBytes(y);
    x[x.Length - 1] = 0;   // non-negative
    y[y.Length - 1] = 0;
    BigInteger bx = new BigInteger(x), by = new BigInteger(y);
    FullInt fx = (FullInt)bx, fy = (FullInt)by;
    void Speed(string op, Action ours, Action theirs)
    {
        double tOurs = Best(ours), tTheirs = Best(theirs);
        double ratio = tTheirs / tOurs;
        Console.WriteLine($"speed {op}: FullInt {tOurs * 1e3:F2} ms, BigInteger {tTheirs * 1e3:F2} ms, {ratio:F1}x");
        Check($"speed {op}: {ratio:F1}x, at least {min}x expected", ratio >= min);
    }
    object? sink = null;
    Speed("a * b (4096 x 4096 words)", () => sink = fx * fx, () => sink = bx * bx);
    Speed("a / b (4096 / 2048 words)", () => sink = fx / fy, () => sink = bx / by);
    Speed("ToString (2048 words)", () => sink = fy.ToString(), () => sink = by.ToString());
    GC.KeepAlive(sink);
}

// the best of a few runs after a warm-up (the JIT's tiers), in seconds
static double Best(Action f)
{
    for (int i = 0; i < 5; i++) f();
    double best = double.MaxValue;
    for (int i = 0; i < 7; i++)
    {
        var sw = Stopwatch.StartNew();
        f();
        best = Math.Min(best, sw.Elapsed.TotalSeconds);
    }
    return best;
}

Console.WriteLine(failed == 0 ? "OK" : $"{failed} failed");
return failed == 0 ? 0 : 1;
