// AArch64: the latency and the throughput of the multiply instructions the kernels use (scalar
// mul / umulh, NEON umull / mul .4s / sqdmulh), in cycles. The cycle is calibrated by a
// chain of dependent adds (one cycle each). Latency: one chain, each instruction on the last
// result; throughput: eight independent chains interleaved.
//   fullint_mul_probe [--cpu K]
#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include <sched.h>

namespace {

constexpr long N = 20000000;   // instructions a measurement: the loop body has 8, N / 8 passes

template <typename F>
double Ns(F f)   // ns an instruction, the minimum of 5 runs
{
    double best = 1e300;
    for (int r = 0; r < 5; r++)
    {
        auto t0 = std::chrono::steady_clock::now();
        f();
        best = std::min(best, std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / N);
    }
    return best;
}

#define REP8(x) x x x x x x x x

double AddChain()
{
    return Ns([] { uint64_t a = 1, b = 3; for (long i = 0; i < N / 8; i++) asm volatile(REP8("add %0, %0, %1\n") : "+r"(a) : "r"(b)); });
}

double MulLat()
{
    return Ns([] { uint64_t a = 1, b = 3; for (long i = 0; i < N / 8; i++) asm volatile(REP8("mul %0, %0, %1\n") : "+r"(a) : "r"(b)); });
}

double UmulhLat()
{
    return Ns([] { uint64_t a = ~0ull, b = ~0ull; for (long i = 0; i < N / 8; i++) asm volatile(REP8("umulh %0, %0, %1\n") : "+r"(a) : "r"(b)); });
}

double MulTput()
{
    return Ns([] {
        uint64_t a0 = 1, a1 = 2, a2 = 3, a3 = 4, a4 = 5, a5 = 6, a6 = 7, a7 = 8, b = 3;
        for (long i = 0; i < N / 8; i++)
            asm volatile("mul %0, %0, %8\n mul %1, %1, %8\n mul %2, %2, %8\n mul %3, %3, %8\n"
                         "mul %4, %4, %8\n mul %5, %5, %8\n mul %6, %6, %8\n mul %7, %7, %8\n"
                         : "+r"(a0), "+r"(a1), "+r"(a2), "+r"(a3), "+r"(a4), "+r"(a5), "+r"(a6), "+r"(a7) : "r"(b));
    });
}

double UmulhTput()
{
    return Ns([] {
        uint64_t a0 = ~1ull, a1 = ~2ull, a2 = ~3ull, a3 = ~4ull, a4 = ~5ull, a5 = ~6ull, a6 = ~7ull, a7 = ~8ull, b = ~0ull;
        for (long i = 0; i < N / 8; i++)
            asm volatile("umulh %0, %0, %8\n umulh %1, %1, %8\n umulh %2, %2, %8\n umulh %3, %3, %8\n"
                         "umulh %4, %4, %8\n umulh %5, %5, %8\n umulh %6, %6, %8\n umulh %7, %7, %8\n"
                         : "+r"(a0), "+r"(a1), "+r"(a2), "+r"(a3), "+r"(a4), "+r"(a5), "+r"(a6), "+r"(a7) : "r"(b));
    });
}

double MulUmulhPairTput()   // a word product as the kernels take it: mul and umulh of the same operands
{
    return Ns([] {
        uint64_t a0 = 1, a1 = 2, a2 = 3, a3 = 4, l0, l1, l2, l3, b = ~0ull;
        for (long i = 0; i < N / 8; i++)
            asm volatile("mul %4, %0, %8\n umulh %0, %0, %8\n mul %5, %1, %8\n umulh %1, %1, %8\n"
                         "mul %6, %2, %8\n umulh %2, %2, %8\n mul %7, %3, %8\n umulh %3, %3, %8\n"
                         : "+r"(a0), "+r"(a1), "+r"(a2), "+r"(a3), "=&r"(l0), "=&r"(l1), "=&r"(l2), "=&r"(l3) : "r"(b));
    });
}

// NEON: eight independent vectors, the instruction on each
#define NEON8(op) \
    asm volatile(op " v0.2d, v0.2s, v8.2s\n" op " v1.2d, v1.2s, v8.2s\n" op " v2.2d, v2.2s, v8.2s\n" op " v3.2d, v3.2s, v8.2s\n" \
                 op " v4.2d, v4.2s, v8.2s\n" op " v5.2d, v5.2s, v8.2s\n" op " v6.2d, v6.2s, v8.2s\n" op " v7.2d, v7.2s, v8.2s\n" \
                 ::: "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7")
#define NEON8S(op) \
    asm volatile(op " v0.4s, v0.4s, v8.4s\n" op " v1.4s, v1.4s, v8.4s\n" op " v2.4s, v2.4s, v8.4s\n" op " v3.4s, v3.4s, v8.4s\n" \
                 op " v4.4s, v4.4s, v8.4s\n" op " v5.4s, v5.4s, v8.4s\n" op " v6.4s, v6.4s, v8.4s\n" op " v7.4s, v7.4s, v8.4s\n" \
                 ::: "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7")

double UmullTput() { return Ns([] { for (long i = 0; i < N / 8; i++) NEON8("umull"); }); }
double MulQTput() { return Ns([] { for (long i = 0; i < N / 8; i++) NEON8S("mul"); }); }
double MulQLat() { return Ns([] { for (long i = 0; i < N / 8; i++) asm volatile(REP8("mul v0.4s, v0.4s, v8.4s\n") ::: "v0"); }); }
double UmullLat() { return Ns([] { for (long i = 0; i < N / 8; i++) asm volatile(REP8("umull v0.2d, v0.2s, v8.2s\n") ::: "v0"); }); }
double AddQTput() { return Ns([] { for (long i = 0; i < N / 8; i++) NEON8S("add"); }); }

}  // namespace

int main(int argc, char** argv)
{
    int cpu = -1;
    for (int i = 1; i < argc; i++)
        if (std::strcmp(argv[i], "--cpu") == 0 && i + 1 < argc) cpu = std::atoi(argv[++i]);
    if (cpu >= 0)
    {
        cpu_set_t cpus;
        CPU_ZERO(&cpus);
        CPU_SET(cpu, &cpus);
        sched_setaffinity(0, sizeof cpus, &cpus);
    }
    double cycle = AddChain();   // ns a cycle
    std::printf("cycle %.3f ns (%.2f GHz, from a dependent add chain)\n", cycle, 1 / cycle);
    std::printf("%-34s %8s\n", "", "cycles");
    auto row = [&](const char* name, double ns) { std::printf("%-34s %8.2f\n", name, ns / cycle); };
    row("mul        latency", MulLat());
    row("mul        throughput (1/n a cycle)", MulTput());
    row("umulh      latency", UmulhLat());
    row("umulh      throughput", UmulhTput());
    row("mul+umulh  a pair (a word product)", MulUmulhPairTput() * 2);
    row("NEON umull .2d latency", UmullLat());
    row("NEON umull .2d throughput", UmullTput());
    row("NEON mul .4s latency", MulQLat());
    row("NEON mul .4s throughput", MulQTput());
    row("NEON add .4s throughput", AddQTput());
    return 0;
}
