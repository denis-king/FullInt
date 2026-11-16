// Port of FullInt.Core/Karatsuba.cs + Toom3.cs: the multiplication dispatcher.
// Below Threshold the basecase kernel, from Toom3Threshold a Toom-3 level, from Toom4Threshold
// Toom-4, from Toom6Threshold Toom-6, from FftThreshold the FFT, in between a Karatsuba level;
// every subproduct chooses again by its own size.
#pragma once

#include <fullint/FullIntCore.h>

#include <vector>

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

    // From this many words in the shorter operand (and not too unbalanced): the AVX2
    // number-theoretic transforms (Ntt.cpp, NttAvx2.asm; AVX2, x64 and x86), up to products of 2^21
    // words (the transform lengths 2^k and 3·2^k: at most 1.5 times the product).
    // x64 (Zen 3), of the Toom / FFT levels: 896 words 0.82, 1024 0.73, 1536 0.76-0.81, 2048
    // 0.59, 4096-8192 0.46-0.50, 16384-131072 0.40-0.50; of GMP 0.42-0.63 from 4096. x86: from
    // 96 words (C#, x86: 112 words 0.64, 128 0.57, 512 0.28, 2048 0.17, 32768 0.08).
#if defined(_M_X64) || defined(__x86_64__)
    static inline int NttThreshold = 896;   // (and from 448 when filled: 512, 768 words)
#else
    static inline int NttThreshold = 96;
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

}  // namespace fullint
