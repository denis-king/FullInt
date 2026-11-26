using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using FullNumerics;

// The package's consumer: which core runs (the native one is expected unless EXPECT_CORE says
// otherwise), and a few results against BigInteger. Exit code 0: all good.
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

string expected = Environment.GetEnvironmentVariable("EXPECT_CORE")
                  ?? (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "FullIntCoreNative32" : "FullIntCoreNative64");
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

Console.WriteLine(failed == 0 ? "OK" : $"{failed} failed");
return failed == 0 ? 0 : 1;
