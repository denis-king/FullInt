// Port of FullInt.Core/NewtonDivision.cs: division through a Newton reciprocal for long divisors.
#pragma once

#include <fullint/FullIntCore.h>

#include <vector>

namespace fullint {

struct NewtonDivision
{
    // The engine divides through the Newton reciprocal from this many words of divisor and of
    // quotient, or from LongQuotientThreshold words of divisor when the quotient is at least
    // twice the divisor (the reciprocal then serves several blocks).
#if defined(_M_X64) || defined(__x86_64__)
    static inline int Threshold = 16384;
#else
    static inline int Threshold = 65536;
#endif
    static inline int LongQuotientThreshold = 8192;

    // Reciprocals of at most this many words are one division (at least 2).
    static inline int InverseBase = 64;

    // Quotient blocks per divisor length; 0: 1 for a quotient of at least two divisor lengths
    // or a divisor below SplitFrom words, otherwise 2.
    static inline int Split = 0;

    // The automatic split halves the blocks from this many divisor words. Here (unlike C#, 0)
    // halving loses below 65536 (2N / N, x64, of Burnikel–Ziegler: split 1 0.95 / 0.82 / 0.85-0.89
    // at 16384 / 32768 / 65536, split 2 1.06 / 1.11 / 0.75).
    static inline int SplitFrom = 65536;

    static bool Use(size_t dividendLength, size_t divisorLength)
    {
        size_t qn = dividendLength - divisorLength;
        return (divisorLength >= (size_t)Threshold && qn >= (size_t)Threshold)
            || (divisorLength >= (size_t)LongQuotientThreshold && qn >= 2 * divisorLength);
    }

    // The kernel Div contract: a and b without leading zero words, a.size() >= b.size();
    // q: a.size() - b.size() + 1 words, r: b.size() + 2 words (the value right-aligned).
    static void DivRem(CSpan a, CSpan b, Span q, Span r);
    static void DivRem(CSpan a, CSpan b, Span q, Span r, int split, int inverseBase);

    // I ~ β^(2s)/D for D of s words with its top bit set: s + 1 words, within a few units of
    // the floor (tests).
    static std::vector<uint64_t> Inverse(CSpan d, int inverseBase);
};

}  // namespace fullint
