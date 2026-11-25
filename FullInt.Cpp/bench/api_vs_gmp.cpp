// API against API: FullInt64 (C++) against GMP's mpz, in like-for-like pairs:
//   op    (add, sub, mul, ...): a new value each time, memory included -
//         FullInt64: r = a op b (a new number, moved into r, r's old buffer released);
//         GMP: mpz_init(t); mpz_op(t, a, b); mpz_swap(r, t); mpz_clear(t)
//   opto  (addto, subto, ...): into an existing result, the arithmetic alone -
//         FullInt64: Op(r, a, b) into r's buffer; GMP: mpz_op(r, a, b) into r's buffer
//   todec: a new std::string on both sides; parse: a new number on both sides
// The same operations, sizes and numbers as the .NET half (FullInt.Benchmarks "cross"):
// the inputs come from the same splitmix64 sequence. With --net <csv> from that run, the
// table shows all four: GMP, FullInt C++, FullInt .NET, BigInteger.
//
// Each cell: a batch of >= 3 ms, the minimum over rounds, the two sides interleaved.
//
// usage: fullint_bench_api [--ops add,mul,...] [--max N] [--cpu K (the core to pin to; default 2)] [--set name=value ...] [--quick] [--net file.csv] [--csv file]
//   --quick: 1 ms batches and 3 rounds - a fast "did it help" check, noisier than the full run
#include <fullint/BurnikelZiegler.h>
#include <fullint/FullInt64.h>
#include <fullint/Karatsuba.h>
#include <fullint/NewtonDivision.h>
#include <gmp.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <functional>
#include <map>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#else
#include <pthread.h>
#include <sched.h>
#endif

using fullint::FullInt64;

namespace {

uint64_t state;
uint64_t Next()   // splitmix64 (the .NET side has the same)
{
    uint64_t z = (state += 0x9E3779B97F4A7C15ull);
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
    return z ^ (z >> 31);
}

// --set name=value: a threshold changed before the run (A/B of the thresholds without a
// rebuild); false on an unknown name
bool SetThreshold(const std::string& kv)
{
    using namespace fullint;
    const std::map<std::string, int*> names = {
        { "kara", &Karatsuba::Threshold }, { "toom3", &Karatsuba::Toom3Threshold },
        { "toom4", &Karatsuba::Toom4Threshold }, { "toom6", &Karatsuba::Toom6Threshold },
        { "fft", &Karatsuba::FftThreshold }, { "ntt", &Karatsuba::NttThreshold },
        { "sqr", &Karatsuba::SqrThreshold }, { "mullo", &Karatsuba::MulLoThreshold },
        { "bz", &BurnikelZiegler::Threshold }, { "bzquotient", &BurnikelZiegler::QuotientFrom },
        { "bzsplit", &BurnikelZiegler::QuotientSplitFrom }, { "newton", &NewtonDivision::Threshold },
        { "newtonlong", &NewtonDivision::LongQuotientThreshold }, { "cyclic", &NewtonDivision::CyclicFrom },
        { "tostring", &DecimalConversion::ToStringThreshold }, { "parse", &DecimalConversion::ParseThreshold },
        { "prepared", &DecimalConversion::PreparedFrom }, { "fraction", &DecimalConversion::FractionFrom },
    };
    size_t eq = kv.find('=');
    auto it = names.find(kv.substr(0, eq));
    if (eq == std::string::npos || it == names.end())
    {
        std::printf("--set: unknown threshold '%s'; known:", kv.c_str());
        for (auto& [name, p] : names) std::printf(" %s", name.c_str());
        std::printf("\n");
        return false;
    }
    *it->second = std::atoi(kv.c_str() + eq + 1);
    std::printf("# %s = %d\n", it->first.c_str(), *it->second);
    return true;
}

std::vector<uint64_t> Words(int n)   // index 0 = most significant, top word nonzero
{
    std::vector<uint64_t> w(n);
    for (auto& x : w) x = Next();
    if (w[0] == 0) w[0] = 1;
    return w;
}

long long Now()
{
    return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

// one operation per direct call: the operation's code compiled whole into Call, as in user code
// (inlined, no std::function's indirect call), and the call keeps the compiler from moving the
// work out of the loop - the same for both libraries
template <class F>
FULLINT_NOINLINE void Call(const F& f) { f(); }

template <class F>
double Batch(const F& f, long long iters)
{
    long long t0 = Now();
    for (long long i = 0; i < iters; i++) Call(f);
    return double(Now() - t0) / iters;
}

double batchTarget = 3e6;   // ns per batch; --quick: 1e6
int quickRounds = 0;        // --quick: 3

template <class F>
long long Calibrate(const F& f)
{
    const double target = batchTarget;
    for (long long iters = 1;; iters *= 4)
    {
        double per = Batch(f, iters);
        if (per * iters >= target / 4 || iters >= (1LL << 30)) return std::max(1LL, (long long)(target / std::max(per, 0.01)));
    }
}

template <class F, class G>
std::pair<double, double> Run(const F& ours, const G& gmp)
{
    long long io = Calibrate(ours), ig = Calibrate(gmp);
    double perCall = Batch(ours, 1);
    int rounds = quickRounds ? quickRounds : perCall > 20e6 ? 3 : perCall > 2e6 ? 5 : 11;
    double bo = 1e300, bg = 1e300;
    for (int r = 0; r < rounds; r++)
    {
        bo = std::min(bo, Batch(ours, io));
        bg = std::min(bg, Batch(gmp, ig));
    }
    return { bo, bg };
}

struct NetRow { double net, big; };
std::map<std::pair<std::string, int>, NetRow> netRows;

void LoadNet(const char* path)
{
    std::ifstream in(path);
    std::string line;
    std::getline(in, line);   // header
    while (std::getline(in, line))
    {
        std::stringstream ss(line);
        std::string op, n, a, b;
        std::getline(ss, op, ',');
        std::getline(ss, n, ',');
        std::getline(ss, a, ',');
        std::getline(ss, b, ',');
        netRows[{ op, std::atoi(n.c_str()) }] = { std::atof(a.c_str()), std::atof(b.c_str()) };
    }
}

std::string Fmt(double ns)
{
    char buf[32];
    if (ns >= 1e6) std::snprintf(buf, sizeof buf, "%.2f ms", ns / 1e6);
    else if (ns >= 1e3) std::snprintf(buf, sizeof buf, "%.2f us", ns / 1e3);
    else std::snprintf(buf, sizeof buf, "%.1f ns", ns);
    return buf;
}

volatile int sink;
FILE* csv;
std::string ops;   // ",add,mul," or empty for all

}  // namespace

int main(int argc, char** argv)
{
    int maxN = 4096;
    int benchCpu = 2;
    for (int i = 1; i < argc; i++)
    {
        std::string a = argv[i];
        if (a == "--max" && i + 1 < argc) maxN = std::atoi(argv[++i]);
        else if (a == "--cpu" && i + 1 < argc) benchCpu = std::atoi(argv[++i]);
        else if (a == "--set" && i + 1 < argc) { if (!SetThreshold(argv[++i])) return 2; }
        else if (a == "--net" && i + 1 < argc) LoadNet(argv[++i]);
        else if (a == "--quick") { batchTarget = 1e6; quickRounds = 3; }
        else if (a == "--ops" && i + 1 < argc) ops = std::string(",") + argv[++i] + ",";
        else if (a == "--csv" && i + 1 < argc) csv = std::fopen(argv[++i], "w");
        else { std::printf("usage: %s [--max N] [--cpu K] [--set name=value] [--net file.csv] [--csv file]\n", argv[0]); return 2; }
    }
    if (csv) std::fprintf(csv, "op,n,fullint_cpp_ns,gmp_ns\n");
    bool withNet = !netRows.empty();

#if defined(_WIN32)
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadAffinityMask(GetCurrentThread(), 1ull << benchCpu);
#else
    cpu_set_t cpus;   // one core, as on Windows (the priority: the default)
    CPU_ZERO(&cpus);
    CPU_SET(benchCpu, &cpus);
    sched_setaffinity(0, sizeof cpus, &cpus);   // (0: this thread; Android's libc has no pthread_setaffinity_np)
#endif

    const int sizes[] = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 1024, 4096, 16384, 65536 };

    std::printf("FullInt kernels: %s\n", fullint::Cpu::Describe());
    std::printf("GMP %s; time per operation (min over rounds); x = time / GMP time (< 1: faster than GMP)\n", gmp_version);
    if (withNet)
        std::printf("%-6s %6s  %10s  %18s  %18s  %18s\n", "op", "words", "GMP", "FullInt C++", "FullInt .NET", "BigInteger");
    else
        std::printf("%-6s %6s  %10s  %18s\n", "op", "words", "GMP", "FullInt C++");

    state = 42;
    for (int n : sizes)
    {
        // the same draw order as CrossBench.cs: a (n), b (n), c (2n)
        auto wa = Words(n), wb = Words(n), wc = Words(2 * n);
        if (n > maxN) break;

        FullInt64 a(false, fullint::CSpan(wa)), b(false, fullint::CSpan(wb)), c(false, fullint::CSpan(wc)), a2(a);
        mpz_t ga, gb, gc, ga2, gr;
        mpz_inits(ga, gb, gc, ga2, gr, nullptr);
        mpz_import(ga, wa.size(), 1, 8, 0, 0, wa.data());
        mpz_import(gb, wb.size(), 1, 8, 0, 0, wb.data());
        mpz_import(gc, wc.size(), 1, 8, 0, 0, wc.data());
        mpz_set(ga2, ga);
        std::string text = a.ToString();
        std::vector<char> gbuf(mpz_sizeinbase(ga, 10) + 2);

        FullInt64 r;
        std::string s;

        auto row = [&](const char* op, const auto& ours, const auto& gmp) {
            if (!ops.empty() && ops.find(std::string(",") + op + ",") == std::string::npos) return;
            auto [o, g] = Run(ours, gmp);
            if (csv) std::fprintf(csv, "%s,%d,%.2f,%.2f\n", op, n, o, g);
            char co[48];
            std::snprintf(co, sizeof co, "%s %5.2fx", Fmt(o).c_str(), o / g);
            if (withNet)
            {
                auto it = netRows.find({ op, n });
                char cn[48] = "-", cb[48] = "-";
                if (it != netRows.end())
                {
                    std::snprintf(cn, sizeof cn, "%s %5.2fx", Fmt(it->second.net).c_str(), it->second.net / g);
                    if (it->second.big >= 0)   // -1: not measured (the .NET side's --bigmax)
                        std::snprintf(cb, sizeof cb, "%s %5.2fx", Fmt(it->second.big).c_str(), it->second.big / g);
                }
                std::printf("%-6s %6d  %10s  %18s  %18s  %18s\n", op, n, Fmt(g).c_str(), co, cn, cb);
            }
            else
            {
                std::printf("%-6s %6d  %10s  %18s\n", op, n, Fmt(g).c_str(), co);
            }
            std::fflush(stdout);
        };

        // GMP's "new value": a fresh mpz per operation (its buffer allocated by the operation),
        // swapped into gr, the old one freed - as r = a op b does with FullInt64
        auto fresh = [&](auto op) {
            mpz_t t;
            mpz_init(t);
            op(t);
            mpz_swap(gr, t);
            mpz_clear(t);
        };
        FullInt64 ri;   // into an existing result: the same call shape as GMP's mpz_op(r, a, b)

        row("add", [&] { r = a + b; }, [&] { fresh([&](mpz_ptr t) { mpz_add(t, ga, gb); }); });
        row("addto", [&] { fullint::Add(ri, a, b); }, [&] { mpz_add(gr, ga, gb); });
        row("sub", [&] { r = a - b; }, [&] { fresh([&](mpz_ptr t) { mpz_sub(t, ga, gb); }); });
        row("subto", [&] { fullint::Subtract(ri, a, b); }, [&] { mpz_sub(gr, ga, gb); });
        row("mul", [&] { r = a * b; }, [&] { fresh([&](mpz_ptr t) { mpz_mul(t, ga, gb); }); });
        row("multo", [&] { fullint::Multiply(ri, a, b); }, [&] { mpz_mul(gr, ga, gb); });
        row("div", [&] { r = c / b; }, [&] { fresh([&](mpz_ptr t) { mpz_tdiv_q(t, gc, gb); }); });
        row("divto", [&] { fullint::Divide(ri, c, b); }, [&] { mpz_tdiv_q(gr, gc, gb); });
        row("mod", [&] { r = c % b; }, [&] { fresh([&](mpz_ptr t) { mpz_tdiv_r(t, gc, gb); }); });
        row("modto", [&] { fullint::Remainder(ri, c, b); }, [&] { mpz_tdiv_r(gr, gc, gb); });
        row("shl", [&] { r = a << 13; }, [&] { fresh([&](mpz_ptr t) { mpz_mul_2exp(t, ga, 13); }); });
        row("shlto", [&] { fullint::ShiftLeft(ri, a, 13); }, [&] { mpz_mul_2exp(gr, ga, 13); });
        row("shr", [&] { r = a >> 13; }, [&] { fresh([&](mpz_ptr t) { mpz_fdiv_q_2exp(t, ga, 13); }); });
        row("shrto", [&] { fullint::ShiftRight(ri, a, 13); }, [&] { mpz_fdiv_q_2exp(gr, ga, 13); });
        row("and", [&] { r = a & b; }, [&] { fresh([&](mpz_ptr t) { mpz_and(t, ga, gb); }); });
        row("andto", [&] { fullint::And(ri, a, b); }, [&] { mpz_and(gr, ga, gb); });
        row("cmp", [&] { sink = a > a2; }, [&] { sink = mpz_cmp(ga, ga2); });
        row("todec", [&] { s = a.ToString(); }, [&] { mpz_get_str(gbuf.data(), 10, ga); s = std::string(gbuf.data()); });
        row("parse", [&] { r = FullInt64::Parse(text); }, [&] { fresh([&](mpz_ptr t) { mpz_set_str(t, text.c_str(), 10); }); });

        mpz_clears(ga, gb, gc, ga2, gr, nullptr);
    }
    if (csv) std::fclose(csv);
    return 0;
}
