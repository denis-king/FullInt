// Port of FullInt.Core/FullIntEngine.Decimal.cs: decimal text by halves.
//
//   P_k = 10^(19·2^k)   (P_0 = 10^19, P_k+1 = P_k²; computed once, cached)
//   ToString(x):  x = q·P_k + r  ->  ToString(q) followed by ToString(r) padded to 19·2^k digits
//   Parse(t):     t = [high][low: 19·2^k digits]  ->  Parse(high)·P_k + Parse(low)
// Leaves below DecimalConversion thresholds are the ToDecString / FromDecString kernels.
#include <fullnumerics/FullInt.h>
#include <fullnumerics/NewtonDivision.h>

#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <vector>

namespace fullnumerics {

namespace {

constexpr double BitsPerPower0 = 63.11663380285988;   // 19·log2(10): bits of P_0; P_k has 2^k times as many

// words of P_k, estimated without computing it
int DecimalPowerWords(int k) { return (int)std::ceil(BitsPerPower0 * (double)(1LL << k) / 64) + 1; }

std::mutex powersLock;
std::vector<std::unique_ptr<FullInt>> powers;   // [k] = P_k; the objects never move

// P_k = 10^(19·2^k) = 2^(19·2^k)·5^(19·2^k): its low 19·2^k bits are zero, about 30% of its words.
// The divisions divide by P_k without its zero low words (P'), the dividend's low words go to the
// remainder as they are (GMP's power table does the same): x = xh·β^z + xl, xh = q·P' + r' ->
// x = q·P_k + (r'·β^z + xl). [k] made on first use, with P' prepared for the division (its
// reciprocal) when it is long enough.
struct ShiftedPower
{
    FullInt p;      // P_k without its zero low words
    uint32_t zeros;   // those words
    std::unique_ptr<NewtonDivision::Prepared> prepared;
};
std::mutex shiftedLock;
std::vector<std::unique_ptr<ShiftedPower>> shifted;

const ShiftedPower& ShiftedDecimalPower(int k, const FullInt& pk)
{
    std::lock_guard<std::mutex> guard(shiftedLock);
    if ((int)shifted.size() <= k) shifted.resize(k + 1);
    if (!shifted[k])
    {
        CSpan w = pk.Words();
        uint32_t z = 0;
        while (z + 1 < w.size() && w[w.size() - 1 - z] == 0) z++;
        auto e = std::make_unique<ShiftedPower>();
        e->p = FullInt(false, w.subspan(0, w.size() - z));
        e->zeros = z;
        if (w.size() - z >= (size_t)DecimalConversion::PreparedFrom)
            e->prepared = std::make_unique<NewtonDivision::Prepared>(NewtonDivision::Prepare(e->p.Words()));
        shifted[k] = std::move(e);
    }
    return *shifted[k];
}

#if FULLINT_WORD64
// The fraction method's reciprocal for n-word numbers: m chunks of 19 digits (10^(19m) > B^n),
// r = ceil(B^(L+n) / 10^(19m)), L = m + 1 words below the point. Made once per n.
struct FractionReciprocal
{
    uint32_t m;
    std::vector<uint64_t> r;   // top first
};
std::atomic<const FractionReciprocal*> fractionReciprocals[DecimalConversion::FractionMaxWords + 1];
std::mutex fractionLock;

const FractionReciprocal& FractionReciprocalFor(uint32_t n)
{
    const FractionReciprocal* p = fractionReciprocals[n].load(std::memory_order_acquire);
    if (p) return *p;
    std::lock_guard<std::mutex> guard(fractionLock);
    p = fractionReciprocals[n].load(std::memory_order_relaxed);
    if (p) return *p;
    auto e = new FractionReciprocal();   // kept for the process
    e->m = (uint32_t)(n * 64 * 0.30102999566398120 / 19) + 1;
    FullInt d(1);
    for (uint32_t i = 0; i < e->m; i++) d = d * FullInt(uint64_t(10000000000000000000ull));
    FullInt num = FullInt(1) << (int)(64 * (e->m + 1 + n));
    FullInt q = (num + d - FullInt(1)) / d;
    CSpan w = q.Words();
    e->r.assign(w.begin(), w.end());
    fractionReciprocals[n].store(e, std::memory_order_release);
    return *e;
}
#endif

}  // namespace

#if FULLINT_WORD64
// Knuth's fraction method (TAOCP vol. 2, 4.4): f = x / 10^(19m) as an L-word fraction, an upper
// bound (x times the reciprocal, the low n words dropped, + 1: below 2 units of its last word);
// then ToDecStringFrac.asm multiplies f by 10^19 m times, each integer part 19 digits. The bound
// keeps every integer part exact (see the kernel). x: n words, top nonzero; zero writes nothing.
size_t FullIntEngine::WriteDecimalFraction(CSpan w, char* text, size_t end)
{
    uint32_t n = (uint32_t)w.size();
    const FractionReciprocal& rc = FractionReciprocalFor(n);
    uint32_t lr = (uint32_t)rc.r.size(), L = rc.m + 1;
    uint64_t prod[2 * DecimalConversion::FractionMaxWords + 8];
    FullIntCore::Mult(w, CSpan(rc.r), Span(prod, n + lr));
    uint64_t f[DecimalConversion::FractionMaxWords + 8];
    for (uint32_t i = 0; i < L; i++) f[i] = prod[lr - L + i];   // the top lr words' low L (the rest zero)
    for (uint32_t i = L; i-- > 0;)                              // + 1: an upper bound
        if (++f[i] != 0) break;
    char digits[19 * (DecimalConversion::FractionMaxWords + 4)];
    Fullint64ToDecFracWIN(f, L, rc.m, (uint8_t*)digits);
    size_t total = (size_t)19 * rc.m, i = 0;
    while (i < total && digits[i] == '0') i++;
    std::memcpy(text + end - (total - i), digits + i, total - i);
    return end - (total - i);
}
#endif

const FullInt& FullIntEngine::DecimalPower(int k)
{
    std::lock_guard<std::mutex> guard(powersLock);
    while ((int)powers.size() <= k)
    {
        if (powers.empty())
            powers.push_back(std::make_unique<FullInt>(uint64_t(10000000000000000000ull)));
        else
            powers.push_back(std::make_unique<FullInt>(mult(*powers.back(), *powers.back())));
    }
    return *powers[k];
}

std::string FullIntEngine::ToDecStringRecursive(const FullInt& value)
{
    FullInt magnitude = abs(value);
    size_t capacity = (size_t)(value.size_ * 19.27) + 2;   // 64·log10(2) = 19.266 digits per word
    std::unique_ptr<char[]> text(new char[capacity + 1]);
    size_t start = WriteDecimal(magnitude, text.get(), capacity + 1, -1);
    if (value.neg_) text[--start] = '-';
    return std::string(text.get() + start, capacity + 1 - start);
}

// x's digits right-aligned at text[.. end); width >= 0: exactly width digits (leading zeros),
// -1: no padding (the top part). Returns the start index.
size_t FullIntEngine::WriteDecimal(const FullInt& x, char* text, size_t end, ptrdiff_t width)
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
    const ShiftedPower& sp = ShiftedDecimalPower(k, DecimalPower(k));
    size_t z = sp.zeros;
    CSpan pw = sp.p.Words(), xh = w.subspan(0, w.size() - z), xl = w.subspan(w.size() - z);
    FullInt q, rh;
    if (sp.prepared)
    {
        // the same P_k divides 2^level numbers: its reciprocal once, then a product for the
        // quotient and one (cyclic when long) for the remainder per division
        uint32_t lq = (uint32_t)(xh.size() - pw.size() + 1), lr = (uint32_t)pw.size() + 2;
        NewtonDivision::DivRem(xh, *sp.prepared, Span(q.Prepare(lq), lq), Span(rh.Prepare(lr), lr));
        q.Normalize(false);
        rh.Normalize(false);
    }
    else if (z == 0)
        divRem(x, sp.p, &q, &rh);
    else
        divRem(FullInt(false, xh), sp.p, &q, &rh);
    // r = r'·β^z + xl
    FullInt r;
    CSpan rw = rh.Words();
    uint32_t lr = (uint32_t)(rw.size() + z);
    uint64_t* pr = r.Prepare(lr);
    std::memcpy(pr, rw.data(), rw.size_bytes());
    if (z) std::memcpy(pr + rw.size(), xl.data(), xl.size_bytes());
    r.Normalize(false);
    size_t low = (size_t)19 << k;
    WriteDecimal(r, text, end, (ptrdiff_t)low);
    return WriteDecimal(q, text, end - low, width < 0 ? -1 : width - (ptrdiff_t)low);
}

// the kernel on a short number; zero writes nothing
size_t FullIntEngine::WriteDecimalLeaf(CSpan w, char* text, size_t end)
{
#if FULLINT_WORD64
    if (w.size() >= (size_t)DecimalConversion::FractionFrom && w.size() <= (size_t)DecimalConversion::FractionMaxWords)
        return WriteDecimalFraction(w, text, end);
#endif
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
FullInt FullIntEngine::ParseDecimal(std::string_view digits)
{
    if (digits.size() <= (size_t)DecimalConversion::ParseThreshold) return ParseDecimalLeaf(digits);

    // the low part: the largest 19·2^k digits below the length (at least half of them)
    int k = 0;
    while (((size_t)19 << (k + 1)) < digits.size()) k++;
    size_t low = (size_t)19 << k;
    FullInt high = ParseDecimal(digits.substr(0, digits.size() - low));
    FullInt lowValue = ParseDecimal(digits.substr(digits.size() - low));
    return add(mult(high, DecimalPower(k)), lowValue);
}

FullInt FullIntEngine::ParseDecimalLeaf(std::string_view digits)
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
    FullInt result;
    uint32_t n = (uint32_t)(resultWords - start);
    if (n == 0) return result;
    std::memcpy(result.Prepare(n), resultBuf + start, n * sizeof(uint64_t));
    result.Normalize(false);
    return result;
}

}  // namespace fullnumerics
