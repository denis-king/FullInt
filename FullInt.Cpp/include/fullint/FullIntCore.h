// Port of FullInt.Core/IFullIntCore.cs + FullIntCoreBase.cs: the span-level kernel calls.
//
// Kernel versions: the baseline kernels run on any x86-64; where the CPU has more (Cpu.h),
// the newer versions are called instead (Mult: BMI2 + ADX; And / Or / Xor, shifts, Compare: AVX2).
// Div by a reciprocal (DivRecip.asm) is plain x86-64: everywhere unless FULLINT_CPU=baseline.
//
// In .NET the core is an interface (asm on win-x64/x86, managed elsewhere) and every call
// pins the spans and crosses into the DLL. Here the asm kernels are linked into the same
// binary: a core call is a direct call to the kernel's ...WIN entry.
//
// Contracts (as in IFullIntCore.cs; limbs big-endian, index 0 = most significant word):
//   Add / Sub: r.size() >= max(a, b) (+1 for a carry word); Sub returns sign(|a| - |b|), r = ||a| - |b||
//   Mult:      r.size() == a.size() + b.size()
//   Div:       divisor without leading zero words; quotient a - b + 1 words, remainder b + 2
//   bitwise:   r.size() >= max(a, b) + 1, returns 1 when the result is negative
//   shifts:    ShiftLeft r >= a + shift / 64 + 1; ShiftRight r >= max(a - shift / 64, 1), r may alias a
#pragma once

#include <fullint/Cpu.h>
#include <fullint/FullHelper.h>

#include <utility>
#include <vector>
#include <fullint/kernels.h>

#include <cstdint>
#include <iterator>
#include <memory>
#include <span>

namespace fullint {

using Span = std::span<uint64_t>;
using CSpan = std::span<const uint64_t>;

struct FullIntCore
{
#if FULLINT_WORD64
    static constexpr size_t RecipWordsMin = 64;
#else
    static constexpr size_t RecipWordsMin = 1;   // x86: the baseline's d >= 2^32 path is fi_div128 per word
#endif

    // scratch of the reciprocal division (its dividend copy): per thread, grown on demand
    static uint64_t* DivWork(size_t words)
    {
        thread_local std::unique_ptr<uint64_t[]> buffer;
        thread_local size_t capacity = 0;
        if (capacity < words)
        {
            buffer = std::make_unique_for_overwrite<uint64_t[]>(words);
            capacity = words;
        }
        return buffer.get();
    }

    // r = a + b; returns the carry out of r's top word. r at least as long as a and b, at most
    // one word longer: AddSubN.asm on the shorter operand's words and the carry through the
    // other one's top here (the general kernel resolves lengths and runs, which cost more than
    // the words on a few: 4.9 ns on one word against 1.4); otherwise Add.asm.
    FULLINT_FORCEINLINE static uint64_t Add(CSpan a, CSpan b, Span r) noexcept
    {
        if (a.size() < b.size()) std::swap(a, b);
        size_t la = a.size(), lb = b.size(), lr = r.size();
        if (lb == 0 || lr < la || lr > la + 1)
            return Fullint64AddWIN(a.data(), b.data(), r.data(), (int)la, (int)lb, (int)lr);
        uint64_t c = Fullint64AddNWIN(a.data() + (la - lb), b.data(), r.data() + (lr - lb), (int64_t)lb);
        for (size_t i = la - lb; i-- > 0;)   // a's top words with the carry (r's words line up with a's at the low end)
        {
            uint64_t v = a[i] + c;
            c = v < c ? 1 : 0;
            r[lr - la + i] = v;
        }
        if (lr == la) return c;
        r[0] = c;
        return 0;
    }

    // r = |a - b|; returns the sign of a - b. Equal lengths: compared from the top here, then
    // AddSubN.asm on the larger minus the smaller; otherwise Sub.asm (it skips leading zeros).
    FULLINT_FORCEINLINE static int Sub(CSpan a, CSpan b, Span r) noexcept
    {
        size_t n = a.size();
        if (n != b.size() || r.size() < n || n == 0)
            return Fullint64SubWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
        size_t i = 0;
        while (i < n && a[i] == b[i]) i++;
        size_t top = r.size() - n;
        for (size_t k = 0; k < top; k++) r[k] = 0;
        if (i == n)
        {
            for (size_t k = top; k < r.size(); k++) r[k] = 0;
            return 0;
        }
        // the words above i are equal: their difference is zero, the rest is n - i words
        bool aBig = a[i] > b[i];
        const uint64_t* x = (aBig ? a : b).data() + i;
        const uint64_t* y = (aBig ? b : a).data() + i;
        Fullint64SubNWIN(x, y, r.data() + top + i, (int64_t)(n - i));
        for (size_t k = top; k < top + i; k++) r[k] = 0;
        return aBig ? 1 : -1;
    }

    static void Mult(CSpan a, CSpan b, Span r) noexcept
    {
#if FULLINT_X86_FAMILY
        if (Cpu::Has(Cpu::Bmi2 | Cpu::Adx))   // mulx / adcx / adox (MultAdx.asm; x86: in 32-bit digits)
        {
            Fullint64MultWIN_adx(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
            return;
        }
#endif
        Fullint64MultWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
    }

    // r = a² (r.size() == 2·a.size(), r not a): the products of different words once, then
    // doubled. SqrAdx.asm with BMI2 + ADX (x64 and x86); without them x64 Sqr.asm (mul / adc),
    // AArch64 Sqr.s, x86 the multiplication a·a
    static void Sqr(CSpan a, Span r) noexcept
    {
#if FULLINT_X86_FAMILY
        if (Cpu::Has(Cpu::Bmi2 | Cpu::Adx))
        {
            Fullint64SqrWIN_adx(a.data(), r.data(), (int)a.size(), (int)r.size());
            return;
        }
#endif
#if FULLINT_X64 || FULLINT_ARM64
        Fullint64SqrWIN(a.data(), r.data(), (int)a.size(), (int)r.size());
#else
        Mult(a, a, r);
#endif
    }

    static void Div(CSpan dividend, CSpan divisor, Span quotient, Span remainder) noexcept
    {
#if FULLINT_WORD64
        if (divisor.size() == 2 && dividend.size() > 2 && Cpu::Has(Cpu::Recip))
        {
            DivBy2(dividend, divisor, quotient, remainder);
            return;
        }
#endif
        // DivRecip.asm; equal lengths (a quotient of 0 or 1) stay with the baseline: it decides
        // those by a compare, the reciprocal's own div would not pay off
        if (Cpu::Has(Cpu::Recip) && divisor.size() >= 2 && dividend.size() > divisor.size())
        {
            size_t words = dividend.size() + divisor.size() + 2;
            uint64_t stackWork[96];   // small: no thread_local lookup (a few ns at 3 words)
            uint64_t* work = words <= std::size(stackWork) ? stackWork : DivWork(words);
#if FULLINT_X64
            // the multiply-subtract pass on mulx / adcx / adox: 1.06-1.10x from 64-word divisors
            // (the Burnikel–Ziegler leaves), even at 16..32, slower below (its set-up per step)
            if (divisor.size() >= 16 && Cpu::Has(Cpu::Bmi2 | Cpu::Adx))
            {
                Fullint64DivWIN_recipadx(dividend.data(), divisor.data(), quotient.data(), remainder.data(),
                                         (int)dividend.size(), (int)divisor.size(), work);
                return;
            }
#endif
            Fullint64DivWIN_recip(dividend.data(), divisor.data(), quotient.data(), remainder.data(),
                                  (int)dividend.size(), (int)divisor.size(), work);
            return;
        }
        Fullint64DivWin(dividend.data(), divisor.data(), quotient.data(), remainder.data(), (int)dividend.size(), (int)divisor.size());
    }

    // The quotient alone (a / b; the Div contract, quotient.size() = lenA - lenB + 1, no
    // remainder): DivRecip.asm's approximate quotient (the multiply-subtract passes of the last
    // lenB steps cut short: 2N / N about half the work) with a guard word below it -
    // floor(A*beta / B) within 1, so the words above are exact unless the guard is 0 or beta - 1
    // (an exact multiple: 0): then the true quotient is they or one less / one more, decided by
    // q*B against A (one product, as GMP's mpn_div_q). Saturated (or without the kernel): Div.
    // below: the guard word's extra step costs more than the shorter passes save. Kernel, 2N / N
    // against Div (the kernel keeping min(lenB, steps left + 2) words of B): x64 (Zen 3) 6 words
    // 1.0, 8 0.95, 12 0.8, 16 0.77, 32 0.67; x86 6 1.03, 8 0.93, 12 0.88, 16 0.77
#if FULLINT_WORD64
    static constexpr size_t QuotientWordsMin = 10;
#else
    static constexpr size_t QuotientWordsMin = 8;
#endif

    static void Quotient(CSpan dividend, CSpan divisor, Span quotient) noexcept
    {
        size_t la = dividend.size(), lb = divisor.size(), lq = quotient.size();
        if (Cpu::Has(Cpu::Recip) && lb >= QuotientWordsMin && la > lb)
        {
            size_t words = (lq + 1) + (la + 3);
            uint64_t stackWork[96];
            uint64_t* qa = words <= std::size(stackWork) ? stackWork : DivWork(words);
            uint64_t* work = qa + lq + 1;
#if FULLINT_X64
            uint64_t saturated = Cpu::Has(Cpu::Bmi2 | Cpu::Adx)
                ? Fullint64DivQWIN_recipadx(dividend.data(), divisor.data(), qa, nullptr, (int)la, (int)lb, work)
                : Fullint64DivQWIN_recip(dividend.data(), divisor.data(), qa, nullptr, (int)la, (int)lb, work);
#else
            uint64_t saturated = Fullint64DivQWIN_recip(dividend.data(), divisor.data(), qa, nullptr, (int)la, (int)lb, work);
#endif
            if (!saturated)
            {
                std::copy(qa, qa + lq, quotient.data());
                if (qa[lq] + 1 > 1) return;   // the guard neither 0 nor beta - 1
                QuotientFix(dividend, divisor, quotient, Span(work, la + 1));
                return;
            }
        }
        size_t rn = divisor.size() + 2;
        if (rn > 64)
        {
            QuotientByDivLong(dividend, divisor, quotient);
            return;
        }
        uint64_t r[64];
        std::fill(r, r + rn, 0);   // the kernels take both zeroed (as the engine leaves them)
        std::fill(quotient.begin(), quotient.end(), 0);
        Div(dividend, divisor, quotient, Span(r, rn));
    }

    // (out of line: the vector would give Quotient an unwind frame on every short call)
    FULLINT_NOINLINE static void QuotientByDivLong(CSpan dividend, CSpan divisor, Span quotient)
    {
        std::vector<uint64_t> r(divisor.size() + 2);
        std::fill(quotient.begin(), quotient.end(), 0);
        Div(dividend, divisor, quotient, Span(r.data(), r.size()));
    }

    // q within 1 of floor(A / B): fixed by D = A - q*B (p: la + 1 words of scratch)
    static void QuotientFix(CSpan a, CSpan b, Span q, Span p) noexcept
    {
        size_t la = a.size();
        Mult(q, b, p);   // lq + lb = la + 1 words
        uint64_t borrow = 0;
        for (size_t i = la + 1; i-- > 0;)   // p = [0, A] - p
        {
            uint64_t x = i ? a[i - 1] : 0, y = p[i];
            uint64_t d = x - y;
            uint64_t b1 = x < y ? 1 : 0;
            p[i] = d - borrow;
            borrow = b1 | (d < borrow ? 1 : 0);
        }
        if (borrow)   // q*B > A: q - 1
        {
            for (size_t i = q.size(); i-- > 0;)
                if (q[i]-- != 0) break;
            return;
        }
        size_t s = 0;
        while (s < p.size() && p[s] == 0) s++;
        if (s < p.size() && Compare(CSpan(p.data() + s, p.size() - s), b) >= 0)   // D >= B: q + 1
            for (size_t i = q.size(); i-- > 0;)
                if (++q[i] != 0) break;
    }

#if FULLINT_WORD64
    // A two-word divisor (GMP's mpn_divrem_2): one 3-by-2 division per quotient word with the
    // reciprocal of the normalized divisor (Moller, Granlund 2011, Algorithm 5; v as invert_pi1
    // in DivRecip.asm), the dividend shifted on the fly, no kernel call (its entry, copy and
    // multiply-subtract set-up cost more than the words here: 2N / N at 2 words 35 ns, GMP 24).
    // The Div contract: quotient lenA - 1 words, remainder 4 words (the value in the low two).
    static void DivBy2(CSpan a, CSpan b, Span q, Span r) noexcept
    {
        size_t la = a.size();
        int sh = FullHelper::LeadingZeroCount(b[0]);
        uint64_t d1 = b[0], d0 = b[1];
        if (sh)
        {
            d1 = d1 << sh | d0 >> (64 - sh);
            d0 <<= sh;
        }
        uint64_t rem, p, t0, t1;
        uint64_t v = FullHelper::Div128(~d1, ~0ull, d1, &rem);   // floor((b^2 - 1) / d1) - b
        p = d1 * v + d0;
        if (p < d0)
        {
            v--;
            if (p >= d1)
            {
                v--;
                p -= d1;
            }
            p -= d1;
        }
        t1 = FullHelper::BigMul(d0, v, &t0);
        p += t1;
        if (p < t1)
        {
            v--;
            if (p > d1 || (p == d1 && t0 >= d0)) v--;
        }

        // u = A << sh, la + 1 words: (n2 n1) = its top two (below d), one quotient word per word after
        auto U = [&](size_t i) -> uint64_t {   // u[i], 0 <= i <= la
            uint64_t hi = i == 0 ? 0 : a[i - 1];
            if (!sh) return hi;
            uint64_t lo = i < la ? a[i] : 0;
            return hi << sh | lo >> (64 - sh);
        };
        uint64_t n2 = U(0), n1 = U(1);
        for (size_t i = 2; i <= la; i++)
        {
            uint64_t n0 = U(i), q0, qq = FullHelper::BigMul(v, n2, &q0);
            q0 += n1;
            qq += n2 + (q0 < n1 ? 1 : 0);
            uint64_t r1 = n1 - d1 * qq;
            uint64_t r0 = n0 - d0;
            r1 = r1 - d1 - (n0 < d0 ? 1 : 0);
            t1 = FullHelper::BigMul(d0, qq, &t0);
            r1 = r1 - t1 - (r0 < t0 ? 1 : 0);
            r0 -= t0;
            qq++;
            if (r1 >= q0)
            {
                qq--;
                uint64_t s = r0 + d0;
                r1 += d1 + (s < r0 ? 1 : 0);
                r0 = s;
            }
            if (r1 >= d1 && (r1 > d1 || r0 >= d0))   // rare
            {
                qq++;
                r1 = r1 - d1 - (r0 < d0 ? 1 : 0);
                r0 -= d0;
            }
            q[i - 2] = qq;
            n2 = r1;
            n1 = r0;
        }
        r[0] = 0;
        r[1] = 0;
        r[2] = sh ? n2 >> sh : n2;
        r[3] = sh ? n1 >> sh | n2 << (64 - sh) : n1;
    }
#endif

    static void Div(CSpan dividend, uint64_t divisor, Span quotient, uint64_t* remainder) noexcept
    {
        // DivRecip.asm from RecipWordsMin words: below that the reciprocal's own div does not pay
        // off (measured on Zen 3: 1-3 words slower, 4-48 even, 64 words 1.1x, 1024 1.4x)
        if (Cpu::Has(Cpu::Recip) && dividend.size() >= RecipWordsMin)
        {
            Fullint64Div2longWIN_recip(dividend.data(), divisor, quotient.data(), remainder, (int)dividend.size());
            return;
        }
        Fullint64Div2longWin(dividend.data(), divisor, quotient.data(), remainder, (int)dividend.size());
    }

    static int Compare(CSpan a, CSpan b) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // CompareAvx2.asm (different lengths: the baseline inside)
            return Fullint64CompareWIN_avx2(a.data(), b.data(), (int)a.size(), (int)b.size());
#elif FULLINT_X86
        if (Cpu::Has(Cpu::Sse2))   // x86 CompareSse2.asm (different lengths: the baseline inside)
            return Fullint64CompareWIN_sse2(a.data(), b.data(), (int)a.size(), (int)b.size());
#endif
        return Fullint64CompareWIN(a.data(), b.data(), (int)a.size(), (int)b.size());
    }

    // valueBuf: value.size() words of scratch; lenResult in = resultBuf size, out = start index
    static void ToDecString(CSpan value, uint64_t* valueBuf, uint8_t* resultBuf, int* lenResult) noexcept
    {
        if (Cpu::Has(Cpu::Recip))   // ToDecStringRecip.asm: 10^19 by its reciprocal
        {
            Fullint64ToDecStringWIN_recip(value.data(), valueBuf, resultBuf, (int)value.size(), lenResult);
            return;
        }
        Fullint64ToDecStringWin(value.data(), valueBuf, resultBuf, (int)value.size(), lenResult);
    }

    // valueBuf / resultBuf: text.size() / 19 + 1 words, zeroed
    static void FromDecString(const uint8_t* text, int length, uint64_t* valueBuf, uint64_t* resultBuf, int* lenResult) noexcept
    {
        Fullint64FromDecStringWin(text, valueBuf, resultBuf, length, lenResult);
    }

    static int And(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64AndWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#elif FULLINT_X86
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64AndWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64AndWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Or(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64OrWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#elif FULLINT_X86
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64OrWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64OrWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Xor(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64XorWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#elif FULLINT_X86
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64XorWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64XorWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    FULLINT_FORCEINLINE static void ShiftLeft(CSpan a, int shift, Span r) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // ShiftAvx2.asm
        {
            Fullint64ShlWIN_avx2(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
            return;
        }
#endif
        Fullint64ShlWIN(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
    }

    FULLINT_FORCEINLINE static void ShiftRight(CSpan a, int shift, Span r) noexcept
    {
#if FULLINT_X64
        if (Cpu::Has(Cpu::Avx2))   // ShiftAvx2.asm
        {
            Fullint64ShrWIN_avx2(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
            return;
        }
#endif
        Fullint64ShrWIN(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
    }

    // exact division for the Toom-Cook interpolation (ToomOps.asm): q = a / d for a multiple
    // of d, d = d'·2^t with d' odd and below 2^32; q may be a (same length); returns 0 when a
    // is a multiple. ~1.55x the speed of Div by one word.
    static uint64_t DivExact(CSpan a, uint64_t d, Span q) noexcept
    {
        return Fullint64DivExactWIN(a.data(), q.data(), (int)a.size(), d);
    }

private:
    static int Signs(bool negA, bool negB) noexcept { return (negA ? 1 : 0) | (negB ? 2 : 0); }
};

}  // namespace fullint
