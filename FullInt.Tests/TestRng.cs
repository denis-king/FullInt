using System.Runtime.CompilerServices;

namespace FullInt.Tests
{
    /// <summary>
    /// Reproducible replacement for RandomNumberGenerator in the randomized tests (same GetBytes
    /// call, same using-block). Every run gets a fresh seed, so the tests keep exploring new
    /// numbers, but the seed is printed to the test output and can be replayed:
    ///
    ///   $env:FULLINT_TEST_SEED = 12345            # the seed from the failed run's output
    ///   $env:FULLINT_FORCE_MANAGED = "1"          # only if the failed run used the managed core
    ///   dotnet test FullInt.Tests -c Release --filter "Name=&lt;test&gt;"
    ///
    /// With FULLINT_TEST_SEED set, every TestRng uses that seed, so the same numbers come back.
    /// </summary>
    internal sealed class TestRng : IDisposable
    {
        private readonly Random _random;

        public int Seed { get; }

        private TestRng(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        public static TestRng Create([CallerMemberName] string caller = "")
        {
            string? fixedSeed = Environment.GetEnvironmentVariable("FULLINT_TEST_SEED");
#if NETFRAMEWORK
            int seed = int.TryParse(fixedSeed, out int s) ? s : Guid.NewGuid().GetHashCode() & int.MaxValue;
#else
            int seed = int.TryParse(fixedSeed, out int s) ? s : Random.Shared.Next();
#endif
            string core = Environment.GetEnvironmentVariable("FULLINT_FORCE_MANAGED") == "1" ? "managed" : "default";
            Console.WriteLine($"[TestRng] {caller}: seed={seed} core={core}  (replay: FULLINT_TEST_SEED={seed})");
            return new TestRng(seed);
        }

        public void GetBytes(byte[] buffer) => _random.NextBytes(buffer);

        public override string ToString() => $"seed={Seed}";

        public void Dispose() { }
    }
}
