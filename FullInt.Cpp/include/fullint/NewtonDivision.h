// Port of FullInt.Core/NewtonDivision.cs: division through a Newton reciprocal for long divisors.
#pragma once

#include <fullint/FullIntCore.h>
#include <fullint/Karatsuba.h>

#include <vector>

namespace fullint {

struct NewtonDivision
{
    // The engine divides through the Newton reciprocal from this many words of divisor and of
    // quotient, or from LongQuotientThreshold words of divisor when the quotient is at least
    // twice the divisor (the reciprocal then serves several blocks). The products whose low
    // part alone is needed (q̂·B', D·Ih) go modulo β^m - 1 (the cyclic NTT / FFT) from
    // CyclicFrom words. x64 with the NTT (Zen 3), 2N / N of GMP, Newton from 2048 against
    // Burnikel–Ziegler: a % b (2 blocks) 2048 0.94 / 1.03, 4096 0.87 / 1.08, 8192 0.81 / 0.97;
    // a / b (Quotient: 1 block, no remainder) 2048 1.05 / 1.20, 4096 0.90 / 1.23, 8192 0.82 /
    // 1.27 (at 1024 Burnikel–Ziegler stays ahead: % 1.09 / 1.35, / 1.16 / 1.22).
#if FULLINT_X64
    static inline int Threshold = 2048;
    static inline int LongQuotientThreshold = 2048;
    static inline int CyclicFrom = 2048;
#elif FULLINT_ARM64
    // AArch64: a narrow-multiplier core (Cpu::ArmNarrow, the NTT from 1024 there): from 2048, as
    // on x64 (Neoverse-N1, of Burnikel–Ziegler: a / b 0.79 at 4096 words, a % b 0.82; from 1024
    // 1.12 / 1.17 at 1024). A wide one (Cortex-X1, the NTT from 4096): Burnikel–Ziegler up to 16384.
    static inline int Threshold = Cpu::ArmNarrow() ? 2048 : 16384;
    static inline int LongQuotientThreshold = Cpu::ArmNarrow() ? 2048 : 8192;
    static inline int CyclicFrom = Cpu::ArmNarrow() ? 2048 : 4096;
#else
    // x86: with the NTT (AVX2) Newton pays off from 1024 words (C#, x86: a % b 0.78 of
    // Burnikel–Ziegler, a / b 0.70); without it from 16384
    static inline int Threshold = (Cpu::Detect() & Cpu::Avx2) ? 1024 : 16384;
    static inline int LongQuotientThreshold = (Cpu::Detect() & Cpu::Avx2) ? 1024 : 8192;
    static inline int CyclicFrom = (Cpu::Detect() & Cpu::Avx2) ? 1024 : 4096;
#endif

    // Reciprocals of at most this many words are one division (at least 2).
    static inline int InverseBase = 64;

    // Quotient blocks per divisor length; 0: 1 for a quotient of at least two divisor lengths
    // or a divisor below SplitFrom words, otherwise 2 (Quotient too: a reciprocal of half the
    // length outweighs the remainder of the first block).
    static inline int Split = 0;

    // The automatic split halves the blocks from this many divisor words: with the NTT a
    // reciprocal of half the length and two blocks win from the first Newton sizes (a % b,
    // 2N / N of GMP at 4096 / 8192: split 2 0.87 / 0.81, split 1 1.07 / 0.93).
    static inline int SplitFrom = 0;

    static bool Use(size_t dividendLength, size_t divisorLength, bool quotientOnly = false)
    {
        (void)quotientOnly;   // the same sizes: Quotient pays off where DivRem does
        size_t qn = dividendLength - divisorLength;
        return (divisorLength >= (size_t)Threshold && qn >= (size_t)Threshold)
            || (divisorLength >= (size_t)LongQuotientThreshold && qn >= 2 * divisorLength);
    }

    // The kernel Div contract: a and b without leading zero words, a.size() >= b.size();
    // q: a.size() - b.size() + 1 words, r: b.size() + 2 words (the value right-aligned).
    static void DivRem(CSpan a, CSpan b, Span q, Span r);
    static void DivRem(CSpan a, CSpan b, Span q, Span r, int split, int inverseBase);
    // same, with the products modulo β^m - 1 from cyclicFrom words (0: CyclicFrom; tests)
    static void DivRem(CSpan a, CSpan b, Span q, Span r, int split, int inverseBase, size_t cyclicFrom);

    // I ~ β^(2s)/D for D of s words with its top bit set: s + 1 words, within a few units of
    // the floor (tests).
    static std::vector<uint64_t> Inverse(CSpan d, int inverseBase);

    // A divisor used many times (the powers of ToString): B << sigma and its whole reciprocal
    // computed once; DivRem then divides in blocks of n words with it, nothing recomputed.
    // With the NTT (n from half of NttThreshold) the two products of every block keep their
    // fixed factor transformed: I' (the reciprocal without its top word) for q̂, and B' for
    // q̂·B' modulo β^m - 1 (cyclic: m from n + 2, below CyclicFrom too).
    struct Prepared
    {
        std::vector<uint64_t> shifted;   // n + 1 words: [0] = the word shifted out (0), then B'
        std::vector<uint64_t> inverse;   // n + 1 words: ~ β^(2n) / B'
        int sigma = 0;
        Karatsuba::NttImage inverseImage;   // I' for X·I' (2n-word products), or none
        Karatsuba::NttImage divisorImage;   // B' modulo β^m - 1, or none
    };
    static Prepared Prepare(CSpan b);
    static void DivRem(CSpan a, const Prepared& p, Span q, Span r);
    static std::vector<uint64_t> Inverse(CSpan d, int inverseBase, size_t cyclicFrom);

    // The quotient alone (a / b; the DivRem contract without r): A·β / B with one guard word
    // below the quotient, and the last block's remainder not computed - the guard word away
    // from 0 and β - 1 (the block's error is a few units) says the truncation is exact;
    // otherwise (about 2^-59 of the time) the whole DivRem.
    static void Quotient(CSpan a, CSpan b, Span q);

private:
    // quotientOnly: the guard word scheme of Quotient; false when the guard word is too close
    // to a boundary (q then not written)
    static bool Run(CSpan a, CSpan bn, int sigma, const std::vector<uint64_t>* inverseN, int split, int inverseBase,
                    size_t cyclicFrom, Span q, Span r, bool quotientOnly = false, const Prepared* prepared = nullptr);
};

}  // namespace fullint
