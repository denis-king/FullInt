// AArch64: a NEON schoolbook product for the narrow-multiplier cores (Neoverse-N1: a scalar
// 64 x 64 -> 128 product takes 7 cycles; NEON umull two 32 x 32 a cycle) - an experiment, kept as
// a probe. The words in 28-bit digits (7 words = 448 bits = 16 digits): a digit product is below
// 2^56, so a 64-bit lane sums up to 256 of them without a carry - the columns accumulate by
// umlal (16 a pass in registers), the carries go once at the end. Checked against the asm
// Fullint64MultWIN and timed against it.
//
// Result (Neoverse-N1, 2026-11-25): of the scalar kernel 1.04 at 16 x 16 words, 0.70 at 32, 0.54
// at 64, 0.48 at 96 (the inner loop ~0.65 cycles a digit product, the bound 0.5) - no better
// than a Karatsuba level over the scalar kernel there (from 12 words: 0.68 at 32, 0.53 at 64).
// As the narrow cores' basecase (Karatsuba from 24 / 32 / 48 over it) the products were 1.05-1.24
// times slower than Karatsuba from 12 over the scalar kernel: not adopted.
//   fullint_neon_mul_probe [--cpu K]
#include <fullnumerics/kernels.h>

#include <arm_neon.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <random>
#include <vector>

#include <sched.h>

namespace {

constexpr uint64_t Mask28 = (1ull << 28) - 1;

// big-endian words (index 0 the top) -> little-endian 28-bit digits; returns the digit count
int ToDigits(const uint64_t* w, int n, uint32_t* d)
{
    int k = 0;
    unsigned __int128 acc = 0;
    int bits = 0;
    for (int i = n - 1; i >= 0; i--)
    {
        acc |= (unsigned __int128)w[i] << bits;
        bits += 64;
        while (bits >= 28)
        {
            d[k++] = (uint32_t)(acc & Mask28);
            acc >>= 28;
            bits -= 28;
        }
    }
    if (bits > 0) d[k++] = (uint32_t)acc;
    return k;
}

// c[0 .. da + db - 2] = the column sums of a·b (digits), by umlal: 8 accumulators (16 columns)
// a pass; column k needs b[k - i], consecutive in k - b read straight (zero-padded: 16 digits
// before and after)
void Columns(const uint32_t* a, int da, const uint32_t* b, int db, uint64_t* c)
{
    int nc = da + db - 1;
    for (int k0 = 0; k0 < nc; k0 += 16)
    {
        uint64x2_t acc0 = vdupq_n_u64(0), acc1 = acc0, acc2 = acc0, acc3 = acc0, acc4 = acc0, acc5 = acc0, acc6 = acc0, acc7 = acc0;
        int iLo = std::max(0, k0 - (db - 1)), iHi = std::min(da - 1, k0 + 15);
        for (int i = iLo; i <= iHi; i++)
        {
            uint32x2_t ai = vdup_n_u32(a[i]);
            const uint32_t* w = b + (k0 - i);   // b[k0 - i .. k0 - i + 15]
            uint32x4_t w0 = vld1q_u32(w), w1 = vld1q_u32(w + 4), w2 = vld1q_u32(w + 8), w3 = vld1q_u32(w + 12);
            acc0 = vmlal_u32(acc0, vget_low_u32(w0), ai);
            acc1 = vmlal_high_u32(acc1, w0, vcombine_u32(ai, ai));
            acc2 = vmlal_u32(acc2, vget_low_u32(w1), ai);
            acc3 = vmlal_high_u32(acc3, w1, vcombine_u32(ai, ai));
            acc4 = vmlal_u32(acc4, vget_low_u32(w2), ai);
            acc5 = vmlal_high_u32(acc5, w2, vcombine_u32(ai, ai));
            acc6 = vmlal_u32(acc6, vget_low_u32(w3), ai);
            acc7 = vmlal_high_u32(acc7, w3, vcombine_u32(ai, ai));
        }
        uint64_t* o = c + k0;   // c has room for 16 past the last column
        vst1q_u64(o, acc0); vst1q_u64(o + 2, acc1); vst1q_u64(o + 4, acc2); vst1q_u64(o + 6, acc3);
        vst1q_u64(o + 8, acc4); vst1q_u64(o + 10, acc5); vst1q_u64(o + 12, acc6); vst1q_u64(o + 14, acc7);
    }
}

// the columns -> big-endian words of r (lenR words, the product's digits carried)
void Pack(const uint64_t* c, int nc, uint64_t* r, int lenR)
{
    unsigned __int128 acc = 0;
    int bits = 0, w = lenR - 1;
    uint64_t carry = 0;
    for (int k = 0; k < nc; k++)
    {
        uint64_t v = c[k] + carry;
        carry = v >> 28;
        acc |= (unsigned __int128)(v & Mask28) << bits;
        bits += 28;
        if (bits >= 64 && w >= 0)
        {
            r[w--] = (uint64_t)acc;
            acc >>= 64;
            bits -= 64;
        }
    }
    while (w >= 0)
    {
        while (carry != 0 && bits < 64)
        {
            acc |= (unsigned __int128)(carry & Mask28) << bits;
            carry >>= 28;
            bits += 28;
        }
        r[w--] = (uint64_t)acc;
        acc >>= 64;
        bits = std::max(0, bits - 64);
    }
}

struct Scratch
{
    uint32_t da[512], db[512 + 64];
    uint64_t c[1100];
};

void MulNeon(const uint64_t* a, const uint64_t* b, uint64_t* r, int la, int lb, Scratch& s)
{
    int na = ToDigits(a, la, s.da);
    uint32_t* bd = s.db + 32;                        // 32 zero digits before b's, 32 after
    int nb = ToDigits(b, lb, bd);
    std::memset(s.db, 0, 32 * 4);
    std::memset(bd + nb, 0, 32 * 4);
    Columns(s.da, na, bd, nb, s.c);
    Pack(s.c, na + nb - 1, r, la + lb);
}

double Time(const std::function<void()>& f)
{
    auto once = [&](int n) {
        auto t0 = std::chrono::steady_clock::now();
        for (int i = 0; i < n; i++) f();
        return std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / n;
    };
    int n = std::max(1, (int)(2e7 / std::max(once(1), 1.0)));
    double best = 1e300;
    for (int r = 0; r < 7; r++) best = std::min(best, once(n));
    return best;
}

}  // namespace

int main(int argc, char** argv)
{
    int cpu = -1;
    for (int i = 1; i < argc; i++)
        if (std::strcmp(argv[i], "--cpu") == 0 && i + 1 < argc) cpu = std::atoi(argv[++i]);
    if (cpu >= 0)
    {
        cpu_set_t cpus;
        CPU_ZERO(&cpus);
        CPU_SET(cpu, &cpus);
        sched_setaffinity(0, sizeof cpus, &cpus);
    }
    std::mt19937_64 rng(1);
    static Scratch s;
    int bad = 0;
    for (int la = 1; la <= 40; la++)
        for (int lb = 1; lb <= 40; lb++)
            for (int kind = 0; kind < 3; kind++)
            {
                std::vector<uint64_t> a(la), b(lb), r1(la + lb), r2(la + lb);
                for (auto& x : a) x = kind == 0 ? rng() : kind == 1 ? ~0ull : (rng() & 1 ? ~0ull : 0);
                for (auto& x : b) x = kind == 0 ? rng() : kind == 1 ? ~0ull : (rng() & 1 ? ~0ull : 0);
                Fullint64MultWIN(a.data(), b.data(), r1.data(), la, lb, la + lb);
                MulNeon(a.data(), b.data(), r2.data(), la, lb, s);
                if (r1 != r2 && ++bad <= 5) std::printf("MISMATCH %d x %d kind %d\n", la, lb, kind);
            }
    std::printf("check: %s\n", bad ? "FAILED" : "OK (1..40 x 1..40, random / ones / mixed)");
    std::printf("%6s %12s %12s %8s\n", "n x n", "asm ns", "neon ns", "neon/asm");
    for (int n : { 4, 8, 12, 16, 24, 32, 48, 64, 96 })
    {
        std::vector<uint64_t> a(n), b(n), r(2 * n);
        for (auto& x : a) x = rng();
        for (auto& x : b) x = rng();
        double t1 = Time([&] { Fullint64MultWIN(a.data(), b.data(), r.data(), n, n, 2 * n); });
        double t2 = Time([&] { MulNeon(a.data(), b.data(), r.data(), n, n, s); });
        std::printf("%6d %12.1f %12.1f %8.2f\n", n, t1, t2, t2 / t1);
    }
    return bad ? 1 : 0;
}
