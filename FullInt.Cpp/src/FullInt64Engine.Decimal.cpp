// Port of FullInt.Core/FullInt64Engine.Decimal.cs: decimal text by halves.
//
//   P_k = 10^(19·2^k)   (P_0 = 10^19, P_k+1 = P_k²; computed once, cached)
//   ToString(x):  x = q·P_k + r  ->  ToString(q) followed by ToString(r) padded to 19·2^k digits
//   Parse(t):     t = [high][low: 19·2^k digits]  ->  Parse(high)·P_k + Parse(low)
// Leaves below DecimalConversion thresholds are the ToDecString / FromDecString kernels.
#include <fullint/FullInt64.h>

#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <vector>

namespace fullint {

namespace {

constexpr double BitsPerPower0 = 63.11663380285988;   // 19·log2(10): bits of P_0; P_k has 2^k times as many

// words of P_k, estimated without computing it
int DecimalPowerWords(int k) { return (int)std::ceil(BitsPerPower0 * (double)(1LL << k) / 64) + 1; }

std::mutex powersLock;
std::vector<std::unique_ptr<FullInt64>> powers;   // [k] = P_k; the objects never move

}  // namespace

const FullInt64& FullInt64Engine::DecimalPower(int k)
{
    std::lock_guard<std::mutex> guard(powersLock);
    while ((int)powers.size() <= k)
    {
        if (powers.empty())
            powers.push_back(std::make_unique<FullInt64>(uint64_t(10000000000000000000ull)));
        else
            powers.push_back(std::make_unique<FullInt64>(mult(*powers.back(), *powers.back())));
    }
    return *powers[k];
}

std::string FullInt64Engine::ToDecStringRecursive(const FullInt64& value)
{
    FullInt64 magnitude = abs(value);
    size_t capacity = (size_t)(value.size_ * 19.27) + 2;   // 64·log10(2) = 19.266 digits per word
    std::unique_ptr<char[]> text(new char[capacity + 1]);
    size_t start = WriteDecimal(magnitude, text.get(), capacity + 1, -1);
    if (value.neg_) text[--start] = '-';
    return std::string(text.get() + start, capacity + 1 - start);
}

// x's digits right-aligned at text[.. end); width >= 0: exactly width digits (leading zeros),
// -1: no padding (the top part). Returns the start index.
size_t FullInt64Engine::WriteDecimal(const FullInt64& x, char* text, size_t end, ptrdiff_t width)
{
    CSpan w = x.Words();
    if (w.size() <= (size_t)DecimalConversion::ToStringThreshold)
    {
        size_t start = WriteDecimalLeaf(w, text, end);
        if (width < 0) return start;
        size_t padded = end - (size_t)width;
        for (size_t i = padded; i < start; i++) text[i] = '0';
        return padded;
    }

    // P_k of about half of x's words
    int k = 0;
    while ((size_t)DecimalPowerWords(k + 1) * 2 <= w.size()) k++;
    FullInt64 q, r;
    divRem(x, DecimalPower(k), &q, &r);
    size_t low = (size_t)19 << k;
    WriteDecimal(r, text, end, (ptrdiff_t)low);
    return WriteDecimal(q, text, end - low, width < 0 ? -1 : width - (ptrdiff_t)low);
}

// the kernel on a short number; zero writes nothing
size_t FullInt64Engine::WriteDecimalLeaf(CSpan w, char* text, size_t end)
{
    if (w.size() == 1)
    {
        for (uint64_t v = w[0]; v != 0; v /= 10) text[--end] = (char)('0' + (int)(v % 10));
        return end;
    }
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
    for (int i = resultBytes - 1; i >= start; i--) text[--end] = (char)resultBuf[i];
    return end;
}

// decimal digits (checked by the caller) to a non-negative number
FullInt64 FullInt64Engine::ParseDecimal(std::string_view digits)
{
    if (digits.size() <= (size_t)DecimalConversion::ParseThreshold) return ParseDecimalLeaf(digits);

    // the low part: the largest 19·2^k digits below the length (at least half of them)
    int k = 0;
    while (((size_t)19 << (k + 1)) < digits.size()) k++;
    size_t low = (size_t)19 << k;
    FullInt64 high = ParseDecimal(digits.substr(0, digits.size() - low));
    FullInt64 lowValue = ParseDecimal(digits.substr(digits.size() - low));
    return add(mult(high, DecimalPower(k)), lowValue);
}

FullInt64 FullInt64Engine::ParseDecimalLeaf(std::string_view digits)
{
    // small: stack buffers (as C# stackalloc), large: heap; both zeroed
    int resultWords = (int)(digits.size() / 19 + 1);
    uint64_t stackWords[2][96];
    std::unique_ptr<uint64_t[]> heap;
    uint64_t* valueBuf = stackWords[0];
    uint64_t* resultBuf = stackWords[1];
    if (resultWords > 96)
    {
        heap = std::make_unique<uint64_t[]>(2 * (size_t)resultWords);
        valueBuf = heap.get();
        resultBuf = heap.get() + resultWords;
    }
    else
    {
        std::memset(valueBuf, 0, resultWords * sizeof(uint64_t));
        std::memset(resultBuf, 0, resultWords * sizeof(uint64_t));
    }
    int start = resultWords;
    FullIntCore::FromDecString((const uint8_t*)digits.data(), (int)digits.size(), valueBuf, resultBuf, &start);
    FullInt64 result;
    uint32_t n = (uint32_t)(resultWords - start);
    if (n == 0) return result;
    std::memcpy(result.Prepare(n), resultBuf + start, n * sizeof(uint64_t));
    result.Normalize(false);
    return result;
}

}  // namespace fullint
