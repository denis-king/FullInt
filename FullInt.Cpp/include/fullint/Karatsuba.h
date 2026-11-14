// Port of FullInt.Core/Karatsuba.cs + Toom3.cs: the multiplication dispatcher.
// Below Threshold the basecase kernel, from Toom3Threshold a Toom-3 level, in between a
// Karatsuba level; every subproduct chooses again by its own size.
#pragma once

#include <fullint/FullIntCore.h>

namespace fullint {

struct Karatsuba
{
    // Shorter operand below this many words: the basecase (column) multiply.
    // C# uses 64: there each core call costs ~10-15 ns more (interop). Here a kernel call is
    // a direct call; tuned with fullint_kara_tune (Zen 3): 28..48 are equal within noise and
    // 7-22% faster than 64 at 48..96 words, below 24 it loses. 32 is the middle.
    static inline int Threshold = 32;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-3.
    // Tuned with fullint_kara_tune toom: Toom-3 gains from ~384 words, 96..256 equal within
    // noise; the C# value is kept.
    static inline int Toom3Threshold = 256;

    // Scratch words needed by Multiply.
    static size_t ScratchLength(size_t lenA, size_t lenB) { return 16 * (lenA > lenB ? lenA : lenB) + 1024; }

    // r = a * b with r.size() == a.size() + b.size().
    static void Multiply(CSpan a, CSpan b, Span r);
    static void Multiply(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold);

private:
    static void Mul(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold);
    static void MulUnbalanced(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold);
    static void Toom3(CSpan a, CSpan b, Span r, Span scratch, int threshold, int toom3Threshold);
    static void Evaluate(CSpan x0, CSpan x1, CSpan x2, Span p1, Span pm1, Span pm2, bool& negPm1, bool& negPm2);
    static bool SignedAdd(CSpan x, bool xNegative, CSpan y, bool yNegative, Span r);
};

}  // namespace fullint
