using System;
using System.Collections.Generic;
using System.Text;

namespace FullInt.Core
{
    public interface IFullIntCore
    {
        ulong Add(Span<ulong> a, Span<ulong> b, Span<ulong> r);
        int Sub(Span<ulong> a, Span<ulong> b, Span<ulong> r);
		void Mult(Span<ulong> a, Span<ulong> b, Span<ulong> r);
		// r = a² (r.Length == 2·a.Length, r not a): the x64 core with BMI2 + ADX squares (SqrAdx.asm,
		// the products of different words once, then doubled: ~0.55 of the multiply from 64 words),
		// the others multiply a·a
		void Sqr(Span<ulong> a, Span<ulong> r);
		void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder);
		void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder);

        // q = a / d for an a known to be a multiple of d (the Toom-Cook interpolation):
        // d = d'·2^t with d' odd and below 2^32 (t < 32); q.Length == a.Length, q may be a.
        // Returns 0 when a is a multiple (anything else: it was not). The asm cores use the exact
        // division by the inverse (ToomOps.asm, ~1.55x the speed of Div by one word).
        ulong DivExact(Span<ulong> a, ulong d, Span<ulong> q);
		
		int Compare(Span<ulong> a, Span<ulong> b);
        void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex);
        void FromDecString(Span<byte> value, Span<ulong> resultBuf, out int resultStartIndex);

        // Bitwise operations with two's complement semantics on sign-magnitude operands.
        // r.Length >= max(a.Length, b.Length) + 1. Returns 1 if the result is negative.
        int And(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r);
        int Or(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r);
        int Xor(Span<ulong> a, bool negA, Span<ulong> b, bool negB, Span<ulong> r);

        // Magnitude shifts, shift >= 0.
        // ShiftLeft:  r.Length >= a.Length + shift / 64 + 1
        // ShiftRight: r.Length >= max(a.Length - shift / 64, 1); r may alias a
        void ShiftLeft(Span<ulong> a, int shift, Span<ulong> r);
        void ShiftRight(Span<ulong> a, int shift, Span<ulong> r);
    }
}
