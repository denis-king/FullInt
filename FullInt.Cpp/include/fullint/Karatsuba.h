// Port of FullInt.Core/Karatsuba.cs + Toom3.cs: the multiplication dispatcher.
// Below Threshold the basecase kernel, from Toom3Threshold a Toom-3 level, from Toom4Threshold
// Toom-4, from Toom6Threshold Toom-6, from FftThreshold the FFT, in between a Karatsuba level;
// every subproduct chooses again by its own size.
#pragma once

#include <fullint/FullIntCore.h>

namespace fullint {

struct Karatsuba
{
    // From this many words in the shorter operand: the Schönhage–Strassen FFT (Fft.cpp).
    // fullint_kara_tune fft (Zen 3), the FFT / the Toom levels (with Toom-6): x64 (the FftAdx.asm
    // butterflies) 0.93 at 8192 words, 0.79 at 16384, 0.69 at 32768, 0.61 at 65536, 0.55 at
    // 131072, 1.14 at 4096; x86 (the portable butterflies) 1.2 at 65536, 0.95 at 131072.
#if defined(_M_X64) || defined(__x86_64__)
    static inline int FftThreshold = 8192;
#else
    static inline int FftThreshold = 131072;
#endif

    // the FFT parameter choice: a butterfly with the native kernels ~[0] + [1]·n ns (n words).
    // Calibrated on the whole product (x64, Zen 3, the unrolled FftAdx.asm kernels): {10, 0.8}
    // 0.97 of {30, 1.0} over 8192..131072 words (8192: 0.86) - the larger K it picks shortens
    // the pointwise products, which are 60-66% of the time
    static inline double FftNativeButterfly[2] = { 10, 0.8 };

#if defined(_M_X64) || defined(__x86_64__)
    // Shorter operand below this many words: the basecase (column) multiply.
    // C# uses 64: there each core call costs ~10-15 ns more (interop). Here a kernel call is
    // a direct call; tuned with fullint_kara_tune (Zen 3): 28..48 are equal within noise and
    // 7-22% faster than 64 at 48..96 words, below 24 it loses. 32 is the middle.
    static inline int Threshold = 32;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-3.
    // Tuned with fullint_kara_tune toom: Toom-3 gains from ~384 words, 96..256 equal within
    // noise; the C# value is kept.
    static inline int Toom3Threshold = 256;

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
#if defined(_M_X64) || defined(__x86_64__)
    static inline int SqrThreshold = 64;
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

    // Products modulo β^m - 1 (C#: Karatsuba.MultiplyMod): through the cyclic FFT from cyclicFrom
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
        int Kara, Toom3, Toom4, Toom6, Fft, SqrKara;
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

}  // namespace fullint
