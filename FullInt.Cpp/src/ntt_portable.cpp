// The number-theoretic transform kernels in portable C++ (AArch64): the contracts of
// FullInt.Asm/Sources/x64/NttAvx2.asm, one value at a time. Ntt.cpp calls them where the AVX2
// kernels are not there; they are also the reference for the AArch64 NEON kernels.
//
// Three primes p < 2^30 (4p < 2^32). The values stay lazy in [0, 2p). A product with a twiddle
// w (Montgomery form: w·2^32 mod p) and wq = w·p^-1 mod 2^32:
//   m = a·wq mod 2^32;  r = hi(a·w) - hi(m·p) = a·w·2^-32 mod p in (-p, p);  r + p in [0, 2p)
// (the low halves of a·w and m·p are equal), for a < 4p and a·w < 4p².
//
// The transform levels: decimation in frequency (x, y) -> (x + y, (x - y)·w^j), in time
// (x, y) -> (x + y·w^j, x - y·w^j), W[h + j] = w_2h^j for every level h - on AArch64 in Shoup's
// form (w itself and floor(w·2^32 / p): MulS), the radix-3 constants and the pointwise product
// in Montgomery's (MulW). Dif2 / Dit2 run two
// levels, Dif3 / Dit3 the last (first) three - here level by level: the permuted order the
// forward transform leaves is undone by the inverse, the pointwise product does not care.
#include <fullint/kernels.h>

#if FULLINT_ARM64

namespace {

inline uint32_t MulW(uint32_t a, uint32_t w, uint32_t wq, uint32_t p)
{
    uint32_t m = a * wq;
    int64_t r = (int64_t)(((uint64_t)a * w) >> 32) - (int64_t)(((uint64_t)m * p) >> 32);
    return (uint32_t)(r + p);
}

// a·w mod p in [0, 2p) with a twiddle in Shoup's form (Ntt.cpp's tables on AArch64: w itself,
// wq = floor(w·2^32 / p)), any a < 2^32
inline uint32_t MulS(uint32_t a, uint32_t w, uint32_t wq, uint32_t p)
{
    uint32_t q = (uint32_t)(((uint64_t)a * wq) >> 32);
    return a * w - q * p;
}

inline uint32_t Red(uint32_t v, uint32_t p2) { return v >= p2 ? v - p2 : v; }   // [0, 4p) -> [0, 2p)

void DifLevel(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    uint32_t p2 = 2 * p;
    for (int64_t b = 0; b < L; b += 2 * h)
        for (int64_t j = 0; j < h; j++)
        {
            uint32_t u = x[b + j], v = x[b + j + h];
            x[b + j] = Red(u + v, p2);
            x[b + j + h] = MulS(u - v + p2, w[h + j], wq[h + j], p);
        }
}

void DitLevel(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    uint32_t p2 = 2 * p;
    for (int64_t b = 0; b < L; b += 2 * h)
        for (int64_t j = 0; j < h; j++)
        {
            uint32_t u = x[b + j], v = MulS(x[b + j + h], w[h + j], wq[h + j], p);
            x[b + j] = Red(u + v, p2);
            x[b + j + h] = Red(u - v + p2, p2);
        }
}

}  // namespace

void Fullint64NttDif1WIN_portable(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DifLevel(x, L, h, w, wq, p);
}

void Fullint64NttDif2WIN_portable(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DifLevel(x, L, h, w, wq, p);
    DifLevel(x, L, h / 2, w, wq, p);
}

void Fullint64NttDif3WIN_portable(uint32_t* x, int64_t L, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DifLevel(x, L, 4, w, wq, p);
    DifLevel(x, L, 2, w, wq, p);
    DifLevel(x, L, 1, w, wq, p);
}

void Fullint64NttDit1WIN_portable(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DitLevel(x, L, h, w, wq, p);
}

void Fullint64NttDit2WIN_portable(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DitLevel(x, L, h / 2, w, wq, p);
    DitLevel(x, L, h, w, wq, p);
}

void Fullint64NttDit3WIN_portable(uint32_t* x, int64_t L, const uint32_t* w, const uint32_t* wq, uint32_t p)
{
    DitLevel(x, L, 1, w, wq, p);
    DitLevel(x, L, 2, w, wq, p);
    DitLevel(x, L, 4, w, wq, p);
}

// The radix-3 level of a transform of N = 3M values (NttAvx2.asm has the formulas). k: nine
// vectors of 8 values - [0] w^0..7, [1] w^0,2,..,14, [6] ω, [7] its q, [8] p^-1 (Montgomery
// form); the twiddles w^j, w^2j made as the loop goes, kept below p.
void Fullint64NttR3DifWIN_portable(uint32_t* x, int64_t M, const uint32_t* k, uint32_t p)
{
    uint32_t p2 = 2 * p, pinv = k[64], om = k[48], omq = k[56];
    uint32_t w1 = k[1], w1q = w1 * pinv, w2 = k[9], w2q = w2 * pinv;
    uint32_t c1 = k[0], c2 = k[8];   // w^j, w^2j (j = 0)
    for (int64_t j = 0; j < M; j++)
    {
        uint32_t x0 = x[j], x1 = x[j + M], x2 = x[j + 2 * M];
        uint32_t u = MulW(x1 - x2 + p2, om, omq, p);                // ω·(x1 - x2)
        x[j] = Red(Red(x0 + x1, p2) + x2, p2);
        x[j + M] = MulW(Red(x0 - x2 + p2, p2) + u, c1, c1 * pinv, p);
        x[j + 2 * M] = MulW(Red(x0 - x1 + p2, p2) - u + p2, c2, c2 * pinv, p);
        c1 = MulW(c1, w1, w1q, p);
        c2 = MulW(c2, w2, w2q, p);
        if (c1 >= p) c1 -= p;
        if (c2 >= p) c2 -= p;
    }
}

void Fullint64NttR3DitWIN_portable(uint32_t* x, int64_t M, const uint32_t* k, uint32_t p)
{
    uint32_t p2 = 2 * p, pinv = k[64], om = k[48], omq = k[56];
    uint32_t w1 = k[1], w1q = w1 * pinv, w2 = k[9], w2q = w2 * pinv;
    uint32_t c1 = k[0], c2 = k[8];
    for (int64_t j = 0; j < M; j++)
    {
        uint32_t y0 = x[j];
        uint32_t a = MulW(x[j + M], c1, c1 * pinv, p), b = MulW(x[j + 2 * M], c2, c2 * pinv, p);
        uint32_t u = MulW(a - b + p2, om, omq, p);                  // ω·(y1' - y2')
        x[j] = Red(Red(y0 + a, p2) + b, p2);
        x[j + M] = Red(Red(y0 - b + p2, p2) + u, p2);
        x[j + 2 * M] = Red(Red(y0 - a + p2, p2) - u + p2, p2);
        c1 = MulW(c1, w1, w1q, p);
        c2 = MulW(c2, w2, w2q, p);
        if (c1 >= p) c1 -= p;
        if (c2 >= p) c2 -= p;
    }
}

void Fullint64NttPointWIN_portable(uint32_t* x, const uint32_t* y, int64_t n, uint32_t p, uint32_t pinv)
{
    for (int64_t i = 0; i < n; i++) x[i] = MulW(x[i], y[i], y[i] * pinv, p);
}

void Fullint64NttScaleWIN_portable(uint32_t* x, int64_t n, uint32_t p, uint32_t c, uint32_t cq)
{
    for (int64_t i = 0; i < n; i++)
    {
        uint32_t v = MulW(x[i], c, cq, p);
        x[i] = v >= p ? v - p : v;
    }
}

// big-endian words -> little-endian 32-bit digits in [0, 2p), zeros up to L
void Fullint64NttLoadWIN_portable(const uint64_t* a, int64_t la, uint32_t* x, int64_t L, uint32_t p)
{
    for (int64_t i = 0; i < la; i++)
    {
        uint64_t w = a[la - 1 - i];
        uint32_t lo = (uint32_t)w, hi = (uint32_t)(w >> 32), p2 = 2 * p;   // < 2^32 < 5p:
        lo = lo >= p2 ? lo - p2 : lo;                                     // twice -2p -> [0, 2p)
        hi = hi >= p2 ? hi - p2 : hi;
        x[2 * i] = lo >= p2 ? lo - p2 : lo;
        x[2 * i + 1] = hi >= p2 ? hi - p2 : hi;
    }
    for (int64_t i = 2 * la; i < L; i++) x[i] = 0;
}

// a·b mod p by Shoup's method (bs = floor(b·2^32 / p), b < p, a < 2^32): in [0, p)
inline uint64_t MulShoup(uint64_t a, uint64_t b, uint64_t bs, uint64_t p)
{
    uint64_t q = (a * bs) >> 32;
    uint64_t r = a * b - q * p;   // in [0, 2p)
    return r >= p ? r - p : r;
}

// Garner over the three residues (each in [0, p)) of nd = 2·len digits: v1 = x0,
// v2 = (x1 - v1)·p1^-1 mod p2, v3 = (x2 - v1 - v2·p1)·(p1p2)^-1 mod p3 (products by Shoup's
// method, the reductions by subtraction: p1 < 2·p2, p1 < 2·p3); the digit's value
// v1 + p1·v2 + p1p2·v3 (< 2^90) added into the running sum, its low 32 bits out; two digits a
// word, from the low end of r up. Returns the carry out of the top word.
uint64_t Fullint64NttCrtWIN_portable(const uint32_t* x0, const uint32_t* x1, const uint32_t* x2, int64_t nd,
                                     uint64_t* r, int64_t len, const uint64_t* k)
{
    uint64_t p1 = k[0], p2 = k[1], p3 = k[2], inv12 = k[3], inv12s = k[4], p1m3 = k[5], p1m3s = k[6];
    uint64_t inv123 = k[7], inv123s = k[8], p1p2 = k[9];
    unsigned __int128 acc = 0;
    uint64_t low = 0;
    for (int64_t i = 0; i < nd; i++)
    {
        uint64_t v1 = x0[i];
        uint64_t v1m2 = v1 >= p2 ? v1 - p2 : v1;
        uint64_t d = x1[i] + p2 - v1m2;                       // (0, 2·p2)
        d = d >= p2 ? d - p2 : d;
        uint64_t v2 = MulShoup(d, inv12, inv12s, p2);
        uint64_t v1m3 = v1 >= p3 ? v1 - p3 : v1;
        uint64_t t = x2[i] + 2 * p3 - v1m3 - MulShoup(v2, p1m3, p1m3s, p3);   // (0, 3·p3)
        t = t >= p3 ? t - p3 : t;
        t = t >= p3 ? t - p3 : t;
        uint64_t v3 = MulShoup(t, inv123, inv123s, p3);
        acc += v1 + p1 * v2 + (unsigned __int128)p1p2 * v3;
        uint64_t digit = (uint32_t)acc;
        acc >>= 32;
        if (i & 1) r[len - 1 - i / 2] = digit << 32 | low;
        else low = digit;
    }
    return (uint64_t)acc;
}

#endif
