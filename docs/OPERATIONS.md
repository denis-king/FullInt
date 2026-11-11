# Operations

This document covers the public surface of `FullInt.Core.FullInt64` and `FullInt64Engine` **as currently implemented**. Behaviour marked ⚠️ is a known defect or a planned change. See [ANALYSIS.md](ANALYSIS.md) §3.

## Construction

| Source | Example | Result |
|---|---|---|
| `int`, `long`, `byte` (implicit) | `FullInt64 x = -5;` | Binary storage, sign set from the value |
| `ulong` | `new FullInt64(ulong.MaxValue)` | Binary storage |
| Limbs | `new FullInt64(sign, new ulong[] { hi, lo })` | Wraps the array **without copying**. Index 0 is the most significant limb. |
| String (implicit) | `FullInt64 x = "-0x1F";` | See [Parsing](#parsing) |
| Copy | `new FullInt64(other)` | ⚠️ Shares the limb array with `other` |
| Decimal mode | `new FullInt64(42, toBase.dec)` | Stores the value in radix 10¹⁹ per limb. ⚠️ Wrong results on the native (Windows x64) path. Avoid. |

## Arithmetic

| Operator | Engine method | Semantics |
|---|---|---|
| `a + b` | `add` | Exact. Mixed signs become a magnitude subtraction. |
| `a - b` | `sub` | `a + (−b)` |
| `-a` | `invertor` | Flips the sign. ⚠️ Shares the array with `a`. |
| `a * b` | `mult` | Exact. Result sign is `sign(a) XOR sign(b)`. ⚠️ `0 * negative` gives a negative zero. |
| `a / b` | `div` | **Truncates toward zero**, like C# `/` and `BigInteger.Divide`. Throws `DivideByZeroException` when `b == 0`. |
| `a % b` | `mod` | Remainder takes **the sign of the dividend**, like C# `%`. Satisfies `a == (a / b) * b + a % b`. |
| `a ^ b` | `pow` | ⚠️ **Power, not XOR.** Square-and-multiply. A negative exponent is treated as positive. Will become `Pow(int)`. |
| `++a`, `--a` | `add(a, 1)`, `sub(a, 1)` | Allocates a new value |
| `abs(a)` | `FullInt64Engine.Instance.abs` | |

Every operation allocates a new result. Results are normalized: leading zero limbs are removed and at least one limb is kept.

## Bitwise operations

Semantics match `System.Numerics.BigInteger`: a negative number behaves as an infinitely sign-extended two's complement value. Native kernels: `Logic.asm` (AND/OR/XOR in one pass, two's complement computed on the fly) and `Shift.asm` (`shld`/`shrd`). The managed fallback core has identical implementations.

| Operator / method | Semantics |
|---|---|
| `a & b`, `a \| b` | AND, OR |
| `FullInt64.Xor(a, b)` | XOR. A method, because `^` is the power operator in `FullInt64` |
| `~a` | `-a - 1` |
| `a << n` | `a · 2ⁿ`; a negative `n` shifts right |
| `a >> n` | Arithmetic shift, rounds toward negative infinity (`-1 >> 100 == -1`); a negative `n` shifts left |

Bitwise operations require binary values and throw `NotSupportedException` for `toBase.dec` values.

## Comparison

| Operator / method | Semantics |
|---|---|
| `==`, `!=`, `<`, `<=`, `>`, `>=` | Numeric comparison by sign, then by magnitude. Operands in different radix modes are converted first. |
| `Equals(object)` | Works for `FullInt64`. ⚠️ Throws `InvalidCastException` for boxed `int`, `long` or `byte`. |
| `GetHashCode()` | ⚠️ Hashes the array reference: equal values can have different hash codes. Do not use as a dictionary key yet. |
| `FullInt64Engine.Compare(a, b)` | Returns −1, 0 or +1 |

## Parsing

`FullInt64 x = "...";` calls `FullInt64Engine.ToFullInt64(string)`:

| Input | Interpreted as |
|---|---|
| `""` | `0` |
| `-` prefix | negative |
| `0x…` or `0X…` | hexadecimal, 16 hex digits per limb |
| `0…` (leading zero, length > 1) | ⚠️ **octal** (`"007"` is 7, `"08"` throws) |
| anything else | decimal, parsed by the native `FromDecString` (19 digits per chunk) |

There is no whitespace trimming, no `+` sign, no digit separators and no validation: invalid characters produce garbage or exceptions. `Parse`/`TryParse` with `NumberStyles` are planned.

## Formatting

| Call | Output |
|---|---|
| `x.ToString()` | ⚠️ **Hex** (`0x…`) for binary-stored values, including values parsed from decimal strings. Decimal only for `toBase.dec` values. |
| `x.ToString(10)` | Decimal, with a leading `-` for negative values. Uses the native `ToDecString` (repeated division by 10¹⁹). |
| `x.ToString(16)` | `0x` + uppercase hex, zero-padded to 16 digits per limb after the first |
| `x.ToString(n)`, other `n` | Repeated single-limb division by `n`. Bases up to 16 give digits `0–9A–F`. |
| `(string)x` | Same as `ToString()` |

## Conversion

| Call | Semantics |
|---|---|
| `(ulong[])x` | Returns the internal limb array (MSW first) without copying |
| `FullInt64Engine.ConvertToBase(x, toBase)` | Converts between binary and decimal storage modes |

## Low-level core (`IFullIntCore`)

For allocation-sensitive code, `FullIntCoreFactory.Create()` returns the platform core. It works on unsigned magnitudes in caller-provided `Span<ulong>` buffers. The contracts are listed in [ARCHITECTURE.md](ARCHITECTURE.md#core-contract-ifullintcore). This surface is internal in spirit and **will change** in Core 2.0 (little-endian limbs, `ReadOnlySpan` inputs).

## Planned API (Core 2.0)

```csharp
FullInteger.Parse(ReadOnlySpan<char>, NumberStyles, IFormatProvider?)
FullInteger.TryParse(...);  x.TryFormat(Span<char>, out int, ReadOnlySpan<char> format, ...)
FullInteger.Pow(x, int); FullInteger.ModPow(x, e, m); FullInteger.GreatestCommonDivisor(a, b)
FullInteger.DivRem(a, b);   FullInteger.Sqrt(x);  x.GetBitLength();  x.TryWriteBytes(...)
&, |, ^ (XOR), ~, <<, >>, >>>   // two's-complement semantics, like BigInteger
explicit/implicit conversions to and from BigInteger, Int128, UInt128, double, decimal
INumber<T>, IBinaryInteger<T>, ISignedNumber<T> (generic math)

FullIntOps.Add(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> r)   // zero-alloc layer
FullIntOps.Multiply(...), FullIntOps.DivRem(...), FullIntOps.Square(...)
```
