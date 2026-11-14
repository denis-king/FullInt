// Port of FullInt.Core/BurnikelZiegler.cs: recursive division on top of the kernels.
// The leaves are the kernel Div (asm Knuth), the products go through Karatsuba.
#pragma once

#include <fullint/FullIntCore.h>

namespace fullint {

struct BurnikelZiegler
{
    // Inside the recursion, blocks below this many words (or odd) go to the kernel Div.
    // The engine uses Burnikel–Ziegler from 2·Threshold words of divisor and of quotient.
    static inline int Threshold = 96;

    // The kernel Div contract: a and b without leading zero words, a.size() >= b.size();
    // q: a.size() - b.size() + 1 words, r: b.size() + 2 words (the value right-aligned).
    static void DivRem(CSpan a, CSpan b, Span q, Span r);
    static void DivRem(CSpan a, CSpan b, Span q, Span r, int threshold);

private:
    static void Div2n1n(CSpan a, CSpan b, Span q, Span r, int threshold);
    static void Div3n2n(CSpan a, CSpan b, Span q, Span r, int threshold);
    static void Basecase(CSpan a, CSpan b, Span q, Span r);
};

}  // namespace fullint
