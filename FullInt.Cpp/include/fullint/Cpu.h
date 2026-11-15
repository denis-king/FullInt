// Which kernel versions this CPU can run (C#: CpuFeatures in FullIntCoreWindowsX64.cs).
// Detected once at start-up by the Cpu.asm kernel (cpuid / xgetbv).
// FULLINT_CPU=baseline in the environment forces the baseline kernels (x86-64 as on a Core 2):
// tests run both, and a "how does an old CPU do" measurement on a new one.
#pragma once

#include <fullint/kernels.h>

#include <cstdlib>
#include <cstring>

namespace fullint {

struct Cpu
{
    // Recip is not a CPU feature: the division by reciprocal (DivRecip.asm) runs on any x86-64,
    // set unless FULLINT_CPU=baseline (then the original Div kernels run)
    enum : int { Bmi2 = 1, Adx = 2, Avx2 = 4, Recip = 8 };

    static int Detect() noexcept
    {
#pragma warning(suppress : 4996)   // getenv: read once at start-up
        const char* forced = std::getenv("FULLINT_CPU");
        if (forced != nullptr && std::strcmp(forced, "baseline") == 0) return 0;
#if defined(_M_X64) || defined(__x86_64__)
        return Fullint64CpuFeatures() | Recip;
#else
        return 0;   // x86: the baseline kernels only, for now
#endif
    }

    static inline const int Features = Detect();

    static bool Has(int features) noexcept { return (Features & features) == features; }

    // which kernel versions this process uses (FullIntCore picks by the same rules)
    static const char* Describe() noexcept
    {
        bool adx = Has(Bmi2 | Adx), avx2 = Has(Avx2);
        if (!Has(Recip)) return "all: baseline";
        return adx && avx2 ? "Mult: adx; And/Or/Xor, Shl/Shr, Compare: avx2; Div: recip; other: baseline"
             : adx         ? "Mult: adx; Div: recip; other: baseline"
             : avx2        ? "And/Or/Xor, Shl/Shr, Compare: avx2; Div: recip; other: baseline"
                           : "Div: recip; other: baseline";
    }
};

}  // namespace fullint
