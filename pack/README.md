<!-- DRAFT: the table gets numbers before publishing (a fresh benchmark against BigInteger, x64 and ARM64) -->
# FullInt

Fast arbitrary-precision integers for .NET. `FullInt` follows the API of `System.Numerics.BigInteger` and replaces its engine.

## Why FullInt

`BigInteger` is fine for small numbers and slow for big ones: it uses 32-bit words, schoolbook multiplication up to large sizes and a quadratic `ToString`. FullInt keeps the API and changes what is underneath:

- **Limbs and kernels.**
  - 64-bit limbs.
  - Hand-written assembly kernels, chosen for the CPU at start-up: BMI2 / ADX and AVX2 on x64, and ARM64.
- **Algorithms.**
  - Multiplication: Karatsuba, Toom-Cook and NTT.
  - Division: Newton and Burnikel–Ziegler.
  - Fast decimal conversion both ways.
- **In-place API.** `FullInt.Add(result, a, b)` and its siblings do not allocate in loops.
- **The C++ version** runs on the same kernels, on par with GMP.

| vs BigInteger (.NET 8, x64) | 8 words | 64 words | 1024 words |
|---|---|---|---|
| Multiply | … | … | … |
| Divide | … | … | … |
| ToString / Parse | … | … | … |

The gain starts at a few hundred bits. On short numbers FullInt is no faster than `BigInteger`, because every operator returns a new object. For values that fit in 64 or 128 bits, `long` and `Int128` beat both.

Typical uses:
- cryptography experiments and number theory;
- exact computation;
- big factorials and constants.

```csharp
using FullNumerics;

FullInt a = FullInt.Parse("123456789012345678901234567890");
FullInt b = 42;                              // from any integer type

var p = FullInt.Pow(a, 100) * b;
var (q, r) = FullInt.DivRem(p, a);           // .NET 7+; DivRem(p, a, out r) everywhere
Console.WriteLine(p.ToString("X"));          // D (default), X, x, B
```

## What is in it

- **Operators.** `+ - * / % & | ^ ~ << >>` and the comparisons. `/` truncates toward zero and `^` is XOR, as for `int` and `BigInteger`. The power is `FullInt.Pow`.
- **Text.** `Parse` / `TryParse` for decimal, and for hexadecimal after `0x`. `ToString` with the `D`, `X` and `B` formats.
- **Conversions.**
  - Bytes, as `BigInteger`: `ToByteArray`, `TryWriteBytes`, the span constructor.
  - 64-bit words in either order: `FromWords`, `ToWords`.
  - `BigInteger`, every integer type, `Int128`.
  - `double` and `float`, correctly rounded; `decimal`.
  - System.Text.Json.
- **Generic math (.NET 7+).** `IBinaryInteger<FullInt>` and `ISignedNumber<FullInt>`.
- **In-place arithmetic.** `FullInt.Add(result, a, b)` and its siblings reuse the result's storage, so a loop does not allocate.

## Platforms

| Runtime identifier | Core |
|---|---|
| `win-x64`, `linux-x64` | x86-64 assembly |
| `win-x86` | x86 assembly |
| `linux-arm64` | AArch64 assembly |
| anything else | managed C#: the same results, slower |

The package has two builds:
- `net8.0`, for .NET 8 and later;
- `netstandard2.0`, for .NET Framework 4.6.1+, Mono and Unity.

If the native library cannot be loaded, the managed core takes over silently. `FULLINT_FORCE_MANAGED=1` forces it.

Source, documentation and benchmarks: https://github.com/denis-king/FullInt
