// Which kernel versions this CPU can run (C#: CpuFeatures in FullIntCoreNative64.cs).
// Detected once at start-up by the Cpu.asm kernel (cpuid / xgetbv).
// FULLINT_CPU=baseline in the environment forces the baseline kernels (x86-64 as on a Core 2):
// tests run both, and a "how does an old CPU do" measurement on a new one.
#pragma once

// small hot helpers on the short-number paths (Prepare, Normalize, the Add / Sub wrappers):
// MSVC left them as calls inside the larger into-functions (a few ns each on 1-8 words)
#if defined(_MSC_VER)
#define FULLINT_FORCEINLINE __forceinline
#define FULLINT_NOINLINE __declspec(noinline)
#else
#define FULLINT_FORCEINLINE inline __attribute__((always_inline))
#define FULLINT_NOINLINE __attribute__((noinline))
#endif

#include <fullnumerics/kernels.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace fullnumerics {

struct Cpu
{
    // Recip is not a CPU feature: the division by reciprocal (DivRecip.asm, ToDecStringRecip.asm)
    // runs on any x86-64; set unless FULLINT_CPU=baseline (then the original kernels run)
    // Sse2: x86 only (x64 has it always; its kernels are the AVX2 ones)
    enum : int { Bmi2 = 1, Adx = 2, Avx2 = 4, Recip = 8, Sse2 = 16 };

    static int Detect() noexcept
    {
#pragma warning(suppress : 4996)   // getenv: read once at start-up
        const char* forced = std::getenv("FULLINT_CPU");
        if (forced != nullptr && std::strcmp(forced, "baseline") == 0) return 0;
#if FULLINT_X64
        return Fullint64CpuFeatures() | Recip;
#elif FULLINT_X86
        return Fullint64CpuFeatures() | Recip;   // x86: SSE2 (x86/Cpu.asm) and the reciprocal division
#else
        return Recip;   // AArch64: the divisions by reciprocal (no 128 / 64 division instruction)
#endif
    }

    static inline const int Features = Detect();

    static bool Has(int features) noexcept { return (Features & features) == features; }

    // AArch64: whether the cores multiply narrowly, for the thresholds (Karatsuba.h). The scalar
    // multiplier of a narrow core takes several cycles a 64 x 64 -> 128 product - Neoverse-N1
    // (a Cortex-A76 relative): mul 3, umulh 4, 7 a word product (fullint_mul_probe) - so the
    // splitting algorithms pay off much earlier than on a wide one (Cortex-X1: about 2 cycles).
    // From MIDR (implementer and part) of every CPU in /proc/cpuinfo (Linux, Android): narrow
    // only when all of them are known narrow ones - a big.LITTLE phone takes its big cores'
    // class. Narrow: ARM's Cortex-A53 / A55 / A57 / A72 .. A77 and Neoverse-N1 (measured: N1;
    // the others by their multiplier generation); anything else, or no /proc/cpuinfo (macOS,
    // Windows): wide (measured: X1). FULLINT_ARM_CORE=narrow / wide overrides.
    static bool ArmNarrow() noexcept
    {
        static const bool narrow = DetectArmNarrow();
        return narrow;
    }

    static bool DetectArmNarrow() noexcept
    {
#pragma warning(suppress : 4996)   // getenv: read once
        const char* forced = std::getenv("FULLINT_ARM_CORE");
        if (forced != nullptr) return std::strcmp(forced, "narrow") == 0;
#if FULLINT_ARM64
#pragma warning(suppress : 4996)
        std::FILE* f = std::fopen("/proc/cpuinfo", "r");
        if (f == nullptr) return false;
        char line[256];
        unsigned implementer = 0;
        int parts = 0, narrow = 0;
        while (std::fgets(line, sizeof line, f))
        {
            unsigned v;
            if (std::sscanf(line, "CPU implementer : %x", &v) == 1) implementer = v;
            else if (std::sscanf(line, "CPU part : %x", &v) == 1)
            {
                parts++;
                bool knownNarrow = implementer == 0x41 && (v == 0xd03 || v == 0xd04 || v == 0xd05 || v == 0xd07
                    || (v >= 0xd08 && v <= 0xd0e));   // A53, A35, A55, A57; A72, A73, A75, A76, N1, A77, A76AE
                if (knownNarrow) narrow++;
            }
        }
        std::fclose(f);
        return parts > 0 && narrow == parts;
#else
        return false;
#endif
    }

    // which kernel versions this process uses (FullIntCore picks by the same rules)
    static const char* Describe() noexcept
    {
        bool adx = Has(Bmi2 | Adx), avx2 = Has(Avx2);
#if FULLINT_GENERIC_KERNELS && FULLINT_ARM64_ASM
        if (ArmNarrow())
            return Has(Recip) ? "all: AArch64 asm (Div, ToDecString: recip); thresholds: narrow multiplier"
                              : "all: AArch64 asm (baseline); thresholds: narrow multiplier";
        return Has(Recip) ? "all: AArch64 asm (Div, ToDecString: recip); thresholds: wide multiplier"
                          : "all: AArch64 asm (baseline); thresholds: wide multiplier";
#elif FULLINT_GENERIC_KERNELS
        return "all: portable C++ (built without the AArch64 kernels)";
#endif
        if (!Has(Recip)) return "all: baseline";
        if (Has(Sse2)) return Has(Bmi2 | Adx) ? "Mult: adx; And/Or/Xor, Compare: sse2; Div, ToDecString: recip; other: baseline"   // x86
                                              : "And/Or/Xor, Compare: sse2; Div, ToDecString: recip; other: baseline";
        return adx && avx2 ? "Mult: adx; And/Or/Xor, Shl/Shr, Compare: avx2; Div, ToDecString: recip; other: baseline"
             : adx         ? "Mult: adx; Div, ToDecString: recip; other: baseline"
             : avx2        ? "And/Or/Xor, Shl/Shr, Compare: avx2; Div, ToDecString: recip; other: baseline"
                           : "Div, ToDecString: recip; other: baseline";
    }
};

}  // namespace fullnumerics
