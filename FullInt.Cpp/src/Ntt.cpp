// Port of FullInt.Core/NttOps.cs: multiplication through number-theoretic transforms with the
// AVX2 kernels (NttAvx2.asm, x64 and x86). 32-bit digits, the cyclic convolution of length L
// (2^k or 3·2^k, the shortest at least the product's digits: at most 1.5 times the product)
// modulo three primes p < 2^30 with 3·2^22 | p - 1 (943718401 = 225·2^22 + 1, 918552577 =
// 219·2^22 + 1, 880803841 = 105·2^23 + 1): forward transforms, pointwise products, the inverse
// transform, times L^-1; every term is below (L/2)·2^64 <= 2^85 < p1·p2·p3 (2^89.3), so
// Garner's CRT gives it exactly and the carries put the words together. L <= 2^22 digits.
// A length 3·2^k: a radix-3 level on top (R3Dif / R3Dit, twiddles made on the way), then
// three transforms of 2^k.
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

// the kernels: NttAvx2.asm on x64 / x86 (with AVX2), the portable C++ ones on AArch64
// (ntt_portable.cpp)
#if FULLINT_X86_FAMILY
bool Available() { return Cpu::Has(Cpu::Avx2); }
#define NTT(name) Fullint64Ntt##name##WIN_avx2
#define NTT_CRT Fullint64NttCrtWIN
#define NTT_V(name) Fullint64Ntt##name##WIN_avx2
#else
bool Available() { return true; }
#define NTT(name) Fullint64Ntt##name##WIN_portable
#define NTT_CRT Fullint64NttCrtWIN_portable
#if FULLINT_ARM64_ASM
#define NTT_V(name) Fullint64Ntt##name##WIN_neon   // the levels, Point, Scale: NttNeon.s
#else
#define NTT_V(name) Fullint64Ntt##name##WIN_portable
#endif
#endif

constexpr size_t Block = size_t(1) << 12;
constexpr uint32_t Primes[3] = { 943718401u, 918552577u, 880803841u };
constexpr uint32_t Generators[3] = { 7u, 5u, 26u };

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
#if FULLINT_X86_FAMILY
        uint64_t cur = (uint64_t(1) << 32) % p;   // w^0 in Montgomery form
#else
        uint64_t cur = 1;                         // AArch64: w^0 itself (Shoup's form, below)
#endif
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
#if FULLINT_X86_FAMILY
            Wq[i] = W[i] * inv;
            IWq[i] = IW[i] * inv;
#else
            // AArch64: the twiddles by Shoup's method, wq = floor(w·2^32 / p): a·w mod p as
            // a·w - hi(a·wq)·p in [0, 2p) for any a < 2^32 - five NEON instructions against
            // Montgomery's nine (ntt_portable.cpp, NttNeon.s)
            Wq[i] = (uint32_t)(((uint64_t)W[i] << 32) / p);
            IWq[i] = (uint32_t)(((uint64_t)IW[i] << 32) / p);
#endif
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
        NTT_V(Dif2)(x, (int64_t)L, (int64_t)(L / 2), w, wq, p);
        size_t q = L / 4;
        for (int i = 0; i < 4; i++) Forward(x + i * q, q, w, wq, p);
        return;
    }
    size_t h = L / 2;
    for (; h >= 16; h /= 4) NTT_V(Dif2)(x, (int64_t)L, (int64_t)h, w, wq, p);
    if (h == 8) NTT_V(Dif1)(x, (int64_t)L, 8, w, wq, p);
    NTT_V(Dif3)(x, (int64_t)L, w, wq, p);
}

// decimation in time with the inverse twiddles: Forward backwards (without the L^-1)
void Inverse(uint32_t* x, size_t L, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    if (L > Block)
    {
        size_t q = L / 4;
        for (int i = 0; i < 4; i++) Inverse(x + i * q, q, w, wq, p);
        NTT_V(Dit2)(x, (int64_t)L, (int64_t)(L / 2), w, wq, p);
        return;
    }
    NTT_V(Dit3)(x, (int64_t)L, w, wq, p);
    size_t h = 8;
    while (h < L)
    {
        if (2 * h < L)
        {
            NTT_V(Dit2)(x, (int64_t)L, (int64_t)(2 * h), w, wq, p);
            h *= 4;
        }
        else
        {
            NTT_V(Dit1)(x, (int64_t)L, (int64_t)h, w, wq, p);
            h *= 2;
        }
    }
}

// the transform length for n digits: the shortest 2^k or 3·2^k (k >= 4) from n
size_t Length(size_t n)
{
    size_t L = 16;
    while (L < n) L *= 2;
    size_t l3 = L / 4 * 3;   // 3·2^(k-2)
    return l3 >= n && l3 >= 48 ? l3 : L;
}

// the radix-3 level's constants for N = 3M (NttAvx2.asm): w = w_N (inverse: w^-1), ω = w^M;
// nine vectors: w^0..7, w^0,2,..,14, w^8 and its q, w^16 and its q, ω and its q, p^-1
// (Montgomery form: ·2^32 mod p; q = ·p^-1 mod 2^32)
struct R3Constants
{
    alignas(32) uint32_t c[72];
    R3Constants(int k, const Tables& t, size_t n, bool inverse)
    {
        uint64_t p = t.p, r = (uint64_t(1) << 32) % p;
        uint64_t w = PowMod(Generators[k], (p - 1) / n, p);
        if (inverse) w = PowMod(w, p - 2, p);
        auto mont = [&](uint64_t v) { return (uint32_t)(v * r % p); };
        uint64_t wj = 1, w2 = w * w % p, w2j = 1;
        for (int j = 0; j < 8; j++)
        {
            c[j] = mont(wj);
            c[8 + j] = mont(w2j);
            wj = wj * w % p;
            w2j = w2j * w2 % p;
        }
        uint32_t s1 = mont(wj), s2 = mont(w2j), om = mont(PowMod(w, n / 3, p));
        for (int j = 0; j < 8; j++)
        {
            c[16 + j] = s1; c[24 + j] = s1 * t.pinv;
            c[32 + j] = s2; c[40 + j] = s2 * t.pinv;
            c[48 + j] = om; c[56 + j] = om * t.pinv;
            c[64 + j] = t.pinv;
        }
    }
};

// a transform of M (r3: 3M) values: the radix-3 level first, then three of M
void Forward3(uint32_t* x, size_t M, const R3Constants* k, const Tables& t)
{
    if (!k)
    {
        Forward(x, M, t.W.data(), t.Wq.data(), t.p);
        return;
    }
    NTT_V(R3Dif)(x, (int64_t)M, k->c, t.p);
    for (int i = 0; i < 3; i++) Forward(x + i * M, M, t.W.data(), t.Wq.data(), t.p);
}

void Inverse3(uint32_t* x, size_t M, const R3Constants* k, const Tables& t)
{
    if (!k)
    {
        Inverse(x, M, t.IW.data(), t.IWq.data(), t.p);
        return;
    }
    for (int i = 0; i < 3; i++) Inverse(x + i * M, M, t.IW.data(), t.IWq.data(), t.p);
    NTT_V(R3Dit)(x, (int64_t)M, k->c, t.p);
}

// the cyclic convolution of a and b in L 32-bit digits (L = 2^k or 3·2^k, at least the digits
// of both; a and b the same words: a square), its digits put together into the r.size() <= L/2
// words of r; returns the carry out of the top
uint64_t NttConvolve(CSpan a, CSpan b, Span r, size_t L, const uint32_t* image = nullptr)
{
    bool square = !image && a.data() == b.data() && a.size() == b.size();
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
    bool r3 = (L & (L - 1)) != 0;   // 3·2^k: a radix-3 level on top
    size_t M = r3 ? L / 3 : L;
    for (int k = 0; k < 3; k++)
    {
        const Tables& t = Get(k, M);
        uint32_t* x = buf + k * L;
        std::unique_ptr<R3Constants> fk, ik;
        if (r3)
        {
            fk = std::make_unique<R3Constants>(k, t, L, false);
            ik = std::make_unique<R3Constants>(k, t, L, true);
        }
        NTT(Load)(a.data(), (int64_t)a.size(), x, (int64_t)L, t.p);
        Forward3(x, M, fk.get(), t);
        if (image)
        {
            NTT_V(Point)(x, image + k * L, (int64_t)L, t.p, t.pinv);   // b's transform kept
        }
        else if (square)
        {
            NTT_V(Point)(x, x, (int64_t)L, t.p, t.pinv);
        }
        else
        {
            NTT(Load)(b.data(), (int64_t)b.size(), y, (int64_t)L, t.p);
            Forward3(y, M, fk.get(), t);
            NTT_V(Point)(x, y, (int64_t)L, t.p, t.pinv);
        }
        Inverse3(x, M, ik.get(), t);
        // × 2^32·L^-1 (a Montgomery product by 2^64·L^-1): the pointwise products left one 2^-32
        uint64_t c = (uint64_t(1) << 32) % t.p;
        c = c * c % t.p * PowMod(L, t.p - 2, t.p) % t.p;
        NTT_V(Scale)(x, (int64_t)((nd + 7) & ~size_t(7)), t.p, (uint32_t)c, (uint32_t)c * t.pinv);
    }
    return NTT_CRT(buf, buf + L, buf + 2 * L, (int64_t)nd, r.data(), (int64_t)len, crt.k);
}

bool Fits(size_t len) { return 2 * len <= (size_t(1) << MaxLog); }

// + carry modulo β^m - 1 (the end-around carry), β^m - 1 written as 0
void WrapCarry(Span r, uint64_t carry)
{
    size_t m = r.size();
    while (carry != 0)
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
    std::fill(r.begin(), r.end(), 0);
}

}  // namespace

bool Karatsuba::NttFits(size_t len) { return Available() && Fits(len); }

bool Karatsuba::NttWorth(size_t len, bool full)
{
    if (!NttFits(len)) return false;
    size_t nd = 2 * len;
    return full || 10 * nd >= 9 * Length(nd);
}

bool Karatsuba::NttMultiply(CSpan a, CSpan b, Span r)
{
    if (r.size() != a.size() + b.size()) throw std::invalid_argument("r.size() must be a.size() + b.size()");
    if (a.empty() || b.empty() || !Available() || !Fits(r.size())) return false;
    NttConvolve(a, b, r, Length(2 * r.size()));
    return true;
}

size_t Karatsuba::NttCyclicLength(size_t minWords)
{
    if (!Available()) return 0;
    size_t m = Length(2 * std::max(minWords, size_t(8))) / 2;   // 2^a or 3·2^a
    return Fits(m) ? m : 0;
}

void Karatsuba::NttMultiplyCyclic(CSpan a, CSpan b, Span r)
{
    WrapCarry(r, NttConvolve(a, b, r, 2 * r.size()));
}

Karatsuba::NttImage Karatsuba::NttPrepare(CSpan b, size_t words, bool cyclic)
{
    NttImage img;
    if (!Available() || !Fits(words)) return img;
    size_t L = cyclic ? 2 * words : Length(2 * words);
    img.L = L;
    img.words = words;
    img.cyclic = cyclic;
    img.data.resize(3 * L);
    bool r3 = (L & (L - 1)) != 0;
    size_t M = r3 ? L / 3 : L;
    for (int k = 0; k < 3; k++)
    {
        const Tables& t = Get(k, M);
        uint32_t* y = img.data.data() + k * L;
        std::unique_ptr<R3Constants> fk;
        if (r3) fk = std::make_unique<R3Constants>(k, t, L, false);
        NTT(Load)(b.data(), (int64_t)b.size(), y, (int64_t)L, t.p);
        Forward3(y, M, fk.get(), t);
    }
    return img;
}

void Karatsuba::NttMultiplyImage(CSpan a, const NttImage& b, Span r)
{
    uint64_t carry = NttConvolve(a, CSpan(), r, b.L, b.data.data());
    if (b.cyclic) WrapCarry(r, carry);
}

}  // namespace fullint
