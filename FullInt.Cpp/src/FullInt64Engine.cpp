// Port of FullInt.Core/FullInt64Engine.cs: the general (out-of-line) paths.
#include <fullint/FullInt64.h>
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <charconv>
#include <memory>
#include <ostream>
#include <stdexcept>

namespace fullint {

namespace {

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

}  // namespace

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

#pragma region mult

FullInt64 FullInt64Engine::multGeneral(const FullInt64& value1, const FullInt64& value2)
{
    CSpan a = value1.Words(), b = value2.Words();
    uint32_t length = (uint32_t)(a.size() + b.size());
    FullInt64 r;
    Span bytes(r.Prepare(length), length);

    if (std::min(a.size(), b.size()) >= (size_t)Karatsuba::Threshold)
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
void FullInt64Engine::divRem(const FullInt64& value1, const FullInt64& value2, FullInt64* quotient, FullInt64* remainder)
{
    if (value2.IsZero()) throw std::domain_error("division by zero");

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
        if (quotient) *quotient = FullInt64();
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
            FullInt64 q;
            FullIntCore::Div(dividend, divisor[0], Span(q.Prepare(la), la), &r1);
            q.Normalize(neg1 != neg2);
            *quotient = std::move(q);
        }
        else
        {
            FullIntCore::Div(dividend, divisor[0], Span(Scratch(la), la), &r1);
        }
        if (remainder) remainder->SetWords(neg1, 0, r1);
        return;
    }

    // the remainder kernel buffer is lenB + 2 words (internal calculation), the value right-aligned
    uint32_t lq = la - lb + 1;
    FullInt64 q, r;
    uint64_t* pr = r.Prepare(lb + 2);
    uint64_t* pq = quotient ? q.Prepare(lq) : Scratch(lq);
    std::memset(pr, 0, (lb + 2) * sizeof(uint64_t));
    std::memset(pq, 0, lq * sizeof(uint64_t));

    FullIntCore::Div(dividend, divisor, Span(pq, lq), Span(pr, lb + 2));

    if (remainder)
    {
        r.Normalize(neg1);
        *remainder = std::move(r);
    }
    if (quotient)
    {
        q.Normalize(neg1 != neg2);
        *quotient = std::move(q);
    }
}

#pragma endregion

#pragma region bitwise

FullInt64 FullInt64Engine::bitwise(int op, const FullInt64& value1, const FullInt64& value2)
{
    CSpan a = value1.Words(), b = value2.Words();
    uint32_t n = (uint32_t)std::max(a.size(), b.size()) + 1;
    FullInt64 r;
    Span rs(r.Prepare(n), n);

    int negative = op == 0 ? FullIntCore::And(a, value1.neg_, b, value2.neg_, rs)
                 : op == 1 ? FullIntCore::Or(a, value1.neg_, b, value2.neg_, rs)
                 : FullIntCore::Xor(a, value1.neg_, b, value2.neg_, rs);

    r.Normalize(negative != 0);
    return r;
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

    int resultBytes = (int)(w.size() * 1.014 + 1) * 19 + 1;
    std::unique_ptr<uint8_t[]> resultBuf(new uint8_t[resultBytes]());
    std::unique_ptr<uint64_t[]> valueBuf(new uint64_t[w.size()]);
    int start = resultBytes;
    FullIntCore::ToDecString(w, valueBuf.get(), resultBuf.get(), &start);

    if (value.neg_ && start > 0) resultBuf[--start] = '-';
    return std::string((const char*)resultBuf.get() + start, (size_t)(resultBytes - start));
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
    for (char c : value)
        if (digitValue(c) >= base) throw std::invalid_argument("not a digit of the base");

    FullInt64 result;
    if (base == 10)
    {
        int resultWords = (int)(value.size() / 19 + 1);
        std::unique_ptr<uint64_t[]> valueBuf(new uint64_t[resultWords]());
        std::unique_ptr<uint64_t[]> resultBuf(new uint64_t[resultWords]());
        int start = resultWords;
        FullIntCore::FromDecString((const uint8_t*)value.data(), (int)value.size(), valueBuf.get(), resultBuf.get(), &start);
        uint32_t n = (uint32_t)(resultWords - start);
        if (n == 0) return result;
        std::memcpy(result.Prepare(n), resultBuf.get() + start, n * sizeof(uint64_t));
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
