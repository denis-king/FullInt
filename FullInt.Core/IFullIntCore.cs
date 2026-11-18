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
		// r = a·b mod β^n, n = r.Length <= a.Length + b.Length (the low half; the x64 core with
		// BMI2 + ADX: MulLoAdx.asm, the triangle of the column multiply; the others multiply in
		// full and keep the low words)
		void MultLow(Span<ulong> a, Span<ulong> b, Span<ulong> r);
		void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder);
		void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder);
		// q = a / b alone (the Div contract, no remainder): the asm cores with the reciprocal kernel
		// from 16 divisor words: DivRecip.asm's approximate quotient with a guard word (the passes of
		// the last steps cut short: 2N / N about half the work; a guard in doubt is settled by q·B
		// against A); the others Div into a scratch remainder
		void Quotient(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient);

        // q = a / d for an a known to be a multiple of d (the Toom-Cook interpolation):
        // d = d'·2^t with d' odd and below 2^32 (t < 32); q.Length == a.Length, q may be a.
        // Returns 0 when a is a multiple (anything else: it was not). The asm cores use the exact
        // division by the inverse (ToomOps.asm, ~1.55x the speed of Div by one word).
        ulong DivExact(Span<ulong> a, ulong d, Span<ulong> q);
		
		int Compare(Span<ulong> a, Span<ulong> b);
        void ToDecString(Span<ulong> value, Span<byte> resultBuf, out int resultStartIndex);
        // Knuth's fraction method's passes (the x64 core: ToDecStringFrac.asm): f (L words, the most
        // significant first, overwritten) times 10^19, m times, each integer part 19 ASCII digits at
        // digits (19·m bytes, leading zeros included). false: not in this core (nothing written).
        bool ToDecFraction(Span<ulong> f, int m, Span<byte> digits);
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
