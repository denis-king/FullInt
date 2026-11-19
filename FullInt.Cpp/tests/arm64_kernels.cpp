// The AArch64 kernels (FullInt.Asm/Sources/arm64) against the portable C++ ones
// (src/kernels_generic.cpp, the same contracts) word for word: every entry with an AArch64 kernel,
// short lengths one by one and some long ones, random words and the extremes (all ones, zeros,
// one-bit and carry / borrow runs), guard words around the outputs. No GMP: quick to build and
// to run under QEMU (docker/test-linux-arm64.ps1 -Kernels); fullint_tests then checks the whole
// library against GMP.
#include <fullint/kernels.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <thread>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <random>
#include <string>
#include <utility>
#include <vector>

#if !(FULLINT_GENERIC_KERNELS && FULLINT_ARM64_ASM)
#error "arm64_kernels.cpp: the AArch64 build with its kernels (FULLINT_ARM64_ASM)"
#endif

namespace {

using V = std::vector<uint64_t>;

long checks = 0;
int failures = 0;

// where the test is (set before a kernel call): a watchdog prints it when nothing moves for a
// minute - under QEMU a kernel caught in a loop shows itself instead of running for hours
std::atomic<long> progress { 0 };
char where[128] = "start";
#define AT(...) (std::snprintf(where, sizeof where, __VA_ARGS__), progress++)

#define CHECK(cond, what)                                                       \
    do                                                                          \
    {                                                                           \
        checks++;                                                               \
        if (!(cond))                                                            \
        {                                                                       \
            if (++failures <= 30) std::printf("FAIL %s (line %d)\n", std::string(what).c_str(), __LINE__); \
        }                                                                       \
    } while (0)

std::mt19937_64 rng(20261119);
constexpr uint64_t Guard = 0x5A5A5A5A5A5A5A5Aull;
constexpr int Kinds = 6;

// kind 0 random, 1 all ones, 2 zeros, 3 ones / zeros words mixed, 4 one bit, 5 the top bit /
// small words (the normalization edges)
void Fill(uint64_t* w, size_t n, int kind)
{
    for (size_t i = 0; i < n; i++)
        w[i] = kind == 0 ? rng()
             : kind == 1 ? ~0ull
             : kind == 2 ? 0
             : kind == 3 ? (rng() & 1 ? ~0ull : 0)
             : kind == 4 ? (rng() % 4 == 0 ? 1ull << (rng() % 64) : 0)
             : (rng() & 1 ? 1ull << 63 : rng() % 3);
}

V Random(size_t n, int kind)
{
    V v(n);
    Fill(v.data(), n, kind);
    return v;
}

// a number without leading zero words (a divisor)
V Normalized(size_t n, int kind)
{
    V v = Random(n, kind);
    if (v[0] == 0) v[0] = kind == 5 ? 1 : (rng() | 1ull << 63) >> (rng() % 64);   // (never 0: any sh)
    return v;
}

// the words of v in hex (a failure's data, the first few failures)
void Dump(const char* name, const V& v)
{
    if (failures > 6) return;
    std::printf("  %s:", name);
    for (uint64_t w : v) std::printf(" %016llx", (unsigned long long)w);
    std::printf("\n");
}

std::string Str(const char* what, long a, long b = -1, long c = -1)
{
    std::string s = what;
    s += " " + std::to_string(a);
    if (b >= 0) s += " " + std::to_string(b);
    if (c >= 0) s += " " + std::to_string(c);
    return s;
}

std::vector<int> Lengths(int upTo, std::initializer_list<int> more)
{
    std::vector<int> v;
    for (int n = 1; n <= upTo; n++) v.push_back(n);
    for (int n : more) v.push_back(n);
    return v;
}

void AddSub()
{
    for (int n : Lengths(40, { 63, 64, 65, 100, 257 }))
        for (int ka = 0; ka < Kinds; ka++)
            for (int kb = 0; kb < Kinds; kb++)
            {
                V a = Random(n, ka), b = Random(n, kb);
                for (int sub = 0; sub < 2; sub++)
                {
                    auto asmK = sub ? Fullint64SubNWIN : Fullint64AddNWIN;
                    auto genK = sub ? Fullint64SubNWIN_generic : Fullint64AddNWIN_generic;
                    V r1(n + 2, Guard), r2(n + 2, Guard);
                    uint64_t c1 = asmK(a.data(), b.data(), r1.data() + 1, n);
                    uint64_t c2 = genK(a.data(), b.data(), r2.data() + 1, n);
                    CHECK(c1 == c2 && r1 == r2, Str(sub ? "SubN" : "AddN", n));
                    V x = a, y = b;   // in place: R = A, then R = B
                    uint64_t cx = asmK(x.data(), b.data(), x.data(), n);
                    uint64_t cy = asmK(a.data(), y.data(), y.data(), n);
                    CHECK(cx == c2 && cy == c2 && V(r2.begin() + 1, r2.end() - 1) == x && x == y, Str(sub ? "SubN in place" : "AddN in place", n));
                }

                // SubCmpN: equal, one word apart, random
                for (int shape = 0; shape < 3; shape++)
                {
                    V x = a, y = shape == 0 ? a : shape == 1 ? a : b;
                    if (shape == 1) y[rng() % n] ^= 1ull << (rng() % 64);
                    V r1(n + 1, Guard), r2(n + 1, Guard);
                    uint64_t g1 = Fullint64SubCmpNWIN(x.data(), y.data(), r1.data(), n);
                    uint64_t g2 = Fullint64SubCmpNWIN_generic(x.data(), y.data(), r2.data(), n);
                    bool ok = g1 == g2 && r1[n] == Guard;
                    for (int i = (int)(uint32_t)g2; ok && i < n; i++) ok = r1[i] == r2[i];
                    CHECK(ok, Str("SubCmpN", n, shape));
                    for (int neg = 0; neg < 2; neg++)
                    {
                        uint32_t h1[3] = { 0xDEAD, (uint32_t)n, (uint32_t)neg }, h2[3] = { 0xDEAD, (uint32_t)n, (uint32_t)neg };
                        V s1(n + 1, Guard), s2(n + 1, Guard);
                        Fullint64SubCmpNHdrWIN(x.data(), y.data(), s1.data(), h1);
                        Fullint64SubCmpNHdrWIN_generic(x.data(), y.data(), s2.data(), h2);
                        bool okh = h1[0] == h2[0] && h1[1] == h2[1] && (h1[2] & 0xFF) == (h2[2] & 0xFF) && s1[n] == Guard;
                        for (uint32_t i = h2[0]; okh && i < h2[0] + h2[1]; i++) okh = s1[i] == s2[i];
                        CHECK(okh, Str("SubCmpNHdr", n, shape));
                    }
                }
                uint32_t h1[3] = { 0xDEAD, (uint32_t)n, 1 }, h2[3] = { 0xDEAD, (uint32_t)n, 1 };
                V s1(n + 2, Guard), s2(n + 2, Guard);
                Fullint64AddNHdrWIN(a.data(), b.data(), s1.data(), h1);
                Fullint64AddNHdrWIN_generic(a.data(), b.data(), s2.data(), h2);
                CHECK(h1[0] == h2[0] && h1[1] == h2[1] && h1[2] == h2[2] && s1 == s2, Str("AddNHdr", n));
            }

    // the general Add / Sub: every length 1-12 of A, B and R (R shorter, equal, longer), leading
    // zero words, equal operands
    for (int la = 1; la <= 12; la++)
        for (int lb = 1; lb <= 12; lb++)
            for (int lr = 1; lr <= 15; lr++)
                for (int kind = 0; kind < Kinds; kind++)
                {
                    V a = Random(la, kind), b = Random(lb, kind == 3 ? 0 : kind);
                    if (kind == 4 && la == lb) b = a;   // equal (Sub: zero)
                    V r1(lr + 2, Guard), r2(lr + 2, Guard);
                    uint64_t c1 = Fullint64AddWIN(a.data(), b.data(), r1.data() + 1, la, lb, lr);
                    uint64_t c2 = Fullint64AddWIN_generic(a.data(), b.data(), r2.data() + 1, la, lb, lr);
                    CHECK(c1 == c2 && r1 == r2, Str("Add", la, lb, lr));
                    if (lr < std::max(la, lb)) continue;   // Sub: R holds the difference
                    V d1(lr + 2, Guard), d2(lr + 2, Guard);
                    int s1 = Fullint64SubWIN(a.data(), b.data(), d1.data() + 1, la, lb, lr);
                    int s2 = Fullint64SubWIN_generic(a.data(), b.data(), d2.data() + 1, la, lb, lr);
                    CHECK(s1 == s2 && d1 == d2, Str("Sub", la, lb, lr));
                }
    for (int la : { 33, 100, 257 })
        for (int lb : { 1, 32, 100, 257 })
        {
            V a = Random(la, 0), b = Random(lb, 0);
            int lr = std::max(la, lb) + 1;
            V r1(lr), r2(lr);
            CHECK(Fullint64AddWIN(a.data(), b.data(), r1.data(), la, lb, lr) == Fullint64AddWIN_generic(a.data(), b.data(), r2.data(), la, lb, lr) && r1 == r2, Str("Add long", la, lb));
            CHECK(Fullint64SubWIN(a.data(), b.data(), r1.data(), la, lb, lr) == Fullint64SubWIN_generic(a.data(), b.data(), r2.data(), la, lb, lr) && r1 == r2, Str("Sub long", la, lb));
        }
}

void Compares()
{
    for (int n = 1; n <= 70; n++)
        for (int pos = -1; pos < n; pos++)
            for (int dir = 0; dir < 2; dir++)
            {
                V a = Random(n, (int)(rng() % Kinds)), b = a;
                if (pos >= 0)
                {
                    uint64_t lo = rng() >> 1, hi = lo + 1 + (rng() & 0xFF);
                    a[pos] = dir ? hi : lo;
                    b[pos] = dir ? lo : hi;
                }
                CHECK(Fullint64CmpNWIN(a.data(), b.data(), n) == Fullint64CmpNWIN_generic(a.data(), b.data(), n), Str("CmpN", n, pos));
                CHECK(Fullint64CompareWIN(a.data(), b.data(), n, n) == Fullint64CompareWIN_generic(a.data(), b.data(), n, n), Str("Compare", n, pos));
            }
    for (int la = 1; la <= 10; la++)   // different lengths, leading zero words
        for (int lb = 1; lb <= 10; lb++)
            for (int kind = 0; kind < Kinds; kind++)
            {
                V a = Random(la, kind), b = Random(lb, (kind + 1) % Kinds);
                CHECK(Fullint64CompareWIN(a.data(), b.data(), la, lb) == Fullint64CompareWIN_generic(a.data(), b.data(), la, lb), Str("Compare", la, lb, kind));
            }
}

void Logic()
{
    auto asmK = [](int op) { return op == 0 ? Fullint64AndWIN : op == 1 ? Fullint64OrWIN : Fullint64XorWIN; };
    auto genK = [](int op) { return op == 0 ? Fullint64AndWIN_generic : op == 1 ? Fullint64OrWIN_generic : Fullint64XorWIN_generic; };
    for (int la = 1; la <= 12; la++)
        for (int lb = 1; lb <= 12; lb++)
            for (int extra = 1; extra <= 3; extra++)
                for (int signs = 0; signs < 4; signs++)
                    for (int kind = 0; kind < Kinds; kind++)
                    {
                        V a = Normalized(la, kind), b = Normalized(lb, (kind + 2) % Kinds);
                        int lr = std::max(la, lb) + extra;
                        for (int op = 0; op < 3; op++)
                        {
                            V r1(lr + 2, Guard), r2(lr + 2, Guard);
                            int s1 = asmK(op)(a.data(), b.data(), r1.data() + 1, la, lb, lr, signs);
                            int s2 = genK(op)(a.data(), b.data(), r2.data() + 1, la, lb, lr, signs);
                            CHECK(s1 == s2 && r1 == r2, Str("Logic", op * 4 + signs, la * 100 + lb, lr));
                        }
                    }
}

void Shifts()
{
    for (int la = 1; la <= 12; la++)
        for (int shift : { 0, 1, 7, 31, 32, 63, 64, 65, 100, 127, 128, 129, 191, 200, 300, 700, 770 })
            for (int extra = 0; extra <= 2; extra++)
                for (int kind = 0; kind < Kinds; kind++)
                {
                    V a = Random(la, kind);
                    int ws = shift / 64;
                    int lrl = la + ws + 1 + extra;
                    V r1(lrl + 2, Guard), r2(lrl + 2, Guard);
                    Fullint64ShlWIN(a.data(), r1.data() + 1, la, lrl, shift);
                    Fullint64ShlWIN_generic(a.data(), r2.data() + 1, la, lrl, shift);
                    CHECK(r1 == r2, Str("Shl", la, shift, extra));
                    int lrr = std::max(la - ws, 1) + extra;
                    V s1(lrr + 2, Guard), s2(lrr + 2, Guard);
                    Fullint64ShrWIN(a.data(), s1.data() + 1, la, lrr, shift);
                    Fullint64ShrWIN_generic(a.data(), s2.data() + 1, la, lrr, shift);
                    CHECK(s1 == s2, Str("Shr", la, shift, extra));
                    if (extra == 0 && ws < la)   // in place: R = A (lenR = lenA)
                    {
                        V x = a, e(la);
                        Fullint64ShrWIN_generic(a.data(), e.data(), la, la, shift);
                        Fullint64ShrWIN(x.data(), x.data(), la, la, shift);
                        CHECK(x == e, Str("Shr in place", la, shift));
                    }
                }
}

void Products()
{
    for (int la = 1; la <= 12; la++)
        for (int lb = 1; lb <= 12; lb++)
            for (int kind = 0; kind < Kinds; kind++)
                for (int extra = 0; extra < 3; extra++)
                {
                    V a = Random(la, kind), b = Random(lb, kind == 3 ? 0 : kind);
                    int lr = la + lb + (extra == 2 ? 2 : 0) - (extra == 1 ? 1 : 0);   // extra 1: shorter R
                    V r1(lr + 2, Guard), r2(lr + 2, Guard);
                    Fullint64MultWIN(a.data(), b.data(), r1.data() + 1, la, lb, lr);
                    Fullint64MultWIN_generic(a.data(), b.data(), r2.data() + 1, la, lb, lr);
                    CHECK(r1 == r2, Str("Mult", la, lb, lr));
                    for (int n = 1; n <= la + lb; n++)
                    {
                        V m1(n + 2, Guard), m2(n + 2, Guard);
                        Fullint64MulLoWIN(a.data(), b.data(), m1.data() + 1, la, lb, n);
                        Fullint64MulLoWIN_generic(a.data(), b.data(), m2.data() + 1, la, lb, n);
                        CHECK(m1 == m2, Str("MulLo", la, lb, n));
                    }
                }
    for (int la : { 33, 64, 100, 257 })
        for (int lb : { 1, 7, 33, 64 })
        {
            V a = Random(la, 0), b = Random(lb, 0), r1(la + lb), r2(la + lb);
            Fullint64MultWIN(a.data(), b.data(), r1.data(), la, lb, la + lb);
            Fullint64MultWIN_generic(a.data(), b.data(), r2.data(), la, lb, la + lb);
            CHECK(r1 == r2, Str("Mult long", la, lb));
            int n = (la + lb) / 2;
            V m1(n), m2(n);
            Fullint64MulLoWIN(a.data(), b.data(), m1.data(), la, lb, n);
            Fullint64MulLoWIN_generic(a.data(), b.data(), m2.data(), la, lb, n);
            CHECK(m1 == m2, Str("MulLo long", la, lb, n));
        }
    for (int n : Lengths(40, { 64, 100, 257 }))
        for (int kind = 0; kind < Kinds; kind++)
            for (int extra = 0; extra < 2; extra++)
            {
                V a = Random(n, kind);
                int lr = 2 * n + extra;
                V r1(lr + 2, Guard), r2(lr + 2, Guard);
                Fullint64SqrWIN(a.data(), r1.data() + 1, n, lr);
                Fullint64SqrWIN_generic(a.data(), r2.data() + 1, n, lr);
                CHECK(r1 == r2, Str("Sqr", n, kind, extra));
            }
}

// A = Q·B + R with chosen quotient and remainder shapes (the corrections' cases: R = B - 1,
// R = 0, quotient words all ones)
V Dividend(const V& b, int lq, int kind)
{
    V q = Random(lq, kind), rem = Random(b.size(), (kind + 3) % Kinds), a(lq + b.size());
    Fullint64MultWIN_generic(q.data(), b.data(), a.data(), lq, (int)b.size(), (int)a.size());
    if (kind == 1)   // R = B - 1
    {
        rem = b;
        for (size_t i = rem.size(); i-- > 0;)
            if (rem[i]-- != 0) break;
    }
    if (Fullint64CompareWIN_generic(rem.data(), b.data(), (int)rem.size(), (int)b.size()) >= 0) rem.assign(b.size(), 0);
    V s(a.size());
    Fullint64AddWIN_generic(a.data(), rem.data(), s.data(), (int)a.size(), (int)rem.size(), (int)s.size());
    return s;
}

void Divisions()
{
    // one word
    for (int la : Lengths(20, { 64, 100 }))
        for (int kind = 0; kind < Kinds; kind++)
            for (uint64_t d : std::initializer_list<uint64_t>{ 1, 3, 10, 0x8000000000000000ull, ~0ull, 0x8AC7230489E80000ull, rng(), rng() >> 33, rng() >> 60 | 1 })
            {
                V a = Random(la, kind), q1(la, Guard), q2(la, Guard);
                uint64_t r1 = Guard, r2 = Guard;
                AT("Div2long la=%d kind=%d d=%llx", la, kind, (unsigned long long)d);
                Fullint64Div2longWin(a.data(), d, q1.data(), &r1, la);
                Fullint64Div2longWin_generic(a.data(), d, q2.data(), &r2, la);
                CHECK(q1 == q2 && r1 == r2, Str("Div2long", la, kind));
                Fullint64Div2longWIN_recip(a.data(), d, q1.data(), &r1, la);
                CHECK(q1 == q2 && r1 == r2, Str("Div2long recip", la, kind));
                V x = a;   // Q = A
                Fullint64Div2longWin(x.data(), d, x.data(), &r1, la);
                CHECK(x == q2 && r1 == r2, Str("Div2long in place", la, kind));
            }

    // long: DivWin (all lengths), Div_recip (lenA > lenB >= 2), DivQ_recip (lenB >= 3)
    for (int lb = 1; lb <= 12; lb++)
        for (int la = 1; la <= 20; la++)
            for (int kind = 0; kind < Kinds; kind++)
                for (int shape = 0; shape < 2; shape++)
                {
                    V b = Normalized(lb, kind);
                    V a = shape == 0 || la <= lb ? Random(la, (kind + 1) % Kinds) : Dividend(b, la - lb, kind);
                    int lq = std::max(la - lb + 1, 1);
                    V q1(lq + 2, Guard), q2(lq + 2, Guard), r1(lb + 4, Guard), r2(lb + 4, Guard);
                    AT("DivWin la=%d lb=%d kind=%d shape=%d", la, lb, kind, shape);
                    Fullint64DivWin(a.data(), b.data(), q1.data() + 1, r1.data() + 1, la, lb);
                    Fullint64DivWin_generic(a.data(), b.data(), q2.data() + 1, r2.data() + 1, la, lb);
                    CHECK(q1 == q2 && r1 == r2, Str("Div", la, lb, kind * 2 + shape));
                    if (q1 != q2 || r1 != r2)
                    {
                        Dump("A", a); Dump("B", b); Dump("Q asm", q1); Dump("Q C++", q2); Dump("R asm", r1); Dump("R C++", r2);
                    }
                    if (la > lb && lb >= 2)
                    {
                        V q3(lq + 2, Guard), r3(lb + 4, Guard), work(la + 1 + 2, Guard);
                        AT("Div_recip la=%d lb=%d kind=%d shape=%d", la, lb, kind, shape);
                        Fullint64DivWIN_recip(a.data(), b.data(), q3.data() + 1, r3.data() + 1, la, lb, work.data() + 1);
                        CHECK(q3 == q2 && r3 == r2 && work[0] == Guard && work[la + 2] == Guard, Str("Div recip", la, lb, kind * 2 + shape));
                    }
                    if (la > lb && lb >= 3)
                    {
                        // Q' ~ floor(A·β / B) within 1: against the exact quotient of [A, 0]
                        V a0 = a;
                        a0.push_back(0);
                        int le = la + 1 - lb + 1;
                        V qe(le), re(lb + 2), qa(le + 2, Guard), work(la + 2 + 2, Guard);
                        Fullint64DivWin_generic(a0.data(), b.data(), qe.data(), re.data(), la + 1, lb);
                        AT("DivQ_recip la=%d lb=%d kind=%d shape=%d", la, lb, kind, shape);
                        uint64_t sat = Fullint64DivQWIN_recip(a.data(), b.data(), qa.data() + 1, nullptr, la, lb, work.data() + 1);
                        bool ok = qa[0] == Guard && qa[le + 1] == Guard && work[0] == Guard && work[la + 3] == Guard;
                        if (sat == 0)
                        {
                            V got(qa.begin() + 1, qa.end() - 1), diff(le);
                            int c = Fullint64CompareWIN_generic(got.data(), qe.data(), le, le);
                            if (c > 0) Fullint64SubNWIN_generic(got.data(), qe.data(), diff.data(), le);
                            else Fullint64SubNWIN_generic(qe.data(), got.data(), diff.data(), le);
                            for (int i = 0; ok && i < le - 1; i++) ok = diff[i] == 0;
                            ok = ok && diff[le - 1] <= 1;
                        }
                        else ok = ok && sat == 1;
                        CHECK(ok, Str("DivQ recip", la, lb, kind * 2 + shape));
                    }
                }
    for (int lb : { 2, 3, 16, 33 })
        for (int la : { 100, 257 })
        {
            V b = Normalized(lb, 0), a = Random(la, 0);
            int lq = la - lb + 1;
            V q1(lq), q2(lq), r1(lb + 2), r2(lb + 2), work(la + 1);
            AT("Div long la=%d lb=%d", la, lb);
            Fullint64DivWin(a.data(), b.data(), q1.data(), r1.data(), la, lb);
            Fullint64DivWin_generic(a.data(), b.data(), q2.data(), r2.data(), la, lb);
            CHECK(q1 == q2 && r1 == r2, Str("Div long", la, lb));
            Fullint64DivWIN_recip(a.data(), b.data(), q1.data(), r1.data(), la, lb, work.data());
            CHECK(q1 == q2 && r1 == r2, Str("Div recip long", la, lb));
        }

    // exact division (Toom-Cook): multiples and not
    for (int len : Lengths(20, { 64, 100 }))
        for (uint64_t d : std::initializer_list<uint64_t>{ 1ull, 2ull, 3ull, 6ull, 9ull, 12ull, 15ull, 24ull, 45ull, 255ull, 0xFFFFFFFFull, 0x80000000ull << 20, 0xFFFFFFFFull << 31 })
            for (int kind = 0; kind < Kinds; kind++)
            {
                V q = Random(len, kind), a(len + 1), x;   // a = q·d (len + 1 words): a multiple
                Fullint64MultWIN_generic(q.data(), &d, a.data(), len, 1, len + 1);
                if (kind == 3) a[len] ^= 1;                // not a multiple (for an even d)
                int la = len + 1;
                V q1(la + 2, Guard), q2(la + 2, Guard);
                AT("DivExact len=%d d=%llx kind=%d", len, (unsigned long long)d, kind);
                uint64_t c1 = Fullint64DivExactWIN(a.data(), q1.data() + 1, la, d);
                uint64_t c2 = Fullint64DivExactWIN_generic(a.data(), q2.data() + 1, la, d);
                CHECK(c1 == c2 && q1 == q2 && (kind == 3 || c1 == 0), Str("DivExact", len, (long)(d & 0x7FFFFFFF), kind));
                x = a;
                Fullint64DivExactWIN(x.data(), x.data(), la, d);
                CHECK(V(q2.begin() + 1, q2.end() - 1) == x, Str("DivExact in place", len));
            }
}

void Decimal()
{
    for (int n : Lengths(24, { 40, 64, 100, 200 }))
        for (int kind = 0; kind < Kinds; kind++)
        {
            V a = Random(n, kind);
            if (kind == 2 && n > 1) a[n - 1] = rng() % 1000;
            int size = n * 20 + 8;
            for (int recip = 0; recip < 2; recip++)
            {
                V buf1(n + 1, Guard), buf2(n + 1, Guard);
                std::vector<uint8_t> t1(size + 2, 0xA5), t2(size + 2, 0xA5);
                int l1 = size, l2 = size;
                (recip ? Fullint64ToDecStringWIN_recip : Fullint64ToDecStringWin)(a.data(), buf1.data(), t1.data() + 1, n, &l1);
                Fullint64ToDecStringWin_generic(a.data(), buf2.data(), t2.data() + 1, n, &l2);
                bool ok = l1 == l2 && t1[0] == 0xA5 && t1[size + 1] == 0xA5 && buf1[n] == Guard;
                for (int i = l2; ok && i < size; i++) ok = t1[1 + i] == t2[1 + i];
                CHECK(ok, Str("ToDecString", n, kind, recip));
            }
        }
    // every digit count 1-400 (the chunk boundaries), back and forth
    for (int digits = 1; digits <= 400; digits++)
        for (int kind = 0; kind < 3; kind++)
        {
            std::string text(digits, '0');
            for (int i = 0; i < digits; i++) text[i] = (char)('0' + (kind == 0 ? rng() % 10 : kind == 1 ? 9 : 0));
            if (text[0] == '0') text[0] = '1';
            int words = digits / 19 + 1;
            V vb1(words + 1, 0), vb2(words + 1, 0), rb1(words + 1, Guard), rb2(words + 1, Guard);
            int l1 = words, l2 = words;
            Fullint64FromDecStringWin((const uint8_t*)text.data(), vb1.data(), rb1.data(), digits, &l1);
            Fullint64FromDecStringWin_generic((const uint8_t*)text.data(), vb2.data(), rb2.data(), digits, &l2);
            bool ok = l1 == l2 && rb1[words] == Guard;
            for (int i = l2; ok && i < words; i++) ok = rb1[i] == rb2[i];
            CHECK(ok, Str("FromDecString", digits, kind));
            if (!ok) continue;
            int n = words - l1, size = digits + 20;   // and back
            V buf(n);
            std::vector<uint8_t> t(size);
            int l = size;
            Fullint64ToDecStringWIN_recip(rb1.data() + l1, buf.data(), t.data(), n, &l);
            CHECK(std::string((const char*)t.data() + l, size - l) == text, Str("ToDecString back", digits, kind));
        }
    // the fraction digits: f = x / 10^(19m) rounded up for a random x
    for (int m = 1; m <= 12; m++)
        for (int kind = 0; kind < Kinds; kind++)
        {
            int L = m + 1;
            V f = Random(L, kind), g = f;
            std::vector<uint8_t> o1(19 * m + 2, 0xA5), o2(19 * m + 2, 0xA5);
            Fullint64ToDecFracWIN(f.data(), L, m, o1.data() + 1);
            Fullint64ToDecFracWIN_generic(g.data(), L, m, o2.data() + 1);
            CHECK(o1 == o2, Str("ToDecFrac", m, kind));
        }
}

}  // namespace

int main()
{
    // (a section's name before it runs: under QEMU a kernel caught in a loop shows where)
    const std::pair<const char*, void (*)()> sections[] = { { "AddSub", AddSub }, { "Compares", Compares },
        { "Logic", Logic }, { "Shifts", Shifts }, { "Products", Products }, { "Divisions", Divisions }, { "Decimal", Decimal } };
    std::thread([] {
        for (long last = -1;;)
        {
            std::this_thread::sleep_for(std::chrono::seconds(60));
            long now = progress.load() + checks;
            if (now == last)
            {
                std::printf("STUCK at %s\n", where);
                std::fflush(stdout);
                std::_Exit(2);
            }
            last = now;
        }
    }).detach();
    for (auto& [name, run] : sections)
    {
        std::snprintf(where, sizeof where, "%s", name);
        std::printf("-- %s\n", name);
        std::fflush(stdout);
        run();
    }
    std::printf("%s: %ld checks, %d failed (the AArch64 kernels against the portable C++ ones)\n", failures ? "FAILED" : "OK", checks, failures);
    return failures ? 1 : 0;
}
