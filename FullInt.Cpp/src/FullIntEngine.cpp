// Port of FullInt.Core/FullIntEngine.cs: the general (out-of-line) paths.
#include <fullnumerics/FullInt.h>
#include <fullnumerics/BurnikelZiegler.h>
#include <fullnumerics/NewtonDivision.h>
#include <fullnumerics/Karatsuba.h>

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <charconv>
#include <cstring>
#include <memory>
#include <ostream>
#include <stdexcept>

#if defined(_M_X64) || defined(__x86_64__) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2) || defined(__SSE2__)
#include <emmintrin.h>
#define FULLINT_SSE2 1
#else
#define FULLINT_SSE2 0
#endif

namespace fullnumerics {

// a few words zeroed / copied in place: MSVC turns the plain loops into memset / memcpy calls, whose
// entry outweighs a few words; long runs still use them. Two words a store (SSE2; the plain loops
// stored one: a copy of 12-16 words 8-12 ns, GMP 6)
static FULLINT_FORCEINLINE void ZeroWords(uint64_t* p, size_t n) noexcept
{
    if (n > 16)
    {
        std::memset(p, 0, n * sizeof(uint64_t));
        return;
    }
#if FULLINT_SSE2
    __m128i z = _mm_setzero_si128();
    size_t i = 0;
    for (; i + 2 <= n; i += 2) _mm_storeu_si128((__m128i*)(p + i), z);
    if (i < n) _mm_storel_epi64((__m128i*)(p + i), z);
#else
    volatile uint64_t* v = p;
    for (size_t i = 0; i < n; i++) v[i] = 0;
#endif
}

// word by word (bitwiseInto: with the SSE2 copy inlined there x86's a & b on 1-16 words ran 1.4-1.8 ns
// slower, though the and path copies nothing)
static FULLINT_FORCEINLINE void CopyWordsScalar(uint64_t* dst, const uint64_t* src, size_t n) noexcept
{
    if (n > 16)
    {
        std::memcpy(dst, src, n * sizeof(uint64_t));
        return;
    }
    volatile uint64_t* v = dst;
    for (size_t i = 0; i < n; i++) v[i] = src[i];
}

static FULLINT_FORCEINLINE void CopyWords(uint64_t* dst, const uint64_t* src, size_t n) noexcept
{
    if (n > 16)
    {
        std::memcpy(dst, src, n * sizeof(uint64_t));
        return;
    }
#if FULLINT_SSE2
    size_t i = 0;
    for (; i + 2 <= n; i += 2) _mm_storeu_si128((__m128i*)(dst + i), _mm_loadu_si128((const __m128i*)(src + i)));
    if (i < n) _mm_storel_epi64((__m128i*)(dst + i), _mm_loadl_epi64((const __m128i*)(src + i)));
#else
    volatile uint64_t* v = dst;
    for (size_t i = 0; i < n; i++) v[i] = src[i];
#endif
}


namespace {

[[noreturn]] FULLINT_NOINLINE void ThrowDivideByZero()
{
    throw std::domain_error("division by zero");
}

// scratch for results that are dropped ("%" drops the quotient); grown on demand, per thread
uint64_t* Scratch(size_t words)
{
    thread_local std::unique_ptr<uint64_t[]> buffer;
    thread_local size_t capacity = 0;
    if (capacity < words)
    {
        buffer = std::make_unique_for_overwrite<uint64_t[]>(words);
        capacity = words;
    }
    return buffer.get();
}

// freed word buffers per thread, by power-of-two size class (8 .. CachedWordsMax words)
struct BufferCache
{
    static constexpr int Classes = 12;   // 8, 16, ..., 16384 words
    static constexpr int PerClass = 4;
    uint64_t* slots[Classes][PerClass];
    int count[Classes] = {};
    bool alive = true;   // false after this thread's cache is gone (static numbers freed at exit)

    ~BufferCache()
    {
        alive = false;
        for (int c = 0; c < Classes; c++)
            for (int i = 0; i < count[c]; i++) delete[] slots[c][i];
    }
};
thread_local BufferCache cache;

}  // namespace

uint64_t* WordBuffers::Allocate(uint32_t words, uint32_t* capacity)
{
    if (words > CachedWordsMax)
    {
        *capacity = words;
        return new uint64_t[words];
    }
    uint32_t cap = std::bit_ceil(std::max(words, 8u));
    *capacity = cap;
    int cls = std::countr_zero(cap) - 3;
    if (cache.alive && cache.count[cls] > 0) return cache.slots[cls][--cache.count[cls]];
    return new uint64_t[cap];
}

void WordBuffers::Free(uint64_t* p, uint32_t capacity) noexcept
{
    if (capacity <= CachedWordsMax && std::has_single_bit(capacity) && cache.alive)
    {
        int cls = std::countr_zero(capacity) - 3;
        if (cache.count[cls] < BufferCache::PerClass)
        {
            cache.slots[cls][cache.count[cls]++] = p;
            return;
        }
    }
    delete[] p;
}

#pragma region add

// a + b, or a - b with negate2 (no FullInt is made for -b), built by addInto in a fresh number
// (never aliased): its short paths (two words straight-line, the kernel entered directly, the
// length set from the carry) serve both API forms. x64 2-64 words: 0.8-0.95 of the separate path.
FullInt FullIntEngine::addOrSub(const FullInt& value1, const FullInt& value2, bool negate2)
{
    FullInt r;
    addIntoGeneral(r, value1, value2, negate2);   // a fresh number: the thin entry's checks would not apply
    return r;
}

// C# allocates the exact size and widens on a carry out; here the carry word is reserved in
// front (one more word costs nothing) and dropped by the offset when it stays zero.
FullInt FullIntEngine::addMagnitudes(CSpan a, CSpan b, bool negative)
{
    uint32_t n = (uint32_t)std::max(a.size(), b.size());
    FullInt r;
    uint64_t* p = r.Prepare(n + 1);
    p[0] = FullIntCore::Add(a, b, Span(p + 1, n));
    r.Normalize(negative);
    return r;
}

FullInt FullIntEngine::subMagnitudes(CSpan a, CSpan b, bool neg1, bool neg2)
{
    uint32_t n = (uint32_t)std::max(a.size(), b.size());   // |a - b| always fits
    FullInt r;
    uint64_t* p = r.Prepare(n);
    int compareResult = FullIntCore::Sub(a, b, Span(p, n));
    r.Normalize(compareResult > 0 ? neg1 : compareResult < 0 ? neg2 : false);
    return r;
}

#pragma endregion

#pragma region into an existing result

// result = a ± b in result's buffer. When result is one of the operands and there are enough
// words in front of its number (a carry word left by a previous add), the kernel writes in
// place: it reads both words before writing, from the low end, and the operand ends where the
// result ends. A result that is not an operand just reuses its capacity. Otherwise (aliased,
// no room) a new number is made and moved in.
#if FULLINT_WORD64
// a ± b when both are at most two words (the caller checks): plain 64-bit arithmetic, no kernel call, all words read
// first (result may be an operand). Writes straight into result's buffer: every buffer holds at
// least InlineWords words, so no Prepare / Grow - and no call: the function stays a leaf.
void FullIntEngine::addIntoShort(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2) noexcept
{
    CSpan a = value1.Words(), b = value2.Words();
    bool neg1 = value1.neg_, neg2 = value2.neg_ != negate2;
    size_t la = a.size(), lb = b.size();
    uint64_t a0 = a[la - 1], a1 = la == 2 ? a[0] : 0;
    uint64_t b0 = b[lb - 1], b1 = lb == 2 ? b[0] : 0;
    uint64_t hi, mid, lo;
    bool negative;
    if (neg1 == neg2)
    {
        lo = a0 + b0;
        uint64_t c = lo < a0 ? 1 : 0;
        mid = a1 + c;
        hi = mid < c ? 1 : 0;
        mid += b1;
        hi += mid < b1 ? 1 : 0;
        negative = neg1;
    }
    else
    {
        bool aBig = a1 != b1 ? a1 > b1 : a0 >= b0;
        uint64_t x1 = aBig ? a1 : b1, x0 = aBig ? a0 : b0, y1 = aBig ? b1 : a1, y0 = aBig ? b0 : a0;
        hi = 0;
        mid = x1 - y1 - (x0 < y0 ? 1 : 0);
        lo = x0 - y0;
        negative = aBig ? neg1 : neg2;
    }
    result.SetShort(hi, mid, lo, negative);
}
#endif

// The common case alone, small (only the kernel call inside, few registers to keep - the whole
// function saved 8-9 registers on every call): equal lengths of at least 3 words, room in result's
// buffer, result neither operand; same signs: AddN, opposite: the first differing word from the top
// decides, SubN on the words from it. Everything else: addIntoGeneral.
void FullIntEngine::addInto(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2)
{
#if FULLINT_WORD64
    // a dispatcher only: every case is a jump with the same arguments, so this saves no registers
    // (with the add and the subtract inline it saved eight on every call, the add's four too)
    uint32_t n = value1.size_;
    if (n <= 2 && value2.size_ <= 2)
    {
        addIntoShort(result, value1, value2, negate2);
        return;
    }
    if (n >= 3 && n == value2.size_ && n < result.cap_ && &result != &value1 && &result != &value2)
    {
        if (value1.neg_ == (value2.neg_ != negate2)) addIntoAddN(result, value1, value2);
        else addIntoSubN(result, value1, value2, negate2);
        return;
    }
#endif
    // x86: straight to addIntoGeneral. MSVC makes no tail calls there: any dispatcher in front (out of
    // line, inline at the caller, or inside addIntoGeneral calling AddN / SubCmpN paths) cost 1-2
    // words 10-20% or 16-128 words 3-7% - tried 2026-11-18
    addIntoGeneral(result, value1, value2, negate2);
}

#if FULLINT_WORD64
// equal lengths >= 3, same signs, room in result (n + 1 <= cap_), result neither operand
// (the kernel writes the header too: nothing kept across the call, a jump to it)
void FullIntEngine::addIntoAddN(FullInt& result, const FullInt& value1, const FullInt& value2) noexcept
{
    static_assert(offsetof(FullInt, size_) == offsetof(FullInt, off_) + 4 &&
                  offsetof(FullInt, neg_) == offsetof(FullInt, off_) + 8, "the kernels' header");
    result.neg_ = value1.neg_;
    result.size_ = value1.size_;   // in: n
    Fullint64AddNHdrWIN(value1.Data(), value2.Data(), result.Base(), &result.off_);
}

// equal lengths >= 3, opposite signs, room in result, result neither operand: one kernel compares
// from the top, subtracts the smaller from the larger at the words' own places and counts the
// zero words above (only volatile registers on either side of the call)
void FullIntEngine::addIntoSubN(FullInt& result, const FullInt& value1, const FullInt& value2, bool) noexcept
{
    // opposite signs: the larger magnitude's sign - value1's, the kernel flips it when it swaps
    result.neg_ = value1.neg_;
    result.size_ = value1.size_;   // in: n
    Fullint64SubCmpNHdrWIN(value1.Data(), value2.Data(), result.Base(), &result.off_);
}
#endif

void FullIntEngine::addIntoGeneral(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2)
{
    bool neg1 = value1.neg_, neg2 = value2.neg_ != negate2;
    CSpan a = value1.Words(), b = value2.Words();
    bool same = neg1 == neg2;

#if FULLINT_WORD64
    if (a.size() <= 2 && b.size() <= 2)
    {
        addIntoShort(result, value1, value2, negate2);
        return;
    }
#else
    // x86: the short cases inline here, as before (the out-of-line leaf with its 64-bit arithmetic
    // on 32-bit registers cost 1-2 words 20-60%)
    if (a.size() == 1 && b.size() == 1)   // one word: plain 64-bit arithmetic (both read first)
    {
        uint64_t x = a[0], y = b[0], hi = 0, lo;
        bool negative;
        if (same) { lo = x + y; hi = lo < x ? 1 : 0; negative = neg1; }
        else if (x >= y) { lo = x - y; negative = neg1; }
        else { lo = y - x; negative = neg2; }
        uint64_t* q = result.Prepare(2);
        q[0] = hi;
        q[1] = lo;
        result.Normalize(negative);
        return;
    }
    if (a.size() <= 2 && b.size() <= 2)   // two words: straight-line, no kernel call (all read first)
    {
        uint64_t a0 = a[a.size() - 1], a1 = a.size() == 2 ? a[0] : 0;
        uint64_t b0 = b[b.size() - 1], b1 = b.size() == 2 ? b[0] : 0;
        if (same)
        {
            uint64_t lo = a0 + b0, c = lo < a0 ? 1 : 0;
            uint64_t mid = a1 + c, c2 = mid < c ? 1 : 0;
            mid += b1;
            c2 += mid < b1 ? 1 : 0;
            uint64_t* q = result.Prepare(3);
            q[0] = c2;
            q[1] = mid;
            q[2] = lo;
            result.Normalize(neg1);
            return;
        }
        bool aBig = a1 != b1 ? a1 > b1 : a0 >= b0;
        uint64_t x1 = aBig ? a1 : b1, x0 = aBig ? a0 : b0, y1 = aBig ? b1 : a1, y0 = aBig ? b0 : a0;
        uint64_t* q = result.Prepare(2);
        q[0] = x1 - y1 - (x0 < y0 ? 1 : 0);
        q[1] = x0 - y0;
        result.Normalize(aBig ? neg1 : neg2);
        return;
    }
#endif


    if (&result == &value1 || &result == &value2)
    {
        addIntoAliased(result, a, b, neg1, neg2);
        return;
    }
    if (same)
    {
        // both at least one word long with a nonzero top: the sum's length is the longer one's, one more
        // with a carry out - set from the carry, no scan for leading zeros
        if (a.size() < b.size()) std::swap(a, b);
        size_t la = a.size(), lb = b.size();
        uint64_t* p = result.Prepare((uint32_t)la + 1);
        uint64_t c = Fullint64AddNWIN(a.data() + (la - lb), b.data(), p + 1 + (la - lb), (int64_t)lb);
        for (size_t i = la - lb; i-- > 0;)
        {
            uint64_t v = a[i] + c;
            c = v < c ? 1 : 0;
            p[1 + i] = v;
        }
        p[0] = c;
        result.off_ = c == 0 ? 1 : 0;
        result.size_ = (uint32_t)(la + c);
        result.neg_ = neg1;
        return;
    }
    {
        // |a| - |b| or |b| - |a|: the longer is the larger; on equal lengths the equal top words cancel
        // first (zero: done). The leading zeros of the difference counted here, the fields set once.
        const uint64_t* x = a.data();
        const uint64_t* y = b.data();
        size_t lx = a.size(), ly = b.size();
        bool negative = neg1;
        if (lx < ly)
        {
            std::swap(x, y);
            std::swap(lx, ly);
            negative = neg2;
        }
        else if (lx == ly)
        {
#if FULLINT_WORD64
            // one kernel: the compare from the top, the larger minus the smaller at the words' own
            // places, the zero words above counted (AddSubN.asm); x86 keeps the compare here (with
            // the kernel there: 16-128 words 3-7% slower, its frame and the 32-bit compare loop)
            uint64_t* p = result.Prepare((uint32_t)lx);
            uint64_t r = Fullint64SubCmpNWIN(x, y, p, (int64_t)lx);
            uint32_t off = (uint32_t)r;
            if (off == lx)
            {
                result.SetWords(false, 0, 0);
                return;
            }
            result.off_ = off;
            result.size_ = (uint32_t)lx - off;
            result.neg_ = r >> 63 != 0 ? neg2 : neg1;
            return;
#else
            size_t i = 0;
            while (i < lx && x[i] == y[i]) i++;
            if (i == lx)
            {
                result.SetWords(false, 0, 0);
                return;
            }
            if (x[i] < y[i])
            {
                std::swap(x, y);
                negative = neg2;
            }
            x += i;
            y += i;
            lx -= i;
            ly -= i;
#endif
        }
        uint64_t* p = result.Prepare((uint32_t)lx);
        uint64_t borrow = Fullint64SubNWIN(x + (lx - ly), y, p + (lx - ly), (int64_t)ly);
        for (size_t i = lx - ly; i-- > 0;)
        {
            uint64_t v = x[i];
            p[i] = v - borrow;
            borrow = v < borrow ? 1 : 0;
        }
        size_t z = 0;
        while (p[z] == 0) z++;   // x > y: a nonzero word below
        result.off_ = (uint32_t)z;
        result.size_ = (uint32_t)(lx - z);
        result.neg_ = negative;
        return;
    }

}

// the result is one of the operands: in its own buffer when the sum fits there (the operand's words are
// read before their places are written), otherwise a new number
void FullIntEngine::addIntoAliased(FullInt& result, CSpan a, CSpan b, bool neg1, bool neg2)
{
    bool same = neg1 == neg2;
    uint32_t n = (uint32_t)std::max(a.size(), b.size()) + (same ? 1 : 0);
    uint32_t end = result.off_ + result.size_;   // the aliased operand's words end here
    if (end < n)
    {
        addIntoFresh(result, a, b, neg1, neg2);
        return;
    }
    uint64_t* p = result.Base() + (end - n);
    result.off_ = end - n;
    result.size_ = n;

    if (same)
    {
        FullIntCore::Add(a, b, Span(p, n));
        result.Normalize(neg1);
    }
    else
    {
        int compareResult = FullIntCore::Sub(a, b, Span(p, n));
        result.Normalize(compareResult > 0 ? neg1 : compareResult < 0 ? neg2 : false);
    }
}

void FullIntEngine::addIntoFresh(FullInt& result, CSpan a, CSpan b, bool neg1, bool neg2)
{
    result = neg1 == neg2 ? addMagnitudes(a, b, neg1) : subMagnitudes(a, b, neg1, neg2);
}

// 2 x 1 and 2 x 2 words inline (FullHelper::Mul2x1 / Mul2x2 / Sqr2), no kernel call; false: longer.
// x86 (no 64 x 64 multiply: four 32 x 32 each) 2 x 1 only: 6.3 ns against the kernel's 9.3, while
// 2 x 2 18 / 15 and a² 14.6 / 12.7 lose to it
static FULLINT_FORCEINLINE bool MultShort(CSpan a, CSpan b, uint64_t* r) noexcept
{
    size_t la = a.size(), lb = b.size();
    if (la + lb == 3)
    {
        if (la == 2) FullHelper::Mul2x1(a.data(), b[0], r);
        else FullHelper::Mul2x1(b.data(), a[0], r);
        return true;
    }
#if FULLINT_WORD64
    if (la == 2 && lb == 2)
    {
        if (Karatsuba::Same(a, b)) FullHelper::Sqr2(a.data(), r);
        else FullHelper::Mul2x2(a.data(), b.data(), r);
        return true;
    }
#endif
    return false;
}

void FullIntEngine::multIntoAliased(FullInt& result, const FullInt& value1, const FullInt& value2)
{
    result = mult(value1, value2);
}

void FullIntEngine::shiftIntoAliased(FullInt& result, const FullInt& value, int shift, bool left)
{
    result = left ? shiftLeft(value, shift) : shiftRight(value, shift);
}

void FullIntEngine::multInto(FullInt& result, const FullInt& value1, const FullInt& value2)
{
    if (&result == &value1 || &result == &value2)   // the multiply reads the operands throughout
    {
        multIntoAliased(result, value1, value2);
        return;
    }
    CSpan a = value1.Words(), b = value2.Words();
    uint32_t length = (uint32_t)(a.size() + b.size());
    Span r(result.Prepare(length), length);
    if (length == 2)
        r[0] = FullHelper::BigMul(a[0], b[0], &r[1]);
    else if (MultShort(a, b, r.data()))
        ;
    else if (Karatsuba::Same(a, b) && a.size() < (size_t)Karatsuba::SqrThreshold)
        FullIntCore::Sqr(a, r);                                       // x·x: the square kernel
    else if (std::min(a.size(), b.size()) >= (size_t)Karatsuba::Threshold || Karatsuba::Same(a, b))
        Karatsuba::Multiply(a, b, r);
    else
        FullIntCore::Mult(a, b, r);
    result.Normalize(value1.neg_ != value2.neg_);
}

// a left shift by bits != 0 inline up to this many words, then the kernel (x64 1-8 words: 4.3-7.8 ns, the
// kernel 6.2-9.9; x86: a variable 64-bit shift is a helper call per word, the kernel from two words)
#if FULLINT_WORD64
static constexpr size_t SHIFT_INLINE = 8;
#else
static constexpr size_t SHIFT_INLINE = 0;
#endif

// The thin entries of the shifts (x64), as addInto: the common case alone in a small function - a
// shift by 1-63 bits of a value up to SHIFT_INLINE words, room in result, result not the value:
// the words here, written straight into result's buffer (no Prepare / Grow, no kernel call, no
// zero runs: the function stays small). Everything else: the general functions (a jump).
void FullIntEngine::shiftLeftInto(FullInt& result, const FullInt& value, int shift)
{
#if FULLINT_WORD64
    uint32_t la = value.size_;
    if ((unsigned)(shift - 1) < 63u && la < result.cap_ && &result != &value)
    {
        const uint64_t* a = value.Data();
        uint64_t* p = result.Base();
        if (la > SHIFT_INLINE)   // longer: the kernel, straight into the buffer
        {
            FullIntCore::ShiftLeft(CSpan(a, la), shift, Span(p, la + 1));
            uint32_t zk = p[0] == 0 ? 1 : 0;
            result.off_ = zk;
            result.size_ = la + 1 - zk;
            result.neg_ = value.neg_;
            return;
        }
        int bits = shift, back = 64 - shift;
        uint64_t prev = 0;   // the word above: its bits shifted out come down into p[i]
        for (uint32_t i = 0; i < la; i++)
        {
            uint64_t cur = a[i];
            p[i] = prev << bits | cur >> back;
            prev = cur;
        }
        p[la] = prev << bits;
        // normalized a: only the top word can be zero; a zero value (one zero word) stays one zero word
        uint32_t z = p[0] == 0 ? 1 : 0;
        result.off_ = z;
        result.size_ = la + 1 - z;
        result.neg_ = value.neg_;
        return;
    }
#endif
    shiftLeftIntoGeneral(result, value, shift);
}

#if defined(_M_X64) && defined(_WIN32)
}  // namespace fullnumerics
// IntoObj.asm's ShlObj jumps here when the case is not its own (the arguments as they came)
extern "C" void Fullint64ShlIntoGeneral(void* r, const void* a, int shift)
{
    fullnumerics::FullIntEngine::shiftLeftInto(*static_cast<fullnumerics::FullInt*>(r), *static_cast<const fullnumerics::FullInt*>(a), shift);
}
namespace fullnumerics {
#endif

void FullIntEngine::shiftLeftIntoGeneral(FullInt& result, const FullInt& value, int shift)
{
    CSpan a = value.Words();
    if (&result == &value || shift < 0 || a[0] == 0)   // (a[0] == 0: the value is zero)
    {
        shiftIntoAliased(result, value, shift, true);
        return;
    }
    size_t la = a.size();
    uint32_t n = (uint32_t)la + (uint32_t)shift / 64 + 1;
    bool negative = value.neg_;
    int bits = shift % 64;
    if (bits != 0 && la > SHIFT_INLINE)   // the kernel
    {
        FullIntCore::ShiftLeft(a, shift, Span(result.Prepare(n), n));
        result.Normalize(negative);
        return;
    }
    uint64_t* p = result.Prepare(n);
    if (bits == 0)   // whole words: a copy at any length
    {
        p[0] = 0;
        CopyWords(p + 1, a.data(), la);
        ZeroWords(p + la + 1, n - la - 1);
    }
    else   // a few words: here, no kernel call (its entry cost more than the words)
    {
        uint64_t prev = 0;   // the word above: its bits shifted out come down into p[i]
        for (size_t i = 0; i < la; i++)
        {
            p[i] = prev << bits | a[i] >> (64 - bits);
            prev = a[i];
        }
        p[la] = prev << bits;
        ZeroWords(p + la + 1, n - la - 1);
    }
    // a[0] != 0: only the top word can be zero (p[0] = a[0] >> (64 - bits) == 0 keeps all of a[0] in p[1])
    uint32_t z = p[0] == 0 ? 1 : 0;
    result.off_ = z;
    result.size_ = n - z;
    result.neg_ = negative;
}

void FullIntEngine::shiftRightInto(FullInt& result, const FullInt& value, int shift)
{
#if FULLINT_WORD64
    uint32_t la = value.size_;
    if ((unsigned)(shift - 1) < 63u && la <= result.cap_ && &result != &value && !value.neg_)
    {
        const uint64_t* a = value.Data();
        uint64_t* p = result.Base();
        if (la > SHIFT_INLINE)   // longer: the kernel, straight into the buffer
        {
            FullIntCore::ShiftRight(CSpan(a, la), shift, Span(p, la));
            uint32_t zk = p[0] == 0 ? 1 : 0;   // la > 1 here
            result.off_ = zk;
            result.size_ = la - zk;
            result.neg_ = false;
            return;
        }
        int bits = shift, back = 64 - shift;
        uint64_t prev = 0;   // the word above: its low bits come down into p[i]
        for (uint32_t i = 0; i < la; i++)
        {
            uint64_t cur = a[i];
            p[i] = cur >> bits | prev << back;
            prev = cur;
        }
        // only the top word can become zero (and with one word the result stays one word)
        uint32_t z = p[0] == 0 && la > 1 ? 1 : 0;
        result.off_ = z;
        result.size_ = la - z;
        result.neg_ = false;
        return;
    }
#endif
    shiftRightIntoGeneral(result, value, shift);
}

// the general right shift into result (inline into the operator; the thin entry jumps to the
// out-of-line copy below)
FULLINT_FORCEINLINE void FullIntEngine::ShiftRightIntoBody(FullInt& result, const FullInt& value, int shift)
{
    if (&result == &value || shift < 0 || value.neg_)   // negative: floor rounding, the ordinary path
    {
        shiftIntoAliased(result, value, shift, false);
        return;
    }
    CSpan a = value.Words();
    uint32_t n = (uint32_t)std::max<int64_t>((int64_t)a.size() - shift / 64, 1);
    FullIntCore::ShiftRight(a, shift, Span(result.Prepare(n), n));
    result.Normalize(false);
}

void FullIntEngine::shiftRightIntoGeneral(FullInt& result, const FullInt& value, int shift)
{
    ShiftRightIntoBody(result, value, shift);
}

#pragma endregion

#pragma region mult

FullInt FullIntEngine::multGeneral(const FullInt& value1, const FullInt& value2)
{
    CSpan a = value1.Words(), b = value2.Words();
    uint32_t length = (uint32_t)(a.size() + b.size());
    FullInt r;
    Span bytes(r.Prepare(length), length);

    if (MultShort(a, b, bytes.data()))
        ;
    else if (Karatsuba::Same(a, b) && a.size() < (size_t)Karatsuba::SqrThreshold)
        FullIntCore::Sqr(a, bytes);                                   // x·x: the square kernel
    else if (std::min(a.size(), b.size()) >= (size_t)Karatsuba::Threshold || Karatsuba::Same(a, b))
        Karatsuba::Multiply(a, b, bytes);   // recursion in C++, leaves in the kernels
    else
        FullIntCore::Mult(a, b, bytes);

    r.Normalize(value1.neg_ != value2.neg_);
    return r;
}

FullInt FullIntEngine::pow(const FullInt& value1, const FullInt& value2)
{
    // integer power is undefined for negative exponents (same as BigInteger.Pow)
    if (value2.neg_) throw std::out_of_range("The exponent must be non-negative.");

    FullInt result(1);
    FullInt value(value1);
    CSpan e = value2.Words();   // the exponent's bits from the low end (C#: its base-2 string)
    size_t bits = e.size() * 64 - (size_t)FullHelper::LeadingZeroCount(e[0]);
    for (size_t i = 0; i < bits; i++)
    {
        if ((e[e.size() - 1 - i / 64] >> (i % 64)) & 1) result = mult(result, value);
        if (i + 1 < bits) value = mult(value, value);
    }
    return result;
}

#pragma endregion

#pragma region div

// a / b: the remainder is not materialized
FullInt FullIntEngine::div(const FullInt& value1, const FullInt& value2)
{
    FullInt q;
    divRem(value1, value2, &q, nullptr);
    return q;
}

// a % b: the quotient is not materialized
FullInt FullIntEngine::mod(const FullInt& value1, const FullInt& value2)
{
    FullInt r;
    divRem(value1, value2, nullptr, &r);
    return r;
}

FullInt FullIntEngine::div(const FullInt& value1, const FullInt& value2, FullInt& remainder)
{
    FullInt q, r;   // locals: remainder may be value1 or value2
    divRem(value1, value2, &q, &r);
    remainder = std::move(r);
    return q;
}

// The division itself. Truncating (the quotient rounds toward zero, the remainder has the
// dividend's sign). Only the requested results are built; a dropped quotient goes to scratch.
// The results are built in their own buffers; one that is an operand goes through
// divRemAliased (locals moved in at the end - out of line: their unwind frame cost every short
// division several ns).
void FullIntEngine::divRem(const FullInt& value1, const FullInt& value2, FullInt* quotient, FullInt* remainder)
{
    if ((quotient && (quotient == &value1 || quotient == &value2)) ||
        (remainder && (remainder == &value1 || remainder == &value2 || remainder == quotient)))
    {
        divRemAliased(value1, value2, quotient, remainder);
        return;
    }
    if (value2.IsZero()) ThrowDivideByZero();

    CSpan dividend = value1.Words(), divisor = value2.Words();   // normalized: divisor[0] != 0
    bool neg1 = value1.neg_, neg2 = value2.neg_;

    // one word by one word: plain 64-bit division (before the compare: a < b gives q = 0, r = a here as well)
    if (dividend.size() == 1 && divisor.size() == 1)
    {
        uint64_t q1 = dividend[0] / divisor[0];
        uint64_t r1 = dividend[0] - q1 * divisor[0];
        if (remainder) remainder->SetWords(neg1, 0, r1);
        if (quotient) quotient->SetWords(neg1 != neg2, 0, q1);
        return;
    }

    if (CompareAbsQuick(dividend, divisor) == -1)
    {
        if (remainder) *remainder = value1;
        if (quotient) quotient->SetWords(false, 0, 0);
        return;
    }

    // two words by one word: the high word by 64/64, then (r:low) / d by Div128, no kernel call.
    // In a 32-bit build 64-bit division is a helper call (Div128 four of them): there the kernel
    // (x86 a / b of 2 by 1 words: 58 ns, GMP 30)
    if (divisor.size() == 1 && dividend.size() == 2 && sizeof(void*) == 8)
    {
        uint64_t d = divisor[0], hi = dividend[0], lo = dividend[1];
        uint64_t qh = hi / d, r1;
        uint64_t ql = FullHelper::Div128(hi - qh * d, lo, d, &r1);
        if (remainder) remainder->SetWords(neg1, 0, r1);
        if (quotient) quotient->SetWords(neg1 != neg2, qh, ql);
        return;
    }

    uint32_t la = (uint32_t)dividend.size(), lb = (uint32_t)divisor.size();

    if (lb == 1)
    {
        uint64_t r1 = 0;
        if (quotient)
        {
            FullIntCore::Div(dividend, divisor[0], Span(quotient->Prepare(la), la), &r1);
            quotient->Normalize(neg1 != neg2);
        }
        else
        {
            FullIntCore::Div(dividend, divisor[0], Span(Scratch(la), la), &r1);
        }
        if (remainder) remainder->SetWords(neg1, 0, r1);
        return;
    }

#if FULLINT_WORD64
    if (lb == 2 && Cpu::Has(Cpu::Recip))   // two words: the 3-by-2 steps directly (FullIntCore::DivBy2)
    {
        uint64_t local[32], rw[4];
        uint64_t* pq = quotient ? quotient->Prepare(la - 1) : la - 1 <= std::size(local) ? local : Scratch(la - 1);
        FullIntCore::DivBy2(dividend, divisor, Span(pq, la - 1), Span(rw, 4));
        if (remainder)
        {
            uint64_t* p = remainder->Prepare(2);
            p[0] = rw[2];
            p[1] = rw[3];
            remainder->Normalize(neg1);
        }
        if (quotient) quotient->Normalize(neg1 != neg2);
        return;
    }
#endif

    // the remainder kernel buffer is lenB + 2 words (internal calculation), the value right-aligned;
    // a result not asked for goes to scratch (one of the two at most)
    uint32_t lq = la - lb + 1;
    uint64_t local[32];   // a short dropped result: on the stack (the thread_local lookup cost a few ns)
    uint32_t dropped = remainder ? lq : lb + 2;
    uint64_t* scratch = dropped <= std::size(local) ? local : Scratch(dropped);
    uint64_t* pr = remainder ? remainder->Prepare(lb + 2) : scratch;
    uint64_t* pq = quotient ? quotient->Prepare(lq) : scratch;
    ZeroWords(pr, lb + 2);
    ZeroWords(pq, lq);

    // long divisor and long quotient: Burnikel–Ziegler (C#: in div_internal), the same contract
    // longer still: the Newton reciprocal
    if (NewtonDivision::Use(la, lb, !remainder))
    {
        if (remainder) NewtonDivision::DivRem(dividend, divisor, Span(pq, lq), Span(pr, lb + 2));
        else NewtonDivision::Quotient(dividend, divisor, Span(pq, lq));   // the last block's remainder not computed
    }
    // the quotient alone (a / b): an approximate quotient with a guard word, no remainder;
    // a long divisor with a short quotient is cut down to the quotient's length there
    else if (!remainder)   // the remainder not computed
    {
        if (BurnikelZiegler::QuotientByKernel(la, lb)) FullIntCore::Quotient(dividend, divisor, Span(pq, lq));
        else BurnikelZiegler::Quotient(dividend, divisor, Span(pq, lq));
    }
    else if (lb >= 2 * (uint32_t)BurnikelZiegler::Threshold && la - lb >= 2 * (uint32_t)BurnikelZiegler::Threshold)
        BurnikelZiegler::DivRem(dividend, divisor, Span(pq, lq), Span(pr, lb + 2));
    else
        FullIntCore::Div(dividend, divisor, Span(pq, lq), Span(pr, lb + 2));

    if (remainder) remainder->Normalize(neg1);
    if (quotient) quotient->Normalize(neg1 != neg2);
}

void FullIntEngine::divRemAliased(const FullInt& value1, const FullInt& value2, FullInt* quotient, FullInt* remainder)
{
    FullInt q, r;
    divRem(value1, value2, quotient ? &q : nullptr, remainder ? &r : nullptr);
    if (remainder) *remainder = std::move(r);
    if (quotient) *quotient = std::move(q);
}

#pragma endregion

#pragma region bitwise

FullInt FullIntEngine::bitwise(int op, const FullInt& value1, const FullInt& value2)
{
    FullInt r;
    bitwiseInto(r, op, value1, value2);
    return r;
}

// result = value1 op value2 in result's buffer; result aliased with an operand: a new number
void FullIntEngine::bitwiseInto(FullInt& result, int op, const FullInt& value1, const FullInt& value2)
{
    // IntoObj.asm (x64 and x86) works on the objects: its offsets (the same on both)
    static_assert(offsetof(FullInt, cap_) == 64 && offsetof(FullInt, off_) == 68 &&
                  offsetof(FullInt, size_) == 72 && offsetof(FullInt, neg_) == 76 && FullInt::InlineWords == 8,
                  "IntoObj.asm's FullInt layout");
    if (&result == &value1 || &result == &value2)
    {
        bitwiseIntoAliased(result, op, value1, value2);
        return;
    }
    CSpan a = value1.Words(), b = value2.Words();
    if (!value1.neg_ && !value2.neg_)
    {
        // both non-negative (the common case): word by word here, no kernel call - and: the
        // low min(la, lb) words; or / xor: the longer one's top words as they are (a & b on
        // 1-16 words through the kernel: 12-19 ns against GMP's 5-8)
        if (a.size() < b.size()) std::swap(a, b);
        size_t la = a.size(), lb = b.size(), d = la - lb;
        if (op == 0)
        {
            uint64_t* p = result.Prepare((uint32_t)lb);
            for (size_t i = 0; i < lb; i++) p[i] = a[d + i] & b[i];
        }
        else
        {
            uint64_t* p = result.Prepare((uint32_t)la);
            CopyWordsScalar(p, a.data(), d);
            if (op == 1) for (size_t i = 0; i < lb; i++) p[d + i] = a[d + i] | b[i];
            else for (size_t i = 0; i < lb; i++) p[d + i] = a[d + i] ^ b[i];
        }
        result.Normalize(false);
        return;
    }
    uint32_t n = (uint32_t)std::max(a.size(), b.size()) + 1;
    Span rs(result.Prepare(n), n);

    int negative = op == 0 ? FullIntCore::And(a, value1.neg_, b, value2.neg_, rs)
                 : op == 1 ? FullIntCore::Or(a, value1.neg_, b, value2.neg_, rs)
                 : FullIntCore::Xor(a, value1.neg_, b, value2.neg_, rs);

    result.Normalize(negative != 0);
}

void FullIntEngine::bitwiseIntoAliased(FullInt& result, int op, const FullInt& value1, const FullInt& value2)
{
    FullInt r;
    bitwiseInto(r, op, value1, value2);
    result = std::move(r);
}

FullInt FullIntEngine::shiftLeftGeneral(const FullInt& value, int shift)
{
    if (shift < 0) return shiftRight(value, shift == INT32_MIN ? INT32_MAX : -shift);

    CSpan a = value.Words();
    uint32_t n = (uint32_t)a.size() + (uint32_t)shift / 64 + 1;
    FullInt r;
    FullIntCore::ShiftLeft(a, shift, Span(r.Prepare(n), n));
    r.Normalize(value.neg_);
    return r;
}

// arithmetic shift: rounds toward negative infinity, like BigInteger
FullInt FullIntEngine::shiftRightGeneral(const FullInt& value, int shift)
{
    if (shift < 0) return shiftLeft(value, shift == INT32_MIN ? INT32_MAX : -shift);

    if (!value.neg_)   // zero is never negative
    {
        FullInt r;
        if constexpr (SHIFT_INLINE > 0)   // x64 16-64 words: 0.6-0.75 of the kernel path below; x86: only a layer more
        {
            // short: the thin entry (its inline words); longer: a fresh number has no room for them
            // there (InlineWords), so straight to the general function
            if (value.size_ <= 4) shiftRightInto(r, value, shift);   // (8 words: the kernel below is 6% ahead of the loop)
            else ShiftRightIntoBody(r, value, shift);
            return r;
        }
        CSpan a = value.Words();
        uint32_t n = (uint32_t)std::max<int64_t>((int64_t)a.size() - shift / 64, 1);
        FullIntCore::ShiftRight(a, shift, Span(r.Prepare(n), n));
        r.Normalize(false);
        return r;
    }

    // -|a| >> k = -(((|a| - 1) >> k) + 1)
    FullInt shifted = shiftRight(sub(abs(value), FullInt(1)), shift);
    return negate(add(shifted, FullInt(1)));
}

#pragma endregion

#pragma region conversions

std::string FullIntEngine::ToDecString(const FullInt& value)
{
    CSpan w = value.Words();
    if (w.size() == 1)   // one word: the standard library formats it, no buffers, no kernel call
    {
        char text[24];
        char* p = text;
        if (value.neg_) *p++ = '-';
        p = std::to_chars(p, text + sizeof(text), w[0]).ptr;
        return std::string(text, p);
    }
    if (w.size() > (size_t)DecimalConversion::ToStringThreshold)   // long: by halves (FullIntEngine.Decimal.cpp)
    {
        return ToDecStringRecursive(value);
    }
#if FULLINT_WORD64
    if (w.size() >= (size_t)DecimalConversion::FractionFrom && w.size() <= (size_t)DecimalConversion::FractionMaxWords)
    {
        char text[19 * (DecimalConversion::FractionMaxWords + 4) + 2];
        size_t start = WriteDecimalFraction(w, text, sizeof(text));
        if (value.neg_) text[--start] = '-';
        return std::string(text + start, sizeof(text) - start);
    }
#endif

    // small: stack buffers (as C# stackalloc), large: heap
    int resultBytes = (int)(w.size() * 1.014 + 1) * 19 + 1;
    uint8_t stackBytes[2048];
    uint64_t stackWords[96];
    std::unique_ptr<uint8_t[]> heapBytes;
    std::unique_ptr<uint64_t[]> heapWords;
    uint8_t* resultBuf = stackBytes;
    uint64_t* valueBuf = stackWords;
    if (resultBytes > (int)sizeof(stackBytes)) resultBuf = (heapBytes = std::make_unique<uint8_t[]>(resultBytes)).get();
    else std::memset(resultBuf, 0, resultBytes);
    if (w.size() > std::size(stackWords)) valueBuf = (heapWords = std::make_unique_for_overwrite<uint64_t[]>(w.size())).get();

    int start = resultBytes;
    FullIntCore::ToDecString(w, valueBuf, resultBuf, &start);

    if (value.neg_ && start > 0) resultBuf[--start] = '-';
    return std::string((const char*)resultBuf + start, (size_t)(resultBytes - start));
}

std::string FullIntEngine::ToString(const FullInt& value, int base)
{
    if (base == 10) return ToDecString(value);

    CSpan w = value.Words();
    std::string result = value.neg_ ? "-" : "";

    if (base == 16)   // as C#: "0x", the top word without padding, the rest 16 digits each, upper case
    {
        static const char hex[] = "0123456789ABCDEF";
        result += "0x";
        for (size_t i = 0; i < w.size(); i++)
        {
            int digits = 16;
            if (i == 0)
            {
                digits = 1;
                while (digits < 16 && (w[0] >> (4 * digits)) != 0) digits++;
            }
            for (int d = digits - 1; d >= 0; d--) result += hex[(w[i] >> (4 * d)) & 15];
        }
        return result;
    }

    if (base == 2 || base == 8)   // C# divides by the base word by word; for 2 and 8 the bits are read directly
    {
        int bitsPerDigit = base == 2 ? 1 : 3;
        size_t totalBits = w.size() * 64 - (size_t)FullHelper::LeadingZeroCount(w[0]);
        if (totalBits == 0) return result + "0";
        size_t digits = (totalBits + bitsPerDigit - 1) / bitsPerDigit;
        auto bitAt = [&](size_t bit) -> unsigned {   // bit 0 = least significant
            return bit < totalBits ? (unsigned)((w[w.size() - 1 - bit / 64] >> (bit % 64)) & 1) : 0;
        };
        for (size_t d = digits; d-- > 0;)
        {
            unsigned v = 0;
            for (int k = bitsPerDigit - 1; k >= 0; k--) v = (v << 1) | bitAt(d * bitsPerDigit + k);
            result += (char)('0' + v);
        }
        return result;
    }

    throw std::invalid_argument("base must be 2, 8, 10 or 16");
}

// As C#: an optional '-', then a leading '0' means octal and "0x" / "x" hexadecimal; otherwise decimal.
// C++ also rejects characters that are not digits of the base (C# hands decimal text to the
// kernel unchecked).
FullInt FullIntEngine::ToFullInt(std::string_view value)
{
    bool s = false;
    int base = 10;
    if (value.empty()) value = "0";

    if (value[0] == '-')
    {
        s = true;
        value.remove_prefix(1);
    }
    if (value.size() > 1 && value[0] == '0')
    {
        base = 8;
        value.remove_prefix(1);
    }
    if (value.size() > 1 && (value[0] == 'x' || value[0] == 'X'))
    {
        base = 16;
        value.remove_prefix(1);
    }
    if (value.empty()) throw std::invalid_argument("no digits");

    auto digitValue = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return 99;
    };
    bool bad = false;
    if (base == 10)
    {
        // eight characters at a time (SWAR): a digit's byte is 30h-39h - its high nibble 3, and still
        // 3 after + 6 (a carry into the next byte only from a byte >= FAh, rejected by the first test).
        // MSVC vectorized the per-character compare, GCC did not: 5-17x slower on Linux.
        const char* p = value.data();
        size_t n = value.size(), i = 0;
        uint64_t acc = 0;
        for (; i + 8 <= n; i += 8)
        {
            uint64_t x;
            std::memcpy(&x, p + i, 8);
            acc |= ((x & 0xF0F0F0F0F0F0F0F0ull) ^ 0x3030303030303030ull)
                 | (((x + 0x0606060606060606ull) & 0xF0F0F0F0F0F0F0F0ull) ^ 0x3030303030303030ull);
        }
        bad = acc != 0;
        for (; i < n; i++) bad |= (unsigned char)(p[i] - '0') > 9;
    }
    else
    {
        for (char c : value) bad |= digitValue(c) >= base;
    }
    if (bad) throw std::invalid_argument("not a digit of the base");

    FullInt result;
    if (base == 10)
    {
        result = value.size() > (size_t)DecimalConversion::ParseThreshold
            ? ParseDecimal(value)          // long text: by halves (FullIntEngine.Decimal.cpp)
            : ParseDecimalLeaf(value);
    }
    else   // 16 and 8: the digits are bit groups (C#: 16-digit chunks for hex, mult/add per digit for octal)
    {
        int bitsPerDigit = base == 16 ? 4 : 3;
        size_t totalBits = value.size() * bitsPerDigit;
        uint32_t n = (uint32_t)((totalBits + 63) / 64);
        uint64_t* p = result.Prepare(n);
        std::memset(p, 0, n * sizeof(uint64_t));
        for (size_t i = 0; i < value.size(); i++)   // i-th digit from the low end
        {
            uint64_t d = (uint64_t)digitValue(value[value.size() - 1 - i]);
            size_t bit = i * bitsPerDigit;
            p[n - 1 - bit / 64] |= d << (bit % 64);
            if (bit % 64 + bitsPerDigit > 64) p[n - 2 - bit / 64] |= d >> (64 - bit % 64);
        }
    }
    result.Normalize(s);
    return result;
}

#pragma endregion

// ---- FullInt members that need the engine ----

FullInt::FullInt(bool negative, CSpan magnitude)
{
    if (magnitude.empty())
    {
        inl_[0] = 0;
        return;
    }
    std::memcpy(Prepare((uint32_t)magnitude.size()), magnitude.data(), magnitude.size_bytes());
    Normalize(negative);
}

FullInt::FullInt(std::string_view text) : FullInt(Parse(text)) {}

// as C# TryNormalize: white space trimmed, a '+' dropped, the digits of the form checked; the
// text for ToFullInt into out (a '-' kept)
static bool NormalizeNumber(std::string_view text, std::string& out)
{
    auto space = [](char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v'; };
    while (!text.empty() && space(text.front())) text.remove_prefix(1);
    while (!text.empty() && space(text.back())) text.remove_suffix(1);
    bool negative = false;
    if (!text.empty() && (text[0] == '-' || text[0] == '+'))
    {
        negative = text[0] == '-';
        text.remove_prefix(1);
    }
    bool hex = text.size() >= 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X');
    size_t from = hex ? 2 : 0;
    if (from >= text.size()) return false;
    for (size_t k = from; k < text.size(); k++)
    {
        char c = text[k];
        bool ok = hex ? (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') : c >= '0' && c <= '9';
        if (!ok) return false;
    }
    out.assign(negative ? "-" : "");
    out.append(text);
    return true;
}

FullInt FullInt::Parse(std::string_view text)
{
    std::string normalized;
    if (!NormalizeNumber(text, normalized)) throw std::invalid_argument("FullInt: not an integer");
    return FullIntEngine::ToFullInt(normalized);
}

bool FullInt::TryParse(std::string_view text, FullInt& result)
{
    std::string normalized;
    if (NormalizeNumber(text, normalized))
    {
        try
        {
            result = FullIntEngine::ToFullInt(normalized);
            return true;
        }
        catch (const std::invalid_argument&) {}   // "08": not an octal digit
    }
    result = FullInt();
    return false;
}

std::string FullInt::ToString(int base) const { return FullIntEngine::ToString(*this, base); }

std::string FullInt::ToString(std::string_view format) const
{
    if (format.empty()) return ToString(10);
    char kind = format[0];
    size_t precision = 0;
    if (format.size() > 1)
    {
        auto [ptr, ec] = std::from_chars(format.data() + 1, format.data() + format.size(), precision);
        if (ec != std::errc() || ptr != format.data() + format.size()) throw std::invalid_argument("FullInt: unsupported format");
    }
    CSpan w = Words();
    std::string digits;
    if (kind == 'D' || kind == 'd')
    {
        digits = FullIntEngine::ToString(Abs(*this), 10);
    }
    else if (kind == 'X' || kind == 'x' || kind == 'B' || kind == 'b')
    {
        int bits = (kind == 'X' || kind == 'x') ? 4 : 1;
        const char* alphabet = kind == 'x' ? "0123456789abcdef" : "0123456789ABCDEF";
        int perWord = 64 / bits;
        uint64_t mask = (1ull << bits) - 1;
        digits.reserve(w.size() * (size_t)perWord);
        for (size_t i = 0; i < w.size(); i++)
            for (int d = perWord - 1; d >= 0; d--)
            {
                unsigned digit = (unsigned)((w[i] >> (d * bits)) & mask);
                if (digits.empty() && digit == 0 && !(i == w.size() - 1 && d == 0)) continue;
                digits += alphabet[digit];
            }
    }
    else
    {
        throw std::invalid_argument("FullInt: unsupported format");
    }
    if (digits.size() < precision) digits.insert(0, precision - digits.size(), '0');
    return neg_ ? "-" + digits : digits;
}

FullInt FullInt::FromBytes(std::span<const uint8_t> bytes, bool isUnsigned, bool isBigEndian)
{
    size_t n = bytes.size();
    if (n == 0) return FullInt();
    uint8_t top = isBigEndian ? bytes[0] : bytes[n - 1];
    bool negative = !isUnsigned && (top & 0x80) != 0;
    uint32_t words = (uint32_t)((n + 7) / 8);
    FullInt r;
    uint64_t* limbs = r.Prepare(words);   // limbs[0] is the most significant word
    std::memset(limbs, 0, words * sizeof(uint64_t));
    for (size_t i = 0; i < n; i++)
    {
        uint8_t b = isBigEndian ? bytes[n - 1 - i] : bytes[i];
        limbs[words - 1 - i / 8] |= (uint64_t)b << ((i % 8) * 8);
    }
    if (negative)
    {
        // the magnitude is 2^(8n) - x: sign-extend to whole words, then negate them
        size_t extra = (size_t)words * 8 - n;
        if (extra > 0) limbs[0] |= ~0ull << (64 - extra * 8);
        uint64_t carry = 1;
        for (uint32_t i = words; i-- > 0;)
        {
            uint64_t v = ~limbs[i] + carry;
            carry = (carry == 1 && v == 0) ? 1 : 0;
            limbs[i] = v;
        }
    }
    r.Normalize(negative);
    return r;
}

// the magnitude's significant bytes (at least 1) and its byte i (0: the least significant)
static size_t MagnitudeByteCount(CSpan w)
{
    size_t bytes = 8;
    while (bytes > 1 && (w[0] >> ((bytes - 1) * 8)) == 0) bytes--;
    return (w.size() - 1) * 8 + bytes;
}
static uint8_t MagnitudeByte(CSpan w, size_t i) { return (uint8_t)(w[w.size() - 1 - i / 8] >> ((i % 8) * 8)); }

size_t FullInt::GetByteCount(bool isUnsigned) const
{
    CSpan w = Words();
    size_t k = MagnitudeByteCount(w);
    if (isUnsigned)
    {
        if (neg_) throw std::overflow_error("FullInt: a negative value has no unsigned bytes");
        return k;
    }
    uint8_t tb = MagnitudeByte(w, k - 1);
    if (!neg_) return tb >= 0x80 ? k + 1 : k;
    // -m fits in k bytes while m <= 2^(8k-1)
    if (tb < 0x80) return k;
    if (tb > 0x80) return k + 1;
    for (size_t i = 0; i + 1 < k; i++)
        if (MagnitudeByte(w, i) != 0) return k + 1;
    return k;
}

bool FullInt::TryWriteBytes(std::span<uint8_t> destination, size_t& bytesWritten, bool isUnsigned, bool isBigEndian) const
{
    size_t count = GetByteCount(isUnsigned);
    bytesWritten = 0;
    if (destination.size() < count) return false;
    CSpan w = Words();
    size_t k = MagnitudeByteCount(w);
    uint8_t* d = destination.data();
    for (size_t i = 0; i < count; i++) d[i] = i < k ? MagnitudeByte(w, i) : 0;
    if (neg_)
    {
        unsigned carry = 1;
        for (size_t i = 0; i < count; i++)
        {
            unsigned v = (uint8_t)~d[i] + carry;
            d[i] = (uint8_t)v;
            carry = v >> 8;
        }
    }
    if (isBigEndian) std::reverse(d, d + count);
    bytesWritten = count;
    return true;
}

std::vector<uint8_t> FullInt::ToBytes(bool isUnsigned, bool isBigEndian) const
{
    std::vector<uint8_t> result(GetByteCount(isUnsigned));
    size_t written;
    TryWriteBytes(result, written, isUnsigned, isBigEndian);
    return result;
}

FullInt FullInt::FromWords(std::span<const uint64_t> magnitude, WordOrder order, bool negative)
{
    if (magnitude.empty()) return FullInt();
    FullInt r;
    uint32_t n = (uint32_t)magnitude.size();
    uint64_t* limbs = r.Prepare(n);
    if (order == WordOrder::MostSignificantFirst)
        std::memcpy(limbs, magnitude.data(), magnitude.size_bytes());
    else
        for (uint32_t i = 0; i < n; i++) limbs[i] = magnitude[n - 1 - i];
    r.Normalize(negative);
    return r;
}

bool FullInt::TryWriteWords(std::span<uint64_t> destination, size_t& wordsWritten, WordOrder order) const noexcept
{
    CSpan w = Words();
    wordsWritten = 0;
    if (destination.size() < w.size()) return false;
    if (order == WordOrder::MostSignificantFirst)
        std::memcpy(destination.data(), w.data(), w.size_bytes());
    else
        for (size_t i = 0; i < w.size(); i++) destination[i] = w[w.size() - 1 - i];
    wordsWritten = w.size();
    return true;
}

std::vector<uint64_t> FullInt::ToWords(WordOrder order) const
{
    std::vector<uint64_t> result(size_);
    size_t written;
    TryWriteWords(result, written, order);
    return result;
}

// ---- floating point (C# FullInt.Conversions.cs) ----

// the magnitude's bit length (0 for zero)
static int64_t MagnitudeBitLength(CSpan w)
{
    if (w[0] == 0) return 0;   // normalized: only zero has a zero top word
    return (int64_t)w.size() * 64 - FullHelper::LeadingZeroCount(w[0]);
}

// the magnitude as top · 2^scale with top below 2^63: its top 63 bits, bit 0 set when any bit
// below them is set (a sticky bit: the one rounding, in the int64 -> double / float conversion,
// is then the rounding of the whole value)
static int64_t MagnitudeTop63Sticky(CSpan w, int64_t& scale)
{
    size_t n = w.size();
    int64_t bits = MagnitudeBitLength(w);
    scale = 0;
    if (bits <= 63) return (int64_t)w[n - 1];
    int64_t shift = bits - 63;
    scale = shift;
    size_t wi = (size_t)(shift / 64);
    int off = (int)(shift % 64);
    uint64_t lo = w[n - 1 - wi];
    uint64_t hi = n >= wi + 2 ? w[n - 2 - wi] : 0;
    uint64_t top = off == 0 ? lo : (lo >> off) | (hi << (64 - off));
    bool sticky = off != 0 && (lo << (64 - off)) != 0;
    for (size_t i = n - 1; i > n - 1 - wi && !sticky; i--) sticky = w[i] != 0;
    return (int64_t)(sticky ? top | 1 : top);
}

FullInt::operator double() const
{
    int64_t scale;
    int64_t top = MagnitudeTop63Sticky(Words(), scale);
    double d = std::ldexp((double)top, (int)std::min<int64_t>(scale, 2000));   // rounded in (double)top
    return neg_ ? -d : d;
}

FullInt::operator float() const
{
    int64_t scale;
    int64_t top = MagnitudeTop63Sticky(Words(), scale);
    float f = (float)std::ldexp((double)(float)top, (int)std::min<int64_t>(scale, 300));   // exact in double
    return neg_ ? -f : f;
}

FullInt FullInt::FromDouble(double value)
{
    if (std::isnan(value) || std::isinf(value)) throw std::overflow_error("FullInt: NaN and the infinities have no integer value");
    uint64_t bits;
    std::memcpy(&bits, &value, 8);
    int exp = (int)((bits >> 52) & 0x7FF);
    if (exp < 1023) return FullInt();   // |value| < 1
    uint64_t mantissa = (bits & 0xF'FFFF'FFFF'FFFFull) | 1ull << 52;
    int shift = exp - 1075;   // value = mantissa · 2^shift
    FullInt magnitude = shift <= 0 ? FullInt(mantissa >> -shift) : FullInt(mantissa) << shift;
    return (bits >> 63) ? -magnitude : magnitude;
}

int64_t FullInt::GetBitLength() const
{
    if (!neg_) return MagnitudeBitLength(Words());
    FullInt m1 = -*this - FullInt(1);   // -m needs the bits of m - 1
    return MagnitudeBitLength(m1.Words());
}

std::ostream& operator<<(std::ostream& os, const FullInt& value) { return os << value.ToString(); }

}  // namespace fullnumerics
