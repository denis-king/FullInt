// Kernel against kernel: the FullInt asm kernels against the GMP mpn layer, same process,
// same inputs, both linked statically - no interop and no memory management on either side.
//
// It shows where the asm itself (or the algorithm in it) stands against GMP; the API level
// (FullInt64 vs mpz_class, and the .NET package) is a separate benchmark.
//
// Note: our Mult/Div/ToDec/FromDec kernels are the basecase (schoolbook) algorithms; the C#
// layer adds Karatsuba/Toom-3 on top. GMP's mpn_mul_n / tdiv_qr / get_str / set_str switch to
// subquadratic algorithms by themselves, so on large sizes these rows compare algorithms.
//
// Limb order: ours index 0 = most significant word, GMP index 0 = least significant word;
// the inputs are the same numbers, stored reversed. Every (op, size) is checked against GMP
// once before it is timed.
//
// usage: fullint_bench_kernels [--ops add,sub,...] [--max N] [--csv file]
#include <fullint/kernels.h>
#include <gmp.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <random>
#include <string>
#include <vector>

#define NOMINMAX
#include <windows.h>

using u64 = uint64_t;
using Vec = std::vector<u64>;

static_assert(sizeof(mp_limb_t) == 8, "GMP built with 64-bit limbs is required (x64)");

namespace {

std::mt19937_64 rng(20261114);

// n random words (GMP order: index 0 = least significant), top word nonzero
Vec randomLimbs(int n)
{
    Vec v(n);
    for (auto& w : v) w = rng();
    if (v[n - 1] == 0) v[n - 1] = 1;
    return v;
}

Vec reversed(const Vec& v) { return Vec(v.rbegin(), v.rend()); }

// ns per call: the batch is sized to run >= targetNs, min over rounds, ours and GMP interleaved
struct Timer
{
    static long long now()
    {
        return std::chrono::duration_cast<std::chrono::nanoseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    static long long calibrate(const std::function<void()>& f, long long targetNs)
    {
        long long iters = 1;
        for (;;)
        {
            long long t0 = now();
            for (long long i = 0; i < iters; i++) f();
            long long dt = now() - t0;
            if (dt >= targetNs / 4 || iters >= (1LL << 30))
            {
                double per = double(std::max(dt, 1LL)) / iters;
                return std::max(1LL, (long long)(targetNs / per));
            }
            iters *= 4;
        }
    }

    static double batch(const std::function<void()>& f, long long iters)
    {
        long long t0 = now();
        for (long long i = 0; i < iters; i++) f();
        return double(now() - t0) / iters;
    }
};

struct Row { std::string op; int n; double ours; double gmp; };

std::vector<Row> rows;
volatile int sink;   // results that would otherwise be dead (mpn_cmp is inline in gmp.h)
FILE* csv = nullptr;

void run(const char* op, int n, const std::function<void()>& ours, const std::function<void()>& gmp)
{
    const long long target = 3'000'000;     // 3 ms per batch
    long long io = Timer::calibrate(ours, target);
    long long ig = Timer::calibrate(gmp, target);
    double perCall = Timer::batch(ours, 1);
    int rounds = perCall > 20e6 ? 3 : perCall > 2e6 ? 5 : 11;

    double bo = 1e300, bg = 1e300;
    for (int r = 0; r < rounds; r++)
    {
        bo = std::min(bo, Timer::batch(ours, io));
        bg = std::min(bg, Timer::batch(gmp, ig));
    }
    rows.push_back({ op, n, bo, bg });
    // ratio > 1: we are faster
    std::printf("%-8s %6d  %12.1f  %12.1f   %5.2fx %s\n", op, n, bo, bg, bg / bo, bg >= bo ? "" : "  <");
    std::fflush(stdout);
    if (csv) std::fprintf(csv, "%s,%d,%.2f,%.2f\n", op, n, bo, bg);
}

[[noreturn]] void fail(const char* op, int n, const char* what)
{
    std::printf("MISMATCH %s n=%d: %s\n", op, n, what);
    std::exit(1);
}

std::string stripZeros(std::string s)
{
    size_t i = s.find_first_not_of('0');
    return i == std::string::npos ? "0" : s.substr(i);
}

Vec stripLimbs(Vec v)   // GMP order: drops high zero words
{
    while (v.size() > 1 && v.back() == 0) v.pop_back();
    return v;
}

Vec stripOurs(const u64* p, int len)   // our order -> GMP order, high zero words dropped
{
    Vec v(p, p + len);
    return stripLimbs(reversed(v));
}

bool want(const std::vector<std::string>& ops, const char* op)
{
    return ops.empty() || std::find(ops.begin(), ops.end(), op) != ops.end();
}

}  // namespace

int main(int argc, char** argv)
{
    std::vector<std::string> ops;
    int maxN = 16384;
    for (int i = 1; i < argc; i++)
    {
        std::string a = argv[i];
        if (a == "--ops" && i + 1 < argc)
        {
            std::string s = argv[++i];
            for (size_t p = 0; p <= s.size();)
            {
                size_t q = s.find(',', p);
                if (q == std::string::npos) q = s.size();
                if (q > p) ops.push_back(s.substr(p, q - p));
                p = q + 1;
            }
        }
        else if (a == "--max" && i + 1 < argc) maxN = std::atoi(argv[++i]);
        else if (a == "--csv" && i + 1 < argc) csv = std::fopen(argv[++i], "w");
        else { std::printf("usage: %s [--ops add,sub,addn,subn,subcmpn,mul,muladx,div,div1,shl,shr,cmp,todec,fromdec] [--max N] [--csv file]\n", argv[0]); return 2; }
    }
    if (csv) std::fprintf(csv, "op,n,fullint_ns,gmp_ns\n");

    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadAffinityMask(GetCurrentThread(), 1ull << 2);

    const int sizes[] = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };

    std::printf("GMP %s, ns per call (min over rounds); ratio = GMP / FullInt, > 1 means FullInt is faster\n", gmp_version);
    std::printf("%-8s %6s  %12s  %12s   %s\n", "op", "words", "FullInt", "GMP", "ratio");

    for (int n : sizes)
    {
        if (n > maxN) break;

        Vec ga = randomLimbs(n), gb = randomLimbs(n);
        Vec oa = reversed(ga), ob = reversed(gb);

        if (want(ops, "add"))
        {
            Vec orr(n), grr(n);
            u64 oc = Fullint64AddWIN(oa.data(), ob.data(), orr.data(), n, n, n);
            u64 gc = mpn_add_n(grr.data(), ga.data(), gb.data(), n);
            if (oc != gc || reversed(orr) != grr) fail("add", n, "sum");
            run("add", n,
                [&] { Fullint64AddWIN(oa.data(), ob.data(), orr.data(), n, n, n); },
                [&] { mpn_add_n(grr.data(), ga.data(), gb.data(), n); });
        }

        if (want(ops, "sub"))
        {
            Vec ga2 = ga, gb2 = gb;
            if (mpn_cmp(ga2.data(), gb2.data(), n) < 0) std::swap(ga2, gb2);
            Vec oa2 = reversed(ga2), ob2 = reversed(gb2);
            Vec orr(n), grr(n);
            Fullint64SubWIN(oa2.data(), ob2.data(), orr.data(), n, n, n);
            mpn_sub_n(grr.data(), ga2.data(), gb2.data(), n);
            if (reversed(orr) != grr) fail("sub", n, "difference");
            run("sub", n,
                [&] { Fullint64SubWIN(oa2.data(), ob2.data(), orr.data(), n, n, n); },
                [&] { mpn_sub_n(grr.data(), ga2.data(), gb2.data(), n); });
        }

        if (want(ops, "addn"))   // the equal-length kernel the engine's add calls (AddSubN.asm)
        {
            Vec orr(n), grr(n);
            u64 oc = Fullint64AddNWIN(oa.data(), ob.data(), orr.data(), n);
            u64 gc = mpn_add_n(grr.data(), ga.data(), gb.data(), n);
            if (oc != gc || reversed(orr) != grr) fail("addn", n, "sum");
            run("addn", n,
                [&] { Fullint64AddNWIN(oa.data(), ob.data(), orr.data(), n); },
                [&] { mpn_add_n(grr.data(), ga.data(), gb.data(), n); });
        }

        if (want(ops, "subn"))
        {
            Vec orr(n), grr(n);
            u64 ob_ = Fullint64SubNWIN(oa.data(), ob.data(), orr.data(), n);
            u64 gb_ = mpn_sub_n(grr.data(), ga.data(), gb.data(), n);
            if (ob_ != gb_ || reversed(orr) != grr) fail("subn", n, "difference");
            run("subn", n,
                [&] { Fullint64SubNWIN(oa.data(), ob.data(), orr.data(), n); },
                [&] { mpn_sub_n(grr.data(), ga.data(), gb.data(), n); });
        }

        if (want(ops, "subcmpn"))   // |a - b| with the compare and the length (the engine's subto kernel)
        {
            Vec orr(n), grr(n);
            auto gmpSubCmp = [&] {
                int c = mpn_cmp(ga.data(), gb.data(), n);
                mp_size_t m = n;
                if (c == 0) m = 0;
                else
                {
                    if (c > 0) mpn_sub_n(grr.data(), ga.data(), gb.data(), n);
                    else mpn_sub_n(grr.data(), gb.data(), ga.data(), n);
                    while (m > 0 && grr[m - 1] == 0) m--;
                }
                return m;
            };
            u64 r = Fullint64SubCmpNWIN(oa.data(), ob.data(), orr.data(), n);
            mp_size_t m = gmpSubCmp();
            Vec got(orr.begin() + (uint32_t)r, orr.end());
            if ((mp_size_t)(n - (uint32_t)r) != m || reversed(got) != Vec(grr.begin(), grr.begin() + m))
                fail("subcmpn", n, "difference");
            run("subcmpn", n,
                [&] { Fullint64SubCmpNWIN(oa.data(), ob.data(), orr.data(), n); },
                [&] { gmpSubCmp(); });
        }

        if (want(ops, "mul"))
        {
            Vec orr(2 * n), grr(2 * n);
            Fullint64MultWIN(oa.data(), ob.data(), orr.data(), n, n, 2 * n);
            mpn_mul_n(grr.data(), ga.data(), gb.data(), n);
            if (reversed(orr) != grr) fail("mul", n, "product");
            run("mul", n,
                [&] { Fullint64MultWIN(oa.data(), ob.data(), orr.data(), n, n, 2 * n); },
                [&] { mpn_mul_n(grr.data(), ga.data(), gb.data(), n); });
        }

        if (want(ops, "muladx") && (Fullint64CpuFeatures() & 3) == 3)   // the BMI2 + ADX kernel
        {
            Vec orr(2 * n), grr(2 * n);
            Fullint64MultWIN_adx(oa.data(), ob.data(), orr.data(), n, n, 2 * n);
            mpn_mul_n(grr.data(), ga.data(), gb.data(), n);
            if (reversed(orr) != grr) fail("muladx", n, "product");
            Vec ob1(ob.begin(), ob.begin() + (n + 1) / 2), gb1(gb.begin(), gb.begin() + (n + 1) / 2);   // unequal lengths, lenR + 1
            Vec o3(n + ob1.size() + 1, 0xDEADull), g3(n + gb1.size());
            Fullint64MultWIN_adx(oa.data(), ob1.data(), o3.data(), n, (int)ob1.size(), (int)o3.size());
            mpn_mul(g3.data(), ga.data(), n, reversed(ob1).data(), (mp_size_t)ob1.size());
            g3.push_back(0);
            if (reversed(o3) != g3) fail("muladx", n, "unequal / longer result");
            run("muladx", n,
                [&] { Fullint64MultWIN_adx(oa.data(), ob.data(), orr.data(), n, n, 2 * n); },
                [&] { mpn_mul_n(grr.data(), ga.data(), gb.data(), n); });
        }

        if (want(ops, "div") && n >= 2)   // 2n words by n words
        {
            Vec gn = randomLimbs(2 * n);
            Vec on = reversed(gn);
            Vec oq(n + 1), orem(n + 2), gq(n + 1), grem(n);
            auto ours = [&] {
                std::memset(orem.data(), 0, orem.size() * 8);
                Fullint64DivWin(on.data(), ob.data(), oq.data(), orem.data(), 2 * n, n);
            };
            ours();
            mpn_tdiv_qr(gq.data(), grem.data(), 0, gn.data(), 2 * n, gb.data(), n);
            if (stripOurs(oq.data(), n + 1) != stripLimbs(gq)) fail("div", n, "quotient");
            if (stripOurs(orem.data(), n + 2) != stripLimbs(grem)) fail("div", n, "remainder");
            run("div", n, ours,
                [&] { mpn_tdiv_qr(gq.data(), grem.data(), 0, gn.data(), 2 * n, gb.data(), n); });
        }

        if (want(ops, "div1"))   // n words by one word
        {
            u64 d = rng() | 1;
            Vec oq(n), gq(n);
            u64 orem = 0;
            Fullint64Div2longWin(oa.data(), d, oq.data(), &orem, n);
            u64 grem = mpn_divrem_1(gq.data(), 0, ga.data(), n, d);
            if (orem != grem || reversed(oq) != gq) fail("div1", n, "quotient/remainder");
            run("div1", n,
                [&] { Fullint64Div2longWin(oa.data(), d, oq.data(), &orem, n); },
                [&] { mpn_divrem_1(gq.data(), 0, ga.data(), n, d); });
        }

        if (want(ops, "shl"))
        {
            const int s = 13;
            Vec orr(n + 1), grr(n + 1);
            Fullint64ShlWIN(oa.data(), orr.data(), n, n + 1, s);
            grr[n] = mpn_lshift(grr.data(), ga.data(), n, s);
            if (reversed(orr) != grr) fail("shl", n, "shift");
            run("shl", n,
                [&] { Fullint64ShlWIN(oa.data(), orr.data(), n, n + 1, s); },
                [&] { mpn_lshift(grr.data(), ga.data(), n, s); });
        }

        if (want(ops, "shr"))
        {
            const int s = 13;
            Vec orr(n), grr(n);
            Fullint64ShrWIN(oa.data(), orr.data(), n, n, s);
            mpn_rshift(grr.data(), ga.data(), n, s);
            if (reversed(orr) != grr) fail("shr", n, "shift");
            run("shr", n,
                [&] { Fullint64ShrWIN(oa.data(), orr.data(), n, n, s); },
                [&] { mpn_rshift(grr.data(), ga.data(), n, s); });
        }

        if (want(ops, "cmp"))   // equal numbers: both walk all words
        {
            Vec ob2 = oa, gb2 = ga;
            if (Fullint64CompareWIN(oa.data(), ob2.data(), n, n) != 0 || mpn_cmp(ga.data(), gb2.data(), n) != 0)
                fail("cmp", n, "equal numbers");
            run("cmp", n,
                [&] { sink = Fullint64CompareWIN(oa.data(), ob2.data(), n, n); },
                [&] { sink = mpn_cmp(ga.data(), gb2.data(), n); });
        }

        if (want(ops, "todec"))
        {
            // ours: the buffer size of FullInt64Engine.ToDecString; GMP clobbers its input, so
            // it gets a fresh copy per call (ours copies into valueBuf inside the kernel)
            int resultBytes = int(n * 1.014 + 1) * 19 + 1;
            std::vector<uint8_t> obuf(resultBytes);
            Vec ovb(n), gtmp(n + 1);
            std::vector<unsigned char> gstr(size_t(n) * 20 + 8);
            int start = 0;
            auto ours = [&] {
                start = resultBytes;
                Fullint64ToDecStringWin(oa.data(), ovb.data(), obuf.data(), n, &start);
            };
            size_t glen = 0;
            auto gmp = [&] {
                std::memcpy(gtmp.data(), ga.data(), size_t(n) * 8);
                glen = mpn_get_str(gstr.data(), 10, gtmp.data(), n);
            };
            ours();
            gmp();
            std::string so(obuf.begin() + start, obuf.end());
            std::string sg(glen, '0');
            for (size_t i = 0; i < glen; i++) sg[i] = char('0' + gstr[i]);
            if (stripZeros(so) != stripZeros(sg)) fail("todec", n, "digits");
            run("todec", n, ours, gmp);
        }

        if (want(ops, "fromdec"))
        {
            // the decimal text of the n-word number a
            Vec gtmp(ga);
            gtmp.push_back(0);
            std::vector<unsigned char> digits(size_t(n) * 20 + 8);
            size_t len = mpn_get_str(digits.data(), 10, gtmp.data(), n);
            digits.resize(len);
            std::vector<uint8_t> ascii(len);
            for (size_t i = 0; i < len; i++) ascii[i] = uint8_t('0' + digits[i]);

            int words = int(len / 19 + 1);   // the buffer sizes of FullIntCoreBase.FromDecString
            Vec ovb(words), ores(words), gres(n + 2);
            int start = 0;
            auto ours = [&] {
                std::memset(ovb.data(), 0, ovb.size() * 8);
                std::memset(ores.data(), 0, ores.size() * 8);
                start = words;
                Fullint64FromDecStringWin(ascii.data(), ovb.data(), ores.data(), int(len), &start);
            };
            mp_size_t gn = 0;
            auto gmp = [&] { gn = mpn_set_str(gres.data(), digits.data(), len, 10); };
            ours();
            gmp();
            if (stripOurs(ores.data() + start, words - start) != stripLimbs(Vec(gres.begin(), gres.begin() + gn)))
                fail("fromdec", n, "words");
            run("fromdec", n, ours, gmp);
        }
    }

    if (csv) std::fclose(csv);
    return 0;
}
