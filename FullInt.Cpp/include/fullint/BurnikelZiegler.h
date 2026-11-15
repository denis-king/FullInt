// Port of FullInt.Core/BurnikelZiegler.cs: recursive division on top of the kernels.
// The leaves are the kernel Div (asm Knuth), the products go through Karatsuba.
#pragma once

#include <fullint/FullIntCore.h>

namespace fullint {

struct BurnikelZiegler
{
    // Inside the recursion, blocks below this many words go to the kernel Div.
    // The engine uses Burnikel–Ziegler from 2·Threshold words of divisor and of quotient.
    // C# keeps 96 (each kernel call costs it more: Burnikel–Ziegler loses up to 128 words there);
    // here 40..48 are best (x64, Zen 3, a % b and a / b at 2N / N and 3N / N, of 96: 0.84-0.89
    // at 96 words, 0.78-0.83 at 128, 0.85-0.96 at 256-1024; mean 0.935).
    static inline int Threshold = 48;

    // The kernel Div contract: a and b without leading zero words, a.size() >= b.size();
    // q: a.size() - b.size() + 1 words, r: b.size() + 2 words (the value right-aligned).
    static void DivRem(CSpan a, CSpan b, Span q, Span r);
    static void DivRem(CSpan a, CSpan b, Span q, Span r, int threshold);

    // The quotient alone (a / b without the remainder), about a fifth cheaper than DivRem
    // (GMP's mpn_div_q does the same): Q̃ ~ floor(A·β / B) within a known error E (Approx),
    // one guard word below the quotient; when that word is in [E, β - 1 - E], floor(A / B) =
    // Q̃ / β exactly, otherwise (a chance of about 2E / 2^64) the full division.
    // a and b without leading zero words, a.size() >= b.size(); q: a.size() - b.size() + 1 words.
    static void Quotient(CSpan a, CSpan b, Span q);
    static void Quotient(CSpan a, CSpan b, Span q, int threshold);

private:
    static void Div2n1n(CSpan a, CSpan b, Span q, Span r, int threshold, Span scratch);
    static void Div3n2n(CSpan a, CSpan b, Span q, Span r, int threshold, Span scratch);
    static void Basecase(CSpan a, CSpan b, Span q, Span r, Span scratch);
    static int Approx(CSpan a, CSpan b, Span q, int threshold);
    static void Exact(CSpan a, CSpan b, Span q, Span r, int threshold);
    // the recursion's work words for a block of n: Div2n1n(n) takes 3n/2 + Div3n2n, which takes
    // 2n + 1 + Div2n1n(n/2); a leaf 2n + 3 (its quotient and remainder)
    // (an odd block splits into ceil/floor halves: <= 7n + O(log n) + a leaf of 2·threshold + 3;
    // the top part Div3n2n(k = h < n) 2n + 1 more)
    static size_t ScratchWords(size_t n, int threshold) { return 10 * n + 2 * (size_t)threshold + 128; }
};

}  // namespace fullint
