// Port of FullInt.Core/FullInt64.cs (the number) and FullInt64Engine.cs (the operations).
//
// The C# version is master: the same operations, the same paths (one word, quick compare,
// two words by one, Karatsuba/Toom-3 above the thresholds, the asm kernels below them).
// What C++ does its own way:
//   - FullInt64 is a value type: copy / move, no GC;
//   - up to InlineWords words live inside the object (no allocation);
//   - the magnitude is always normalized (no leading zero words, zero is never negative);
//     leading zeros of a result are dropped by moving the start offset, not by copying;
//   - the one-word paths are inline here, so the compiler builds them into the caller.
// Not ported: the decimal storage mode (C# IsDec) - this build is binary only.
// Differences in the API, C++ conventions: ^ is XOR (C#: power, see Pow); ToString() is
// decimal (C#: the storage base).
//
// Limbs: 64-bit words, index 0 = most significant word (as in C# and the asm kernels).
#pragma once

#include <fullint/FullHelper.h>
#include <fullint/FullIntCore.h>

#include <compare>
#include <concepts>
#include <cstdint>
#include <cstring>
#include <functional>
#include <iosfwd>
#include <span>
#include <string>
#include <string_view>

namespace fullint {

class FullInt64Engine;

// Thresholds of the decimal conversion by halves (C# DecimalConversion)
struct DecimalConversion
{
    // words; below: the ToDecString kernel. 32 with PreparedFrom 96 (x64, Zen 3): 0.95 of 48 / 48
    // over 64..4096 words (0.89-0.92 at 192-1024); 24 (2026-11-18): 0.94-0.98 of 32 at 32-1024
    // words (16: no gain - the 32 / 16 division ate the smaller kernel's). x64 with the fraction
    // leaves (2026-11-19): 48 - 32-256 words 0.61-0.80 of the division leaves at 24, 1024-4096
    // 0.93 (64: the same, 96: worse from 256)
#if defined(_M_X64) || defined(__x86_64__)
    static inline int ToStringThreshold = 48;
#else
    static inline int ToStringThreshold = 24;
#endif
    static inline int ParseThreshold = 48 * 19;    // digits; below: the FromDecString kernel
    // ToString divides by the powers P_k from this many words of P_k through a reciprocal
    // computed once per power (NewtonDivision::Prepared), not the general division (below:
    // Burnikel-Ziegler / the kernel are faster)
    static inline int PreparedFrom = 96;
    // x64: from this many words a leaf (and a short ToString) by Knuth's fraction method - one
    // product by a cached reciprocal, then one-word multiplications (ToDecStringFrac.asm) instead
    // of a division by 10^19 per word per 19 digits
    static inline int FractionFrom = 8;
    static constexpr int FractionMaxWords = 128;
};

// Word buffers for the heap-stored numbers. Freed buffers are kept per thread by size class
// (powers of two up to CachedWordsMax words) and handed to the next result: "r = a + b" in a
// loop gives r's old buffer to the new value instead of a heap allocation and free per
// operation (~20-25 ns each with the MSVC heap). Larger buffers are exact and not cached.
struct WordBuffers
{
    static constexpr uint32_t CachedWordsMax = 1u << 14;   // 128 KB
    static uint64_t* Allocate(uint32_t words, uint32_t* capacity);
    static void Free(uint64_t* p, uint32_t capacity) noexcept;
};

class FullInt64
{
public:
    static constexpr uint32_t InlineWords = 8;

    FullInt64() noexcept { inl_[0] = 0; }

    template <std::integral T>
        requires(!std::same_as<T, bool>)
    FullInt64(T value) noexcept
    {
        if constexpr (std::is_signed_v<T>)
        {
            neg_ = value < 0;
            inl_[0] = neg_ ? 0 - (uint64_t)(int64_t)value : (uint64_t)value;
        }
        else
        {
            inl_[0] = (uint64_t)value;
        }
    }

    // sign + magnitude words (index 0 = most significant), as the C# (bool, ulong[]) constructor
    FullInt64(bool negative, CSpan magnitude);

    explicit FullInt64(std::string_view text);

    // Magnitude from bytes, little-endian: magnitude[0] is the least significant byte.
    static FullInt64 FromBytes(std::span<const uint8_t> magnitude, bool negative = false);

    static FullInt64 Parse(std::string_view text);

    // base 10 (default), 16 ("0x" + upper-case hex, as C#), 8 or 2
    std::string ToString(int base = 10) const;

    FullInt64(const FullInt64& other) { CopyFrom(other); }
    FullInt64(FullInt64&& other) noexcept { MoveFrom(other); }
    FullInt64& operator=(const FullInt64& other)
    {
        if (this != &other) CopyFrom(other);
        return *this;
    }
    FullInt64& operator=(FullInt64&& other) noexcept
    {
        if (this != &other)
        {
            Release();
            MoveFrom(other);
        }
        return *this;
    }
    ~FullInt64() { Release(); }

    bool Sing() const noexcept { return neg_; }   // the C# name: true = negative
    bool IsNegative() const noexcept { return neg_; }
    bool IsZero() const noexcept { return size_ == 1 && Data()[0] == 0; }
    uint32_t Length() const noexcept { return size_; }
    CSpan Words() const noexcept { return CSpan(Data(), size_); }

private:
    friend class FullInt64Engine;
    friend bool operator==(const FullInt64& a, const FullInt64& b) noexcept;
    friend struct std::hash<FullInt64>;

    bool IsHeap() const noexcept { return cap_ > InlineWords; }
    uint64_t* Base() noexcept { return IsHeap() ? heap_ : inl_; }
    const uint64_t* Data() const noexcept { return (IsHeap() ? heap_ : inl_) + off_; }
    uint64_t Word0() const noexcept { return Data()[0]; }

    // room for n words at offset 0 (the contents are not kept); returns the words
    FULLINT_FORCEINLINE uint64_t* Prepare(uint32_t n)
    {
        if (n > cap_) Grow(n);
        off_ = 0;
        size_ = n;
        return Base();
    }

    FULLINT_NOINLINE void Grow(uint32_t n)
    {
        uint32_t capacity;
        uint64_t* p = WordBuffers::Allocate(n, &capacity);
        if (IsHeap()) WordBuffers::Free(heap_, cap_);
        heap_ = p;
        cap_ = capacity;
    }

    // drops leading zero words (by the offset) and sets the sign; zero is never negative
    FULLINT_FORCEINLINE void Normalize(bool negative) noexcept
    {
        const uint64_t* p = Base() + off_;
        while (size_ > 1 && *p == 0)
        {
            p++;
            off_++;
            size_--;
        }
        neg_ = negative && !(size_ == 1 && *p == 0);
    }

    // a result of up to three words (hi : mid : lo) in the buffer as it is (it holds at least
    // InlineWords words): no Grow, no Release; leading zero words dropped by the offset
    FULLINT_FORCEINLINE void SetShort(uint64_t hi, uint64_t mid, uint64_t lo, bool negative) noexcept
    {
        uint64_t* p = Base();
        p[0] = hi;
        p[1] = mid;
        p[2] = lo;
        uint32_t z = hi != 0 ? 0 : mid != 0 ? 1 : 2;
        off_ = z;
        size_ = 3 - z;
        neg_ = negative && (hi | mid | lo) != 0;
    }

    void SetWords(bool negative, uint64_t hi, uint64_t lo) noexcept   // a one- or two-word result
    {
        Release();   // a fresh result is inline already; this keeps it safe for any object
        off_ = 0;
        size_ = 1;
        if (hi != 0)
        {
            inl_[0] = hi;
            inl_[1] = lo;
            size_ = 2;
        }
        else
        {
            inl_[0] = lo;
        }
        neg_ = negative && (hi | lo) != 0;
    }

    void CopyFrom(const FullInt64& other)
    {
        uint64_t* p = Prepare(other.size_);
        std::memcpy(p, other.Data(), other.size_ * sizeof(uint64_t));
        neg_ = other.neg_;
    }

    // this holds no heap buffer (fresh, or Release()d); other is left as zero
    void MoveFrom(FullInt64& other) noexcept
    {
        if (other.IsHeap())
        {
            heap_ = other.heap_;
            cap_ = other.cap_;
            off_ = other.off_;
        }
        else if (other.size_ <= 2)
        {
            // one or two words (just written by SetWords etc.): word loads, to the start. The 16-byte
            // loads of the whole-buffer copy spanned those 8-byte stores - no store forwarding, ~3 ns
            // on every short r = a op b. Longer: the whole-buffer copy is faster than a word loop.
            volatile uint64_t* d = inl_;
            const uint64_t* s = other.inl_ + other.off_;
            d[0] = s[0];
            if (other.size_ == 2) d[1] = s[1];
            cap_ = InlineWords;
            off_ = 0;
        }
        else
        {
            std::memcpy(inl_, other.inl_, sizeof(inl_));
            cap_ = InlineWords;
            off_ = other.off_;
        }
        size_ = other.size_;
        neg_ = other.neg_;
        other.cap_ = InlineWords;
        other.inl_[0] = 0;
        other.off_ = 0;
        other.size_ = 1;
        other.neg_ = false;
    }

    void Release() noexcept
    {
        if (IsHeap()) WordBuffers::Free(heap_, cap_);
        cap_ = InlineWords;
    }

    union
    {
        uint64_t inl_[InlineWords];
        uint64_t* heap_;
    };
    uint32_t cap_ = InlineWords;   // > InlineWords: heap_ is in use
    uint32_t off_ = 0;             // start of the number in the buffer (leading zeros dropped)
    uint32_t size_ = 1;
    bool neg_ = false;
};

// The operations (C# FullInt64Engine; static, no instance). The thin public entry points
// hold the one-word paths inline; the general paths are out of line (FullInt64Engine.cpp).
class FullInt64Engine
{
public:
    static FullInt64 add(const FullInt64& value1, const FullInt64& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
            return addOneWord(value1.Word0(), value1.neg_, value2.Word0(), value2.neg_);
        return addOrSub(value1, value2, false);
    }

    static FullInt64 sub(const FullInt64& value1, const FullInt64& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
            return addOneWord(value1.Word0(), value1.neg_, value2.Word0(), !value2.neg_);
        return addOrSub(value1, value2, true);
    }

    // one word by one word: a single 64x64 -> 128 multiply, no kernel call
    static FullInt64 mult(const FullInt64& value1, const FullInt64& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
        {
            uint64_t lo, hi = FullHelper::BigMul(value1.Word0(), value2.Word0(), &lo);
            FullInt64 r;
            r.SetWords(value1.neg_ != value2.neg_, hi, lo);
            return r;
        }
        return multGeneral(value1, value2);
    }

    static FullInt64 div(const FullInt64& value1, const FullInt64& value2);
    static FullInt64 mod(const FullInt64& value1, const FullInt64& value2);
    static FullInt64 div(const FullInt64& value1, const FullInt64& value2, FullInt64& remainder);
    static FullInt64 pow(const FullInt64& value1, const FullInt64& value2);
    static FullInt64 abs(const FullInt64& value)
    {
        FullInt64 r(value);
        r.neg_ = false;
        return r;
    }
    static FullInt64 negate(const FullInt64& value)
    {
        FullInt64 r(value);
        r.neg_ = !value.neg_ && !value.IsZero();
        return r;
    }

    static int Compare(const FullInt64& value1, const FullInt64& value2) noexcept
    {
        bool neg1 = value1.neg_, neg2 = value2.neg_;
        if (neg1 != neg2) return neg1 ? -1 : 1;
        int abs = CompareAbsQuick(value1.Words(), value2.Words());
        return neg1 ? -abs : abs;
    }

    // bitwise, BigInteger semantics: negative numbers behave as infinite two's complement
    static FullInt64 And(const FullInt64& value1, const FullInt64& value2) { return bitwise(0, value1, value2); }
    static FullInt64 Or(const FullInt64& value1, const FullInt64& value2) { return bitwise(1, value1, value2); }
    static FullInt64 Xor(const FullInt64& value1, const FullInt64& value2) { return bitwise(2, value1, value2); }
    static FullInt64 Not(const FullInt64& value) { return sub(negate(value), FullInt64(1)); }   // ~x = -x - 1

    static FullInt64 shiftLeft(const FullInt64& value, int shift)
    {
        if (value.size_ == 1 && (unsigned)shift < 64)
        {
            uint64_t m = value.Word0(), lo = m << shift, hi = shift == 0 ? 0 : m >> (64 - shift);
            FullInt64 r;
            r.SetWords(value.neg_, hi, lo);
            return r;
        }
        return shiftLeftGeneral(value, shift);
    }

    // arithmetic shift: rounds toward negative infinity, like BigInteger
    static FullInt64 shiftRight(const FullInt64& value, int shift)
    {
        if (value.size_ == 1 && shift >= 0)
        {
            uint64_t m = value.Word0();   // negative: -m >> k = -(((m - 1) >> k) + 1)
            FullInt64 r;
            if (value.neg_)
                r.SetWords(true, 0, (shift >= 64 ? 0 : (m - 1) >> shift) + 1);
            else
                r.SetWords(false, 0, shift >= 64 ? 0 : m >> shift);
            return r;
        }
        return shiftRightGeneral(value, shift);
    }

    static std::string ToString(const FullInt64& value, int base);
    static FullInt64 ToFullInt64(std::string_view text);

    // GMP style, into an existing result (C#: FullInt64.Add(result, a, b) etc.): result's
    // buffer is reused; result may be value1 or value2 (see FullInt64Engine.cpp)
    static void addInto(FullInt64& result, const FullInt64& value1, const FullInt64& value2, bool negate2);
    static void multInto(FullInt64& result, const FullInt64& value1, const FullInt64& value2);
    static void shiftLeftInto(FullInt64& result, const FullInt64& value, int shift);
    static void shiftRightInto(FullInt64& result, const FullInt64& value, int shift);
    // op: 0 and, 1 or, 2 xor
    static void bitwiseInto(FullInt64& result, int op, const FullInt64& value1, const FullInt64& value2);
    // quotient / remainder straight into these numbers (either may be null; neither may be
    // value1 or value2 - divRem takes care of that)
    static void divRemInto(FullInt64* quotient, FullInt64* remainder, const FullInt64& value1, const FullInt64& value2)
    {
        divRem(value1, value2, quotient, remainder);
    }
private:
    // the into-functions' rare cases that build a new number (kept out of them: the temporary
    // gave each a large frame and an unwind state, several ns on every short call)
    // both operands at most two words: here, into result's buffer (FullInt64Engine.cpp)
#if defined(_M_X64) || defined(__x86_64__)
    FULLINT_NOINLINE static void addIntoShort(FullInt64& result, const FullInt64& value1, const FullInt64& value2, bool negate2) noexcept;
    FULLINT_NOINLINE static void addIntoAddN(FullInt64& result, const FullInt64& value1, const FullInt64& value2) noexcept;
    FULLINT_NOINLINE static void addIntoSubN(FullInt64& result, const FullInt64& value1, const FullInt64& value2, bool negate2) noexcept;
#endif
    // addInto's cases other than its short common one (see FullInt64Engine.cpp)
    FULLINT_NOINLINE static void addIntoGeneral(FullInt64& result, const FullInt64& value1, const FullInt64& value2, bool negate2);
    FULLINT_NOINLINE static void addIntoFresh(FullInt64& result, CSpan a, CSpan b, bool neg1, bool neg2);
    FULLINT_NOINLINE static void addIntoAliased(FullInt64& result, CSpan a, CSpan b, bool neg1, bool neg2);
    FULLINT_NOINLINE static void multIntoAliased(FullInt64& result, const FullInt64& value1, const FullInt64& value2);
    FULLINT_NOINLINE static void shiftIntoAliased(FullInt64& result, const FullInt64& value, int shift, bool left);
    // the shifts' cases other than their short common one (FullInt64Engine.cpp)
    FULLINT_NOINLINE static void shiftLeftIntoGeneral(FullInt64& result, const FullInt64& value, int shift);
    FULLINT_NOINLINE static void shiftRightIntoGeneral(FullInt64& result, const FullInt64& value, int shift);
    static void ShiftRightIntoBody(FullInt64& result, const FullInt64& value, int shift);
    FULLINT_NOINLINE static void bitwiseIntoAliased(FullInt64& result, int op, const FullInt64& value1, const FullInt64& value2);
    FULLINT_NOINLINE static void divRemAliased(const FullInt64& value1, const FullInt64& value2, FullInt64* quotient, FullInt64* remainder);
public:

    // equal lengths up to this many words: a plain loop instead of the kernel call (x86 2-16 words:
    // 0.66-0.78 of the call; x64 from 8 words the AVX2 kernel is ahead, 2 words still 0.6 for % )
#if defined(_M_X64) || defined(__x86_64__)
    static constexpr size_t ShortLoopCompareWords = 2;
#else
    static constexpr size_t ShortLoopCompareWords = 16;
#endif

    // |a| vs |b| on normalized magnitudes: the length or the top word almost always decides
    static int CompareAbsQuick(CSpan value1, CSpan value2) noexcept
    {
        size_t n = value1.size();
        if (n != value2.size()) return n > value2.size() ? 1 : -1;
        if (value1[0] != value2[0]) return value1[0] > value2[0] ? 1 : -1;
        if (n == 1) return 0;
        if constexpr (ShortLoopCompareWords == 2)   // x64: the second word only (a loop here grew the inline body)
        {
            if (n == 2) return value1[1] == value2[1] ? 0 : value1[1] > value2[1] ? 1 : -1;
        }
        else if (n <= ShortLoopCompareWords)   // the first differing word decides: a plain loop (C# the same)
        {
            for (size_t i = 1; i < n; i++)
                if (value1[i] != value2[i]) return value1[i] > value2[i] ? 1 : -1;
            return 0;
        }
        return FullIntCore::Compare(value1, value2);
    }

private:
    // one word and one word: plain 64-bit arithmetic, no kernel call
    static FullInt64 addOneWord(uint64_t a, bool aNegative, uint64_t b, bool bNegative) noexcept
    {
        FullInt64 r;
        if (aNegative == bNegative)
        {
            uint64_t sum = a + b;
            r.SetWords(aNegative, sum < a ? 1 : 0, sum);
        }
        else if (a >= b)   // opposite signs: the larger magnitude minus the smaller, with its sign
        {
            r.SetWords(aNegative, 0, a - b);
        }
        else
        {
            r.SetWords(bNegative, 0, b - a);
        }
        return r;
    }

    static FullInt64 addOrSub(const FullInt64& value1, const FullInt64& value2, bool negate2);
    static FullInt64 addMagnitudes(CSpan a, CSpan b, bool negative);
    static FullInt64 subMagnitudes(CSpan a, CSpan b, bool neg1, bool neg2);
    static FullInt64 multGeneral(const FullInt64& value1, const FullInt64& value2);
    static void divRem(const FullInt64& value1, const FullInt64& value2, FullInt64* quotient, FullInt64* remainder);
    static FullInt64 bitwise(int op, const FullInt64& value1, const FullInt64& value2);
    static FullInt64 shiftLeftGeneral(const FullInt64& value, int shift);
    static FullInt64 shiftRightGeneral(const FullInt64& value, int shift);
    static std::string ToDecString(const FullInt64& value);

    // decimal text by halves (FullInt64Engine.Decimal.cpp)
    static const FullInt64& DecimalPower(int k);
    static std::string ToDecStringRecursive(const FullInt64& value);
    static size_t WriteDecimal(const FullInt64& x, char* text, size_t end, ptrdiff_t width);
    static size_t WriteDecimalLeaf(CSpan w, char* text, size_t end);
    static size_t WriteDecimalFraction(CSpan w, char* text, size_t end);
    static FullInt64 ParseDecimal(std::string_view digits);
    static FullInt64 ParseDecimalLeaf(std::string_view digits);

    friend class FullInt64;
};

inline FullInt64 operator+(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::add(a, b); }
inline FullInt64 operator-(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::sub(a, b); }
inline FullInt64 operator*(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::mult(a, b); }
inline FullInt64 operator/(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::div(a, b); }
inline FullInt64 operator%(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::mod(a, b); }
inline FullInt64 operator-(const FullInt64& a) { return FullInt64Engine::negate(a); }
inline FullInt64 operator+(const FullInt64& a) { return a; }
inline FullInt64 operator&(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::And(a, b); }
inline FullInt64 operator|(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::Or(a, b); }
inline FullInt64 operator^(const FullInt64& a, const FullInt64& b) { return FullInt64Engine::Xor(a, b); }
inline FullInt64 operator~(const FullInt64& a) { return FullInt64Engine::Not(a); }
inline FullInt64 operator<<(const FullInt64& a, int shift) { return FullInt64Engine::shiftLeft(a, shift); }
inline FullInt64 operator>>(const FullInt64& a, int shift) { return FullInt64Engine::shiftRight(a, shift); }

// GMP style: result = a op b into result's buffer (no new number per call in a loop);
// result may be a or b
#if defined(_M_X64) || defined(__x86_64__)
inline void Add(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::addInto(result, a, b, false); }
inline void Subtract(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::addInto(result, a, b, true); }
#else
// x86: result neither operand with room - one kernel on the objects (IntoObj.asm), the rest as on x64
inline void Add(FullInt64& result, const FullInt64& a, const FullInt64& b)
{
    if (Fullint64AddObjWIN(&result, &a, &b, 0) != 0) FullInt64Engine::addInto(result, a, b, false);
}
inline void Subtract(FullInt64& result, const FullInt64& a, const FullInt64& b)
{
    if (Fullint64AddObjWIN(&result, &a, &b, 1) != 0) FullInt64Engine::addInto(result, a, b, true);
}
#endif
inline void Multiply(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::multInto(result, a, b); }
#if defined(_M_X64) || defined(__x86_64__)
inline void ShiftLeft(FullInt64& result, const FullInt64& a, int shift) { FullInt64Engine::shiftLeftInto(result, a, shift); }
#else
// x86: by bits (not whole words) into a result with room - one kernel on the objects (IntoObj.asm)
inline void ShiftLeft(FullInt64& result, const FullInt64& a, int shift)
{
    if (Fullint64ShlObjWIN(&result, &a, shift) != 0) FullInt64Engine::shiftLeftInto(result, a, shift);
}
#endif
inline void ShiftRight(FullInt64& result, const FullInt64& a, int shift) { FullInt64Engine::shiftRightInto(result, a, shift); }
#if defined(_M_X64) || defined(__x86_64__)
inline void And(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(result, 0, a, b); }
#else
// x86: the short non-negative case on the objects in one kernel (IntoObj.asm), the rest as on x64
inline void And(FullInt64& result, const FullInt64& a, const FullInt64& b)
{
    if (Fullint64AndObjWIN(&result, &a, &b) != 0) FullInt64Engine::bitwiseInto(result, 0, a, b);
}
#endif
inline void Or(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(result, 1, a, b); }
inline void Xor(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(result, 2, a, b); }
// truncating, as / and %
inline void Divide(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::divRemInto(&result, nullptr, a, b); }
inline void Remainder(FullInt64& result, const FullInt64& a, const FullInt64& b) { FullInt64Engine::divRemInto(nullptr, &result, a, b); }

// compound assignment: in place where the operation allows it (+, - in a's own buffer)
inline FullInt64& operator+=(FullInt64& a, const FullInt64& b) { FullInt64Engine::addInto(a, a, b, false); return a; }
inline FullInt64& operator-=(FullInt64& a, const FullInt64& b) { FullInt64Engine::addInto(a, a, b, true); return a; }
inline FullInt64& operator*=(FullInt64& a, const FullInt64& b) { FullInt64Engine::multInto(a, a, b); return a; }
inline FullInt64& operator/=(FullInt64& a, const FullInt64& b) { return a = a / b; }
inline FullInt64& operator%=(FullInt64& a, const FullInt64& b) { return a = a % b; }
inline FullInt64& operator&=(FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(a, 0, a, b); return a; }
inline FullInt64& operator|=(FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(a, 1, a, b); return a; }
inline FullInt64& operator^=(FullInt64& a, const FullInt64& b) { FullInt64Engine::bitwiseInto(a, 2, a, b); return a; }
inline FullInt64& operator<<=(FullInt64& a, int shift) { return a = a << shift; }
inline FullInt64& operator>>=(FullInt64& a, int shift) { return a = a >> shift; }
inline FullInt64& operator++(FullInt64& a) { return a += FullInt64(1); }
inline FullInt64& operator--(FullInt64& a) { return a -= FullInt64(1); }
inline FullInt64 operator++(FullInt64& a, int) { FullInt64 old(a); ++a; return old; }
inline FullInt64 operator--(FullInt64& a, int) { FullInt64 old(a); --a; return old; }

// normalized storage: equal numbers have equal words
inline bool operator==(const FullInt64& a, const FullInt64& b) noexcept
{
    return a.neg_ == b.neg_ && a.size_ == b.size_ && std::memcmp(a.Data(), b.Data(), a.size_ * sizeof(uint64_t)) == 0;
}
inline std::strong_ordering operator<=>(const FullInt64& a, const FullInt64& b) noexcept
{
    return FullInt64Engine::Compare(a, b) <=> 0;
}

inline FullInt64 Pow(const FullInt64& value, const FullInt64& exponent) { return FullInt64Engine::pow(value, exponent); }
inline FullInt64 Abs(const FullInt64& value) { return FullInt64Engine::abs(value); }

std::ostream& operator<<(std::ostream& os, const FullInt64& value);

}  // namespace fullint

template <>
struct std::hash<fullint::FullInt64>
{
    size_t operator()(const fullint::FullInt64& v) const noexcept
    {
        size_t h = v.neg_ ? 0x9E3779B97F4A7C15ull : 0;
        const uint64_t* p = v.Data();
        for (uint32_t i = 0; i < v.size_; i++) h = (h ^ (size_t)p[i]) * 0x100000001B3ull + (h >> 7);
        return h;
    }
};
