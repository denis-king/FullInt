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

#include <utility>
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
#if defined(_M_X64) || defined(__x86_64__)
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
    static uint64_t Add(CSpan a, CSpan b, Span r) noexcept
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
    static int Sub(CSpan a, CSpan b, Span r) noexcept
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
        if (Cpu::Has(Cpu::Bmi2 | Cpu::Adx))   // mulx / adcx / adox (MultAdx.asm; x86: in 32-bit digits)
        {
            Fullint64MultWIN_adx(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
            return;
        }
        Fullint64MultWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
    }

    // r = a² (r.size() == 2·a.size(), r not a): the products of different words once, then
    // doubled. SqrAdx.asm with BMI2 + ADX (x64 and x86); without them x64 Sqr.asm (mul / adc),
    // x86 the multiplication a·a
    static void Sqr(CSpan a, Span r) noexcept
    {
        if (Cpu::Has(Cpu::Bmi2 | Cpu::Adx))
        {
            Fullint64SqrWIN_adx(a.data(), r.data(), (int)a.size(), (int)r.size());
            return;
        }
#if defined(_M_X64) || defined(__x86_64__)
        Fullint64SqrWIN(a.data(), r.data(), (int)a.size(), (int)r.size());
#else
        Mult(a, a, r);
#endif
    }

    static void Div(CSpan dividend, CSpan divisor, Span quotient, Span remainder) noexcept
    {
        // DivRecip.asm; equal lengths (a quotient of 0 or 1) stay with the baseline: it decides
        // those by a compare, the reciprocal's own div would not pay off
        if (Cpu::Has(Cpu::Recip) && divisor.size() >= 2 && dividend.size() > divisor.size())
        {
            size_t words = dividend.size() + divisor.size() + 2;
            uint64_t stackWork[96];   // small: no thread_local lookup (a few ns at 3 words)
            uint64_t* work = words <= std::size(stackWork) ? stackWork : DivWork(words);
#if defined(_M_X64) || defined(__x86_64__)
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
#if defined(_M_X64) || defined(__x86_64__)
        if (Cpu::Has(Cpu::Avx2))   // CompareAvx2.asm (different lengths: the baseline inside)
            return Fullint64CompareWIN_avx2(a.data(), b.data(), (int)a.size(), (int)b.size());
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
#if defined(_M_X64) || defined(__x86_64__)
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64AndWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#else
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64AndWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64AndWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Or(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
#if defined(_M_X64) || defined(__x86_64__)
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64OrWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#else
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64OrWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64OrWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Xor(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
#if defined(_M_X64) || defined(__x86_64__)
        if (Cpu::Has(Cpu::Avx2))   // LogicAvx2.asm (negative operands: it falls back to the baseline)
            return Fullint64XorWIN_avx2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#else
        if (Cpu::Has(Cpu::Sse2))   // x86 LogicSse2.asm (negative operands: the baseline inside)
            return Fullint64XorWIN_sse2(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
#endif
        return Fullint64XorWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static void ShiftLeft(CSpan a, int shift, Span r) noexcept
    {
#if defined(_M_X64) || defined(__x86_64__)
        if (Cpu::Has(Cpu::Avx2))   // ShiftAvx2.asm
        {
            Fullint64ShlWIN_avx2(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
            return;
        }
#endif
        Fullint64ShlWIN(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
    }

    static void ShiftRight(CSpan a, int shift, Span r) noexcept
    {
#if defined(_M_X64) || defined(__x86_64__)
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
