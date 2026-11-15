// Port of FullInt.Core/Fft.cs: Schönhage–Strassen multiplication.
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
#include <fullint/Karatsuba.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>

namespace fullint {

namespace {

CSpan Trim(CSpan v)
{
    size_t z = 0;
    while (z + 1 < v.size() && v[z] == 0) z++;
    return v.subspan(z);
}

bool IsZero(CSpan v)
{
    for (uint64_t x : v)
        if (x != 0) return false;
    return true;
}

struct FftContext
{
    int K, LogK, NBits;
    size_t N1;
    Span P, T1, T2;
};

// dst = (x + y) mod p   (x, y in [0, p))
void AddMod(const FftContext& c, CSpan x, CSpan y, Span dst)
{
    FullIntCore::Add(x, y, dst);
    if (dst[0] != 0 && FullIntCore::Compare(dst, c.P) >= 0) FullIntCore::Sub(dst, c.P, dst);
}

// dst = (x - y) mod p
void SubMod(const FftContext& c, CSpan x, CSpan y, Span dst)
{
    if (FullIntCore::Sub(x, y, dst) < 0) FullIntCore::Sub(c.P, dst, dst);
}

// dst = x·2^s mod p, 0 <= s < 2N; dst must not be x
void MulPow2Mod(const FftContext& c, CSpan x, int s, Span dst)
{
    size_t n1 = c.N1, w = n1 - 1;
    bool negate = false;
    if (s >= c.NBits) { s -= c.NBits; negate = true; }
    size_t ws = (size_t)s >> 6;
    int bs = s & 63;
    Span t = c.T2;                                       // n1 + 1 words: x << bs
    if (bs == 0) { t[0] = 0; std::copy(x.begin(), x.end(), t.begin() + 1); }
    else FullIntCore::ShiftLeft(x, bs, t);
    // x·2^s = t·B^ws = t_lo·B^ws + t_hi·2^N == t_lo·B^ws - t_hi
    //   t_lo: the low w - ws words of t, t_hi: the ws + 2 words above them
    dst[0] = 0;
    std::copy(t.begin() + ws + 2, t.begin() + ws + 2 + (w - ws), dst.begin() + 1);
    std::memset(dst.data() + 1 + (w - ws), 0, ws * sizeof(uint64_t));
    CSpan hi = Trim(CSpan(t).subspan(0, ws + 2));
    if (!(hi.size() == 1 && hi[0] == 0) && FullIntCore::Sub(dst, hi, dst) < 0)
        FullIntCore::Sub(c.P, dst, dst);
    if (negate && !IsZero(dst))
        FullIntCore::Sub(c.P, dst, dst);
}

// x = prod mod p (prod: 2·n1 words)
void ReduceProduct(const FftContext& c, CSpan prod, Span x)
{
    size_t n1 = c.N1, w = n1 - 1;
    x[0] = 0;
    std::copy(prod.begin() + (2 * n1 - w), prod.end(), x.begin() + 1);
    CSpan hi = Trim(prod.subspan(0, 2 * n1 - w));
    if (!(hi.size() == 1 && hi[0] == 0) && FullIntCore::Sub(x, hi, x) < 0)
        FullIntCore::Sub(c.P, x, x);
}

// decimation in frequency, natural order in, bit-reversed out
void ForwardDif(const FftContext& c, Span v)
{
    size_t n1 = c.N1;
    for (int half = c.K / 2; half >= 1; half /= 2)
    {
        int step = c.NBits / half;                       // w_(2·half) = 2^(N / half)
        for (int start = 0; start < c.K; start += 2 * half)
            for (int j = 0; j < half; j++)
            {
                Span u = v.subspan((size_t)(start + j) * n1, n1), x = v.subspan((size_t)(start + j + half) * n1, n1);
                SubMod(c, u, x, c.T1);
                AddMod(c, u, x, u);
                if (j == 0) std::copy(c.T1.begin(), c.T1.end(), x.begin());
                else MulPow2Mod(c, c.T1, j * step, x);
            }
    }
}

// decimation in time with w^-1, bit-reversed order in, natural out (without the 1/K)
void InverseDit(const FftContext& c, Span v)
{
    size_t n1 = c.N1;
    for (int half = 1; half < c.K; half *= 2)
    {
        int step = c.NBits / half;
        for (int start = 0; start < c.K; start += 2 * half)
            for (int j = 0; j < half; j++)
            {
                Span u = v.subspan((size_t)(start + j) * n1, n1), x = v.subspan((size_t)(start + j + half) * n1, n1);
                if (j == 0) std::copy(x.begin(), x.end(), c.T1.begin());
                else MulPow2Mod(c, x, 2 * c.NBits - j * step, c.T1);
                SubMod(c, u, c.T1, x);
                AddMod(c, u, c.T1, u);
            }
    }
}

// the pieces of x (M words each, from the low end) into K vectors of n1 words, zero-padded
void Split(CSpan x, size_t m, int K, size_t n1, Span v)
{
    std::memset(v.data(), 0, v.size_bytes());
    size_t lx = x.size();
    for (int i = 0; i < K; i++)
    {
        if ((size_t)i * m >= lx) break;
        size_t end = lx - (size_t)i * m;
        size_t start = end > m ? end - m : 0;
        std::copy(x.begin() + start, x.begin() + end, v.begin() + ((size_t)i + 1) * n1 - (end - start));
    }
}

}  // namespace

// k, M, N for an la x lb product: the smallest estimated cost (or forceK = k for tests)
void Karatsuba::ChooseFft(size_t la, size_t lb, int forceK, int& k, size_t& m, int& nBits)
{
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
        // ns, measured (Zen 3): an n-word product ~2.3·n^1.55 from 32 words, a butterfly ~40 + 4·n
        double mulCost = n1 < 32 ? 0.45 * n1 * n1 : 2.3 * std::pow(n1, 1.55);
        double cost = (double)K * mulCost + 3.0 * (double)K * kk * (40 + 4.0 * n1);
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
    FftMul(a, b, r, Threshold, Toom3Threshold, Toom4Threshold, FftThreshold, k);
}

void Karatsuba::FftMul(CSpan a, CSpan b, Span r, int threshold, int toom3Threshold, int toom4Threshold, int fftThreshold, int forceK)
{
    size_t la = a.size(), lb = b.size(), len = la + lb;
    int k, nBits;
    size_t m;
    ChooseFft(la, lb, forceK, k, m, nBits);
    int K = 1 << k;
    size_t n1 = (size_t)nBits / 64 + 1;

    size_t words = 2 * (size_t)K * n1 + 5 * n1 + 1 + ScratchLength(n1, n1);
    std::unique_ptr<uint64_t[]> buf = std::make_unique_for_overwrite<uint64_t[]>(words);
    Span all(buf.get(), words);
    Span va = all.subspan(0, K * n1), vb = all.subspan(K * n1, K * n1);
    size_t o = 2 * (size_t)K * n1;
    Span p = all.subspan(o, n1), t1 = all.subspan(o + n1, n1), t2 = all.subspan(o + 2 * n1, n1 + 1);
    Span prod = all.subspan(o + 3 * n1 + 1, 2 * n1);
    Span rest = all.subspan(o + 5 * n1 + 1);
    std::memset(p.data(), 0, p.size_bytes());
    p[0] = 1;
    p[n1 - 1] = 1;                                        // p = 2^N + 1

    Split(a, m, K, n1, va);
    Split(b, m, K, n1, vb);

    FftContext ctx{ K, k, nBits, n1, p, t1, t2 };
    ForwardDif(ctx, va);
    ForwardDif(ctx, vb);

    for (int i = 0; i < K; i++)
    {
        Span x = va.subspan((size_t)i * n1, n1), y = vb.subspan((size_t)i * n1, n1);
        Mul(x, y, prod, rest, threshold, toom3Threshold, toom4Threshold, fftThreshold);
        ReduceProduct(ctx, prod, x);
    }

    InverseDit(ctx, va);

    // r = sum (c_i / K)·B^(M·i)
    std::memset(r.data(), 0, r.size_bytes());
    for (int i = 0; i < K && (size_t)i * m < len; i++)
    {
        Span c = va.subspan((size_t)i * n1, n1);
        MulPow2Mod(ctx, c, 2 * nBits - k, t1);           // / K
        CSpan v = Trim(t1);
        if (v.size() == 1 && v[0] == 0) continue;
        Span up = r.subspan(0, len - (size_t)i * m);
        FullIntCore::Add(up, v, up);
    }
}

}  // namespace fullint
