// The kernels' entry points in portable C++ (kernels.h, the same names and contracts as the asm
// ones), on AArch64: the reference the AArch64 kernels (FullInt.Asm/Sources/arm64) are tested and
// timed against, and the kernels themselves in the build without them (FULLINT_ARM64_ASM=OFF). A port of the C#
// managed core (FullInt.Core/FullIntCoreManaged.cs) plus the entries it has no counterpart for
// (the equal-length add / sub forms, the header forms, the fraction digits). Speed is not the
// point here: these are the reference the AArch64 kernels replace one by one (A/B against them).
//
// Limbs big-endian: index 0 = the most significant word. Cpu::Detect() gives no x86 feature bits
// on these CPUs, so the engine never calls the x86 vector kernels (NTT): they are here only so the
// library links. The reciprocal entries here: the baseline under their names (DivQ: "saturated").
#include <fullint/kernels.h>
#include <fullint/FullHelper.h>

#include <cstdlib>
#include <cstring>
#include <vector>

#if !FULLINT_GENERIC_KERNELS
#error "kernels_generic.cpp: only for the targets without asm kernels (kernels.h)"
#endif

// The entries with an AArch64 kernel (FullInt.Asm/Sources/arm64, FULLINT_ARM64_ASM): this file's
// version is named ..._generic then - the reference to test and time the kernel against, and the
// kernel's own fallback for the cases it leaves out.
#if FULLINT_ARM64_ASM
#define FULLINT_GENERIC(name) name##_generic
#else
#define FULLINT_GENERIC(name) name
#endif

using fullint::FullHelper;

namespace {

constexpr uint64_t Base10 = 10000000000000000000ull;   // 10^19

inline uint64_t Shld(uint64_t hi, uint64_t lo, int s) { return s == 0 ? hi : hi << s | lo >> (64 - s); }

int CompareWords(const uint64_t* a, const uint64_t* b, int lenA, int lenB)
{
    int za = 0, zb = 0;
    while (za < lenA && a[za] == 0) za++;
    while (zb < lenB && b[zb] == 0) zb++;
    int na = lenA - za, nb = lenB - zb;
    if (na != nb) return na < nb ? -1 : 1;
    for (int i = 0; i < na; i++)
        if (a[za + i] != b[zb + i]) return a[za + i] < b[zb + i] ? -1 : 1;
    return 0;
}

// the long division's window R[0 .. n+1] (the C# core's Div): R[2 .. n+1] against D
int CompareWindow(const uint64_t* R, const uint64_t* D, int n)
{
    for (int i = 0; i < n; i++)
        if (R[2 + i] != D[i]) return R[2 + i] < D[i] ? -1 : 1;
    return 0;
}

void AddWindow(uint64_t* R, const uint64_t* D, int n)
{
    uint64_t carry = 0;
    for (int i = n - 1; i >= 0; i--)
    {
        uint64_t a = R[i + 2], s1 = a + D[i], s2 = s1 + carry;
        carry = (s1 < a || s2 < s1) ? 1 : 0;
        R[i + 2] = s2;
    }
    for (int i = 1; i >= 0 && carry != 0; i--)
    {
        R[i]++;
        carry = R[i] == 0 ? 1 : 0;
    }
}

void SubWindow(uint64_t* R, const uint64_t* D, int n)
{
    uint64_t borrow = 0;
    for (int i = n - 1; i >= 0; i--)
    {
        uint64_t a = R[i + 2], d1 = a - D[i], d2 = d1 - borrow;
        borrow = (a < D[i] || d1 < borrow) ? 1 : 0;
        R[i + 2] = d2;
    }
    for (int i = 1; i >= 0 && borrow != 0; i--)
    {
        borrow = R[i] == 0 ? 1 : 0;
        R[i]--;
    }
}

// one quotient word: the window R[1 .. n+1] (< D * beta) divided by D. The estimate from the top
// two words of the window over the divisor's top word, both normalized (Knuth D: at most 2 above
// the digit), then the multiply-subtract and the corrections until exact.
uint64_t DivDigit(uint64_t* R, const uint64_t* D, int n, int s, uint64_t d1)
{
    if (n == 1)
    {
        uint64_t rem, q = FullHelper::Div128(R[1], R[2], D[0], &rem);
        R[1] = 0;
        R[2] = rem;
        return q;
    }
    uint64_t u2 = Shld(R[1], R[2], s);
    uint64_t u1 = Shld(R[2], R[3], s);
    uint64_t q, rem;
    q = u2 >= d1 ? ~0ull : FullHelper::Div128(u2, u1, d1, &rem);

    uint64_t carry = 0;   // window -= q * D (D aligned with R[2 .. n+1]; the borrow runs into R[1], R[0])
    for (int i = n - 1; i >= 0; i--)
    {
        uint64_t lo, hi = FullHelper::BigMul(q, D[i], &lo);
        lo += carry;
        if (lo < carry) hi++;
        uint64_t r = R[i + 2];
        R[i + 2] = r - lo;
        if (r < lo) hi++;
        carry = hi;
    }
    uint64_t r1 = R[1];
    R[1] = r1 - carry;
    if (r1 < carry) R[0]--;

    while (true)
    {
        if ((int64_t)R[0] < 0)
        {
            do { AddWindow(R, D, n); q--; } while ((int64_t)R[0] < 0);
            break;
        }
        if (R[1] != 0 || CompareWindow(R, D, n) >= 0)
        {
            SubWindow(R, D, n);
            q++;
            continue;
        }
        break;
    }
    return q;
}

int Bitwise(int op, const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs)
{
    auto Op = [op](uint64_t x, uint64_t y) { return op == 0 ? x & y : op == 1 ? x | y : x ^ y; };
    // two's complement on the fly: t = (word ^ mask) + carry
    uint64_t maskA = (signs & 1) ? ~0ull : 0, carryA = maskA & 1;
    uint64_t maskB = (signs & 2) ? ~0ull : 0, carryB = maskB & 1;
    uint64_t maskR = Op(maskA, maskB), carryR = maskR & 1;
    int ia = lenA, ib = lenB;
    for (int ir = lenR - 1; ir >= 0; ir--)
    {
        uint64_t wa = (ia > 0 ? a[--ia] : 0) ^ maskA;
        uint64_t ta = wa + carryA;
        carryA = ta < wa ? 1 : 0;
        uint64_t wb = (ib > 0 ? b[--ib] : 0) ^ maskB;
        uint64_t tb = wb + carryB;
        carryB = tb < wb ? 1 : 0;
        uint64_t w = Op(ta, tb) ^ maskR;
        uint64_t tr = w + carryR;
        carryR = tr < w ? 1 : 0;
        r[ir] = tr;
    }
    return (int)(maskR & 1);
}

// the 19 digits of v < 10^19 at out (leading zeros included)
void Digits19(uint64_t v, uint8_t* out)
{
    for (int d = 18; d >= 0; d--)
    {
        out[d] = (uint8_t)('0' + v % 10);
        v /= 10;
    }
}

[[noreturn]] void NotHere()
{
    std::abort();   // an x86 vector kernel: the engine asks Cpu::Has first, which is false here
}

}  // namespace

extern "C" {

uint64_t FULLINT_GENERIC(Fullint64AddWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR)
{
    // r = a + b over lenR words (the operands' missing top words 0); the carry out of r's top word
    int i1 = lenA, i2 = lenB, ir = lenR;
    uint64_t carry = 0;
    while (ir > 0 && i1 > 0 && i2 > 0)
    {
        uint64_t x = a[--i1], y = b[--i2];
        uint64_t t = x + y, c = t < x ? 1 : 0;
        t += carry;
        c += t < carry ? 1 : 0;
        r[--ir] = t;
        carry = c;
    }
    const uint64_t* rest = i2 > 0 ? b : a;
    int ik = i2 > 0 ? i2 : i1;
    while (ir > 0 && ik > 0)
    {
        uint64_t t = rest[--ik] + carry;
        carry = t < carry ? 1 : 0;
        r[--ir] = t;
    }
    if (ir > 0)
    {
        r[--ir] = carry;
        carry = 0;
        std::memset(r, 0, (size_t)ir * 8);
    }
    return carry;
}

uint64_t FULLINT_GENERIC(Fullint64AddNWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
{
    uint64_t carry = 0;
    for (int64_t i = n; i-- > 0;)
    {
        uint64_t x = a[i], s = x + b[i], c = s < x ? 1 : 0;
        uint64_t t = s + carry;
        c |= t < s ? 1 : 0;
        r[i] = t;
        carry = c;
    }
    return carry;
}

uint64_t FULLINT_GENERIC(Fullint64SubNWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
{
    uint64_t borrow = 0;
    for (int64_t i = n; i-- > 0;)
    {
        uint64_t x = a[i], y = b[i], d = x - y, bw = x < y ? 1 : 0;
        bw |= d < borrow ? 1 : 0;
        r[i] = d - borrow;
        borrow = bw;
    }
    return borrow;
}

uint64_t FULLINT_GENERIC(Fullint64SubCmpNWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
{
    int64_t i = 0;
    while (i < n && a[i] == b[i]) i++;
    if (i == n) return (uint64_t)n;
    uint64_t swap = a[i] < b[i] ? 1 : 0;
    const uint64_t* x = swap ? b : a;
    const uint64_t* y = swap ? a : b;
    FULLINT_GENERIC(Fullint64SubNWIN)(x + i, y + i, r + i, n - i);
    int64_t off = i;
    while (off < n - 1 && r[off] == 0) off++;   // a nonzero difference: its top word is in R
    return (uint64_t)off | swap << 63;
}

void FULLINT_GENERIC(Fullint64AddNHdrWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, uint32_t* hdr)
{
    uint32_t n = hdr[1];
    uint64_t carry = FULLINT_GENERIC(Fullint64AddNWIN)(a, b, r + 1, n);
    r[0] = carry;
    hdr[0] = 1 - (uint32_t)carry;
    hdr[1] = n + (uint32_t)carry;
}

void FULLINT_GENERIC(Fullint64SubCmpNHdrWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, uint32_t* hdr)
{
    uint32_t n = hdr[1];
    uint8_t* neg = reinterpret_cast<uint8_t*>(hdr + 2);
    uint64_t res = FULLINT_GENERIC(Fullint64SubCmpNWIN)(a, b, r, n);
    uint32_t off = (uint32_t)res;
    if (off == n)
    {
        r[0] = 0;
        hdr[0] = 0;
        hdr[1] = 1;
        *neg = 0;
        return;
    }
    hdr[0] = off;
    hdr[1] = n - off;
    *neg ^= (uint8_t)(res >> 63);
}

int FULLINT_GENERIC(Fullint64SubWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR)
{
    int cmp = CompareWords(a, b, lenA, lenB);
    if (cmp == 0)
    {
        std::memset(r, 0, (size_t)lenR * 8);
        return 0;
    }
    const uint64_t* g = cmp > 0 ? a : b;
    const uint64_t* l = cmp > 0 ? b : a;
    int ig = cmp > 0 ? lenA : lenB, il = cmp > 0 ? lenB : lenA, ir = lenR;
    uint64_t borrow = 0;
    while (ir > 0 && il > 0)
    {
        uint64_t x = ig > 0 ? g[--ig] : 0, y = l[--il];
        uint64_t d = x - y, bw = x < y ? 1 : 0;
        bw |= d < borrow ? 1 : 0;
        r[--ir] = d - borrow;
        borrow = bw;
    }
    while (ir > 0 && ig > 0)
    {
        uint64_t x = g[--ig];
        r[--ir] = x - borrow;
        borrow = x < borrow ? 1 : 0;
    }
    if (ir > 0) std::memset(r, 0, (size_t)ir * 8);
    return cmp;
}

void FULLINT_GENERIC(Fullint64ToDecFracWIN)(uint64_t* f, int64_t L, int64_t m, uint8_t* out)
{
    // m passes of f *= 10^19, each one's integer part (the word carried out) 19 digits; after a
    // pass f loses its lowest word and the new lowest is raised by one (x64 ToDecStringFrac.asm:
    // f stays an upper bound within what the digits allow; the carry out of the top dropped)
    int64_t active = L;
    for (int64_t k = 0; k < m; k++)
    {
        uint64_t carry = 0;
        for (int64_t i = active; i-- > 0;)
        {
            uint64_t lo, hi = FullHelper::BigMul(f[i], Base10, &lo);
            lo += carry;
            hi += lo < carry ? 1 : 0;
            f[i] = lo;
            carry = hi;
        }
        if (active > 1)
        {
            active--;
            for (int64_t i = active; i-- > 0;)
                if (++f[i] != 0) break;
        }
        Digits19(carry, out + 19 * k);
    }
}

uint64_t FULLINT_GENERIC(Fullint64MultWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR)
{
    if (lenR < lenA + lenB)   // the low words of the product
    {
        std::vector<uint64_t> full((size_t)lenA + lenB);
        FULLINT_GENERIC(Fullint64MultWIN)(a, b, full.data(), lenA, lenB, lenA + lenB);
        std::memcpy(r, full.data() + full.size() - lenR, (size_t)lenR * 8);
        return 0;
    }
    if (lenA < lenB)
    {
        std::swap(a, b);
        std::swap(lenA, lenB);
    }
    std::memset(r, 0, (size_t)lenR * 8);
    for (int j = 0; j < lenB; j++)   // a row per word of the shorter operand, from the low end
    {
        uint64_t bj = b[lenB - 1 - j];
        if (bj == 0) continue;
        uint64_t carry = 0;
        int k = lenR - 1 - j;
        for (int i = lenA - 1; i >= 0; i--, k--)
        {
            uint64_t lo, hi = FullHelper::BigMul(a[i], bj, &lo);
            lo += carry;
            hi += lo < carry ? 1 : 0;
            uint64_t t = r[k] + lo;
            hi += t < lo ? 1 : 0;
            r[k] = t;
            carry = hi;
        }
        r[k] = carry;
    }
    return 0;
}

uint64_t FULLINT_GENERIC(Fullint64DivWin)(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r, int lenA, int lenB)
{
    // remainder: lenB + 2 words, [0] the sign word, [1 .. n+1] the window, the result in [2 .. n+1];
    // quotient: max(lenA - lenB + 1, 1) words. b[0] != 0.
    int m = lenA, n = lenB;
    uint64_t* R = r;
    std::memset(R, 0, (size_t)(n + 2) * 8);
    if (m < n)
    {
        std::memcpy(R + (n + 2 - m), a, (size_t)m * 8);   // copied first: q may be a
        q[0] = 0;
        return 0;
    }
    int s = FullHelper::LeadingZeroCount(b[0]);
    uint64_t d1 = n >= 2 ? Shld(b[0], b[1], s) : 0;
    std::memcpy(R + 2, a, (size_t)n * 8);
    int pos = n, qi = 0;
    while (true)
    {
        uint64_t digit;
        int cmp = R[1] != 0 ? 1 : CompareWindow(R, b, n);
        if (cmp < 0) digit = 0;
        else if (cmp == 0)
        {
            digit = 1;
            std::memset(R + 2, 0, (size_t)n * 8);
        }
        else digit = DivDigit(R, b, n, s, d1);
        q[qi++] = digit;
        if (pos == m) break;
        R[0] = 0;   // the window one word up, the next dividend word in
        std::memmove(R + 1, R + 2, (size_t)n * 8);
        R[n + 1] = a[pos++];
    }
    return 0;
}

uint64_t FULLINT_GENERIC(Fullint64Div2longWin)(const uint64_t* a, uint64_t b, uint64_t* q, uint64_t* r, int lenA)
{
    uint64_t rem = 0;   // every dividend word gives a quotient word; q may be a
    for (int i = 0; i < lenA; i++) q[i] = FullHelper::Div128(rem, a[i], b, &rem);
    *r = rem;
    return 0;
}

int FULLINT_GENERIC(Fullint64CompareWIN)(const uint64_t* a, const uint64_t* b, int lenA, int lenB)
{
    return CompareWords(a, b, lenA, lenB);
}

int FULLINT_GENERIC(Fullint64CmpNWIN)(const uint64_t* a, const uint64_t* b, int64_t n)
{
    for (int64_t i = 0; i < n; i++)
        if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
    return 0;
}

int Fullint64CmpObjWIN(const void*, const void*)
{
    NotHere();   // x64 only (FullInt.h)
}

void FULLINT_GENERIC(Fullint64ToDecStringWin)(const uint64_t* value, uint64_t* valueBuf, uint8_t* resultBuf, int lenValue, int* lenResult)
{
    // repeated division by 10^19, the digits right-aligned in resultBuf (*lenResult bytes); out:
    // the first digit's index
    std::memcpy(valueBuf, value, (size_t)lenValue * 8);
    int start = 0;
    while (start < lenValue && valueBuf[start] == 0) start++;
    int pos = *lenResult;
    while (true)
    {
        uint64_t rem = 0;
        for (int i = start; i < lenValue; i++) valueBuf[i] = FullHelper::Div128(rem, valueBuf[i], Base10, &rem);
        while (start < lenValue && valueBuf[start] == 0) start++;
        bool last = start == lenValue;
        for (int d = 0; d < 19; d++)   // the top chunk without leading zeros
        {
            resultBuf[--pos] = (uint8_t)('0' + rem % 10);
            rem /= 10;
            if (last && rem == 0) break;
        }
        if (last) break;
    }
    *lenResult = pos;
}

void FULLINT_GENERIC(Fullint64FromDecStringWin)(const uint8_t* value, uint64_t* valueBuf, uint64_t* resultBuf, int lenValue, int* lenResult)
{
    // the digits as base-10^19 blocks (valueBuf: lenValue / 19 + 1 words), then base 2^64: each pass
    // divides the blocks by 2^64, the last remainder a binary word; out: the first word's index
    int len = lenValue;
    int first = len % 19;
    if (first == 0) first = 19;
    int pos = 0, count = 0;
    uint64_t block = 0;
    for (int i = 0; i < first; i++) block = block * 10 + (uint64_t)(value[pos++] - '0');
    valueBuf[count++] = block;
    while (pos < len)
    {
        block = 0;
        for (int i = 0; i < 19 && pos < len; i++) block = block * 10 + (uint64_t)(value[pos++] - '0');
        valueBuf[count++] = block;
    }
    int index = *lenResult, start = 0;
    while (start < count)
    {
        uint64_t remainder = 0;
        for (int i = start; i < count; i++)
        {
            uint64_t lo, hi = FullHelper::BigMul(remainder, Base10, &lo);
            uint64_t sum = lo + valueBuf[i];
            if (sum < lo) hi++;
            valueBuf[i] = hi;
            remainder = sum;
        }
        resultBuf[--index] = remainder;
        while (start < count && valueBuf[start] == 0) start++;
    }
    *lenResult = index;
}

int FULLINT_GENERIC(Fullint64AndWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs)
{
    return Bitwise(0, a, b, r, lenA, lenB, lenR, signs);
}

int FULLINT_GENERIC(Fullint64OrWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs)
{
    return Bitwise(1, a, b, r, lenA, lenB, lenR, signs);
}

int FULLINT_GENERIC(Fullint64XorWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs)
{
    return Bitwise(2, a, b, r, lenA, lenB, lenR, signs);
}

void FULLINT_GENERIC(Fullint64ShlWIN)(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift)
{
    // r >= lenA + shift / 64 + 1 words
    int ws = shift >> 6, s = shift & 63;
    int lead = lenR - (lenA + 1 + ws), k = 0;
    for (; k < lead; k++) r[k] = 0;
    uint64_t prev = 0;
    for (int i = 0; i < lenA; i++)
    {
        uint64_t cur = a[i];
        r[k++] = s == 0 ? prev : prev << s | cur >> (64 - s);
        prev = cur;
    }
    r[k++] = prev << s;
    for (; k < lenR; k++) r[k] = 0;
}

void FULLINT_GENERIC(Fullint64ShrWIN)(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift)
{
    // r >= max(lenA - shift / 64, 1) words, r may be a
    int ws = shift >> 6, s = shift & 63;
    int count = lenA - ws;
    if (count <= 0)
    {
        std::memset(r, 0, (size_t)lenR * 8);
        return;
    }
    int lead = lenR - count;
    for (int k = 0; k < lead; k++) r[k] = 0;
    uint64_t prev = 0;
    for (int i = 0; i < count; i++)
    {
        uint64_t cur = a[i];
        r[lead + i] = s == 0 ? cur : cur >> s | prev << (64 - s);
        prev = cur;
    }
}

uint64_t FULLINT_GENERIC(Fullint64DivExactWIN)(const uint64_t* a, uint64_t* q, int len, uint64_t d)
{
    // Hensel division from the low end on x = a >> t (x64 ToomOps.asm): d = d'·2^t, inv = d'^-1
    // mod 2^64; q_i = (x_i - c)·inv, c' = hi(q_i·d') + (x_i < c); returns the last c (0 for a
    // multiple). q may be a: a word and the one above it are read before q_i is written.
    int t = 0;
    while (((d >> t) & 1) == 0) t++;
    uint64_t dd = d >> t, inv = (3 * dd) ^ 2;
    for (int k = 0; k < 4; k++) inv *= 2 - dd * inv;
    uint64_t c = 0;
    for (int i = len - 1; i >= 0; i--)
    {
        uint64_t x = a[i] >> t;
        if (t != 0 && i > 0) x |= a[i - 1] << (64 - t);
        uint64_t borrow = x < c ? 1 : 0, qi = (x - c) * inv, lo;
        q[i] = qi;
        c = FullHelper::BigMul(qi, dd, &lo) + borrow;
    }
    return c;
}

void FULLINT_GENERIC(Fullint64SqrWIN)(const uint64_t* a, uint64_t* r, int lenA, int lenR)
{
    FULLINT_GENERIC(Fullint64MultWIN)(a, a, r, lenA, lenA, lenR);   // lenR >= 2·lenA: the full product
}

void FULLINT_GENERIC(Fullint64MulLoWIN)(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int n)
{
    std::vector<uint64_t> full((size_t)lenA + lenB);   // R = A·B mod β^n: the product's low n words
    FULLINT_GENERIC(Fullint64MultWIN)(a, b, full.data(), lenA, lenB, lenA + lenB);
    std::memcpy(r, full.data() + full.size() - n, (size_t)n * 8);
}

int FULLINT_GENERIC(Fullint64CpuFeatures)(void) { return 0; }   // (AArch64 asm: Cpu.s, for the .NET core)

// the FullInt-object kernels: not this case (the C++ paths take every case)
int Fullint64AndObjWIN(void*, const void*, const void*) { return 1; }
int Fullint64ShlObjWIN(void*, const void*, int) { return 1; }

// the reciprocal versions: the baseline under their names (Cpu::Recip is not set here)
void FULLINT_GENERIC(Fullint64Div2longWIN_recip)(const uint64_t* a, uint64_t b, uint64_t* q, uint64_t* r, int lenA)
{
    FULLINT_GENERIC(Fullint64Div2longWin)(a, b, q, r, lenA);
}

void FULLINT_GENERIC(Fullint64DivWIN_recip)(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r, int lenA, int lenB, uint64_t*)
{
    FULLINT_GENERIC(Fullint64DivWin)(a, b, q, r, lenA, lenB);
}

uint64_t FULLINT_GENERIC(Fullint64DivQWIN_recip)(const uint64_t*, const uint64_t*, uint64_t*, uint64_t*, int, int, uint64_t*)
{
    return 1;   // "saturated": the caller divides exactly
}

void FULLINT_GENERIC(Fullint64ToDecStringWIN_recip)(const uint64_t* value, uint64_t* valueBuf, uint8_t* resultBuf, int lenValue, int* lenResult)
{
    FULLINT_GENERIC(Fullint64ToDecStringWin)(value, valueBuf, resultBuf, lenValue, lenResult);
}

// NttAvx2.asm's entries: Ntt.cpp calls them only when Cpu::Has(Cpu::Avx2)
void Fullint64NttDif1WIN_avx2(uint32_t*, int64_t, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttDif2WIN_avx2(uint32_t*, int64_t, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttDif3WIN_avx2(uint32_t*, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttDit1WIN_avx2(uint32_t*, int64_t, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttDit2WIN_avx2(uint32_t*, int64_t, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttDit3WIN_avx2(uint32_t*, int64_t, const uint32_t*, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttR3DifWIN_avx2(uint32_t*, int64_t, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttR3DitWIN_avx2(uint32_t*, int64_t, const uint32_t*, uint32_t) { NotHere(); }
void Fullint64NttPointWIN_avx2(uint32_t*, const uint32_t*, int64_t, uint32_t, uint32_t) { NotHere(); }
void Fullint64NttScaleWIN_avx2(uint32_t*, int64_t, uint32_t, uint32_t, uint32_t) { NotHere(); }
void Fullint64NttLoadWIN_avx2(const uint64_t*, int64_t, uint32_t*, int64_t, uint32_t) { NotHere(); }
uint64_t Fullint64NttCrtWIN(const uint32_t*, const uint32_t*, const uint32_t*, int64_t, uint64_t*, int64_t, const uint64_t*) { NotHere(); }

}  // extern "C"
