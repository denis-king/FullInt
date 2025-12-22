using System;

namespace FullInt.Core
{
    public class FullInt64CoreDefault : IFullIntCore
    {
        public ulong Add(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            throw new NotImplementedException();
        }

        public void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder)
        {
            throw new NotImplementedException();
        }

        public void Mult(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            throw new NotImplementedException();
        }

        public int Sub(Span<ulong> a, Span<ulong> b, Span<ulong> r)
        {
            throw new NotImplementedException();
        }
    }
}