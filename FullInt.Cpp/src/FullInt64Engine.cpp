// Port of FullInt.Core/FullInt64Engine.cs: the general (out-of-line) paths.
#include <fullint/FullInt64.h>
#include <fullint/BurnikelZiegler.h>
#include <fullint/NewtonDivision.h>
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <charconv>
#include <cstring>
#include <memory>
#include <ostream>
#include <stdexcept>

namespace fullint {

// a few words zeroed / copied in place: MSVC turns the plain loops into memset / memcpy calls, whose
// entry outweighs a few words; long runs still use them
static FULLINT_FORCEINLINE void ZeroWords(uint64_t* p, size_t n) noexcept
{
    if (n > 16)
    {
        std::memset(p, 0, n * sizeof(uint64_t));
        return;
    }
    volatile uint64_t* v = p;
    for (size_t i = 0; i < n; i++) v[i] = 0;
}

static FULLINT_FORCEINLINE void CopyWords(uint64_t* dst, const uint64_t* src, size_t n) noexcept
{
    if (n > 16)
    {
        std::memcpy(dst, src, n * sizeof(uint64_t));
        return;
    }
    volatile uint64_t* v = dst;
    for (size_t i = 0; i < n; i++) v[i] = src[i];
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

// a + b, or a - b with negate2 (no FullInt64 is made for -b). The signs are read once.
FullInt64 FullInt64Engine::addOrSub(const FullInt64& value1, const FullInt64& value2, bool negate2)
{
    bool neg1 = value1.neg_, neg2 = value2.neg_ != negate2;
    // two separate paths, as in C# (there for Dynamic PGO; here they keep each path's code small)
    return neg1 == neg2 ? addMagnitudes(value1.Words(), value2.Words(), neg1)
                        : subMagnitudes(value1.Words(), value2.Words(), neg1, neg2);
}

// C# allocates the exact size and widens on a carry out; here the carry word is reserved in
// front (one more word costs nothing) and dropped by the offset when it stays zero.
FullInt64 FullInt64Engine::addMagnitudes(CSpan a, CSpan b, bool negative)
{
    uint32_t n = (uint32_t)std::max(a.size(), b.size());
    FullInt64 r;
    uint64_t* p = r.Prepare(n + 1);
    p[0] = FullIntCore::Add(a, b, Span(p + 1, n));
    r.Normalize(negative);
    return r;
}

FullInt64 FullInt64Engine::subMagnitudes(CSpan a, CSpan b, bool neg1, bool neg2)
{
    uint32_t n = (uint32_t)std::max(a.size(), b.size());   // |a - b| always fits
    FullInt64 r;
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
void FullInt64Engine::addInto(FullInt64& result, const FullInt64& value1, const FullInt64& value2, bool negate2)
{
    bool neg1 = value1.neg_, neg2 = value2.neg_ != negate2;
    CSpan a = value1.Words(), b = value2.Words();
    bool same = neg1 == neg2;

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
void FullInt64Engine::addIntoAliased(FullInt64& result, CSpan a, CSpan b, bool neg1, bool neg2)
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

void FullInt64Engine::addIntoFresh(FullInt64& result, CSpan a, CSpan b, bool neg1, bool neg2)
{
    result = neg1 == neg2 ? addMagnitudes(a, b, neg1) : subMagnitudes(a, b, neg1, neg2);
}

// 2 x 1 and 2 x 2 words inline (FullHelper::Mul2x1 / Mul2x2 / Sqr2), no kernel call; false: longer.
// x64 only: x86 has no 64 x 64 multiply (the 32-bit split: 2 x 2 43 ns, the kernel 15)
static FULLINT_FORCEINLINE bool MultShort(CSpan a, CSpan b, uint64_t* r) noexcept
{
#if !(defined(_M_X64) || defined(__x86_64__))
    (void)a; (void)b; (void)r;
    return false;
#else
    size_t la = a.size(), lb = b.size();
    if (la + lb == 3)
    {
        if (la == 2) FullHelper::Mul2x1(a.data(), b[0], r);
        else FullHelper::Mul2x1(b.data(), a[0], r);
        return true;
    }
    if (la == 2 && lb == 2)
    {
        if (Karatsuba::Same(a, b)) FullHelper::Sqr2(a.data(), r);
        else FullHelper::Mul2x2(a.data(), b.data(), r);
        return true;
    }
    return false;
#endif
}

void FullInt64Engine::multIntoAliased(FullInt64& result, const FullInt64& value1, const FullInt64& value2)
{
    result = mult(value1, value2);
}

void FullInt64Engine::shiftIntoAliased(FullInt64& result, const FullInt64& value, int shift, bool left)
{
    result = left ? shiftLeft(value, shift) : shiftRight(value, shift);
}

void FullInt64Engine::multInto(FullInt64& result, const FullInt64& value1, const FullInt64& value2)
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
#if defined(_M_X64) || defined(__x86_64__)
static constexpr size_t SHIFT_INLINE = 8;
#else
static constexpr size_t SHIFT_INLINE = 0;
#endif

void FullInt64Engine::shiftLeftInto(FullInt64& result, const FullInt64& value, int shift)
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

void FullInt64Engine::shiftRightInto(FullInt64& result, const FullInt64& value, int shift)
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

#pragma endregion

#pragma region mult

FullInt64 FullInt64Engine::multGeneral(const FullInt64& value1, const FullInt64& value2)
{
    CSpan a = value1.Words(), b = value2.Words();
    uint32_t length = (uint32_t)(a.size() + b.size());
    FullInt64 r;
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

FullInt64 FullInt64Engine::pow(const FullInt64& value1, const FullInt64& value2)
{
    // integer power is undefined for negative exponents (same as BigInteger.Pow)
    if (value2.neg_) throw std::out_of_range("The exponent must be non-negative.");

    FullInt64 result(1);
    FullInt64 value(value1);
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
FullInt64 FullInt64Engine::div(const FullInt64& value1, const FullInt64& value2)
{
    FullInt64 q;
    divRem(value1, value2, &q, nullptr);
    return q;
}

// a % b: the quotient is not materialized
FullInt64 FullInt64Engine::mod(const FullInt64& value1, const FullInt64& value2)
{
    FullInt64 r;
    divRem(value1, value2, nullptr, &r);
    return r;
}

FullInt64 FullInt64Engine::div(const FullInt64& value1, const FullInt64& value2, FullInt64& remainder)
{
    FullInt64 q, r;   // locals: remainder may be value1 or value2
    divRem(value1, value2, &q, &r);
    remainder = std::move(r);
    return q;
}

// The division itself. Truncating (the quotient rounds toward zero, the remainder has the
// dividend's sign). Only the requested results are built; a dropped quotient goes to scratch.
// The results are built in their own buffers; one that is an operand goes through
// divRemAliased (locals moved in at the end - out of line: their unwind frame cost every short
// division several ns).
void FullInt64Engine::divRem(const FullInt64& value1, const FullInt64& value2, FullInt64* quotient, FullInt64* remainder)
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
    // In a 32-bit build 64-bit division is a helper call: there "%" goes to the kernel (as C#).
    if (divisor.size() == 1 && dividend.size() == 2 && (quotient || sizeof(void*) == 8))
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

#if defined(_M_X64) || defined(__x86_64__)
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

void FullInt64Engine::divRemAliased(const FullInt64& value1, const FullInt64& value2, FullInt64* quotient, FullInt64* remainder)
{
    FullInt64 q, r;
    divRem(value1, value2, quotient ? &q : nullptr, remainder ? &r : nullptr);
    if (remainder) *remainder = std::move(r);
    if (quotient) *quotient = std::move(q);
}

#pragma endregion

#pragma region bitwise

FullInt64 FullInt64Engine::bitwise(int op, const FullInt64& value1, const FullInt64& value2)
{
    FullInt64 r;
    bitwiseInto(r, op, value1, value2);
    return r;
}

// result = value1 op value2 in result's buffer; result aliased with an operand: a new number
void FullInt64Engine::bitwiseInto(FullInt64& result, int op, const FullInt64& value1, const FullInt64& value2)
{
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
            CopyWords(p, a.data(), d);
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

void FullInt64Engine::bitwiseIntoAliased(FullInt64& result, int op, const FullInt64& value1, const FullInt64& value2)
{
    FullInt64 r;
    bitwiseInto(r, op, value1, value2);
    result = std::move(r);
}

FullInt64 FullInt64Engine::shiftLeftGeneral(const FullInt64& value, int shift)
{
    if (shift < 0) return shiftRight(value, shift == INT32_MIN ? INT32_MAX : -shift);

    CSpan a = value.Words();
    uint32_t n = (uint32_t)a.size() + (uint32_t)shift / 64 + 1;
    FullInt64 r;
    FullIntCore::ShiftLeft(a, shift, Span(r.Prepare(n), n));
    r.Normalize(value.neg_);
    return r;
}

// arithmetic shift: rounds toward negative infinity, like BigInteger
FullInt64 FullInt64Engine::shiftRightGeneral(const FullInt64& value, int shift)
{
    if (shift < 0) return shiftLeft(value, shift == INT32_MIN ? INT32_MAX : -shift);

    CSpan a = value.Words();
    if (!value.neg_)   // zero is never negative
    {
        int64_t left = (int64_t)a.size() - shift / 64;
        uint32_t n = (uint32_t)std::max<int64_t>(left, 1);
        FullInt64 r;
        FullIntCore::ShiftRight(a, shift, Span(r.Prepare(n), n));
        r.Normalize(false);
        return r;
    }

    // -|a| >> k = -(((|a| - 1) >> k) + 1)
    FullInt64 shifted = shiftRight(sub(abs(value), FullInt64(1)), shift);
    return negate(add(shifted, FullInt64(1)));
}

#pragma endregion

#pragma region conversions

std::string FullInt64Engine::ToDecString(const FullInt64& value)
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
    if (w.size() > (size_t)DecimalConversion::ToStringThreshold)   // long: by halves (FullInt64Engine.Decimal.cpp)
    {
        return ToDecStringRecursive(value);
    }

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

std::string FullInt64Engine::ToString(const FullInt64& value, int base)
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
FullInt64 FullInt64Engine::ToFullInt64(std::string_view value)
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
    if (base == 10)   // one compare per character, no early exit: the compiler vectorizes it
    {
        for (char c : value) bad |= (unsigned char)(c - '0') > 9;
    }
    else
    {
        for (char c : value) bad |= digitValue(c) >= base;
    }
    if (bad) throw std::invalid_argument("not a digit of the base");

    FullInt64 result;
    if (base == 10)
    {
        result = value.size() > (size_t)DecimalConversion::ParseThreshold
            ? ParseDecimal(value)          // long text: by halves (FullInt64Engine.Decimal.cpp)
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

// ---- FullInt64 members that need the engine ----

FullInt64::FullInt64(bool negative, CSpan magnitude)
{
    if (magnitude.empty())
    {
        inl_[0] = 0;
        return;
    }
    std::memcpy(Prepare((uint32_t)magnitude.size()), magnitude.data(), magnitude.size_bytes());
    Normalize(negative);
}

FullInt64::FullInt64(std::string_view text) : FullInt64(FullInt64Engine::ToFullInt64(text)) {}

FullInt64 FullInt64::Parse(std::string_view text) { return FullInt64Engine::ToFullInt64(text); }

std::string FullInt64::ToString(int base) const { return FullInt64Engine::ToString(*this, base); }

FullInt64 FullInt64::FromBytes(std::span<const uint8_t> magnitude, bool negative)
{
    uint32_t words = (uint32_t)std::max<size_t>((magnitude.size() + 7) / 8, 1);
    FullInt64 r;
    uint64_t* limbs = r.Prepare(words);   // limbs[0] is the most significant word
    std::memset(limbs, 0, words * sizeof(uint64_t));
    for (size_t i = 0; i < magnitude.size(); i++)
        limbs[words - 1 - i / 8] |= (uint64_t)magnitude[i] << ((i % 8) * 8);
    r.Normalize(negative);
    return r;
}

std::ostream& operator<<(std::ostream& os, const FullInt64& value) { return os << value.ToString(); }

}  // namespace fullint
