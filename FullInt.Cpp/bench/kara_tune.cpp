// Picks the Karatsuba and Toom-3 thresholds of the C++ build (FullInt.Benchmarks
// KaratsubaTune.cs does it for .NET, where every kernel call costs ~10-15 ns more).
// N x N products; each variant timed in batches >= 3 ms, minimum over rounds, the variants
// interleaved. Ratio = variant time / base time (< 1: the variant is faster).
//
//   fullint_kara_tune            Karatsuba threshold: base = basecase kernel alone
//   fullint_kara_tune toom       Toom-3 threshold: base = Karatsuba alone (at --kara T)
//   fullint_kara_tune toom4      Toom-4 threshold: base = Karatsuba + Toom-3 (--kara, default Toom-3)
//   fullint_kara_tune fft        FFT threshold: base = the Toom levels (default thresholds)
//   --cpu K: the core to pin to (default 2; the S21+'s big core: 7)
#include <fullnumerics/Karatsuba.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <random>
#include <string>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#else
#include <sched.h>
#endif

using namespace fullnumerics;

namespace {

double Batch(const std::function<void()>& f, long long iters)
{
    auto t0 = std::chrono::steady_clock::now();
    for (long long i = 0; i < iters; i++) f();
    return std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / iters;
}

long long Iters(const std::function<void()>& f)
{
    double per = std::max(Batch(f, 1), 1.0);
    per = std::min(per, Batch(f, 3));
    return std::max(1LL, (long long)(3e6 / per));
}

}  // namespace

int main(int argc, char** argv)
{
    bool fft = argc > 1 && std::strcmp(argv[1], "fft") == 0;
    bool toom4 = argc > 1 && std::strcmp(argv[1], "toom4") == 0;
    bool toom6 = argc > 1 && std::strcmp(argv[1], "toom6") == 0;
    bool toom = fft || toom4 || toom6 || (argc > 1 && std::strcmp(argv[1], "toom") == 0);
    int kara = Karatsuba::Threshold, rounds = 7, cpu = 2;
    for (int i = 1; i < argc; i++)
    {
        if (std::strcmp(argv[i], "--kara") == 0 && i + 1 < argc) kara = std::atoi(argv[++i]);
        if (std::strcmp(argv[i], "--rounds") == 0 && i + 1 < argc) rounds = std::atoi(argv[++i]);
        if (std::strcmp(argv[i], "--cpu") == 0 && i + 1 < argc) cpu = std::atoi(argv[++i]);
    }
#if defined(_WIN32)
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadAffinityMask(GetCurrentThread(), 1ull << cpu);
#else
    cpu_set_t cpus;
    CPU_ZERO(&cpus);
    CPU_SET(cpu, &cpus);
    sched_setaffinity(0, sizeof cpus, &cpus);
#endif

    std::vector<int> sizes = fft ? std::vector<int>{ 2048, 4096, 8192, 16384, 32768, 65536, 131072 }
                           : toom6 ? std::vector<int>{ 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192 }
                           : toom4 ? std::vector<int>{ 256, 384, 512, 768, 1024, 1536, 2048, 4096, 8192 }
                           : toom ? std::vector<int>{ 64, 96, 128, 192, 256, 384, 512, 768, 1024, 2048, 4096 }
                                  : std::vector<int>{ 12, 16, 20, 24, 32, 48, 64, 96, 128, 256, 512, 1024 };
    std::vector<int> thresholds = fft ? std::vector<int>{ 1024, 2048, 4096, 8192, 16384, 32768 }
                                : toom6 ? std::vector<int>{ 384, 512, 768, 1024, 1536, 2048, 3072 }
                                : toom4 ? std::vector<int>{ 192, 256, 384, 512, 768, 1024, 1536 }
                                : toom ? std::vector<int>{ 48, 64, 96, 128, 192, 256, 384 }
                                       : std::vector<int>{ 8, 12, 16, 20, 24, 28, 32, 40, 48, 64 };

    std::printf(fft ? "FFT threshold T (Karatsuba at %d, Toom defaults); ratio = with the FFT from T / without\n"
              : toom6 ? "Toom-6 threshold T (Karatsuba at %d, Toom-3/4 defaults, no FFT); ratio = with Toom-6 from T / without\n"
              : toom4 ? "Toom-4 threshold T (Karatsuba at %d, Toom-3 default); ratio = with Toom-4 from T / without\n"
              : toom ? "Toom-3 threshold T (Karatsuba at %d); ratio = with Toom-3 from T / Karatsuba alone\n"
                     : "Karatsuba threshold T%.0d; ratio = Karatsuba from T / basecase kernel alone\n", kara);
    std::printf("%6s %12s", "N", "base");
    for (int t : thresholds) std::printf(" %7d", t);
    std::printf("\n");

    std::mt19937_64 rng(1);
    for (int n : sizes)
    {
        std::vector<uint64_t> a(n), b(n), r(2 * n);
        for (auto& x : a) x = rng();
        for (auto& x : b) x = rng();
        a[0] |= 1;
        b[0] |= 1;

        std::vector<std::function<void()>> fs;
        int t3 = Karatsuba::Toom3Threshold;
        int t4 = Karatsuba::Toom4Threshold;
        int t6 = Karatsuba::Toom6Threshold;
        constexpr int off = 1 << 30;
        if (fft) fs.push_back([&] { Karatsuba::Multiply(a, b, r, kara, t3, t4, t6, off); });
        else if (toom6) fs.push_back([&] { Karatsuba::Multiply(a, b, r, kara, t3, t4, off, off); });
        else if (toom4) fs.push_back([&] { Karatsuba::Multiply(a, b, r, kara, t3, off, off, off); });
        else if (toom) fs.push_back([&] { Karatsuba::Multiply(a, b, r, kara, off, off, off, off); });
        else fs.push_back([&] { FullIntCore::Mult(a, b, r); });
        for (int t : thresholds)
        {
            if (fft) fs.push_back([&, t] { Karatsuba::Multiply(a, b, r, kara, t3, t4, t6, t); });
            else if (toom6) fs.push_back([&, t] { Karatsuba::Multiply(a, b, r, kara, t3, t4, t, off); });
            else if (toom4) fs.push_back([&, t] { Karatsuba::Multiply(a, b, r, kara, t3, t, off, off); });
            else if (toom) fs.push_back([&, t] { Karatsuba::Multiply(a, b, r, kara, t, off, off, off); });
            else fs.push_back([&, t] { Karatsuba::Multiply(a, b, r, t, off, off, off, off); });
        }

        std::vector<long long> iters;
        for (auto& f : fs) iters.push_back(Iters(f));
        std::vector<double> best(fs.size(), 1e300);
        for (int round = 0; round < rounds; round++)
            for (size_t i = 0; i < fs.size(); i++) best[i] = std::min(best[i], Batch(fs[i], iters[i]));

        std::printf("%6d %10.0f ns", n, best[0]);
        for (size_t i = 1; i < fs.size(); i++) std::printf(" %7.3f", best[i] / best[0]);
        std::printf("\n");
        std::fflush(stdout);
    }
    return 0;
}
