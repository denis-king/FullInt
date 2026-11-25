// The NTT kernels of AArch64 one by one (ntt_portable.cpp, NttNeon.s) on a transform of L
// values: where a product's time goes. The twiddles are arbitrary values below p (the time
// does not depend on them).
//   fullint_ntt_parts [L] [--cpu K]
#include <fullnumerics/kernels.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <random>
#include <vector>

#include <sched.h>

namespace {

double Time(const std::function<void()>& f)   // ns a call, the minimum of 7 batches of >= 20 ms
{
    auto once = [&](int n) {
        auto t0 = std::chrono::steady_clock::now();
        for (int i = 0; i < n; i++) f();
        return std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / n;
    };
    int n = std::max(1, (int)(2e7 / std::max(once(1), 1.0)));
    double best = 1e300;
    for (int r = 0; r < 7; r++) best = std::min(best, once(n));
    return best;
}

}  // namespace

int main(int argc, char** argv)
{
    int64_t L = 16384;
    int cpu = 7;
    for (int i = 1; i < argc; i++)
    {
        if (std::strcmp(argv[i], "--cpu") == 0 && i + 1 < argc) cpu = std::atoi(argv[++i]);
        else L = std::atoll(argv[i]);
    }
    cpu_set_t cpus;
    CPU_ZERO(&cpus);
    CPU_SET(cpu, &cpus);
    sched_setaffinity(0, sizeof cpus, &cpus);

    const uint32_t p = 943718401u;
    uint32_t pinv = 1;
    for (int i = 0; i < 5; i++) pinv *= 2 - p * pinv;
    std::mt19937 rng(1);
    std::vector<uint32_t> W(L + 8), Wq(L + 8), x(L), y(L), k(72);
    for (int64_t i = 0; i < L + 8; i++) { W[i] = rng() % p; Wq[i] = W[i] * pinv; }
    for (auto& v : x) v = rng() % p;
    for (auto& v : y) v = rng() % p;
    for (int i = 0; i < 72; i++) k[i] = rng() % p;
    for (int i = 64; i < 72; i++) k[i] = pinv;
    std::vector<uint64_t> a(L / 4), r(L / 2);
    for (auto& v : a) v = ((uint64_t)rng() << 32) | rng();
    uint64_t crt[10] = { 943718401u, 918552577u, 880803841u, 1, 1, 1, 1, 1, 1, 1 };
    std::vector<uint32_t> x1(L), x2(L);
    for (int64_t i = 0; i < L; i++) { x1[i] = rng() % 918552577u; x2[i] = rng() % 880803841u; }

    std::printf("L = %lld values; ns a call (per value)\n", (long long)L);
    auto row = [&](const char* name, double ns) { std::printf("%-28s %10.0f ns  %6.2f ns/value\n", name, ns, ns / L); };
    row("Dif2 (2 levels, h = L/2)", Time([&] { Fullint64NttDif2WIN_neon(x.data(), L, L / 2, W.data(), Wq.data(), p); }));
    row("Dif2 (2 levels, h = 32)", Time([&] { Fullint64NttDif2WIN_neon(x.data(), L, 32, W.data(), Wq.data(), p); }));
    row("Dif1 (h = 8)", Time([&] { Fullint64NttDif1WIN_neon(x.data(), L, 8, W.data(), Wq.data(), p); }));
    row("Dif3 (levels 4, 2, 1)", Time([&] { Fullint64NttDif3WIN_neon(x.data(), L, W.data(), Wq.data(), p); }));
    row("Dit2 (2 levels, h = 32)", Time([&] { Fullint64NttDit2WIN_neon(x.data(), L, 32, W.data(), Wq.data(), p); }));
    row("Dit3 (levels 1, 2, 4)", Time([&] { Fullint64NttDit3WIN_neon(x.data(), L, W.data(), Wq.data(), p); }));
    row("Point", Time([&] { Fullint64NttPointWIN_neon(x.data(), y.data(), L, p, pinv); }));
    row("Scale", Time([&] { Fullint64NttScaleWIN_neon(x.data(), L, p, 12345, 12345 * pinv); }));
    row("R3Dif (M = L/3 of L)", Time([&] { Fullint64NttR3DifWIN_neon(x.data(), L / 3 / 8 * 8, k.data(), p); }));
    row("R3Dit (M = L/3 of L)", Time([&] { Fullint64NttR3DitWIN_neon(x.data(), L / 3 / 8 * 8, k.data(), p); }));
    row("Load (L/4 words, zeros to L)", Time([&] { Fullint64NttLoadWIN_portable(a.data(), L / 4, x.data(), L, p); }));
    row("Crt (L/2 digits)", Time([&] { Fullint64NttCrtWIN_portable(x.data(), x1.data(), x2.data(), L / 2, r.data(), L / 4, crt); }));
    row("Load asm", Time([&] { Fullint64NttLoadWIN_neon(a.data(), L / 4, x.data(), L, p); }));
    row("Crt asm", Time([&] { Fullint64NttCrtWIN_neon(x.data(), x1.data(), x2.data(), L / 2, r.data(), L / 4, crt); }));
    row("Dif1 portable (h = 8)", Time([&] { Fullint64NttDif1WIN_portable(x.data(), L, 8, W.data(), Wq.data(), p); }));
    return 0;
}
