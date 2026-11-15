// Port of FullInt.Core/Fft.cs + FftOps.cs: Schönhage–Strassen multiplication.
//
//   a and b are cut into pieces of M words (piece i = the words of weight B^(M·i)),
//   K = 2^k >= the number of product pieces, so the cyclic convolution of length K does not
//   wrap: c_i = sum a_j·b_(i-j), and r = sum c_i·B^(M·i).
//   The convolution is computed modulo p = 2^N + 1 with N >= 128·M + k + 1 bits (every c_i < p)
//   and N a multiple of 64 and of K/2: then w = 2^(2N/K) is a K-th root of unity mod p
//   (2^N = -1, so 2^(2N) = 1), and every twiddle multiply is a shift.
//   forward FFT of a and b (decimation in frequency: natural in, bit-reversed out), pointwise
//   products mod p (the multiplication dispatcher again), inverse FFT (decimation in time:
//   bit-reversed in, natural out), times K^-1 = 2^(2N - k).
// Values mod p are kept in n1 = N/64 + 1 words, in [0, 2^N].
// A butterfly is two passes: u, v = u + v, u - v in place, and the twiddle shift into a spare
// vector - the vectors are reached through a slot table, so the spare and the shifted vector
// swap slots instead of a copy back. Both passes: the x64 kernels (FftAdx.asm, BMI2 + ADX),
// otherwise the portable code below.
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>

namespace fullint {

namespace {

// The work buffer of one FFT product, kept per thread and reused: a fresh one of a megabyte or
// more each time came as new pages from the OS, every 4 KB page faulting on its first write
// (16384 words: the split 0.009 -> 0.15 ms, 32768: 0.34 ms, more in the transforms). A nested
// FFT (a pointwise product long enough for one) takes a buffer of its own.
struct FftBuffer
{
    uint64_t* data;
    explicit FftBuffer(size_t words)
    {
        if (!busy && capacity >= words) { data = kept.get(); busy = owner = true; return; }
        if (!busy)
        {
            kept = std::make_unique_for_overwrite<uint64_t[]>(words);
            capacity = words;
            data = kept.get();
            busy = owner = true;
            return;
        }
        own = std::make_unique_for_overwrite<uint64_t[]>(words);
        data = own.get();
    }
    ~FftBuffer()
    {
        if (owner) busy = false;
    }
    FftBuffer(const FftBuffer&) = delete;
    FftBuffer& operator=(const FftBuffer&) = delete;

private:
    bool owner = false;
    std::unique_ptr<uint64_t[]> own;
    static thread_local std::unique_ptr<uint64_t[]> kept;
    static thread_local size_t capacity;
    static thread_local bool busy;
};
thread_local std::unique_ptr<uint64_t[]> FftBuffer::kept;
thread_local size_t FftBuffer::capacity = 0;
thread_local bool FftBuffer::busy = false;

bool NativeKernels() noexcept
{
#if defined(_M_X64) || defined(__x86_64__)
    return Cpu::Has(Cpu::Bmi2 | Cpu::Adx);
#else
    return false;
#endif
}

CSpan Trim(CSpan v)
{
    size_t z = 0;
    while (z + 1 < v.size() && v[z] == 0) z++;
    return v.subspan(z);
}

// the low part + 1, the top word = 0 or the carry out
void LowPlusOne(Span v)
{
    v[0] = 0;
    for (size_t k = v.size() - 1; k >= 1; k--)
        if (++v[k] != 0) return;
    v[0] = 1;
}

// u, v = u + v, u - v (mod p), in place
void AddSub(bool native, Span u, Span v)
{
#if defined(_M_X64) || defined(__x86_64__)
    if (native) { Fullint64FftAddSubWIN_adx(u.data(), v.data(), (int)u.size()); return; }
#endif
    size_t w = u.size() - 1;
    uint64_t carry = 0, borrow = 0;
    for (size_t k = w + 1; k-- > 0;)
    {
        uint64_t a = u[k], b = v[k];
        uint64_t s = a + b, c = s < a;
        uint64_t s2 = s + carry;
        c |= s2 < s;
        uint64_t d = a - b, br = a < b;
        uint64_t d2 = d - borrow;
        br |= d < borrow;
        u[k] = s2;
        v[k] = d2;
        carry = c;
        borrow = br;
    }
    uint64_t t = u[0];
    if (t != 0)
    {
        u[0] = 0;
        uint64_t bw = t;
        for (size_t k = w; k >= 1 && bw != 0; k--)
        {
            uint64_t x = u[k];
            u[k] = x - bw;
            bw = x < bw;
        }
        if (bw != 0) LowPlusOne(u);
    }
    if (borrow != 0) LowPlusOne(v);
}

// word j (from the low end) of x << bs; x word j is x[w - j]
inline uint64_t Sh(CSpan x, size_t w, size_t j, int bs)
{
    uint64_t lo = x[w - j] << bs;
    if (bs != 0 && j > 0) lo |= x[w - j + 1] >> (64 - bs);
    return lo;
}

// dst = x·2^s mod p, 0 <= s < 2N; dst must not be x
void ShlMod(bool native, CSpan x, int s, Span dst)
{
#if defined(_M_X64) || defined(__x86_64__)
    if (native) { Fullint64FftShlModWIN_bmi2(x.data(), dst.data(), (int)x.size(), s); return; }
#endif
    size_t w = x.size() - 1;
    int nBits = (int)(64 * w);
    bool negate = false;
    if (s >= nBits) { s -= nBits; negate = true; }
    size_t ws = (size_t)s >> 6;
    int bs = s & 63;
    uint64_t borrow = 0;
    for (size_t i = 0; i < w; i++)
    {
        uint64_t l = i < ws ? 0 : Sh(x, w, i - ws, bs);
        uint64_t h = i < ws ? Sh(x, w, i + w - ws, bs) : i == ws ? Sh(x, w, w, bs) : 0;
        if (negate) std::swap(l, h);
        uint64_t d = l - h, br = l < h;
        uint64_t d2 = d - borrow;
        br |= d < borrow;
        dst[w - i] = d2;
        borrow = br;
    }
    dst[0] = 0;
    if (borrow != 0) LowPlusOne(dst);
}

// x = p - x for 0 < x <= 2^N:  p - x = (2^N - 1 - x) + 2, the low part complemented + 2
void NegateMod(Span x)
{
    size_t w = x.size() - 1;
    if (x[0] != 0) { x[0] = 0; x[w] = 1; return; }      // x = 2^N: p - x = 1
    for (size_t i = 1; i <= w; i++) x[i] = ~x[i];
    uint64_t add = 2;
    for (size_t i = w; i >= 1 && add != 0; i--)
    {
        uint64_t y = x[i] + add;
        add = y < add;
        x[i] = y;
    }
    x[0] = add;
}

// x = prod mod p (prod: 2·n1 words): prod = hi·2^N + lo == lo - hi
void ReduceProduct(CSpan prod, Span x)
{
    size_t n1 = x.size(), w = n1 - 1;
    x[0] = 0;
    std::copy(prod.begin() + (2 * n1 - w), prod.end(), x.begin() + 1);
    CSpan hi = Trim(prod.subspan(0, 2 * n1 - w));
    if (hi.size() == 1 && hi[0] == 0) return;
    if (FullIntCore::Sub(x, hi, x) < 0) NegateMod(x);
}

// decimation in frequency, natural order in, bit-reversed out; slots[K] is the spare
void ForwardDif(bool native, uint64_t* all, size_t* slots, int K, size_t n1, int nBits)
{
    for (int half = K / 2; half >= 1; half /= 2)
    {
        int step = nBits / half;                         // w_(2·half) = 2^(N / half)
        for (int start = 0; start < K; start += 2 * half)
            for (int j = 0; j < half; j++)
            {
                int iu = start + j, ix = start + j + half;
                AddSub(native, Span(all + slots[iu], n1), Span(all + slots[ix], n1));
                if (j == 0) continue;
                ShlMod(native, CSpan(all + slots[ix], n1), j * step, Span(all + slots[K], n1));
                std::swap(slots[ix], slots[K]);
            }
    }
}

// decimation in time with w^-1, bit-reversed order in, natural out (without the 1/K)
void InverseDit(bool native, uint64_t* all, size_t* slots, int K, size_t n1, int nBits)
{
    for (int half = 1; half < K; half *= 2)
    {
        int step = nBits / half;
        for (int start = 0; start < K; start += 2 * half)
            for (int j = 0; j < half; j++)
            {
                int iu = start + j, ix = start + j + half;
                if (j != 0)
                {
                    ShlMod(native, CSpan(all + slots[ix], n1), 2 * nBits - j * step, Span(all + slots[K], n1));
                    std::swap(slots[ix], slots[K]);
                }
                AddSub(native, Span(all + slots[iu], n1), Span(all + slots[ix], n1));
            }
    }
}

// the pieces of x (M words each, from the low end) into the K vectors, zero-padded
void Split(CSpan x, size_t m, int K, size_t n1, uint64_t* all, const size_t* slots)
{
    size_t lx = x.size();
    for (int i = 0; i < K; i++)
    {
        Span v(all + slots[i], n1);
        std::memset(v.data(), 0, v.size_bytes());
        if ((size_t)i * m >= lx) continue;
        size_t end = lx - (size_t)i * m;
        size_t start = end > m ? end - m : 0;
        std::copy(x.begin() + start, x.begin() + end, v.end() - (end - start));
    }
}

}  // namespace

// k, M, N for an la x lb product: the smallest estimated cost (or forceK = k for tests)
void Karatsuba::ChooseFft(size_t la, size_t lb, int forceK, int& k, size_t& m, int& nBits)
{
    bool native = NativeKernels();
    size_t len = la + lb;
    double best = 1e300;
    k = 0; m = 0; nBits = 0;
    for (int kk = forceK > 0 ? forceK : 3; kk <= (forceK > 0 ? forceK : 20); kk++)
    {
        size_t K = (size_t)1 << kk;
        size_t mm = (len + K - 1) / K;
        while ((la + mm - 1) / mm + (lb + mm - 1) / mm - 1 > K) mm++;   // no wrap
        long long unit = std::max<long long>(64, (long long)K / 2);
        long long bits = 128LL * (long long)mm + kk + 1;
        long long nb = (bits + unit - 1) / unit * unit;
        if (nb > (1LL << 30)) continue;
        double n1 = (double)(nb / 64 + 1);
        // ns, measured (Zen 3): an n-word product ~2.3·n^1.55 from 32 words; a butterfly
        double mulCost = n1 < 32 ? 0.45 * n1 * n1 : 2.3 * std::pow(n1, 1.55);
        double bfly = native ? FftNativeButterfly[0] + FftNativeButterfly[1] * n1 : 20 + 6.0 * n1;   // portable: as managed C#
        double cost = (double)K * mulCost + 3.0 * (double)K * kk * bfly;
        if (cost < best || forceK > 0)
        {
            best = cost; k = kk; m = mm; nBits = (int)nb;
        }
        if (mm == 1) break;
    }
}

void Karatsuba::FftMultiply(CSpan a, CSpan b, Span r, int k)
{
    if (r.size() != a.size() + b.size()) throw std::invalid_argument("r.size() must be a.size() + b.size()");
    if (k < 1 || k > 20) throw std::out_of_range("k");
    FftMul(a, b, r, Limits{ Threshold, Toom3Threshold, Toom4Threshold, Toom6Threshold, FftThreshold, SqrThreshold }, k);
}

void Karatsuba::FftMul(CSpan a, CSpan b, Span r, const Limits& lim, int forceK)
{
    int k, nBits;
    size_t m;
    ChooseFft(a.size(), b.size(), forceK, k, m, nBits);
    Convolve(a, b, r, k, m, nBits, lim);
}

size_t Karatsuba::MulModLength(size_t minWords, size_t cyclicFrom)
{
    if (minWords < cyclicFrom) return minWords;
    int k, nBits;
    size_t m;
    ChooseFftCyclic(minWords, false, k, m, nBits);
    return m << k;
}

void Karatsuba::MultiplyMod(CSpan a, CSpan b, Span r, size_t cyclicFrom)
{
    size_t m = r.size();
    if (m >= cyclicFrom)
    {
        int k, nBits;
        size_t pm;
        ChooseFftCyclic(m, true, k, pm, nBits);
        if (pm << k == m)
        {
            // K = 2^k pieces of M words, K·M = m: sum c_i·β^(M·i) = a·b mod β^(KM) - 1
            size_t n1 = (size_t)nBits / 64 + 1;
            std::vector<uint64_t> acc(m + n1 + 1);
            Convolve(a, b, acc, k, pm, nBits, Limits{ Threshold, Toom3Threshold, Toom4Threshold, Toom6Threshold, FftThreshold, SqrThreshold });
            FoldMod(acc, r);
            return;
        }
    }
    std::vector<uint64_t> prod(a.size() + b.size());
    Multiply(a, b, prod);
    FoldMod(prod, r);
}

void Karatsuba::FoldMod(CSpan x, Span r)
{
    size_t m = r.size();
    std::memset(r.data(), 0, r.size_bytes());
    for (size_t end = x.size(); end > 0; end = end > m ? end - m : 0)
    {
        size_t start = end > m ? end - m : 0;
        CSpan chunk = Trim(x.subspan(start, end - start));
        uint64_t c = FullIntCore::Add(r, chunk, r);
        while (c != 0)                                                // β^m = 1
        {
            c = 1;
            for (size_t i = m; i-- > 0 && c != 0;) c = ++r[i] == 0 ? 1 : 0;
        }
    }
    for (uint64_t w : r)
        if (w != ~0ull) return;
    std::memset(r.data(), 0, r.size_bytes());                         // β^m - 1 = 0
}

// k, M, N for a cyclic product modulo β^(K·M) - 1 of at least mMin words (exact: K·M = mMin,
// only the K that divide it)
void Karatsuba::ChooseFftCyclic(size_t mMin, bool exact, int& k, size_t& m, int& nBits)
{
    bool native = NativeKernels();
    double best = 1e300;
    k = 0; m = 0; nBits = 0;
    for (int kk = 3; kk <= 20; kk++)
    {
        size_t K = (size_t)1 << kk;
        if (exact && mMin % K != 0) continue;
        size_t mm = (mMin + K - 1) / K;
        long long unit = std::max<long long>(64, (long long)K / 2);
        long long bits = 128LL * (long long)mm + kk + 1;
        long long nb = (bits + unit - 1) / unit * unit;
        if (nb > (1LL << 30)) continue;
        double n1 = (double)(nb / 64 + 1);
        double mulCost = n1 < 32 ? 0.45 * n1 * n1 : 2.3 * std::pow(n1, 1.55);
        double bfly = native ? FftNativeButterfly[0] + FftNativeButterfly[1] * n1 : 20 + 6.0 * n1;
        double cost = (double)K * mulCost + 3.0 * (double)K * kk * bfly;
        if (cost < best)
        {
            best = cost; k = kk; m = mm; nBits = (int)nb;
        }
        if (mm == 1) break;
    }
}

// r = sum c_i·β^(M·i) of the convolution of a and b in K = 2^k pieces of M words mod 2^N + 1
// (r: the words the coefficients land on, cleared here; a coefficient beyond it is zero)
void Karatsuba::Convolve(CSpan a, CSpan b, Span r, int k, size_t m, int nBits, const Limits& lim)
{
    bool native = NativeKernels();
    size_t len = r.size();
    int K = 1 << k;
    size_t n1 = (size_t)nBits / 64 + 1;

    // vectors: K for a, K for b, one spare each; then the product and the scratch
    size_t vectors = 2 * (size_t)K + 2;
    size_t words = vectors * n1 + 2 * n1 + ScratchLength(n1, n1);
    FftBuffer buf(words);
    uint64_t* all = buf.data;
    std::vector<size_t> slotTable(vectors);
    for (size_t i = 0; i < vectors; i++) slotTable[i] = i * n1;
    size_t* sa = slotTable.data();
    size_t* sb = slotTable.data() + K + 1;               // [K] = the spare
    Span prod(all + vectors * n1, 2 * n1);
    Span rest(all + vectors * n1 + 2 * n1, ScratchLength(n1, n1));

    // a square: one transform, the pointwise products squares
    bool square = Same(a, b);
    Split(a, m, K, n1, all, sa);
    if (!square) Split(b, m, K, n1, all, sb);

    ForwardDif(native, all, sa, K, n1, nBits);
    if (!square) ForwardDif(native, all, sb, K, n1, nBits);

    for (int i = 0; i < K; i++)
    {
        Span x(all + sa[i], n1), y = square ? x : Span(all + sb[i], n1);
        Mul(x, y, prod, rest, lim);
        ReduceProduct(prod, x);
    }

    InverseDit(native, all, sa, K, n1, nBits);

    // r = sum (c_i / K)·B^(M·i)
    std::memset(r.data(), 0, r.size_bytes());
    Span t(all + sa[K], n1);                             // the spare
    for (int i = 0; i < K && (size_t)i * m < len; i++)
    {
        ShlMod(native, CSpan(all + sa[i], n1), 2 * nBits - k, t);   // / K
        CSpan v = Trim(t);
        if (v.size() == 1 && v[0] == 0) continue;
        // only the words c_i lands on (and one for its carry); a carry beyond: on by hand
        size_t end = len - (size_t)i * m, width = std::min(end, v.size() + 1);
        Span target = r.subspan(end - width, width);
        if (FullIntCore::Add(target, v, target) != 0)
            for (size_t j = end - width; j-- > 0;)
                if (++r[j] != 0) break;
    }
}

}  // namespace fullint
