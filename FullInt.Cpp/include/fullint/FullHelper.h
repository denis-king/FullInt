// Port of FullInt.Core/FullHelper.cs: the word-level helpers the engine needs.
// (The C# buffer pooling - stackalloc limits, ArrayPool - has no counterpart here: results
// are written straight into the result objects.)
#pragma once

#include <bit>
#include <cstdint>

#if defined(_MSC_VER)
#include <intrin.h>
#endif

namespace fullint {

struct FullHelper
{
    static int LeadingZeroCount(uint64_t x) noexcept { return std::countl_zero(x); }

    // Full 64 x 64 -> 128 product, returns the high word.
    static uint64_t BigMul(uint64_t x, uint64_t y, uint64_t* lo) noexcept
    {
#if defined(_MSC_VER) && defined(_M_X64)
        uint64_t hi;
        *lo = _umul128(x, y, &hi);   // returns the low word, the high one through the pointer
        return hi;
#elif defined(__SIZEOF_INT128__)
        unsigned __int128 p = (unsigned __int128)x * y;
        *lo = (uint64_t)p;
        return (uint64_t)(p >> 64);
#else
        const uint64_t mask32 = 0xFFFFFFFF;   // the C# 32-bit split
        uint64_t x0 = x & mask32, x1 = x >> 32, y0 = y & mask32, y1 = y >> 32;
        uint64_t z0 = x0 * y0;
        uint64_t mid1 = x0 * y1 + (z0 >> 32);
        uint64_t mid2 = x1 * y0 + (mid1 & mask32);
        *lo = (mid2 << 32) | (z0 & mask32);
        return x1 * y1 + (mid1 >> 32) + (mid2 >> 32);
#endif
    }

    // x + y + carry, the carry out in carry (0 / 1)
    static uint64_t AddC(uint64_t x, uint64_t y, unsigned char& carry) noexcept
    {
#if defined(_MSC_VER) && defined(_M_X64)
        uint64_t s;
        carry = _addcarry_u64(carry, x, y, &s);
        return s;
#else
        uint64_t s = x + y, t = s + carry;
        carry = (unsigned char)((s < x) | (t < s));
        return t;
#endif
    }

    // Products of two-word numbers inline (words big-endian, as everywhere): the kernel call cost more
    // than the four (three) multiplies. x64: r = a·b 1.9-2.4 ns, the kernel 5.3; a² 2.3, the kernel 4.2.
    // r: 4 words
    static void Mul2x2(const uint64_t* a, const uint64_t* b, uint64_t* r) noexcept
    {
        uint64_t l00, h00 = BigMul(a[1], b[1], &l00);
        uint64_t l01, h01 = BigMul(a[1], b[0], &l01);
        uint64_t l10, h10 = BigMul(a[0], b[1], &l10);
        uint64_t l11, h11 = BigMul(a[0], b[0], &l11);
        unsigned char c = 0;
        uint64_t w1 = AddC(h00, l01, c);
        uint64_t w2 = AddC(h01, l11, c);
        uint64_t w3 = h11 + c;
        c = 0;
        w1 = AddC(w1, l10, c);
        w2 = AddC(w2, h10, c);
        w3 += c;
        r[0] = w3; r[1] = w2; r[2] = w1; r[3] = l00;
    }

    // r = a², a two words, r 4 words
    static void Sqr2(const uint64_t* a, uint64_t* r) noexcept
    {
        uint64_t l00, h00 = BigMul(a[1], a[1], &l00);
        uint64_t l01, h01 = BigMul(a[1], a[0], &l01);
        uint64_t l11, h11 = BigMul(a[0], a[0], &l11);
        uint64_t t2 = h01 >> 63;   // 2·a0·a1 = t2 : h01 : l01
        h01 = h01 << 1 | l01 >> 63;
        l01 <<= 1;
        unsigned char c = 0;
        uint64_t w1 = AddC(h00, l01, c);
        uint64_t w2 = AddC(l11, h01, c);
        r[0] = h11 + t2 + c; r[1] = w2; r[2] = w1; r[3] = l00;
    }

    // r = a·b, a two words, b one, r 3 words
    static void Mul2x1(const uint64_t* a, uint64_t b, uint64_t* r) noexcept
    {
        uint64_t l0, h0 = BigMul(a[1], b, &l0);
        uint64_t l1, h1 = BigMul(a[0], b, &l1);
        unsigned char c = 0;
        r[1] = AddC(h0, l1, c);
        r[0] = h1 + c;
        r[2] = l0;
    }

    // (hi:lo) / d with hi < d (the quotient fits 64 bits), the contract of x64 `div r64`.
    // x64: the instruction itself (_udiv128); elsewhere Hacker's Delight divlu as in C#.
    static uint64_t Div128(uint64_t hi, uint64_t lo, uint64_t d, uint64_t* remainder) noexcept
    {
#if defined(_MSC_VER) && defined(_M_X64)
        return _udiv128(hi, lo, d, remainder);
#elif defined(__SIZEOF_INT128__)
        unsigned __int128 n = ((unsigned __int128)hi << 64) | lo;
        *remainder = (uint64_t)(n % d);
        return (uint64_t)(n / d);
#else
        const uint64_t b = 1ULL << 32;
        int s = LeadingZeroCount(d);
        uint64_t v = d << s, vn1 = v >> 32, vn0 = v & 0xFFFFFFFF;
        uint64_t un32 = s == 0 ? hi : (hi << s) | (lo >> (64 - s));
        uint64_t un10 = lo << s, un1 = un10 >> 32, un0 = un10 & 0xFFFFFFFF;

        uint64_t q1 = un32 / vn1, rhat = un32 - q1 * vn1;
        while (q1 >= b || q1 * vn0 > b * rhat + un1)
        {
            q1--;
            rhat += vn1;
            if (rhat >= b) break;
        }

        uint64_t un21 = un32 * b + un1 - q1 * v;
        uint64_t q0 = un21 / vn1;
        rhat = un21 - q0 * vn1;
        while (q0 >= b || q0 * vn0 > b * rhat + un0)
        {
            q0--;
            rhat += vn1;
            if (rhat >= b) break;
        }

        *remainder = (un21 * b + un0 - q0 * v) >> s;
        return q1 * b + q0;
#endif
    }
};

}  // namespace fullint
