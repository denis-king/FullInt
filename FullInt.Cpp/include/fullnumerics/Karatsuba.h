// Port of FullInt.Core/Karatsuba.cs + Toom3.cs: the multiplication dispatcher.
// Below Threshold the basecase kernel, from Toom3Threshold a Toom-3 level, from Toom4Threshold
// Toom-4, from Toom6Threshold Toom-6, from FftThreshold the FFT, in between a Karatsuba level;
// every subproduct chooses again by its own size.
#pragma once

#include <fullnumerics/FullIntCore.h>

#include <vector>

namespace fullnumerics {

struct Karatsuba
{
    // From this many words in the shorter operand: the Schönhage–Strassen FFT (Fft.cpp).
    // fullint_kara_tune fft (Zen 3), the FFT / the Toom levels (with Toom-6): x64 (the FftAdx.asm
    // butterflies) 0.93 at 8192 words, 0.79 at 16384, 0.69 at 32768, 0.61 at 65536, 0.55 at
    // 131072, 1.14 at 4096; x86 (the portable butterflies) 1.2 at 65536, 0.95 at 131072.
    // AArch64 (Cortex-X1, the portable butterflies, Toom at the ARM thresholds): 1.34 at 8192
    // words, 1.07 at 16384, 0.88 at 32768, 0.84 at 65536, 0.71 at 131072.
#if FULLINT_ARM64
    static inline int FftThreshold = 32768;
#elif FULLINT_WORD64
    static inline int FftThreshold = 8192;
#else
    static inline int FftThreshold = 131072;
#endif

    // the FFT parameter choice: a butterfly with the native kernels ~[0] + [1]·n ns (n words).
    // Calibrated on the whole product (x64, Zen 3, the unrolled FftAdx.asm kernels): {10, 0.8}
    // 0.97 of {30, 1.0} over 8192..131072 words (8192: 0.86) - the larger K it picks shortens
    // the pointwise products, which are 60-66% of the time
    static inline double FftNativeButterfly[2] = { 10, 0.8 };

    // From this many words in the shorter operand (and not too unbalanced): the AVX2
    // number-theoretic transforms (Ntt.cpp, NttAvx2.asm; AVX2, x64 and x86), up to products of 2^21
    // words (the transform lengths 2^k and 3·2^k: at most 1.5 times the product).
    // x64 (Zen 3), of the Toom / FFT levels: 896 words 0.82, 1024 0.73, 1536 0.76-0.81, 2048
    // 0.59, 4096-8192 0.46-0.50, 16384-131072 0.40-0.50; of GMP 0.42-0.63 from 4096. x86: from
    // 256 words, from 128 when filled (128, 192: of GMP 0.47-0.87; 96 of GMP: products from 48
    // words through the NTT 1.0-3.6, the Toom levels 0.87-1.03 there).
    // AArch64 (Cortex-X1; NttNeon.s, Shoup's twiddles, 4 lanes): 4096 x 4096 words 0.98 of the
    // Toom levels (1.09 of GMP); 1024 still 1.16-1.22 of them, and the divisions / Parse with
    // the NTT on their products from 448 words 1.15-1.25 of without: from 4096.
    // A narrow-multiplier core (Cpu::ArmNarrow; Neoverse-N1, the Toom levels there cost more):
    // from 1024 - of from 4096: mul 1024 0.83, todec 4096 0.87, parse 4096 0.90 (768..2048 equal).
#if FULLINT_ARM64
    static inline int NttThreshold = Cpu::ArmNarrow() ? 1024 : 4096;
#elif FULLINT_WORD64
    static inline int NttThreshold = 896;   // (and from 448 when filled: 512, 768 words)
#elif FULLINT_ARM32
    // ARMv7 (Neoverse-N1 in AArch32). NEON (arm32/NttNeon.s): from 1024 words - 1024 0.55 of GMP's
    // time (Toom-6: 0.88), 4096 0.51 (the portable NTT: 1.52); from 512 256 words lost (0.98 /
    // 0.84). Without NEON (the portable C++ NTT): from 4096 - 128 words 22.4 us without it, 90 us
    // with it from 128; 1024 words 524 / 818 us (from 2048); 4096 3.74 / 3.69 ms
    static inline int NttThreshold = Cpu::Has(Cpu::Neon) ? 1024 : 4096;
#else
    static inline int NttThreshold = 256;
#endif
    // the NTT product alone (tests): false, r untouched, without the kernels or too long
    static bool NttMultiply(CSpan a, CSpan b, Span r);
    // whether the NTT kernels are there and a product of len words fits them
    static bool NttFits(size_t len);
    // whether the NTT pays off for a product of len words (C#: NttOps.Worth): from NttThreshold
    // (full) whenever it fits, from half of it only when the product fills 0.9 of the transform
    // (512, 768 words on 2048, 3072 digits: x64 of GMP 1.00-1.03 / 0.98-1.00 against the Toom
    // levels' 1.12 / 1.13; not filled, 384 / 640 words: 1.25 / 1.16 against 1.11 / 1.12)
    static bool NttWorth(size_t len, bool full);
    // the cyclic NTT (C#: NttOps.CyclicLength / MultiplyCyclic): the shortest m = 2^a or 3·2^a >= minWords
    // (0: no kernels or too long); r = a·b mod β^m - 1 through the cyclic convolution of 2m
    // digits, the carry out of the top brought in at the bottom, β^m - 1 written as 0
    static size_t NttCyclicLength(size_t minWords);
    static void NttMultiplyCyclic(CSpan a, CSpan b, Span r);

    // A factor used many times (the prepared divisors of ToString): its forward transforms for
    // the three primes, computed once; a product with it then transforms only the other factor
    // (two transforms instead of three). Full: products of productWords words (the length the
    // full product would take); cyclic: products modulo β^m - 1 (m from NttCyclicLength).
    struct NttImage
    {
        size_t L = 0;                  // the transform length (0: none)
        size_t words = 0;              // full: the product's words; cyclic: m
        bool cyclic = false;
        std::vector<uint32_t> data;    // 3·L values
    };
    static NttImage NttPrepare(CSpan b, size_t words, bool cyclic);
    // r = a·b (full: r.size() = image.words) or a·b mod β^m - 1 (cyclic: r.size() = m)
    static void NttMultiplyImage(CSpan a, const NttImage& b, Span r);

#if FULLINT_ARM64
    // AArch64, fullint_kara_tune on a Cortex-X1 (Galaxy S21+, 2026-11-21; the 4-way rows of
    // arith.inc): the basecase ~1.9 cycles a word product, a Karatsuba level pays off from 24
    // words (32: 0.89 of the basecase, 48: 0.80, 64: 0.72, 128: 0.57; 48 was 0.08-0.10 worse
    // from 64 words). Toom-3 from 192 (of Karatsuba alone: 192 0.96, 768 0.90, 2048 0.83, 4096
    // 0.78; from 128 it loses 5% at 128). Toom-4 from 256 (512 0.94, 1024-4096 0.90, 8192 0.83).
    // Toom-6 from 512, as on x64 (768 0.92, 1024-1536 0.95, 3072-8192 0.86-0.91).
    // A narrow-multiplier core (Cpu::ArmNarrow; fullint_kara_tune on a Neoverse-N1, Oracle
    // Ampere A1, 2026-11-24: 7 cycles a word product): Karatsuba from 12 words (of the basecase:
    // 16 0.87, 32 0.68, 64 0.53, 128 0.40, 1024 0.17; 24 was 0.12-0.14 worse from 32), Toom-3 from
    // 96 (of Karatsuba alone: 192 0.96, 512 0.89, 1024 0.82, 4096 0.71; from 64 it loses 7% at
    // 64); Toom-4 / Toom-6 the same as on the wide cores (N1: 192-256 / 384-512 equal).
    static inline int Threshold = Cpu::ArmNarrow() ? 12 : 24;
    static inline int Toom3Threshold = Cpu::ArmNarrow() ? 96 : 192;
    static inline int Toom4Threshold = 256;
    static inline int Toom6Threshold = 512;
#elif FULLINT_WORD64
    // Shorter operand below this many words: the basecase (column) multiply.
    // C# uses 64: there each core call costs ~10-15 ns more (interop). Here a kernel call is
    // a direct call; tuned with fullint_kara_tune (Zen 3): 28..48 are equal within noise and
    // 7-22% faster than 64 at 48..96 words, below 24 it loses. 32 was the middle; with the
    // 8-way MultAdx.asm the kernel alone wins up to ~44 words (40: 603 ns against a Karatsuba
    // level's 786; 48: 856 against 778): 48.
    static inline int Threshold = 48;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-3.
    // fullint_kara_tune toom (Zen 3, 2026-11-18, the 8-way MultAdx and the Karatsuba at 48): from
    // 256 Toom-3 is 1.10 of Karatsuba alone at 256 words, from 384 1.00 there; 384 also 0.92 at
    // 768, 0.99 at 1024 (from 256: 1.00, 1.06). C# keeps 256 (its interop per kernel call).
    static inline int Toom3Threshold = 384;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-4
    // (C#: Toom4Threshold, the same value).
    static inline int Toom4Threshold = 512;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-6 (Toom6.cpp).
    // fullint_kara_tune toom6 (Zen 3), with Toom-6 / without: 512..768 best, 0.92-0.93 at
    // 1024-2048 words, 0.86 at 4096, 0.85 at 6144-8192 (C# 1024: its interop per core call).
    static inline int Toom6Threshold = 512;
#else
    // x86: a word product is four 32x32 multiplies, the basecase ~4x dearer than on x64, so
    // every level pays off earlier. fullint_kara_tune on the x86 build (Zen 3): Karatsuba
    // 16..24 best (48 words 0.77 of the basecase, 32: 0.83); Toom-3 from 96 (4096 words 0.71
    // of Karatsuba alone, 256: 0.76); Toom-4 from 256 (0.86-0.94, 512: 0.88-0.95); Toom-6 from
    // 512 (0.94 at 768, 0.92-0.93 at 1024-2048, 0.85 at 6144-8192).
    static inline int Threshold = 20;
    static inline int Toom3Threshold = 96;
    static inline int Toom4Threshold = 256;
    static inline int Toom6Threshold = 512;
#endif

    // Squares (a·a, the same words twice) below this many words go to FullIntCore::Sqr (SqrAdx.asm),
    // from it a Karatsuba square level (C#: Karatsuba.SqrThreshold). A Multiply with an explicit
    // Karatsuba threshold other than Threshold (tests, tuning) uses that one for squares too.
#if FULLINT_WORD64
    // 96 with the 8-way SqrAdx.asm (64..832 words of GMP's squares: mean 1.075, with 64 1.12)
    static inline int SqrThreshold = 96;
#else
    static inline int SqrThreshold = 32;   // x86/SqrAdx.asm: 32..48 equal, a square 0.64-0.75 of a product
#endif

    // a and b the same words: a square (one evaluation / transform, the square kernel below)
    static bool Same(CSpan a, CSpan b) { return a.data() == b.data() && a.size() == b.size(); }

    // Scratch words needed by Multiply.
    static size_t ScratchLength(size_t lenA, size_t lenB) { return 16 * (lenA > lenB ? lenA : lenB) + 1024; }

    // r = a * b with r.size() == a.size() + b.size().
    static void Multiply(CSpan a, CSpan b, Span r);
    static void Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold);
    static void Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold);
    static void Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold);
    static void Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold, int toom6Threshold, int fftThreshold);
    // the FFT product alone with K = 2^k pieces (tests: the FFT on small numbers)
    static void FftMultiply(CSpan a, CSpan b, Span r, int k);

    // Products modulo β^m - 1 (C#: Karatsuba.MultiplyMod): through the cyclic NTT (a power of
    // two m; AVX2) or the cyclic FFT from cyclicFrom
    // words (the convolution without zero padding wraps: about half the transform of the full
    // product), otherwise the full product folded. MulModLength: the m that MultiplyMod takes
    // for at least minWords words. r in [0, β^m - 1) (β^m - 1, the other zero, written as 0);
    // a and b at most m words.
    static size_t MulModLength(size_t minWords, size_t cyclicFrom);
    static void MultiplyMod(CSpan a, CSpan b, Span r, size_t cyclicFrom);
    // r = x mod (β^m - 1), m = r.size(), in [0, β^m - 1)
    static void FoldMod(CSpan x, Span r);

    // r = a·b mod β^n, n = r.size() (the low half; Mulders: the low k = 0.7n words multiplied in
    // full, the two cross terms of the rest short again; the MulLoAdx.asm kernel below
    // MulLoThreshold words, the full product without BMI2 + ADX)
    static void MultiplyLow(CSpan a, CSpan b, Span r);
    static inline int MulLoThreshold = 96;

private:
    // the thresholds of one Multiply call, passed down the recursion
    struct Limits
    {
        int Kara, Toom3, Toom4, Toom6, Fft, SqrKara, Ntt;
    };


    static void Toom6(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim);
    static int Evaluate6(CSpan x0, CSpan x1, CSpan x2, CSpan x3, CSpan x4, CSpan x5, Span v, size_t e, Span tmp);
    static void Mul(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim);
    static void AddAt(Span r, CSpan c, size_t upTo);
    static void MulUnbalanced(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim);
    static void Toom3(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim);
    static void Toom4(CSpan a, CSpan b, Span r, Span scratch, const Limits& lim);
    static void FftMul(CSpan a, CSpan b, Span r, const Limits& lim, int forceK);
    static void ChooseFft(size_t la, size_t lb, int forceK, int& k, size_t& m, int& nBits);
    static void ChooseFftCyclic(size_t mMin, bool exact, int& k, size_t& m, int& nBits);
    static void Convolve(CSpan a, CSpan b, Span r, int k, size_t m, int nBits, const Limits& lim);
    static void Evaluate4(CSpan x0, CSpan x1, CSpan x2, CSpan x3, Span p1, Span pm1, Span p2, Span pm2, Span ph,
                          Span t, Span u, bool& negPm1, bool& negPm2);
    static void Evaluate(CSpan x0, CSpan x1, CSpan x2, Span p1, Span pm1, Span pm2, bool& negPm1, bool& negPm2);
    static bool SignedAdd(CSpan x, bool xNegative, CSpan y, bool yNegative, Span r);
};

}  // namespace fullnumerics
