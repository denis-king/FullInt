// Short products: the low half of a product (Mulders' recursion over the full multiply).
//
//   low_n(a·b) = low_n(a0·b0) + β^k·( low_l(a1·b0) + low_l(a0·b1) ),  l = n - k
//   a0, b0: the low k words, a1, b1: the next l words (the words above n do not matter).
//   k = 0.7n: one full product of k words and two short ones of l words - about 0.8 of the
//   full n x n product over Karatsuba / Toom (0.7^1.58 + 2·0.3^1.58·0.8), about 0.55 at the
//   basecase sizes (the MulLoAdx.asm kernel: the triangle of the column multiply).
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cstring>
#include <vector>

namespace fullint {

namespace {

// the low w words of x (all of x if shorter)
CSpan Low(CSpan x, size_t w) { return x.size() <= w ? x : x.subspan(x.size() - w); }

// words [k, k + l) from the low end of x, as a span (empty if x is shorter than k)
CSpan Mid(CSpan x, size_t k, size_t l)
{
    if (x.size() <= k) return CSpan();
    size_t top = std::min(x.size(), k + l);
    return x.subspan(x.size() - top, top - k);
}

}  // namespace

void Karatsuba::MultiplyLow(CSpan a, CSpan b, Span r)
{
    size_t n = r.size();
    a = Low(a, n);
    b = Low(b, n);
    if (a.empty() || b.empty()) { std::memset(r.data(), 0, r.size_bytes()); return; }
    size_t la = a.size(), lb = b.size();
    if (std::min(la, lb) < (size_t)MulLoThreshold || n < 8)
    {
        if (Cpu::Has(Cpu::Bmi2 | Cpu::Adx) && n <= la + lb)   // MulLoAdx.asm (x86: in 32-bit digits)
        {
            Fullint64MulLoWIN_adx(a.data(), b.data(), r.data(), (int)la, (int)lb, (int)n);
            return;
        }
        std::vector<uint64_t> full(la + lb);
        Multiply(a, b, full);
        std::memset(r.data(), 0, r.size_bytes());
        size_t w = std::min(n, la + lb);
        std::copy(full.end() - w, full.end(), r.end() - w);
        return;
    }

    size_t k = (n * 7 + 9) / 10, l = n - k;
    // low_n(a0·b0): a0, b0 the low k words (2k >= n words of product)
    CSpan a0 = Low(a, k), b0 = Low(b, k);
    std::vector<uint64_t> full(a0.size() + b0.size());
    Multiply(a0, b0, full);
    std::memset(r.data(), 0, r.size_bytes());
    size_t w = std::min(n, full.size());
    std::copy(full.end() - w, full.end(), r.end() - w);
    if (l == 0) return;
    // + β^k·(low_l(a1·b0) + low_l(a0·b1)) mod β^n: into r's top l words
    std::vector<uint64_t> t(l);
    Span top = r.subspan(0, l);
    CSpan a1 = Mid(a, k, l), b1 = Mid(b, k, l);
    if (!a1.empty())
    {
        MultiplyLow(a1, Low(b, l), t);
        FullIntCore::Add(top, t, top);              // a carry out of the top: dropped (mod β^n)
    }
    if (!b1.empty())
    {
        MultiplyLow(Low(a, l), b1, t);
        FullIntCore::Add(top, t, top);
    }
}

}  // namespace fullint
