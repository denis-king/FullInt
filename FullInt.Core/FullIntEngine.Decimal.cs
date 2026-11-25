using System;
using System.Threading;

namespace FullNumerics
{
    /// <summary>
    /// Thresholds of the divide-and-conquer decimal conversions (FullIntEngine.Decimal.cs).
    /// </summary>
    public static class DecimalConversion
    {
        /// <summary>
        /// Numbers above this many words are converted to text by halves; below, by the core's
        /// ToDecString. 32 with <see cref="PreparedFrom"/> 128 (`FullInt.Benchmarks decprep`, x64,
        /// Zen 3): 0.95 of 48 without the prepared division over 64..4096 words.
        /// </summary>
        public static int ToStringThreshold { get; set; } = 32;

        /// <summary>Text above this many digits is parsed by halves; below, by the core's FromDecString.</summary>
        public static int ParseThreshold { get; set; } = 48 * 19;

        /// <summary>
        /// ToString divides by the powers P_k from this many words of P_k through a reciprocal
        /// computed once per power (<see cref="NewtonDivision.Prepared"/>), not the general
        /// division: the same P_k divides 2^level numbers. Below it Burnikel–Ziegler / the core's
        /// Div are faster.
        /// </summary>
        public static int PreparedFrom { get; set; } = 128;

        /// <summary>
        /// From this many words a leaf (and a short ToString) by Knuth's fraction method when the
        /// core has its passes (x64): one product by a reciprocal cached per length, then one-word
        /// multiplications instead of a division by 10^19 per word per 19 digits. 12: at 8 words the
        /// two native calls still cost more than they save (1.18 of the kernel; C++: 0.94);
        /// 16 words 0.93, 32 words 0.75. Up to <see cref="FractionMaxWords"/>.
        /// </summary>
        public static int FractionFrom { get; set; } = 12;

        /// <summary>The longest leaf the fraction method takes (its reciprocals are cached per length).</summary>
        public const int FractionMaxWords = 128;
    }

    /// <summary>
    /// Decimal text by halves. The core kernels convert word by word (n² work); here a long
    /// number is split by a power of ten into two halves of about the same size, recursively,
    /// so the work follows the division / multiplication (Burnikel–Ziegler, Karatsuba, Toom-3).
    ///
    ///   P_k = 10^(19·2^k)   (P_0 = 10^19, P_k+1 = P_k²; computed once, cached)
    ///   ToString(x):  x = q·P_k + r  ->  ToString(q) followed by ToString(r) padded to 19·2^k digits
    ///   Parse(t):     t = [high][low: 19·2^k digits]  ->  Parse(high)·P_k + Parse(low)
    /// </summary>
    public partial class FullIntEngine
    {
        private const ulong Pow10_19 = 10_000_000_000_000_000_000UL;   // 19 decimal digits a word

        private static FullInt[] _decimalPowers = Array.Empty<FullInt>();   // [k] = P_k
        // P_k = 10^(19·2^k) = 2^(19·2^k)·5^(19·2^k): its low 19·2^k bits are zero, about 30% of its
        // words. ToString divides by P_k without its zero low words (P'), the dividend's low words go
        // to the remainder as they are (GMP's power table does the same): x = xh·β^z + xl,
        // xh = q·P' + r' -> x = q·P_k + (r'·β^z + xl). P' prepared for the division (its reciprocal)
        // when it is long enough.
        private sealed class ShiftedPower
        {
            public FullInt P = null!;                  // P_k without its zero low words
            public int Zeros;                            // those words
            public NewtonDivision.Prepared? Prepared;
        }
        private static ShiftedPower?[] _shiftedPowers = Array.Empty<ShiftedPower?>();   // [k]

        private ShiftedPower ShiftedDecimalPower(int k)
        {
            var shifted = Volatile.Read(ref _shiftedPowers);
            if (k < shifted.Length && shifted[k] != null) return shifted[k]!;
            var pk = DecimalPower(k);
            lock (_decimalPowersLock)
            {
                shifted = _shiftedPowers;
                if (k < shifted.Length && shifted[k] != null) return shifted[k]!;
                Span<ulong> w = FullHelper.DeleteZeros(pk.value);
                int z = 0;
                while (z + 1 < w.Length && w[w.Length - 1 - z] == 0) z++;
                var e = new ShiftedPower { P = new FullInt(false, w.Slice(0, w.Length - z).ToArray()), Zeros = z };
                if (w.Length - z >= DecimalConversion.PreparedFrom)
                    e.Prepared = NewtonDivision.Prepare(_core, e.P.value);
                var grown = new ShiftedPower?[Math.Max(shifted.Length, k + 1)];
                Array.Copy(shifted, grown, shifted.Length);
                grown[k] = e;
                Volatile.Write(ref _shiftedPowers, grown);
                return e;
            }
        }
        private static readonly object _decimalPowersLock = new object();

        private const double BitsPerPower0 = 63.11663380285988;   // 19·log2(10): bits of P_0; P_k has 2^k times as many

        private FullInt DecimalPower(int k)
        {
            var powers = Volatile.Read(ref _decimalPowers);
            if (k < powers.Length) return powers[k];
            lock (_decimalPowersLock)
            {
                powers = _decimalPowers;
                if (k < powers.Length) return powers[k];
                var grown = new FullInt[k + 1];
                Array.Copy(powers, grown, powers.Length);
                for (int i = powers.Length; i <= k; i++)
                {
                    grown[i] = i == 0
                        ? new FullInt(false, new ulong[] { Pow10_19 })
                        : mult(grown[i - 1], grown[i - 1]);
                }
                Volatile.Write(ref _decimalPowers, grown);
                return grown[k];
            }
        }

        // words of P_k, estimated without computing it
        private static int DecimalPowerWords(int k) => (int)Math.Ceiling(BitsPerPower0 * (1L << k) / 64) + 1;

        #region ToString

        private string ToDecStringRecursive(FullInt value)
        {
            var magnitude = new FullInt(false, value.value);   // read only here
            var trimmed = FullHelper.DeleteZeros(value.value);
            if (trimmed.Length == 1 && trimmed[0] == 0) return "0";   // a long array holding zero (in-place results)
            int words = trimmed.Length;
            int capacity = (int)(words * 19.27) + 2;              // 64·log10(2) = 19.266 digits per word
            var text = new char[capacity + 1];
            int start = WriteDecimal(magnitude, text, text.Length, -1);
            if (value.Sing) text[--start] = '-';
            return new string(text, start, text.Length - start);
        }

        // x's digits right-aligned at text[.. end); width >= 0: exactly width digits (leading
        // zeros), -1: no padding (the top part). Returns the start index.
        private int WriteDecimal(FullInt x, char[] text, int end, int width)
        {
            Span<ulong> w = FullHelper.DeleteZeros(x.value);
            if (w.Length <= DecimalConversion.ToStringThreshold)
            {
                int start = WriteDecimalLeaf(w, text, end);
                if (width < 0) return start;
                int padded = end - width;
                for (int i = padded; i < start; i++) text[i] = '0';
                return padded;
            }

            // P_k of about half of x's words
            int k = 0;
            while (DecimalPowerWords(k + 1) * 2 <= w.Length) k++;
            var sp = ShiftedDecimalPower(k);
            int z = sp.Zeros;
            Span<ulong> pw = sp.P.value, xh = w.Slice(0, w.Length - z), xl = w.Slice(w.Length - z);
            FullInt q;
            Span<ulong> rh;
            if (sp.Prepared != null)
            {
                // the same P_k divides 2^level numbers: its reciprocal once, then a product for the
                // quotient and one (cyclic when long) for the remainder per division
                var qw = new ulong[xh.Length - pw.Length + 1];
                var rw = new ulong[pw.Length + 2];
                NewtonDivision.DivRem(_core, xh, sp.Prepared, qw, rw);
                q = new FullInt(false, qw);
                rh = FullHelper.DeleteZeros(rw);
            }
            else
            {
                var xd = z == 0 ? x : new FullInt(false, xh.ToArray());
                q = divRem(xd, sp.P, wantQuotient: true, wantRemainder: true, out var rr)!;
                rh = FullHelper.DeleteZeros(rr!.value);
            }
            // r = r'·β^z + xl
            var rWords = new ulong[rh.Length + z];
            rh.CopyTo(rWords);
            xl.CopyTo(rWords.AsSpan(rh.Length));
            var r = new FullInt(false, rWords);
            int low = 19 << k;
            WriteDecimal(r, text, end, low);
            return WriteDecimal(q, text, end - low, width < 0 ? -1 : width - low);
        }

        // The fraction method's reciprocal for n-word numbers: m chunks of 19 digits (10^(19m) > β^n),
        // r = ceil(β^(L+n) / 10^(19m)), L = m + 1 words below the point. Made once per n.
        private sealed class FractionReciprocal
        {
            public int M;
            public ulong[] R = null!;   // the most significant first
        }
        private static readonly FractionReciprocal?[] _fractionReciprocals = new FractionReciprocal?[DecimalConversion.FractionMaxWords + 1];

        private FractionReciprocal FractionReciprocalFor(int n)
        {
            var e = Volatile.Read(ref _fractionReciprocals[n]);
            if (e != null) return e;
            int m = (int)(n * 64 * 0.30102999566398120 / 19) + 1;
            var d = new FullInt(1UL);
            var p19 = new FullInt(false, new ulong[] { Pow10_19 });
            for (int i = 0; i < m; i++) d = mult(d, p19);
            var num = shiftLeft(new FullInt(1UL), 64 * (m + 1 + n));
            var q = div(sub(add(num, d), new FullInt(1UL)), d);
            e = new FractionReciprocal { M = m, R = FullHelper.DeleteZeros(q.value).ToArray() };
            Volatile.Write(ref _fractionReciprocals[n], e);   // a race makes the same value twice
            return e;
        }

        // Knuth's fraction method (TAOCP vol. 2, 4.4): f = x / 10^(19m) as an L-word fraction, an upper
        // bound (x times the reciprocal, the low n words dropped, + 1: below 2 units of its last word);
        // the core multiplies f by 10^19 m times, each integer part 19 digits (ToDecStringFrac.asm: the
        // bound keeps every integer part exact). x: n words, top nonzero. -1: the core has no passes.
#if NET5_0_OR_GREATER
        [System.Runtime.CompilerServices.SkipLocalsInit]   // every buffer below is written in full before it is read
#endif
        private int WriteDecimalFraction(Span<ulong> w, Span<char> text, int end)
        {
            int n = w.Length;
            var rc = FractionReciprocalFor(n);
            int lr = rc.R.Length, L = rc.M + 1;
            Span<ulong> prod = stackalloc ulong[n + lr];
            _core.Mult(w, rc.R, prod);
            Span<ulong> f = stackalloc ulong[L];
            prod.Slice(lr - L, L).CopyTo(f);   // the top lr words' low L (the rest zero)
            for (int i = L - 1; i >= 0; i--)   // + 1: an upper bound
                if (++f[i] != 0) break;
            int total = 19 * rc.M;
            Span<byte> digits = stackalloc byte[total];
            if (!_core.ToDecFraction(f, rc.M, digits)) return -1;
            int s = 0;
            while (s < total && digits[s] == (byte)'0') s++;
            for (int i = total - 1; i >= s; i--) text[--end] = (char)digits[i];
            return end;
        }

        // the core kernel on a short number; zero writes nothing
        private int WriteDecimalLeaf(Span<ulong> w, char[] text, int end)
        {
            if (w.Length >= DecimalConversion.FractionFrom && w.Length <= DecimalConversion.FractionMaxWords)
            {
                int fs = WriteDecimalFraction(w, text, end);
                if (fs >= 0) return fs;
            }
            if (w.Length == 1)
            {
                for (ulong v = w[0]; v != 0; v /= 10) text[--end] = (char)('0' + (int)(v % 10));
                return end;
            }
            int resultBytes = (int)(w.Length * 1.014 + 1) * 19 + 1;
            byte[]? rented = null;
            Span<byte> buf = resultBytes <= FullHelper.StackAllocLimitBytes
                ? stackalloc byte[resultBytes]
                : FullHelper.RentZeroedBytes(resultBytes, out rented);
            _core.ToDecString(w, buf, out int s);
            for (int i = buf.Length - 1; i >= s; i--) text[--end] = (char)buf[i];
            FullHelper.Return(rented);
            return end;
        }

        #endregion

        #region Parse

        // decimal digits (ASCII) to a non-negative number
        private FullInt ParseDecimal(Span<byte> digits)
        {
            if (digits.Length <= DecimalConversion.ParseThreshold) return ParseDecimalLeaf(digits);

            // the low part: the largest 19·2^k digits below the length (at least half of them)
            int k = 0;
            while ((19L << (k + 1)) < digits.Length) k++;
            int low = 19 << k;
            var high = ParseDecimal(digits.Slice(0, digits.Length - low));
            var lowValue = ParseDecimal(digits.Slice(digits.Length - low));
            return add(mult(high, DecimalPower(k)), lowValue);
        }

        private FullInt ParseDecimalLeaf(Span<byte> digits)
        {
            int resultWords = digits.Length / 19 + 1;
            ulong[]? rented = null;
            Span<ulong> resultBuf = resultWords <= FullHelper.StackAllocLimitWords
                ? stackalloc ulong[resultWords]
                : FullHelper.RentZeroed(resultWords, out rented);   // a work buffer: copied out below
            _core.FromDecString(digits, resultBuf, out var startPosition);
            var parsed = new FullInt(false, startPosition < resultWords ? resultBuf.Slice(startPosition).ToArray() : new ulong[] { 0 });
            FullHelper.Return(rented);
            return parsed;
        }

        #endregion
    }
}
