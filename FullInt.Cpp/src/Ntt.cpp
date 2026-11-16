// Port of FullInt.Core/NttOps.cs: multiplication through number-theoretic transforms with the
// AVX2 kernels (NttAvx2.asm, x64 and x86). 32-bit digits, the cyclic convolution of length L (a power of
// two, at least the product's digits) modulo three primes p < 2^30 (998244353 = 119·2^23 + 1,
// 985661441 = 235·2^22 + 1, 943718401 = 225·2^22 + 1): forward transforms, pointwise products,
// the inverse transform, times L^-1; every term is below L·2^64 <= 2^86 < p1·p2·p3 (2^89.6), so
// Garner's CRT gives it exactly and the carries put the words together. L <= 2^22 digits.
// Transforms: radix-2 levels two at a time, depth-first above Block values; the last three
// levels inside the registers. Twiddles in Montgomery form, W[h + j] = w_2h^j·2^32 mod p for
// every level h, with Wq = W·p^-1 mod 2^32; the inverse transform has its own tables.
#include <fullint/Karatsuba.h>
#include <fullint/kernels.h>

#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>

namespace fullint {

namespace {

constexpr int MaxLog = 22;

bool Available() { return Cpu::Has(Cpu::Avx2); }   // NttAvx2.asm (x64 and x86)

constexpr size_t Block = size_t(1) << 12;
constexpr uint32_t Primes[3] = { 998244353u, 985661441u, 943718401u };
constexpr uint32_t Generators[3] = { 3u, 3u, 7u };

uint64_t PowMod(uint64_t a, uint64_t e, uint64_t p)
{
    uint64_t r = 1;
    a %= p;
    while (e != 0)
    {
        if (e & 1) r = r * a % p;
        a = a * a % p;
        e >>= 1;
    }
    return r;
}

// the twiddles of one prime for transforms up to size values; immutable once published
struct Tables
{
    size_t size;
    uint32_t p, pinv;
    std::vector<uint32_t> W, Wq, IW, IWq;   // size + 8 entries: the kernels read 4 bytes past a vector

    Tables(int k, size_t n) : size(n), p(Primes[k])
    {
        uint32_t inv = 1;
        for (int i = 0; i < 5; i++) inv *= 2 - p * inv;
        pinv = inv;
        W.assign(n + 8, 0); Wq.assign(n + 8, 0); IW.assign(n + 8, 0); IWq.assign(n + 8, 0);
        // the top level h = n/2 by multiplication, the others every other entry of the next
        size_t h = n / 2;
        uint64_t w = PowMod(Generators[k], (p - 1) / n, p);
        uint64_t cur = (uint64_t(1) << 32) % p;   // w^0 in Montgomery form
        for (size_t j = 0; j < h; j++)
        {
            W[h + j] = (uint32_t)cur;
            cur = cur * w % p;
        }
        // w^-j = w^(2h - j) = -w^(h - j)
        IW[h] = W[h];
        for (size_t j = 1; j < h; j++) IW[h + j] = p - W[2 * h - j];
        for (size_t lh = h / 2; lh >= 1; lh /= 2)
            for (size_t j = 0; j < lh; j++)
            {
                W[lh + j] = W[2 * lh + 2 * j];
                IW[lh + j] = IW[2 * lh + 2 * j];
            }
        for (size_t i = 1; i < n; i++)
        {
            Wq[i] = W[i] * inv;
            IWq[i] = IW[i] * inv;
        }
    }
};

// the published tables; the replaced ones stay alive (a transform may still read them)
std::atomic<const Tables*> current[3];
std::mutex gate;
std::vector<std::unique_ptr<Tables>> built;

const Tables& Get(int k, size_t size)
{
    const Tables* t = current[k].load(std::memory_order_acquire);
    if (t && t->size >= size) return *t;
    std::lock_guard<std::mutex> lock(gate);
    t = current[k].load(std::memory_order_relaxed);
    if (!t || t->size < size)
    {
        size_t s = std::max(size, Block);
        if (t) s = std::max(s, std::min(2 * t->size, size_t(1) << MaxLog));
        built.push_back(std::make_unique<Tables>(k, s));
        t = built.back().get();
        current[k].store(t, std::memory_order_release);
    }
    return *t;
}

// Garner's constants: p1, p2, p3, p1^-1 mod p2 and its Shoup factor, p1 mod p3 and its Shoup
// factor, (p1·p2)^-1 mod p3 and its Shoup factor, p1·p2
struct Crt
{
    uint64_t k[10];
    Crt()
    {
        uint64_t p1 = Primes[0], p2 = Primes[1], p3 = Primes[2];
        uint64_t inv12 = PowMod(p1 % p2, p2 - 2, p2);
        uint64_t p1m3 = p1 % p3;
        uint64_t inv123 = PowMod(p1 % p3 * (p2 % p3) % p3, p3 - 2, p3);
        uint64_t v[10] = { p1, p2, p3, inv12, (inv12 << 32) / p2, p1m3, (p1m3 << 32) / p3, inv123, (inv123 << 32) / p3, p1 * p2 };
        std::copy(v, v + 10, k);
    }
};
const Crt crt;

// decimation in frequency, depth-first
void Forward(uint32_t* x, size_t L, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    if (L > Block)
    {
        Fullint64NttDif2WIN_avx2(x, (int64_t)L, (int64_t)(L / 2), w, wq, p);
        size_t q = L / 4;
        for (int i = 0; i < 4; i++) Forward(x + i * q, q, w, wq, p);
        return;
    }
    size_t h = L / 2;
    for (; h >= 16; h /= 4) Fullint64NttDif2WIN_avx2(x, (int64_t)L, (int64_t)h, w, wq, p);
    if (h == 8) Fullint64NttDif1WIN_avx2(x, (int64_t)L, 8, w, wq, p);
    Fullint64NttDif3WIN_avx2(x, (int64_t)L, w, wq, p);
}

// decimation in time with the inverse twiddles: Forward backwards (without the L^-1)
void Inverse(uint32_t* x, size_t L, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    if (L > Block)
    {
        size_t q = L / 4;
        for (int i = 0; i < 4; i++) Inverse(x + i * q, q, w, wq, p);
        Fullint64NttDit2WIN_avx2(x, (int64_t)L, (int64_t)(L / 2), w, wq, p);
        return;
    }
    Fullint64NttDit3WIN_avx2(x, (int64_t)L, w, wq, p);
    size_t h = 8;
    while (h < L)
    {
        if (2 * h < L)
        {
            Fullint64NttDit2WIN_avx2(x, (int64_t)L, (int64_t)(2 * h), w, wq, p);
            h *= 4;
        }
        else
        {
            Fullint64NttDit1WIN_avx2(x, (int64_t)L, (int64_t)h, w, wq, p);
            h *= 2;
        }
    }
}

// the cyclic convolution of a and b in L 32-bit digits (L a power of two, at least the digits
// of both; a and b the same words: a square), its digits put together into the r.size() <= L/2
// words of r; returns the carry out of the top
uint64_t NttConvolve(CSpan a, CSpan b, Span r, size_t L)
{
    bool square = a.data() == b.data() && a.size() == b.size();
    size_t len = r.size(), nd = 2 * len;
    // one buffer per thread, grown on demand (the transforms call nothing that multiplies)
    thread_local std::unique_ptr<uint32_t[]> buffer;
    thread_local size_t capacity = 0;
    if (capacity < 4 * L)
    {
        buffer = std::make_unique_for_overwrite<uint32_t[]>(4 * L);
        capacity = 4 * L;
    }
    uint32_t* buf = buffer.get();
    uint32_t* y = buf + 3 * L;
    for (int k = 0; k < 3; k++)
    {
        const Tables& t = Get(k, L);
        uint32_t* x = buf + k * L;
        Fullint64NttLoadWIN_avx2(a.data(), (int64_t)a.size(), x, (int64_t)L, t.p);
        Forward(x, L, t.W.data(), t.Wq.data(), t.p);
        if (square)
        {
            Fullint64NttPointWIN_avx2(x, x, (int64_t)L, t.p, t.pinv);
        }
        else
        {
            Fullint64NttLoadWIN_avx2(b.data(), (int64_t)b.size(), y, (int64_t)L, t.p);
            Forward(y, L, t.W.data(), t.Wq.data(), t.p);
            Fullint64NttPointWIN_avx2(x, y, (int64_t)L, t.p, t.pinv);
        }
        Inverse(x, L, t.IW.data(), t.IWq.data(), t.p);
        // × 2^32·L^-1 (a Montgomery product by 2^64·L^-1): the pointwise products left one 2^-32
        uint64_t c = (uint64_t(1) << 32) % t.p;
        c = c * c % t.p * PowMod(L, t.p - 2, t.p) % t.p;
        Fullint64NttScaleWIN_avx2(x, (int64_t)((nd + 7) & ~size_t(7)), t.p, (uint32_t)c, (uint32_t)c * t.pinv);
    }
    return Fullint64NttCrtWIN(buf, buf + L, buf + 2 * L, (int64_t)nd, r.data(), (int64_t)len, crt.k);
}

bool Fits(size_t len) { return 2 * len <= (size_t(1) << MaxLog); }

}  // namespace

bool Karatsuba::NttWorth(size_t len, bool large)
{
    if (!Fits(len) || !Available()) return false;
    size_t nd = 2 * len, L = 16;
    while (L < nd) L *= 2;
    return large || 5 * nd >= 3 * L;
}

bool Karatsuba::NttMultiply(CSpan a, CSpan b, Span r)
{
    if (r.size() != a.size() + b.size()) throw std::invalid_argument("r.size() must be a.size() + b.size()");
    if (a.empty() || b.empty() || !Available() || !Fits(r.size())) return false;
    size_t nd = 2 * r.size(), L = 16;
    while (L < nd) L *= 2;
    NttConvolve(a, b, r, L);
    return true;
}

size_t Karatsuba::NttCyclicLength(size_t minWords)
{
    if (!Available()) return 0;
    size_t m = 8;
    while (m < minWords) m *= 2;
    return Fits(m) ? m : 0;
}

void Karatsuba::NttMultiplyCyclic(CSpan a, CSpan b, Span r)
{
    size_t m = r.size();
    uint64_t carry = NttConvolve(a, b, r, 2 * m);
    while (carry != 0)                                   // + carry (mod β^m - 1): the end-around carry
    {
        uint64_t c = carry;
        for (size_t i = m; i-- > 0 && c != 0;)
        {
            uint64_t v = r[i] + c;
            c = v < c ? 1 : 0;
            r[i] = v;
        }
        carry = c;
    }
    for (uint64_t w : r) if (w != ~0ull) return;
    std::fill(r.begin(), r.end(), 0);                    // β^m - 1: the other zero
}

}  // namespace fullint
