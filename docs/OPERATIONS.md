# Operations

The public surface of `FullNumerics.FullInt` (C#). The engine, the cores and the algorithms are internal. The C++ `fullnumerics::FullInt` follows the same rules (see the notes at the end).

## Construction

| Source | Example | Result |
|---|---|---|
| Any integer type (implicit) | `FullInt x = -5;`, `FullInt y = ulong.MaxValue;` | `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` |
| Bytes | `new FullInt(bytes, isUnsigned: false, isBigEndian: false)` | As `BigInteger(ReadOnlySpan<byte>, bool, bool)`: two's complement unless `isUnsigned`, least significant byte first unless `isBigEndian`. No bytes: 0. |
| Words | `FullInt.FromWords(words, WordOrder.LeastSignificantFirst, negative)` | The magnitude's 64-bit words in the given order and a sign, like GMP `mpz_import`. The bytes inside a word are the machine's own. No words: 0. |
| `BigInteger` (explicit) | `(FullInt)big` | Exact |
| Text | `FullInt.Parse("-123")`, `FullInt.TryParse(s, out x)` | See [Parsing](#parsing) |
| Copy | `new FullInt(other)` | Shares the storage until either is written by the in-place API (copy on write) |
| Constants | `FullInt.Zero`, `One`, `MinusOne` | A new object each time (a result object may be written in place) |

## Arithmetic

| Operator / method | Semantics |
|---|---|
| `a + b`, `a - b`, `a * b` | Exact |
| `a / b` | **Truncates toward zero**, like C# `/` and `BigInteger.Divide`. `DivideByZeroException` when `b == 0`. |
| `a % b` | The remainder takes **the sign of the dividend**, like C# `%`: `a == (a / b) * b + a % b`. |
| `FullInt.DivRem(a, b, out r)` | Both at once |
| `FullInt.Pow(a, n)` | `n ≥ 0` (`int`); `ArgumentOutOfRangeException` otherwise |
| `-a`, `+a`, `FullInt.Negate(a)`, `FullInt.Abs(a)` | |
| `++a`, `--a` | A new value |

Every operator allocates a new result. Zero is never negative.

### In place (GMP style)

`FullInt.Add(result, a, b)`, `Subtract`, `Multiply`, `Divide`, `Remainder`, `ShiftLeft`, `ShiftRight`, `And`, `Or`, `Xor`: `result = a op b`, reusing `result`'s storage when it is long enough (no allocation in a loop). `result` may be `a` or `b`. Storage shared with another object (a copy, an operator's result) is never written.

## Bitwise operations

Semantics of `System.Numerics.BigInteger`: a negative number behaves as an infinitely sign-extended two's complement value.

| Operator | Semantics |
|---|---|
| `a & b`, `a \| b`, `a ^ b` | AND, OR, **XOR** (as for `int` and `BigInteger`; the power is `Pow`) |
| `~a` | `-a - 1` |
| `a << n` | `a · 2ⁿ`; a negative `n` shifts right |
| `a >> n` | Arithmetic shift, rounds toward negative infinity (`-1 >> 100 == -1`); a negative `n` shifts left |

## Comparison and properties

| Member | Semantics |
|---|---|
| `==`, `!=`, `<`, `<=`, `>`, `>=` | Numeric |
| `CompareTo`, `FullInt.Compare`, `Min`, `Max` | `IComparable<FullInt>`, `IComparable` (sorting works) |
| `Equals(object)` | `FullInt`, the boxed integer types and `BigInteger` by value |
| `GetHashCode()` | Equal values, equal hashes |
| `Sign`, `IsNegative`, `IsZero`, `IsOne`, `IsEven` | |

## Parsing

`FullInt.Parse(string)` throws `FormatException` for invalid text; `TryParse` returns false.

| Input | Read as |
|---|---|
| White space around | ignored |
| `-` or `+` in front | the sign |
| `0x…` / `0X…` | hexadecimal |
| `0…` (a leading zero, more than one digit) | ⚠️ octal (`"007"` is 7, `"08"` is an error); under review |
| digits `0-9` | decimal |

Anything else (empty text, a lone sign, other characters, fractions, exponents) is an error.

## Formatting

`FullInt` is `IFormattable`, so the formats work in `string.Format` and interpolation (`$"{x:X}"`).

| Call | Output |
|---|---|
| `x.ToString()`, `"D"`, `null` | Decimal |
| `"X"` / `"x"` | Hexadecimal, upper / lower case |
| `"B"` | Binary |
| A number after the letter (`"D20"`, `"X8"`) | The least number of digits, zeros in front |

A negative value is written as `-` and the digits of the magnitude (not two's complement, unlike `BigInteger`'s `"X"`).

## Conversion and serialization

| Call | Semantics |
|---|---|
| `ToByteArray(isUnsigned, isBigEndian)`, `GetByteCount`, `TryWriteBytes(span, out n, …)` | As `BigInteger`: the fewest bytes; `isUnsigned` on a negative value throws `OverflowException`. The binary format of the number. |
| `ToWords(order)`, `GetWordCount()`, `TryWriteWords(span, out n, order)` | The magnitude's words (the sign is `IsNegative`), like GMP `mpz_export`. Zero is one zero word. |
| `(BigInteger)x` | Exact |
| `(long)x`, `(ulong)x`, `(int)x`, `(uint)x`, `(Int128)x`, `(UInt128)x` | `OverflowException` when out of range |
| `(double)x`, `(float)x`, `(Half)x` | Rounded to nearest, ties to even (correctly rounded: `BigInteger`'s conversion truncates in places, e.g. 2⁶³ − 1 → 2⁶³ − 1024); beyond the range an infinity |
| `(decimal)x` | `OverflowException` beyond 96 bits |
| `(FullInt)double`, `float`, `Half`, `decimal` | Truncated toward zero; NaN and the infinities throw `OverflowException` |
| `x.GetBitLength()` | Bits of the shortest two's complement form without the sign bit, as `BigInteger.GetBitLength` |
| System.Text.Json | `FullIntJsonConverter` (applied to the type, .NET 5+): written as a string of decimal digits (`"123"`, safe for readers with double numbers); a string or a number is read |

## Generic math (.NET 7+)

`FullInt` implements `IBinaryInteger<FullInt>` and `ISignedNumber<FullInt>` (as `BigInteger`), so generic code over `T : INumber<T>` / `IBinaryInteger<T>` takes it: `T.Zero`, `T.One`, `T.Abs`, `T.Clamp`, `T.CreateChecked(x)`, `T.IsEvenInteger`, `>>>`, and so on.

| Member | Semantics |
|---|---|
| `CreateChecked` / `CreateSaturating` / `CreateTruncating` | From every integer type, `Int128`, `UInt128`, `BigInteger`, `double`, `float`, `Half`, `decimal`; to them: checked throws when out of range, saturating clamps, truncating wraps (the low bits of two's complement) |
| `Parse` / `TryParse` with `NumberStyles` and a provider | `Integer` (white space, the provider's sign, decimal digits; no octal) and `HexNumber` (two's complement, as `BigInteger`: `"FF"` is −1, `"0FF"` is 255); other styles: `ArgumentException` |
| `TryFormat(Span<char>, …)` | The formats of `ToString` |
| `IsPow2`, `Log2`, `PopCount`, `TrailingZeroCount`, `LeadingZeroCount`, `RotateLeft`, `RotateRight`, `>>>` | On the shortest two's complement form in **64-bit words**. `BigInteger` uses 32-bit words, so `LeadingZeroCount`, the bit counts of negative values, `>>>` and the rotations can differ from it; every arithmetic result is the same. |
| `DivRem(a, b)` | A `(Quotient, Remainder)` tuple |

## C++ (`fullnumerics::FullInt`)

The same semantics with C++ conventions: value type (copies are deep, moves are cheap), `^` is XOR, `ToString()` is decimal, `operator<<` on streams. Words: `FullInt::FromWords`, `ToWords` with `WordOrder`; bytes: `FromBytes`, `ToBytes`; `static_cast` to the integer types (`std::overflow_error` out of range), to `double` / `float` (correctly rounded), `FromDouble`, `GetBitLength`. Generic code is templates there: no interfaces needed.

## Planned

```text
ModPow, GreatestCommonDivisor, Sqrt; TryFormat without the string in between; UTF-8 parsing / formatting of its own
```
