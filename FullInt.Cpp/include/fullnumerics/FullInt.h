// Port of FullInt.Core/FullInt.cs (the number) and FullIntEngine.cs (the operations).
//
// The C# version is master: the same operations, the same paths (one word, quick compare,
// two words by one, Karatsuba/Toom-3 above the thresholds, the asm kernels below them).
// What C++ does its own way:
//   - FullInt is a value type: copy / move, no GC;
//   - up to InlineWords words live inside the object (no allocation);
//   - the magnitude is always normalized (no leading zero words, zero is never negative);
//     leading zeros of a result are dropped by moving the start offset, not by copying;
//   - the one-word paths are inline here, so the compiler builds them into the caller.
// The API as C#: ^ is XOR, the power is Pow; ToString() is decimal; bytes and words in and out
// with an explicit order (FromBytes / ToBytes as BigInteger, FromWords / ToWords as GMP
// mpz_import / mpz_export).
//
// Limbs: 64-bit words, index 0 = most significant word (as in C# and the asm kernels).
#pragma once

#include <fullnumerics/FullHelper.h>
#include <fullnumerics/FullIntCore.h>

#include <compare>
#include <concepts>
#include <limits>
#include <cstdint>
#include <cstring>
#include <functional>
#include <iosfwd>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace fullnumerics {

class FullIntEngine;

// The order of the words in FromWords / ToWords / TryWriteWords (the bytes inside a word are the
// machine's own)
enum class WordOrder
{
    LeastSignificantFirst,   // words[0] is the least significant word (GMP order -1)
    MostSignificantFirst,    // words[0] is the most significant word (GMP order 1)
};

// Thresholds of the decimal conversion by halves (C# DecimalConversion)
struct DecimalConversion
{
    // words; below: the ToDecString kernel. 32 with PreparedFrom 96 (x64, Zen 3): 0.95 of 48 / 48
    // over 64..4096 words (0.89-0.92 at 192-1024); 24 (2026-11-18): 0.94-0.98 of 32 at 32-1024
    // words (16: no gain - the 32 / 16 division ate the smaller kernel's). x64 with the fraction
    // leaves (2026-11-19): 48 - 32-256 words 0.61-0.80 of the division leaves at 24, 1024-4096
    // 0.93 (64: the same, 96: worse from 256)
    // AArch64 narrow-multiplier cores (Cpu::ArmNarrow; Neoverse-N1, --set tostring=): 24 -
    // 0.92-0.96 of 48 at 32-1024 words.
#if FULLINT_ARM64
    static inline int ToStringThreshold = Cpu::ArmNarrow() ? 24 : 48;
#elif FULLINT_WORD64
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
    // (AArch64 narrow cores: 16 - at 8 words the division kernel is 0.88 of the fraction method
    // there, its products by the cached reciprocal dearer on the slow multiplier)
#if FULLINT_ARM64
    static inline int FractionFrom = Cpu::ArmNarrow() ? 16 : 8;
#else
    static inline int FractionFrom = 8;
#endif
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

class FullInt
{
public:
    static constexpr uint32_t InlineWords = 8;

    FullInt() noexcept { inl_[0] = 0; }

    template <std::integral T>
        requires(!std::same_as<T, bool>)
    FullInt(T value) noexcept
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
    FullInt(bool negative, CSpan magnitude);

    explicit FullInt(std::string_view text);   // Parse

    // Bytes as C# FullInt(ReadOnlySpan<byte>, isUnsigned, isBigEndian) and BigInteger: two's
    // complement unless isUnsigned, least significant byte first unless isBigEndian; no bytes: 0.
    static FullInt FromBytes(std::span<const uint8_t> bytes, bool isUnsigned = false, bool isBigEndian = false);
    // the fewest bytes; isUnsigned on a negative value throws std::overflow_error
    size_t GetByteCount(bool isUnsigned = false) const;
    std::vector<uint8_t> ToBytes(bool isUnsigned = false, bool isBigEndian = false) const;
    // false (nothing written) when destination is too short
    bool TryWriteBytes(std::span<uint8_t> destination, size_t& bytesWritten, bool isUnsigned = false, bool isBigEndian = false) const;

    // The magnitude's words in the given order and a sign (GMP mpz_import); no words: 0
    static FullInt FromWords(std::span<const uint64_t> magnitude, WordOrder order, bool negative = false);
    size_t GetWordCount() const noexcept { return size_; }   // zero is one zero word
    std::vector<uint64_t> ToWords(WordOrder order) const;
    bool TryWriteWords(std::span<uint64_t> destination, size_t& wordsWritten, WordOrder order) const noexcept;

    // Decimal digits with an optional sign ('-' or '+'), white space around allowed; "0x..." is
    // hexadecimal; leading zeros are decimal ("017" is 17). std::invalid_argument for anything else.
    static FullInt Parse(std::string_view text);
    static bool TryParse(std::string_view text, FullInt& result);

    // base 10 (default), 16 ("0x" + upper-case hex), 8 or 2
    std::string ToString(int base = 10) const;
    // as C#: "D" decimal, "X" / "x" hexadecimal, "B" binary; a number after the letter is the
    // least number of digits; a negative value is '-' and the magnitude's digits
    std::string ToString(std::string_view format) const;

    FullInt(const FullInt& other) { CopyFrom(other); }
    FullInt(FullInt&& other) noexcept { MoveFrom(other); }
    FullInt& operator=(const FullInt& other)
    {
        if (this != &other) CopyFrom(other);
        return *this;
    }
    FullInt& operator=(FullInt&& other) noexcept
    {
        if (this != &other)
        {
            Release();
            MoveFrom(other);
        }
        return *this;
    }
    ~FullInt() { Release(); }

    bool IsNegative() const noexcept { return neg_; }
    bool IsZero() const noexcept { return size_ == 1 && Data()[0] == 0; }
    bool IsOne() const noexcept { return !neg_ && size_ == 1 && Data()[0] == 1; }
    bool IsEven() const noexcept { return (Data()[size_ - 1] & 1) == 0; }
    int Sign() const noexcept { return IsZero() ? 0 : neg_ ? -1 : 1; }

    // as C#: to double / float rounded to nearest (ties to even), beyond the range an infinity;
    // from double truncated toward zero, std::overflow_error for NaN and the infinities
    explicit operator double() const;
    explicit operator float() const;
    static FullInt FromDouble(double value);
    // the bits of the shortest two's complement form without the sign bit (0 for 0 and -1)
    int64_t GetBitLength() const;

    // to an integer type, std::overflow_error when out of its range (C#: explicit operators)
    template <std::integral T>
        requires(!std::same_as<T, bool>)
    explicit operator T() const
    {
        uint64_t m = Data()[size_ - 1];
        if (size_ > 1) throw std::overflow_error("FullInt: out of range");
        if (neg_)
        {
            if constexpr (std::is_signed_v<T>)
            {
                if (m <= (uint64_t)std::numeric_limits<T>::max() + 1) return (T)(0 - m);
            }
            throw std::overflow_error("FullInt: out of range");
        }
        if (m > (uint64_t)std::numeric_limits<T>::max()) throw std::overflow_error("FullInt: out of range");
        return (T)m;
    }
    uint32_t Length() const noexcept { return size_; }
    CSpan Words() const noexcept { return CSpan(Data(), size_); }

private:
    friend class FullIntEngine;
    friend bool operator==(const FullInt& a, const FullInt& b) noexcept;
    friend struct std::hash<FullInt>;

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

    void CopyFrom(const FullInt& other)
    {
        uint64_t* p = Prepare(other.size_);
        std::memcpy(p, other.Data(), other.size_ * sizeof(uint64_t));
        neg_ = other.neg_;
    }

    // this holds no heap buffer (fresh, or Release()d); other is left as zero
    void MoveFrom(FullInt& other) noexcept
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

// The operations (C# FullIntEngine; static, no instance). The thin public entry points
// hold the one-word paths inline; the general paths are out of line (FullIntEngine.cpp).
class FullIntEngine
{
public:
    static FullInt add(const FullInt& value1, const FullInt& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
            return addOneWord(value1.Word0(), value1.neg_, value2.Word0(), value2.neg_);
        return addOrSub(value1, value2, false);
    }

    static FullInt sub(const FullInt& value1, const FullInt& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
            return addOneWord(value1.Word0(), value1.neg_, value2.Word0(), !value2.neg_);
        return addOrSub(value1, value2, true);
    }

    // one word by one word: a single 64x64 -> 128 multiply, no kernel call
    static FullInt mult(const FullInt& value1, const FullInt& value2)
    {
        if (value1.size_ == 1 && value2.size_ == 1)
        {
            uint64_t lo, hi = FullHelper::BigMul(value1.Word0(), value2.Word0(), &lo);
            FullInt r;
            r.SetWords(value1.neg_ != value2.neg_, hi, lo);
            return r;
        }
        return multGeneral(value1, value2);
    }

    static FullInt div(const FullInt& value1, const FullInt& value2);
    static FullInt mod(const FullInt& value1, const FullInt& value2);
    static FullInt div(const FullInt& value1, const FullInt& value2, FullInt& remainder);
    static FullInt pow(const FullInt& value1, const FullInt& value2);
    static FullInt abs(const FullInt& value)
    {
        FullInt r(value);
        r.neg_ = false;
        return r;
    }
    static FullInt negate(const FullInt& value)
    {
        FullInt r(value);
        r.neg_ = !value.neg_ && !value.IsZero();
        return r;
    }

    static int Compare(const FullInt& value1, const FullInt& value2) noexcept
    {
#if FULLINT_X64
        // one kernel on the objects (CompareN.asm): the signs, lengths, words - nothing around the
        // call here (the inline checks before a kernel call cost 4 words 1.3x GMP)
        return Fullint64CmpObjWIN(&value1, &value2);
#endif
        bool neg1 = value1.neg_, neg2 = value2.neg_;
        if (neg1 != neg2) return neg1 ? -1 : 1;
        int abs = CompareAbsQuick(value1.Words(), value2.Words());
        return neg1 ? -abs : abs;
    }

    // bitwise, BigInteger semantics: negative numbers behave as infinite two's complement
    static FullInt And(const FullInt& value1, const FullInt& value2) { return bitwise(0, value1, value2); }
    static FullInt Or(const FullInt& value1, const FullInt& value2) { return bitwise(1, value1, value2); }
    static FullInt Xor(const FullInt& value1, const FullInt& value2) { return bitwise(2, value1, value2); }
    static FullInt Not(const FullInt& value) { return sub(negate(value), FullInt(1)); }   // ~x = -x - 1

    static FullInt shiftLeft(const FullInt& value, int shift)
    {
        if (value.size_ == 1 && (unsigned)shift < 64)
        {
            uint64_t m = value.Word0(), lo = m << shift, hi = shift == 0 ? 0 : m >> (64 - shift);
            FullInt r;
            r.SetWords(value.neg_, hi, lo);
            return r;
        }
        return shiftLeftGeneral(value, shift);
    }

    // arithmetic shift: rounds toward negative infinity, like BigInteger
    static FullInt shiftRight(const FullInt& value, int shift)
    {
        if (value.size_ == 1 && shift >= 0)
        {
            uint64_t m = value.Word0();   // negative: -m >> k = -(((m - 1) >> k) + 1)
            FullInt r;
            if (value.neg_)
                r.SetWords(true, 0, (shift >= 64 ? 0 : (m - 1) >> shift) + 1);
            else
                r.SetWords(false, 0, shift >= 64 ? 0 : m >> shift);
            return r;
        }
        return shiftRightGeneral(value, shift);
    }

    static std::string ToString(const FullInt& value, int base);
    static FullInt ToFullInt(std::string_view text);

    // GMP style, into an existing result (C#: FullInt.Add(result, a, b) etc.): result's
    // buffer is reused; result may be value1 or value2 (see FullIntEngine.cpp)
    static void addInto(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2);
    static void multInto(FullInt& result, const FullInt& value1, const FullInt& value2);
    static void shiftLeftInto(FullInt& result, const FullInt& value, int shift);
    static void shiftRightInto(FullInt& result, const FullInt& value, int shift);
    // op: 0 and, 1 or, 2 xor
    static void bitwiseInto(FullInt& result, int op, const FullInt& value1, const FullInt& value2);
    // quotient / remainder straight into these numbers (either may be null; neither may be
    // value1 or value2 - divRem takes care of that)
    static void divRemInto(FullInt* quotient, FullInt* remainder, const FullInt& value1, const FullInt& value2)
    {
        divRem(value1, value2, quotient, remainder);
    }
private:
    // the into-functions' rare cases that build a new number (kept out of them: the temporary
    // gave each a large frame and an unwind state, several ns on every short call)
    // both operands at most two words: here, into result's buffer (FullIntEngine.cpp)
#if FULLINT_WORD64
    FULLINT_NOINLINE static void addIntoShort(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2) noexcept;
    FULLINT_NOINLINE static void addIntoAddN(FullInt& result, const FullInt& value1, const FullInt& value2) noexcept;
    FULLINT_NOINLINE static void addIntoSubN(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2) noexcept;
#endif
    // addInto's cases other than its short common one (see FullIntEngine.cpp)
    FULLINT_NOINLINE static void addIntoGeneral(FullInt& result, const FullInt& value1, const FullInt& value2, bool negate2);
    FULLINT_NOINLINE static void addIntoFresh(FullInt& result, CSpan a, CSpan b, bool neg1, bool neg2);
    FULLINT_NOINLINE static void addIntoAliased(FullInt& result, CSpan a, CSpan b, bool neg1, bool neg2);
    FULLINT_NOINLINE static void multIntoAliased(FullInt& result, const FullInt& value1, const FullInt& value2);
    FULLINT_NOINLINE static void shiftIntoAliased(FullInt& result, const FullInt& value, int shift, bool left);
    // the shifts' cases other than their short common one (FullIntEngine.cpp)
    FULLINT_NOINLINE static void shiftLeftIntoGeneral(FullInt& result, const FullInt& value, int shift);
    FULLINT_NOINLINE static void shiftRightIntoGeneral(FullInt& result, const FullInt& value, int shift);
    static void ShiftRightIntoBody(FullInt& result, const FullInt& value, int shift);
    FULLINT_NOINLINE static void bitwiseIntoAliased(FullInt& result, int op, const FullInt& value1, const FullInt& value2);
    FULLINT_NOINLINE static void divRemAliased(const FullInt& value1, const FullInt& value2, FullInt* quotient, FullInt* remainder);
public:

    // equal lengths up to this many words: a plain loop instead of the kernel call (x86 2-16 words:
    // 0.66-0.78 of the call; x64 from 8 words the AVX2 kernel is ahead, 2 words still 0.6 for % ;
    // AArch64: the loop up to 16 was 1.17-1.25 of GMP at 8-16 words, the CmpN kernel (ldp pairs)
    // 0.77 at 32)
#if FULLINT_X64 || FULLINT_ARM64
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
            return Fullint64CmpNWIN(value1.data(), value2.data(), (int64_t)n);   // equal lengths: CompareN.asm
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
    static FullInt addOneWord(uint64_t a, bool aNegative, uint64_t b, bool bNegative) noexcept
    {
        FullInt r;
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

    static FullInt addOrSub(const FullInt& value1, const FullInt& value2, bool negate2);
    static FullInt addMagnitudes(CSpan a, CSpan b, bool negative);
    static FullInt subMagnitudes(CSpan a, CSpan b, bool neg1, bool neg2);
    static FullInt multGeneral(const FullInt& value1, const FullInt& value2);
    static void divRem(const FullInt& value1, const FullInt& value2, FullInt* quotient, FullInt* remainder);
    static FullInt bitwise(int op, const FullInt& value1, const FullInt& value2);
    static FullInt shiftLeftGeneral(const FullInt& value, int shift);
    static FullInt shiftRightGeneral(const FullInt& value, int shift);
    static std::string ToDecString(const FullInt& value);

    // decimal text by halves (FullIntEngine.Decimal.cpp)
    static const FullInt& DecimalPower(int k);
    static std::string ToDecStringRecursive(const FullInt& value);
    static size_t WriteDecimal(const FullInt& x, char* text, size_t end, ptrdiff_t width);
    static size_t WriteDecimalLeaf(CSpan w, char* text, size_t end);
    static size_t WriteDecimalFraction(CSpan w, char* text, size_t end);
    static FullInt ParseDecimal(std::string_view digits);
    static FullInt ParseDecimalLeaf(std::string_view digits);

    friend class FullInt;
};

inline FullInt operator+(const FullInt& a, const FullInt& b) { return FullIntEngine::add(a, b); }
inline FullInt operator-(const FullInt& a, const FullInt& b) { return FullIntEngine::sub(a, b); }
inline FullInt operator*(const FullInt& a, const FullInt& b) { return FullIntEngine::mult(a, b); }
inline FullInt operator/(const FullInt& a, const FullInt& b) { return FullIntEngine::div(a, b); }
inline FullInt operator%(const FullInt& a, const FullInt& b) { return FullIntEngine::mod(a, b); }
inline FullInt operator-(const FullInt& a) { return FullIntEngine::negate(a); }
inline FullInt operator+(const FullInt& a) { return a; }
inline FullInt operator&(const FullInt& a, const FullInt& b) { return FullIntEngine::And(a, b); }
inline FullInt operator|(const FullInt& a, const FullInt& b) { return FullIntEngine::Or(a, b); }
inline FullInt operator^(const FullInt& a, const FullInt& b) { return FullIntEngine::Xor(a, b); }
inline FullInt operator~(const FullInt& a) { return FullIntEngine::Not(a); }
inline FullInt operator<<(const FullInt& a, int shift) { return FullIntEngine::shiftLeft(a, shift); }
inline FullInt operator>>(const FullInt& a, int shift) { return FullIntEngine::shiftRight(a, shift); }

// GMP style: result = a op b into result's buffer (no new number per call in a loop);
// result may be a or b
#if !FULLINT_X86
inline void Add(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::addInto(result, a, b, false); }
inline void Subtract(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::addInto(result, a, b, true); }
#else
// x86: result neither operand with room - one kernel on the objects (IntoObj.asm), the rest as on x64
inline void Add(FullInt& result, const FullInt& a, const FullInt& b)
{
    if (Fullint64AddObjWIN(&result, &a, &b, 0) != 0) FullIntEngine::addInto(result, a, b, false);
}
inline void Subtract(FullInt& result, const FullInt& a, const FullInt& b)
{
    if (Fullint64AddObjWIN(&result, &a, &b, 1) != 0) FullIntEngine::addInto(result, a, b, true);
}
#endif
inline void Multiply(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::multInto(result, a, b); }
// by bits (not whole words) into a result with room - one kernel on the objects (IntoObj.asm)
inline void ShiftLeft(FullInt& result, const FullInt& a, int shift)
{
#if defined(_M_X64) && defined(_WIN32)
    // x64 1-32 words in the kernel; anything else: the kernel jumps on to shiftLeftInto itself
    // (Fullint64ShlIntoGeneral) - a jump from here, no frame here
    Fullint64ShlObjWIN(&result, &a, shift);
#elif FULLINT_GENERIC_KERNELS
    FullIntEngine::shiftLeftInto(result, a, shift);   // no object kernels here
#else
    if (Fullint64ShlObjWIN(&result, &a, shift) != 0) FullIntEngine::shiftLeftInto(result, a, shift);
#endif
}
inline void ShiftRight(FullInt& result, const FullInt& a, int shift) { FullIntEngine::shiftRightInto(result, a, shift); }
// the non-negative case on the objects in one kernel (x64 / x86 IntoObj.asm), the rest in C++
inline void And(FullInt& result, const FullInt& a, const FullInt& b)
{
#if FULLINT_X64
    // x64: the kernel up to 32 words (the shorter one sets the length); longer: straight to the C++
    // loop (a call in front of it slowed 64-1024 words 7-9%). x86: the kernel at any length.
    if (a.Length() > 32 && b.Length() > 32)
    {
        FullIntEngine::bitwiseInto(result, 0, a, b);
        return;
    }
#endif
#if FULLINT_GENERIC_KERNELS
    FullIntEngine::bitwiseInto(result, 0, a, b);   // no object kernels here
#else
    if (Fullint64AndObjWIN(&result, &a, &b) != 0) FullIntEngine::bitwiseInto(result, 0, a, b);
#endif
}
inline void Or(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::bitwiseInto(result, 1, a, b); }
inline void Xor(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::bitwiseInto(result, 2, a, b); }
// truncating, as / and %
inline void Divide(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::divRemInto(&result, nullptr, a, b); }
inline void Remainder(FullInt& result, const FullInt& a, const FullInt& b) { FullIntEngine::divRemInto(nullptr, &result, a, b); }

// compound assignment: in place where the operation allows it (+, - in a's own buffer)
inline FullInt& operator+=(FullInt& a, const FullInt& b) { FullIntEngine::addInto(a, a, b, false); return a; }
inline FullInt& operator-=(FullInt& a, const FullInt& b) { FullIntEngine::addInto(a, a, b, true); return a; }
inline FullInt& operator*=(FullInt& a, const FullInt& b) { FullIntEngine::multInto(a, a, b); return a; }
inline FullInt& operator/=(FullInt& a, const FullInt& b) { return a = a / b; }
inline FullInt& operator%=(FullInt& a, const FullInt& b) { return a = a % b; }
inline FullInt& operator&=(FullInt& a, const FullInt& b) { FullIntEngine::bitwiseInto(a, 0, a, b); return a; }
inline FullInt& operator|=(FullInt& a, const FullInt& b) { FullIntEngine::bitwiseInto(a, 1, a, b); return a; }
inline FullInt& operator^=(FullInt& a, const FullInt& b) { FullIntEngine::bitwiseInto(a, 2, a, b); return a; }
inline FullInt& operator<<=(FullInt& a, int shift) { return a = a << shift; }
inline FullInt& operator>>=(FullInt& a, int shift) { return a = a >> shift; }
inline FullInt& operator++(FullInt& a) { return a += FullInt(1); }
inline FullInt& operator--(FullInt& a) { return a -= FullInt(1); }
inline FullInt operator++(FullInt& a, int) { FullInt old(a); ++a; return old; }
inline FullInt operator--(FullInt& a, int) { FullInt old(a); --a; return old; }

// normalized storage: equal numbers have equal words
inline bool operator==(const FullInt& a, const FullInt& b) noexcept
{
    return a.neg_ == b.neg_ && a.size_ == b.size_ && std::memcmp(a.Data(), b.Data(), a.size_ * sizeof(uint64_t)) == 0;
}
inline std::strong_ordering operator<=>(const FullInt& a, const FullInt& b) noexcept
{
    return FullIntEngine::Compare(a, b) <=> 0;
}

// exponent >= 0 (std::out_of_range otherwise)
inline FullInt Pow(const FullInt& value, int exponent) { return FullIntEngine::pow(value, FullInt(exponent)); }
inline FullInt Abs(const FullInt& value) { return FullIntEngine::abs(value); }
// the truncated quotient and the remainder (the sign of the dividend), as / and %
inline FullInt DivRem(const FullInt& dividend, const FullInt& divisor, FullInt& remainder)
{
    return FullIntEngine::div(dividend, divisor, remainder);
}

std::ostream& operator<<(std::ostream& os, const FullInt& value);

}  // namespace fullnumerics

template <>
struct std::hash<fullnumerics::FullInt>
{
    size_t operator()(const fullnumerics::FullInt& v) const noexcept
    {
        size_t h = v.neg_ ? 0x9E3779B97F4A7C15ull : 0;
        const uint64_t* p = v.Data();
        for (uint32_t i = 0; i < v.size_; i++) h = (h ^ (size_t)p[i]) * 0x100000001B3ull + (h >> 7);
        return h;
    }
};
