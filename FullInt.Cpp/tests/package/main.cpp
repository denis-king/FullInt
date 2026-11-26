// A consumer of the FullInt C++ library (tests/package/CMakeLists.txt): links FullInt::FullInt
// and checks results that need the kernels and the fast algorithms - known values, and identities
// on numbers large enough for Karatsuba, Toom-Cook, the NTT and the fast divisions.
#include <fullnumerics/FullInt.h>

#include <cstdio>
#include <random>
#include <string>
#include <vector>

using fullnumerics::FullInt;

static int failed = 0;
static void Check(const char* what, bool ok)
{
    if (!ok) { failed++; std::printf("FAIL: %s\n", what); }
}

static FullInt Random(std::mt19937_64& rng, size_t words)
{
    std::vector<uint64_t> w(words);
    for (auto& x : w) x = rng();
    w.back() |= 1ull << 63;
    return FullInt::FromWords(w, fullnumerics::WordOrder::LeastSignificantFirst);
}

int main()
{
    Check("2^128", fullnumerics::Pow(FullInt(2), 128).ToString() == "340282366920938463463374607431768211456");
    Check("Parse", FullInt::Parse("-123456789012345678901234567890").ToString() == "-123456789012345678901234567890");
    Check("hex", FullInt::Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF") + FullInt(1) == fullnumerics::Pow(FullInt(2), 128));

    FullInt f(1);
    for (int i = 2; i <= 1000; i++) f = f * FullInt(i);
    std::string s = f.ToString();
    int digitSum = 0;
    for (char c : s) digitSum += c - '0';
    Check("1000! has 2568 digits", s.size() == 2568);
    Check("1000! digit sum 10539", digitSum == 10539);
    for (int i = 1000; i >= 2; i--) f = f / FullInt(i);
    Check("1000! / 1000 / ... / 2 == 1", f == FullInt(1));

    std::mt19937_64 rng(12345);
    for (size_t words : {1, 7, 40, 300, 3000, 30000})
    {
        FullInt x = Random(rng, words), y = Random(rng, words / 2 + 1);
        FullInt p = x * y;
        std::string at = " at " + std::to_string(words) + " words";
        Check(("x*y == y*x" + at).c_str(), p == y * x);
        Check(("(x*y)/y == x" + at).c_str(), p / y == x);
        Check(("(x*y)%y == 0" + at).c_str(), (p % y).IsZero());
        Check(("(x+y)(x-y) == x^2 - y^2" + at).c_str(), (x + y) * (x - y) == x * x - y * y);
        FullInt q, r;
        q = fullnumerics::DivRem(p + y - FullInt(1), y, r);
        Check(("DivRem" + at).c_str(), q == x && r == y - FullInt(1));
        if (words <= 3000) Check(("Parse(ToString)" + at).c_str(), FullInt::Parse(p.ToString()) == p);
    }

    std::printf(failed == 0 ? "OK\n" : "%d failed\n", failed);
    return failed == 0 ? 0 : 1;
}
