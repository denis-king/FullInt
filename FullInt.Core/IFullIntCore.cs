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
		void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder);
    }
}
