// Port of FullInt.Core/Karatsuba.cs + Toom3.cs: the multiplication dispatcher.
// Below Threshold the basecase kernel, from Toom3Threshold a Toom-3 level, in between a
// Karatsuba level; every subproduct chooses again by its own size.
#pragma once

#include <fullint/FullIntCore.h>

namespace fullint {

struct Karatsuba
{
    // Shorter operand below this many words: the basecase (column) multiply.
    // C#: 64 (each core call from C# costs ~10-15 ns there); the same value here until the
    // C++ build is tuned on its own (no interop: the best value is likely lower).
    static inline int Threshold = 64;

    // From this many words in the shorter operand (operands not too unbalanced): Toom-3.
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
