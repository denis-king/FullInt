// FullInt C++ against GMP (mpz) as the reference: every operation on random and edge values,
// sizes from one word to Toom-3 range, all sign combinations; plus the C# semantics that GMP
// does not define the same way (parse prefixes, hex format).
#include <fullint/BurnikelZiegler.h>
#include <fullint/NewtonDivision.h>
#include <fullint/FullInt64.h>
#include <fullint/Karatsuba.h>
#include <fullint/kernels.h>
#include <gmp.h>

#include <cstdio>
#include <cstdlib>
#include <random>
#include <stdexcept>
#include <string>
#include <vector>

using fullint::FullInt64;

namespace {

int failures = 0;
long checks = 0;

#define CHECK(cond, what)                                                          \
    do                                                                             \
    {                                                                              \
        checks++;                                                                  \
        if (!(cond))                                                               \
        {                                                                          \
            failures++;                                                            \
            if (failures <= 20) std::printf("FAIL %s:%d %s\n", __FILE__, __LINE__, std::string(what).c_str()); \
        }                                                                          \
    } while (0)

struct Mpz
{
    mpz_t v;
    Mpz() { mpz_init(v); }
    ~Mpz() { mpz_clear(v); }
    Mpz(const Mpz&) = delete;
    Mpz& operator=(const Mpz&) = delete;
};

void ToMpz(const FullInt64& x, mpz_t out)
{
    auto w = x.Words();
    mpz_import(out, w.size(), 1, sizeof(uint64_t), 0, 0, w.data());   // order 1: most significant word first
    if (x.IsNegative()) mpz_neg(out, out);
}

std::string MpzDec(const mpz_t v)
{
    std::vector<char> buf(mpz_sizeinbase(v, 10) + 2);
    mpz_get_str(buf.data(), 10, v);
    return buf.data();
}

bool Same(const FullInt64& x, const mpz_t expected)
{
    Mpz t;
    ToMpz(x, t.v);
    return mpz_cmp(t.v, expected) == 0;
}

std::mt19937_64 rng(20261114);

FullInt64 RandomValue(int words)
{
    std::vector<uint64_t> w(words);
    int kind = (int)(rng() % 8);
    for (auto& x : w) x = rng();
    if (kind == 0) for (auto& x : w) x = ~0ull;                         // all ones
    if (kind == 1) { for (auto& x : w) x = 0; w[0] = 1ull << (rng() % 64); }   // a power of two
    if (kind == 2 && words > 1) w[words - 1] = 0;                        // low zero word
    if (w[0] == 0) w[0] = 1;
    return FullInt64((rng() & 1) != 0, fullint::CSpan(w));
}

std::string Desc(const char* op, const FullInt64& a, const FullInt64& b)
{
    return std::string(op) + " a(" + std::to_string(a.Length()) + (a.IsNegative() ? "w,-) b(" : "w,+) b(") +
           std::to_string(b.Length()) + (b.IsNegative() ? "w,-)" : "w,+)");
}

void CheckPair(const FullInt64& a, const FullInt64& b)
{
    Mpz ma, mb, e, e2;
    ToMpz(a, ma.v);
    ToMpz(b, mb.v);

    mpz_add(e.v, ma.v, mb.v);
    CHECK(Same(a + b, e.v), Desc("add", a, b));
    mpz_sub(e.v, ma.v, mb.v);
    CHECK(Same(a - b, e.v), Desc("sub", a, b));
    mpz_mul(e.v, ma.v, mb.v);
    CHECK(Same(a * b, e.v), Desc("mul", a, b));

    if (!b.IsZero())
    {
        mpz_tdiv_qr(e.v, e2.v, ma.v, mb.v);   // truncating, as C#/BigInteger
        CHECK(Same(a / b, e.v), Desc("div", a, b));
        CHECK(Same(a % b, e2.v), Desc("mod", a, b));
        FullInt64 r;
        FullInt64 q = fullint::FullInt64Engine::div(a, b, r);
        CHECK(Same(q, e.v) && Same(r, e2.v), Desc("divrem", a, b));
    }

    mpz_and(e.v, ma.v, mb.v);
    CHECK(Same(a & b, e.v), Desc("and", a, b));
    mpz_ior(e.v, ma.v, mb.v);
    CHECK(Same(a | b, e.v), Desc("or", a, b));
    mpz_xor(e.v, ma.v, mb.v);
    CHECK(Same(a ^ b, e.v), Desc("xor", a, b));

    int c = mpz_cmp(ma.v, mb.v);
    c = c < 0 ? -1 : c > 0 ? 1 : 0;
    CHECK(fullint::FullInt64Engine::Compare(a, b) == c, Desc("compare", a, b));
    CHECK((a == b) == (c == 0) && (a < b) == (c < 0) && (a > b) == (c > 0), Desc("relational", a, b));
}

void CheckSingle(const FullInt64& a)
{
    Mpz ma, e;
    ToMpz(a, ma.v);

    std::string dec = MpzDec(ma.v);
    CHECK(a.ToString() == dec, "ToString(10) " + dec.substr(0, 40));
    CHECK(FullInt64::Parse(dec) == a, "Parse(dec) " + dec.substr(0, 40));

    std::string hex = a.ToString(16);
    std::vector<char> buf(mpz_sizeinbase(ma.v, 16) + 2);
    mpz_abs(e.v, ma.v);
    mpz_get_str(buf.data(), -16, e.v);   // upper case
    CHECK(hex == std::string(a.IsNegative() ? "-0x" : "0x") + buf.data(), "ToString(16) " + hex.substr(0, 40));
    CHECK(FullInt64::Parse(hex) == a, "Parse(hex) " + hex.substr(0, 40));

    for (int base : { 2, 8 })
    {
        std::vector<char> b2(mpz_sizeinbase(ma.v, base) + 2);
        mpz_get_str(b2.data(), base, ma.v);
        CHECK(a.ToString(base) == b2.data(), "ToString(" + std::to_string(base) + ")");
    }
    std::vector<char> oct(mpz_sizeinbase(e.v, 8) + 2);
    mpz_get_str(oct.data(), 8, e.v);
    CHECK(FullInt64::Parse(std::string(a.IsNegative() ? "-0" : "0") + oct.data()) == a, "Parse(octal)");

    mpz_neg(e.v, ma.v);
    CHECK(Same(-a, e.v), "negate");
    mpz_com(e.v, ma.v);
    CHECK(Same(~a, e.v), "not");

    for (int s : { 0, 1, 13, 63, 64, 65, 127, 128, 200, 1000 })
    {
        mpz_mul_2exp(e.v, ma.v, s);
        CHECK(Same(a << s, e.v), "shl " + std::to_string(s));
        mpz_fdiv_q_2exp(e.v, ma.v, s);   // arithmetic: rounds toward negative infinity
        CHECK(Same(a >> s, e.v), "shr " + std::to_string(s));
        CHECK(Same(a << -s, e.v), "shl negative " + std::to_string(s));
    }
}

void EdgeCases()
{
    CHECK(FullInt64() == FullInt64(0) && !FullInt64().IsNegative(), "zero");
    CHECK(FullInt64::Parse("-0") == FullInt64(0) && !FullInt64::Parse("-0").IsNegative(), "-0 is zero");
    CHECK((FullInt64(5) - FullInt64(5)).IsNegative() == false, "5 - 5 not negative");
    CHECK((FullInt64(-3) * FullInt64(0)).IsNegative() == false, "-3 * 0 not negative");
    CHECK(FullInt64(INT64_MIN).ToString() == "-9223372036854775808", "INT64_MIN");
    CHECK(FullInt64(UINT64_MAX).ToString() == "18446744073709551615", "UINT64_MAX");
    CHECK(FullInt64::Parse("") == FullInt64(0), "empty parses as 0");
    CHECK(FullInt64::Parse("0x1F") == FullInt64(31), "0x prefix");
    CHECK(FullInt64::Parse("x1f") == FullInt64(31), "x prefix");
    CHECK(FullInt64::Parse("017") == FullInt64(15), "octal prefix");

    bool threw = false;
    try { (void)(FullInt64(1) / FullInt64(0)); } catch (const std::domain_error&) { threw = true; }
    CHECK(threw, "division by zero throws");
    threw = false;
    try { (void)FullInt64::Parse("12a"); } catch (const std::invalid_argument&) { threw = true; }
    CHECK(threw, "bad digit throws");
    threw = false;
    try { (void)Pow(FullInt64(2), FullInt64(-1)); } catch (const std::out_of_range&) { threw = true; }
    CHECK(threw, "negative exponent throws");

    // copy / move / self-assignment, inline and heap
    for (int words : { 1, 4, 5, 40 })
    {
        FullInt64 a = RandomValue(words);
        FullInt64 b(a);
        CHECK(a == b, "copy");
        FullInt64 c(std::move(b));
        CHECK(c == a && b == FullInt64(0), "move");
        c = c;
        CHECK(c == a, "self copy-assign");
        FullInt64 d = RandomValue(70);
        d = a;
        CHECK(d == a, "copy-assign over heap");
        d = std::move(c);
        CHECK(d == a, "move-assign");
        FullInt64 bytes = FullInt64::FromBytes(std::span<const uint8_t>((const uint8_t*)a.Words().data(), 0), false);
        CHECK(bytes == FullInt64(0), "FromBytes empty");
    }
    {
        uint8_t le[] = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        CHECK(FullInt64::FromBytes(le, true).ToString(16) == "-0x90807060504030201", "FromBytes");
    }

    Mpz e;
    for (int p : { 0, 1, 2, 7, 64, 100 })
    {
        mpz_ui_pow_ui(e.v, 3, p);
        CHECK(Same(Pow(FullInt64(3), FullInt64(p)), e.v), "pow 3^" + std::to_string(p));
        mpz_set_si(e.v, -7);
        mpz_pow_ui(e.v, e.v, p);
        CHECK(Same(Pow(FullInt64(-7), FullInt64(p)), e.v), "pow -7^" + std::to_string(p));
    }
}

// the multiplication dispatcher with small thresholds, so every level runs on small sizes
void MultiplyThresholds()
{
    for (int round = 0; round < 1500; round++)
    {
        int la = 1 + (int)(rng() % 120), lb = 1 + (int)(rng() % 120);
        if (round % 50 == 0) { la = 300 + (int)(rng() % 900); lb = 300 + (int)(rng() % 900); }
        FullInt64 a = RandomValue(la), b = RandomValue(lb);
        std::vector<uint64_t> r(a.Length() + b.Length());
        int threshold = 2 + (int)(rng() % 10), toom3 = 3 + (int)(rng() % 20);
        int toom4 = rng() % 3 == 0 ? 1 << 30 : 4 + (int)(rng() % 40);   // Toom-4 levels too
        int toom6 = rng() % 3 == 0 ? 1 << 30 : 6 + (int)(rng() % 60);   // Toom-6 levels too (from 36 words)
        fullint::Karatsuba::Multiply(a.Words(), b.Words(), r, threshold, toom3, toom4, toom6, fullint::Karatsuba::FftThreshold);
        Mpz ma, mb, e, got;
        ToMpz(Abs(a), ma.v);
        ToMpz(Abs(b), mb.v);
        mpz_mul(e.v, ma.v, mb.v);
        mpz_import(got.v, r.size(), 1, sizeof(uint64_t), 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "Karatsuba thresholds " + std::to_string(threshold) + "/" + std::to_string(toom3) + "/" +
                                            std::to_string(toom4) + "/" + std::to_string(toom6) + " " + std::to_string(la) + "x" + std::to_string(lb));
    }
}

// the Schönhage–Strassen FFT with forced K = 2^k on small and unbalanced numbers, and the
// dispatcher with small FFT thresholds (the pointwise products recurse), against GMP
void FftProducts()
{
    for (int round = 0; round < 800; round++)
    {
        int la = 1 + (int)(rng() % (round % 10 == 0 ? 400 : 60)), lb = 1 + (int)(rng() % (round % 10 == 0 ? 400 : 60));
        FullInt64 a = RandomValue(la), b = RandomValue(lb);
        std::vector<uint64_t> r(a.Length() + b.Length());
        int k = 1 + (int)(rng() % 9);
        fullint::Karatsuba::FftMultiply(a.Words(), b.Words(), r, k);
        Mpz ma, mb, e, got;
        ToMpz(Abs(a), ma.v);
        ToMpz(Abs(b), mb.v);
        mpz_mul(e.v, ma.v, mb.v);
        mpz_import(got.v, r.size(), 1, sizeof(uint64_t), 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "FFT k=" + std::to_string(k) + " " + std::to_string(la) + "x" + std::to_string(lb));
    }
    for (int round = 0; round < 12; round++)
    {
        int la = 100 + (int)(rng() % 600), lb = 100 + (int)(rng() % 600);
        FullInt64 a = RandomValue(la), b = RandomValue(lb);
        std::vector<uint64_t> r(a.Length() + b.Length());
        int fft = round % 3 == 0 ? 24 : round % 3 == 1 ? 48 : 100;
        fullint::Karatsuba::Multiply(a.Words(), b.Words(), r, 8, 24, 48, fft);
        Mpz ma, mb, e, got;
        ToMpz(Abs(a), ma.v);
        ToMpz(Abs(b), mb.v);
        mpz_mul(e.v, ma.v, mb.v);
        mpz_import(got.v, r.size(), 1, sizeof(uint64_t), 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "FFT threshold " + std::to_string(fft) + " " + std::to_string(la) + "x" + std::to_string(lb));
    }
}

// Burnikel–Ziegler with small thresholds (deep recursion on small numbers), against GMP;
// the shapes hit the Q̂ = β^k - 1 branch (b copied into the top of a, all ones) and divisors
// with small top words (large normalization shifts)
void DivideThresholds()
{
    for (int round = 0; round < 1500; round++)
    {
        int threshold = 2 + (int)(rng() % 32);
        int lb = 1 + (int)(rng() % 120), la = lb + (int)(rng() % 160);
        std::vector<uint64_t> a(la), b(lb);
        for (auto& x : a) x = rng();
        for (auto& x : b) x = rng();
        int shape = (int)(rng() % 5);
        if (shape == 0) { std::fill(a.begin(), a.end(), ~0ull); std::fill(b.begin(), b.end(), ~0ull); }
        if (shape == 1) b[0] = 1 + rng() % 15;
        if (shape == 2) std::copy(b.begin(), b.end(), a.begin());
        if (a[0] == 0) a[0] = 1;
        if (b[0] == 0) b[0] = 1;

        std::vector<uint64_t> q(la - lb + 1, 0xDEADBEEFDEADBEEFull), r(lb + 2, 0xDEADBEEFDEADBEEFull);
        fullint::BurnikelZiegler::DivRem(a, b, q, r, threshold);

        Mpz ma, mb, eq, er, gq, gr;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        mpz_tdiv_qr(eq.v, er.v, ma.v, mb.v);
        mpz_import(gq.v, q.size(), 1, 8, 0, 0, q.data());
        mpz_import(gr.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(eq.v, gq.v) == 0 && mpz_cmp(er.v, gr.v) == 0,
              "Burnikel–Ziegler threshold " + std::to_string(threshold) + " " + std::to_string(la) + "/" + std::to_string(lb));
    }
}

// squares (the same words as both operands): the square kernel, Karatsuba / Toom-3 / 4 / 6 with
// one evaluation, the FFT with one transform; small thresholds put every level on small
// numbers; all-ones and equal halves; the engine's x * x
void SquaresCheck()
{
    auto shaped = [](size_t n) {
        std::vector<uint64_t> w(n);
        for (auto& x : w) x = rng();
        int shape = (int)(rng() % 5);
        if (shape == 0) std::fill(w.begin(), w.end(), ~0ull);
        if (shape == 1) std::copy(w.begin() + n / 2, w.begin() + n / 2 + n / 2, w.begin());   // equal halves
        return w;
    };
    auto check = [](const std::vector<uint64_t>& a, const std::vector<uint64_t>& r, const std::string& what) {
        Mpz ma, e, got;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_mul(e.v, ma.v, ma.v);
        mpz_import(got.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, what + " " + std::to_string(a.size()));
    };
    for (int round = 0; round < 2000; round++)
    {
        size_t n = 1 + rng() % 259;
        auto a = shaped(n);
        std::vector<uint64_t> r(2 * n, 0xDEADBEEFDEADBEEFull);
        int t2 = 2 + (int)(rng() % 18), t3 = 3 + (int)(rng() % 57), t4 = 4 + (int)(rng() % 86), t6 = 6 + (int)(rng() % 114);
        if (rng() % 3 == 0) t3 = t4 = t6 = 1 << 30;
        int fft = rng() % 4 == 0 ? 8 + (int)(rng() % 56) : fullint::Karatsuba::FftThreshold;
        fullint::Karatsuba::Multiply(a, a, r, t2, t3, t4, t6, fft);
        check(a, r, "square thresholds " + std::to_string(t2) + "/" + std::to_string(t3) + "/" + std::to_string(t4) + "/" +
                        std::to_string(t6) + "/" + std::to_string(fft));
    }
    for (int round = 0; round < 60; round++)
    {
        size_t n = 1 + rng() % 199;
        auto a = shaped(n);
        std::vector<uint64_t> r(2 * n);
        fullint::Karatsuba::FftMultiply(a, a, r, 1 + (int)(rng() % 5));
        check(a, r, "FFT square");
        fullint::FullIntCore::Sqr(a, r);
        check(a, r, "Sqr kernel");
    }
    for (int round = 0; round < 40; round++)
    {
        FullInt64 x = RandomValue(1 + (int)(rng() % 600));
        FullInt64 sq = x * x;
        Mpz mx, e, got;
        ToMpz(Abs(x), mx.v);
        mpz_mul(e.v, mx.v, mx.v);
        ToMpz(sq, got.v);
        CHECK(mpz_cmp(e.v, got.v) == 0 && !sq.IsNegative(), "engine x * x");
    }
}

// the NTT product (NttAvx2.asm; x64 with AVX2) against GMP: the smallest transforms to a few
// hundred thousand digits, unbalanced, squares, all-ones and sparse operands; the dispatcher
void NttCheck()
{
    std::vector<uint64_t> probe(2);
    uint64_t one = 1;
    if (!fullint::Karatsuba::NttMultiply(fullint::CSpan(&one, 1), fullint::CSpan(&one, 1), probe)) return;
    auto shaped = [](size_t len) {
        std::vector<uint64_t> v(len);
        int kind = (int)(rng() % 6);
        for (size_t i = 0; i < len; i++) v[i] = kind == 0 ? ~0ull : kind == 1 ? (i % 3 == 0 ? 0 : ~0ull) : rng();
        return v;
    };
    for (int round = 0; round < 700; round++)
    {
        size_t max = round % 50 == 0 ? 40000 : round % 5 == 0 ? 3000 : 70;
        size_t la = 1 + rng() % (max - 1), lb = 1 + rng() % (max - 1);
        auto a = shaped(la);
        auto b = round % 4 == 0 ? a : shaped(lb);
        bool square = round % 4 == 0;
        std::vector<uint64_t> r(a.size() + b.size(), 0xDEADBEEFDEADBEEFull);
        fullint::CSpan sb = square ? fullint::CSpan(a) : fullint::CSpan(b);
        CHECK(fullint::Karatsuba::NttMultiply(a, sb, r), "NttMultiply available");
        Mpz ma, mb, e, got;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        mpz_mul(e.v, ma.v, mb.v);
        mpz_import(got.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "NttMultiply " + std::to_string(a.size()) + "x" + std::to_string(b.size()));
    }
    for (int round = 0; round < 6; round++)
    {
        auto a = shaped(1000 + rng() % 8000);
        auto b = round == 0 ? a : shaped(1000 + rng() % 8000);
        std::vector<uint64_t> r(a.size() + b.size());
        fullint::Karatsuba::Multiply(a, round == 0 ? fullint::CSpan(a) : fullint::CSpan(b), r);
        Mpz ma, mb, e, got;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        mpz_mul(e.v, ma.v, mb.v);
        mpz_import(got.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "Multiply (NTT) " + std::to_string(a.size()) + "x" + std::to_string(b.size()));
    }
}

// the low half of a product (MulLoAdx.asm and Mulders' recursion), against GMP mod β^n
void MultiplyLowCheck()
{
    int saved = fullint::Karatsuba::MulLoThreshold;
    for (int round = 0; round < 1500; round++)
    {
        size_t la = 1 + rng() % 599, lb = 1 + rng() % 599;
        size_t n = rng() % 3 == 0 ? la + rng() % 3 : 1 + rng() % (la + lb);
        std::vector<uint64_t> a(la), b(lb), r(n, 0xDEADBEEFDEADBEEFull);
        for (auto& x : a) x = rng();
        for (auto& x : b) x = rng();
        if (round % 5 == 0) std::fill(a.begin(), a.end(), ~0ull);
        if (round % 7 == 0) std::fill(b.begin(), b.end(), ~0ull);
        fullint::Karatsuba::MulLoThreshold = round % 2 == 0 ? saved : 4 + (int)(rng() % 60);
        fullint::Karatsuba::MultiplyLow(a, b, r);
        Mpz ma, mb, e, got;
        mpz_import(ma.v, la, 1, 8, 0, 0, a.data());
        mpz_import(mb.v, lb, 1, 8, 0, 0, b.data());
        mpz_mul(e.v, ma.v, mb.v);
        mpz_tdiv_r_2exp(e.v, e.v, 64 * n);
        mpz_import(got.v, n, 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "MultiplyLow " + std::to_string(la) + "x" + std::to_string(lb) + " n " + std::to_string(n));
    }
    fullint::Karatsuba::MulLoThreshold = saved;
}

// products modulo β^m - 1: the cyclic FFT forced from small sizes and the folded full product;
// all-ones operands (the β^m - 1 = 0 representation)
void MultiplyModCheck()
{
    for (int round = 0; round < 300; round++)
    {
        size_t minWords = 8 + rng() % 392;
        size_t cyclicFrom = rng() % 3 == 0 ? (size_t)1 << 30 : 8;
        size_t m = fullint::Karatsuba::MulModLength(minWords, cyclicFrom);
        CHECK(m >= minWords, "MulModLength");
        std::vector<uint64_t> a(1 + rng() % m), b(1 + rng() % m), r(m, 0xDEADBEEFDEADBEEFull);
        for (auto& x : a) x = rng();
        for (auto& x : b) x = rng();
        if (rng() % 5 == 0) std::fill(a.begin(), a.end(), ~0ull);
        if (rng() % 5 == 0) std::fill(b.begin(), b.end(), ~0ull);
        if (rng() % 4 == 0) b = a;   // a square (the same words: one transform)
        fullint::Karatsuba::MultiplyMod(a, b == a ? fullint::CSpan(a) : fullint::CSpan(b), r, cyclicFrom);
        Mpz ma, mb, mod, e, got;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        mpz_set_ui(mod.v, 1);
        mpz_mul_2exp(mod.v, mod.v, 64 * m);
        mpz_sub_ui(mod.v, mod.v, 1);
        mpz_mul(e.v, ma.v, mb.v);
        mpz_mod(e.v, e.v, mod.v);
        mpz_import(got.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(e.v, got.v) == 0, "MultiplyMod " + std::to_string(a.size()) + "x" + std::to_string(b.size()) + " m " + std::to_string(m));
    }
}

// the quotient alone: small thresholds (the approximation recurses on small numbers), long
// divisors with short quotients (cut down), exact multiples A = B·Q (+ 0/1) put the guard word
// at the boundary (the fallback to the full division)
void QuotientOnly()
{
    auto shaped = [](size_t len) {
        std::vector<uint64_t> w(len);
        for (auto& x : w) x = rng();
        int shape = (int)(rng() % 5);
        if (shape == 0) std::fill(w.begin(), w.end(), ~0ull);
        if (shape == 1) w[0] = 1 + rng() % 15;
        if (w[0] == 0) w[0] = 1;
        return w;
    };
    const int thresholds[] = { 2, 3, 4, 5, 8, 16, 33, 96 };
    for (int round = 0; round < 3000; round++)
    {
        int threshold = thresholds[rng() % 8];
        size_t lb = 1 + rng() % 150, la = lb + (rng() % 3 == 0 ? rng() % 8 : rng() % 250);
        auto b = shaped(lb);
        std::vector<uint64_t> a;
        int kind = (int)(rng() % 6);
        Mpz ma, mb, mq, got;
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        if (kind == 0)
        {
            auto qv = shaped(std::max<size_t>(1, la - lb));
            mpz_import(mq.v, qv.size(), 1, 8, 0, 0, qv.data());
            mpz_mul(ma.v, mb.v, mq.v);
            if (rng() % 2) mpz_add_ui(ma.v, ma.v, 1);
            size_t words = (mpz_sizeinbase(ma.v, 2) + 63) / 64;
            a.assign(words, 0);
            mpz_export(a.data() + (words - (mpz_sizeinbase(ma.v, 2) + 63) / 64), nullptr, 1, 8, 0, 0, ma.v);
        }
        else
        {
            a = shaped(la);
            if (kind == 1) std::copy(b.begin(), b.end(), a.begin());
            mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        }
        if (a.size() < b.size()) continue;
        std::vector<uint64_t> q(a.size() - b.size() + 1, 0xDEADBEEFDEADBEEFull);
        int savedSplit = fullint::BurnikelZiegler::QuotientSplitFrom;
        if (round % 2) fullint::BurnikelZiegler::QuotientSplitFrom = 2 + (int)(rng() % 40);   // splits on small numbers
        fullint::BurnikelZiegler::Quotient(a, b, q, threshold);
        fullint::BurnikelZiegler::QuotientSplitFrom = savedSplit;
        mpz_tdiv_q(mq.v, ma.v, mb.v);
        mpz_import(got.v, q.size(), 1, 8, 0, 0, q.data());
        CHECK(mpz_cmp(mq.v, got.v) == 0, "quotient alone threshold " + std::to_string(threshold) + " kind " + std::to_string(kind) +
                                             " " + std::to_string(a.size()) + "/" + std::to_string(lb));
    }
}

// division through the Newton reciprocal: small reciprocal bases and block splits make every
// path run on small numbers; a few at the sizes the engine sends there; the reciprocal itself
void NewtonDivide()
{
    auto check = [](const std::vector<uint64_t>& a, const std::vector<uint64_t>& b, int split, int base, size_t cyclicFrom = 0) {
        std::vector<uint64_t> q(a.size() - b.size() + 1, 0xDEADBEEFDEADBEEFull), r(b.size() + 2, 0xDEADBEEFDEADBEEFull);
        fullint::NewtonDivision::DivRem(a, b, q, r, split, base, cyclicFrom);
        Mpz ma, mb, eq, er, gq, gr;
        mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
        mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
        mpz_tdiv_qr(eq.v, er.v, ma.v, mb.v);
        mpz_import(gq.v, q.size(), 1, 8, 0, 0, q.data());
        mpz_import(gr.v, r.size(), 1, 8, 0, 0, r.data());
        CHECK(mpz_cmp(eq.v, gq.v) == 0 && mpz_cmp(er.v, gr.v) == 0,
              "Newton division split " + std::to_string(split) + " base " + std::to_string(base) + " cyclic " + std::to_string(cyclicFrom) + " " +
                  std::to_string(a.size()) + "/" + std::to_string(b.size()));
    };
    auto shaped = [](size_t len) {
        std::vector<uint64_t> w(len);
        for (auto& x : w) x = rng();
        int shape = (int)(rng() % 6);
        if (shape == 0) std::fill(w.begin(), w.end(), ~0ull);
        if (shape == 1) w[0] = 1 + rng() % 15;
        if (shape == 2) { std::fill(w.begin(), w.end(), 0); w[0] = 1ull << (rng() % 64); }
        if (w[0] == 0) w[0] = 1;
        return w;
    };
    const int bases[] = { 2, 3, 4, 5, 8, 16, 64 };
    for (int round = 0; round < 1500; round++)
    {
        int base = bases[rng() % 7], split = (int)(rng() % 4);
        size_t lb = 1 + rng() % 120, la = lb + rng() % 300;
        auto a = shaped(la), b = shaped(lb);
        if (rng() % 4 == 0) std::copy(b.begin(), b.end(), a.begin());
        check(a, b, split, base, rng() % 2 ? 0 : 8 + rng() % 56);   // the products modulo β^m - 1 on small numbers
    }
    check(shaped(20000), shaped(9000), 0, fullint::NewtonDivision::InverseBase);
    check(shaped(30000), shaped(8200), 0, fullint::NewtonDivision::InverseBase);

    // the quotient alone (a guard word, the last block's remainder skipped): random operands,
    // exact multiples A = B·Q (guard word 0: the fallback) and B·Q - 1 (the top boundary)
    {
        int savedSplit = fullint::NewtonDivision::Split, savedBase = fullint::NewtonDivision::InverseBase;
        int savedCyclic = fullint::NewtonDivision::CyclicFrom;
        for (int round = 0; round < 1500; round++)
        {
            fullint::NewtonDivision::Split = (int)(rng() % 4);
            fullint::NewtonDivision::InverseBase = bases[rng() % 7];
            fullint::NewtonDivision::CyclicFrom = rng() % 2 ? 1 << 30 : 8 + (int)(rng() % 56);
            size_t lb = 1 + rng() % 120, la = lb + rng() % 300;
            auto a = shaped(la), b = shaped(lb);
            int kind = (int)(rng() % 4);
            Mpz ma, mb, eq, gq;
            mpz_import(mb.v, b.size(), 1, 8, 0, 0, b.data());
            if (kind >= 2)
            {
                auto qv = shaped(1 + rng() % 200);
                Mpz mq;
                mpz_import(mq.v, qv.size(), 1, 8, 0, 0, qv.data());
                mpz_mul(ma.v, mq.v, mb.v);
                if (kind == 3 && mpz_sgn(ma.v) > 0) mpz_sub_ui(ma.v, ma.v, 1);
                size_t words = (mpz_sizeinbase(ma.v, 2) + 63) / 64;
                a.assign(std::max(words, b.size()), 0);
                size_t count = 0;
                mpz_export(a.data() + (a.size() - words), &count, 1, 8, 0, 0, ma.v);
            }
            else
            {
                mpz_import(ma.v, a.size(), 1, 8, 0, 0, a.data());
            }
            if (a.size() < b.size()) continue;
            std::vector<uint64_t> q(a.size() - b.size() + 1, 0xDEADBEEFDEADBEEFull);
            fullint::NewtonDivision::Quotient(a, b, q);
            mpz_tdiv_q(eq.v, ma.v, mb.v);
            mpz_import(gq.v, q.size(), 1, 8, 0, 0, q.data());
            CHECK(mpz_cmp(eq.v, gq.v) == 0, "Newton quotient kind " + std::to_string(kind) + " " + std::to_string(a.size()) + "/" + std::to_string(b.size()));
        }
        fullint::NewtonDivision::Split = savedSplit;
        fullint::NewtonDivision::InverseBase = savedBase;
        fullint::NewtonDivision::CyclicFrom = savedCyclic;
    }

    for (int round = 0; round < 300; round++)
    {
        size_t s = 1 + rng() % 200;
        auto d = shaped(s);
        d[0] |= 1ull << 63;
        auto inv = fullint::NewtonDivision::Inverse(d, bases[rng() % 7], rng() % 2 ? (size_t)1 << 30 : 8 + rng() % 56);
        Mpz md, exact, got;
        mpz_import(md.v, d.size(), 1, 8, 0, 0, d.data());
        mpz_set_ui(exact.v, 1);
        mpz_mul_2exp(exact.v, exact.v, 128 * s);
        mpz_tdiv_q(exact.v, exact.v, md.v);
        mpz_import(got.v, inv.size(), 1, 8, 0, 0, inv.data());
        mpz_sub(got.v, got.v, exact.v);
        mpz_abs(got.v, got.v);
        CHECK(mpz_cmp_ui(got.v, 4) <= 0, "Newton inverse s " + std::to_string(s));
    }
}

// the in-place API (Add(result, a, b) etc.) and the compound operators against the plain
// operators: result fresh, reused across sizes, and the same object as a or b
void InPlaceApi()
{
    FullInt64 shared;
    for (int round = 0; round < 3000; round++)
    {
        int la = 1 + (int)(rng() % (round % 3 == 0 ? 90 : 9)), lb = 1 + (int)(rng() % (round % 3 == 0 ? 90 : 9));
        FullInt64 a = RandomValue(la), b = RandomValue(lb);
        int shift = (int)(rng() % 200);
        std::string ctx = std::to_string(la) + "/" + std::to_string(lb) + " round " + std::to_string(round);

        fullint::Add(shared, a, b);
        CHECK(shared == a + b, "Add " + ctx);
        fullint::Subtract(shared, a, b);
        CHECK(shared == a - b, "Subtract " + ctx);
        fullint::Multiply(shared, a, b);
        CHECK(shared == a * b, "Multiply " + ctx);
        fullint::ShiftLeft(shared, a, shift);
        CHECK(shared == (a << shift), "ShiftLeft " + ctx);
        fullint::ShiftRight(shared, a, shift);
        CHECK(shared == (a >> shift), "ShiftRight " + ctx);
        fullint::And(shared, a, b);
        CHECK(shared == (a & b), "And " + ctx);
        fullint::Or(shared, a, b);
        CHECK(shared == (a | b), "Or " + ctx);
        fullint::Xor(shared, a, b);
        CHECK(shared == (a ^ b), "Xor " + ctx);
        if (!b.IsZero())
        {
            fullint::Divide(shared, a, b);
            CHECK(shared == a / b, "Divide " + ctx);
            fullint::Remainder(shared, a, b);
            CHECK(shared == a % b, "Remainder " + ctx);
            FullInt64 d(a), m(a), e(b);
            fullint::Divide(d, d, b);
            CHECK(d == a / b, "Divide into a " + ctx);
            fullint::Remainder(m, m, b);
            CHECK(m == a % b, "Remainder into a " + ctx);
            fullint::Divide(e, a, e);
            CHECK(e == a / b, "Divide into b " + ctx);
        }
        FullInt64 g(a), h(b);
        g &= b;
        CHECK(g == (a & b), "&= " + ctx);
        h ^= a;
        CHECK(h == (a ^ b), "^= " + ctx);

        FullInt64 x(a);   // into a
        fullint::Add(x, x, b);
        CHECK(x == a + b, "Add into a " + ctx);
        fullint::Subtract(x, x, b);
        CHECK(x == a, "Subtract into a " + ctx);
        fullint::Multiply(x, x, b);
        CHECK(x == a * b, "Multiply into a " + ctx);
        FullInt64 y(b);   // into b
        fullint::Add(y, a, y);
        CHECK(y == a + b, "Add into b " + ctx);
        fullint::Subtract(y, a, y);
        CHECK(y == -b, "Subtract into b " + ctx);
        FullInt64 z(a);   // both operands are the result
        fullint::Add(z, z, z);
        CHECK(z == a + a, "Add z = z + z " + ctx);

        FullInt64 c(a);   // compound operators, a few steps in a row (the carry room is reused)
        c += b; c += b; c -= a; c *= b; c -= b;
        CHECK(c == (a + b + b - a) * b - b, "compound " + ctx);
    }

    FullInt64 acc = FullInt64(UINT64_MAX) * FullInt64(UINT64_MAX);   // a long run of += 1
    FullInt64 expect = acc;
    for (int i = 0; i < 1000; i++) ++acc;
    CHECK(acc == expect + FullInt64(1000), "++ x1000");
}

// decimal text by halves against GMP: powers of ten and neighbours, zero runs, random
// digits; default and tiny thresholds (deep recursion)
void DecimalByHalves()
{
    std::vector<std::string> texts;
    for (int e : { 19, 38, 76, 152, 304, 608, 1216, 2432, 4864, 9728, 20000, 40000, 80000 })   // (the prepared powers' NTT transforms from ~20000)
    {
        std::string p = "1" + std::string(e, '0');
        texts.push_back(p);                          // 10^e
        texts.push_back(std::string(e, '9'));        // 10^e - 1
        texts.push_back("1" + std::string(e - 1, '0') + "1");   // 10^e + 1
    }
    for (int i = 0; i < 33; i++)
    {
        size_t digits = 20 + rng() % (i < 25 ? 3000 : i < 30 ? 30000 : 120000);
        std::string t(1, (char)('1' + rng() % 9));
        while (t.size() < digits)
            t += (rng() % 10 == 0) ? std::string(1 + rng() % 120, '0') : std::string(1, (char)('0' + rng() % 10));
        texts.push_back(t);
    }

    int savedTo = fullint::DecimalConversion::ToStringThreshold, savedParse = fullint::DecimalConversion::ParseThreshold;
    int savedPrepared = fullint::DecimalConversion::PreparedFrom;
    for (auto [to, parse] : { std::pair{ savedTo, savedParse }, std::pair{ 2, 20 }, std::pair{ 3, 39 }, std::pair{ 5, 100 } })
    {
        fullint::DecimalConversion::ToStringThreshold = to;
        fullint::DecimalConversion::ParseThreshold = parse;
        // small thresholds: the division by prepared powers from small powers too
        fullint::DecimalConversion::PreparedFrom = to == savedTo ? savedPrepared : 2 + to;
        for (auto& t : texts)
        {
            if (to != savedTo && t.size() > 5000) continue;
            for (const std::string& text : { t, "-" + t })
            {
                FullInt64 x = FullInt64::Parse(text);
                Mpz e, got;
                mpz_set_str(e.v, text.c_str(), 10);
                ToMpz(x, got.v);
                CHECK(mpz_cmp(e.v, got.v) == 0, "Parse by halves " + std::to_string(text.size()) + " chars, thresholds " + std::to_string(to));
                CHECK(x.ToString() == text, "ToString by halves " + std::to_string(text.size()) + " chars, thresholds " + std::to_string(to));
            }
        }
    }
    fullint::DecimalConversion::ToStringThreshold = savedTo;
    fullint::DecimalConversion::ParseThreshold = savedParse;
    fullint::DecimalConversion::PreparedFrom = savedPrepared;
}

}  // namespace

// Fullint64SubCmpNWIN against a plain compare + subtract: random words and the hard shapes (equal,
// differing only low, top words differing by one with long zero runs in the difference)
static void SubCmpNKernel()
{
    for (int round = 0; round < 20000; round++)
    {
        size_t n = 1 + rng() % (round % 20 == 0 ? 300 : 40);
        std::vector<uint64_t> a(n), b(n);
        for (auto& x : a) x = rng();
        int shape = (int)(rng() % 6);
        if (shape == 0) b = a;                                       // equal
        else if (shape == 1) { b = a; b[n - 1 - rng() % n] ^= 1ull << (rng() % 64); }   // one bit
        else if (shape == 2)                                         // a = b + 1 at some word: borrow run
        {
            for (auto& x : b) x = rng();
            size_t k = rng() % n;
            for (size_t i = 0; i < k; i++) a[i] = b[i];
            if (k < n)
            {
                b[k] = rng() >> 1; a[k] = b[k] + (rng() & 1);   // +1: the difference 1 at the bottom
                for (size_t i = k + 1; i < n; i++) { a[i] = 0; b[i] = ~0ull; }
                if (rng() & 1) std::swap(a, b);
            }
        }
        else if (shape == 3) { for (auto& x : b) x = rng(); }
        else if (shape == 4) { b = a; size_t k = rng() % n; for (size_t i = k; i < n; i++) b[i] = rng(); }
        else { for (auto& x : a) x = 0; for (auto& x : b) x = 0; a[rng() % n] = rng(); b[rng() % n] = rng(); }

        // reference
        size_t i = 0;
        while (i < n && a[i] == b[i]) i++;
        bool swap = i < n && a[i] < b[i];
        std::vector<uint64_t> e(n, 0);
        const auto& x = swap ? b : a; const auto& y = swap ? a : b;
        uint64_t borrow = 0;
        for (size_t j = n; j-- > i;)
        {
            uint64_t d = x[j] - y[j] - borrow;
            borrow = (x[j] < y[j]) || (x[j] == y[j] && borrow) ? 1 : 0;
            e[j] = d;
        }
        size_t off = i;
        while (off < n && e[off] == 0) off++;

        std::vector<uint64_t> r(n + 1, 0x5A5A5A5A5A5A5A5Aull);
        uint64_t got = Fullint64SubCmpNWIN(a.data(), b.data(), r.data(), (int64_t)n);
        bool ok = (uint32_t)got == off && (got >> 32 & 0x7FFFFFFF) == 0 && (off == n || (got >> 63 != 0) == swap);
        for (size_t j = off; ok && j < n; j++) ok = r[j] == e[j];
        ok = ok && r[n] == 0x5A5A5A5A5A5A5A5Aull;
        for (size_t j = 0; ok && j < i && i < n; j++) ok = r[j] == 0x5A5A5A5A5A5A5A5Aull;   // above i untouched
        CHECK(ok, "SubCmpN kernel");

#if defined(_M_X64) || defined(__x86_64__)
        // the header form (x64): off / size / neg written by the kernel, both its paths
        for (int neg0 = 0; neg0 < 2; neg0++)
        {
            uint32_t hdr[3] = { 0xDEADu, (uint32_t)n, (uint32_t)neg0 };
            std::vector<uint64_t> r2(n + 1, 0x5A5A5A5A5A5A5A5Aull);
            Fullint64SubCmpNHdrWIN(a.data(), b.data(), r2.data(), hdr);
            bool ok2;
            if (off == n)
                ok2 = r2[0] == 0 && hdr[0] == 0 && hdr[1] == 1 && (hdr[2] & 0xFF) == 0;
            else
            {
                ok2 = hdr[0] == off && hdr[1] == n - off && (hdr[2] & 0xFF) == (uint32_t)(neg0 ^ (swap ? 1 : 0));
                for (size_t j = off; ok2 && j < n; j++) ok2 = r2[j] == e[j];
            }
            ok2 = ok2 && r2[n] == 0x5A5A5A5A5A5A5A5Aull;
            CHECK(ok2, "SubCmpNHdr kernel");
        }
#endif
    }
}

int main()
{
    const int sizes[] = { 1, 2, 3, 4, 5, 8, 17, 63, 64, 65, 100, 255, 256, 300, 700, 1500 };

    EdgeCases();
    SubCmpNKernel();
    MultiplyThresholds();
    FftProducts();
    DivideThresholds();
    NewtonDivide();
    QuotientOnly();
    MultiplyModCheck();
    SquaresCheck();
    MultiplyLowCheck();
    NttCheck();
    InPlaceApi();
    DecimalByHalves();

    for (int sa : sizes)
        for (int sb : sizes)
            for (int rep = 0; rep < 3; rep++)
                CheckPair(RandomValue(sa), RandomValue(sb));

    for (int s : sizes)
        for (int rep = 0; rep < 4; rep++)
            CheckSingle(RandomValue(s));
    CheckSingle(FullInt64(0));
    CheckPair(FullInt64(0), RandomValue(3));
    CheckPair(RandomValue(3), FullInt64(0));

    std::printf("%s: %ld checks, %d failed\n", failures ? "FAILED" : "OK", checks, failures);
    return failures ? 1 : 0;
}
