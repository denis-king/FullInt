// Port of FullInt.Core/IFullIntCore.cs + FullIntCoreBase.cs: the span-level kernel calls.
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

#include <fullint/kernels.h>

#include <cstdint>
#include <span>

namespace fullint {

using Span = std::span<uint64_t>;
using CSpan = std::span<const uint64_t>;

struct FullIntCore
{
    static uint64_t Add(CSpan a, CSpan b, Span r) noexcept
    {
        return Fullint64AddWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
    }

    static int Sub(CSpan a, CSpan b, Span r) noexcept
    {
        return Fullint64SubWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
    }

    static void Mult(CSpan a, CSpan b, Span r) noexcept
    {
        Fullint64MultWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size());
    }

    static void Div(CSpan dividend, CSpan divisor, Span quotient, Span remainder) noexcept
    {
        Fullint64DivWin(dividend.data(), divisor.data(), quotient.data(), remainder.data(), (int)dividend.size(), (int)divisor.size());
    }

    static void Div(CSpan dividend, uint64_t divisor, Span quotient, uint64_t* remainder) noexcept
    {
        Fullint64Div2longWin(dividend.data(), divisor, quotient.data(), remainder, (int)dividend.size());
    }

    static int Compare(CSpan a, CSpan b) noexcept
    {
        return Fullint64CompareWIN(a.data(), b.data(), (int)a.size(), (int)b.size());
    }

    // valueBuf: value.size() words of scratch; lenResult in = resultBuf size, out = start index
    static void ToDecString(CSpan value, uint64_t* valueBuf, uint8_t* resultBuf, int* lenResult) noexcept
    {
        Fullint64ToDecStringWin(value.data(), valueBuf, resultBuf, (int)value.size(), lenResult);
    }

    // valueBuf / resultBuf: text.size() / 19 + 1 words, zeroed
    static void FromDecString(const uint8_t* text, int length, uint64_t* valueBuf, uint64_t* resultBuf, int* lenResult) noexcept
    {
        Fullint64FromDecStringWin(text, valueBuf, resultBuf, length, lenResult);
    }

    static int And(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
        return Fullint64AndWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Or(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
        return Fullint64OrWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static int Xor(CSpan a, bool negA, CSpan b, bool negB, Span r) noexcept
    {
        return Fullint64XorWIN(a.data(), b.data(), r.data(), (int)a.size(), (int)b.size(), (int)r.size(), Signs(negA, negB));
    }

    static void ShiftLeft(CSpan a, int shift, Span r) noexcept
    {
        Fullint64ShlWIN(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
    }

    static void ShiftRight(CSpan a, int shift, Span r) noexcept
    {
        Fullint64ShrWIN(a.data(), r.data(), (int)a.size(), (int)r.size(), shift);
    }

private:
    static int Signs(bool negA, bool negB) noexcept { return (negA ? 1 : 0) | (negB ? 2 : 0); }
};

}  // namespace fullint
