// API against API: FullInt64 (C++) against GMP's mpz, the way each is normally used:
//   FullInt64: r = a op b   (a new value, moved into r); addto / multo: Add(r, a, b) into r
//   GMP:       mpz_op(r, a, b)  (into r's existing buffer)
// The same operations, sizes and numbers as the .NET half (FullInt.Benchmarks "cross"):
// the inputs come from the same splitmix64 sequence. With --net <csv> from that run, the
// table shows all four: GMP, FullInt C++, FullInt .NET, BigInteger.
//
// Each cell: a batch of >= 3 ms, the minimum over rounds, the two sides interleaved.
//
// usage: fullint_bench_api [--ops add,mul,...] [--max N] [--quick] [--net file.csv] [--csv file]
//   --quick: 1 ms batches and 3 rounds - a fast "did it help" check, noisier than the full run
#include <fullint/FullInt64.h>
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

#define NOMINMAX
#include <windows.h>

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

double Batch(const std::function<void()>& f, long long iters)
{
    long long t0 = Now();
    for (long long i = 0; i < iters; i++) f();
    return double(Now() - t0) / iters;
}

double batchTarget = 3e6;   // ns per batch; --quick: 1e6
int quickRounds = 0;        // --quick: 3

long long Calibrate(const std::function<void()>& f)
{
    const double target = batchTarget;
    for (long long iters = 1;; iters *= 4)
    {
        double per = Batch(f, iters);
        if (per * iters >= target / 4 || iters >= (1LL << 30)) return std::max(1LL, (long long)(target / std::max(per, 0.01)));
    }
}

std::pair<double, double> Run(const std::function<void()>& ours, const std::function<void()>& gmp)
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
    for (int i = 1; i < argc; i++)
    {
        std::string a = argv[i];
        if (a == "--max" && i + 1 < argc) maxN = std::atoi(argv[++i]);
        else if (a == "--net" && i + 1 < argc) LoadNet(argv[++i]);
        else if (a == "--quick") { batchTarget = 1e6; quickRounds = 3; }
        else if (a == "--ops" && i + 1 < argc) ops = std::string(",") + argv[++i] + ",";
        else if (a == "--csv" && i + 1 < argc) csv = std::fopen(argv[++i], "w");
        else { std::printf("usage: %s [--max N] [--net file.csv] [--csv file]\n", argv[0]); return 2; }
    }
    if (csv) std::fprintf(csv, "op,n,fullint_cpp_ns,gmp_ns\n");
    bool withNet = !netRows.empty();

    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadAffinityMask(GetCurrentThread(), 1ull << 2);

    const int sizes[] = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 1024, 4096, 16384 };

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

        auto row = [&](const char* op, const std::function<void()>& ours, const std::function<void()>& gmp) {
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

        row("add", [&] { r = a + b; }, [&] { mpz_add(gr, ga, gb); });
        row("sub", [&] { r = a - b; }, [&] { mpz_sub(gr, ga, gb); });
        FullInt64 ri;   // into an existing result: the same call shape as GMP's
        row("addto", [&] { fullint::Add(ri, a, b); }, [&] { mpz_add(gr, ga, gb); });
        row("multo", [&] { fullint::Multiply(ri, a, b); }, [&] { mpz_mul(gr, ga, gb); });
        row("mul", [&] { r = a * b; }, [&] { mpz_mul(gr, ga, gb); });
        row("div", [&] { r = c / b; }, [&] { mpz_tdiv_q(gr, gc, gb); });
        row("mod", [&] { r = c % b; }, [&] { mpz_tdiv_r(gr, gc, gb); });
        row("shl", [&] { r = a << 13; }, [&] { mpz_mul_2exp(gr, ga, 13); });
        row("shr", [&] { r = a >> 13; }, [&] { mpz_fdiv_q_2exp(gr, ga, 13); });
        row("and", [&] { r = a & b; }, [&] { mpz_and(gr, ga, gb); });
        row("cmp", [&] { sink = a > a2; }, [&] { sink = mpz_cmp(ga, ga2); });
        row("todec", [&] { s = a.ToString(); }, [&] { mpz_get_str(gbuf.data(), 10, ga); });
        row("parse", [&] { r = FullInt64::Parse(text); }, [&] { mpz_set_str(gr, text.c_str(), 10); });

        mpz_clears(ga, gb, gc, ga2, gr, nullptr);
    }
    if (csv) std::fclose(csv);
    return 0;
}
