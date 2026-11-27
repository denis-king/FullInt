# FullInt

Fast arbitrary-precision integers for .NET. `FullInt` follows the API of `System.Numerics.BigInteger` and replaces its engine.

## Why FullInt

`BigInteger` is fine for small numbers and slow for big ones: it uses 32-bit words and plain C#, and its multiplication, division and decimal conversion slow down much faster than the numbers grow. FullInt keeps the API and changes what is underneath:

- **Limbs and kernels.**
  - 64-bit limbs.
  - Hand-written assembly kernels for x64 (BMI2 / ADX and AVX2 versions chosen at start-up), x86, ARM64 and ARMv7 (NEON).
- **Algorithms.**
  - Multiplication: Karatsuba, Toom-Cook and NTT.
  - Division: Newton and Burnikel–Ziegler.
  - Fast decimal conversion both ways.
- **In-place API.** `FullInt.Add(result, a, b)` and its siblings do not allocate in loops.
- **The C++ version** runs on the same kernels, on par with GMP on x64 and x86.

How many times faster than `BigInteger` on .NET 10 (Windows x64, Ryzen 7 7730U; a word is 64 bits):

| Operation | 1 word | 4 | 16 | 64 | 256 | 1024 | 4096 | 16384 |
|---|---|---|---|---|---|---|---|---|
| `a * b` | 1.8 | 2.7 | 4.4 | 5.0 | 4.8 | 8.5 | 18 | 35 |
| `c / b` | 1.9 | 1.6 | 4.6 | 6.8 | 5.2 | 6.9 | 11 | 23 |
| `ToString()` | 2.4 | 1.1 | 1.5 | 2.9 | 7.1 | 14 | 28 | 97 |
| `Parse` | 1.7 | 2.1 | 2.6 | 2.8 | 3.9 | 4.7 | 6.9 | 10 |
| `a + b` | 1.5 | 1.4 | 2.3 | 2.4 | 3.7 | 3.4 | 2.7 | 2.6 |

FullInt is at least as fast in every measured cell, from a single word up, and the gain grows with the size. On ARM64 the gains are smaller, and a few operations on 2–4 words are on par. On 32-bit ARM multiplication and division are 3–20× faster from 8 words, but operations on 1–8 words are slower there (the runtime allocates 64-bit arrays slowly on 32-bit ARM). For values that fit in 64 or 128 bits, `long` and `Int128` beat both. Details: https://github.com/denis-king/FullInt/blob/master/docs/benchmarks/2026-11-27-all-platforms.md

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
| `linux-arm` (ARMv7, 32-bit) | ARMv7 assembly |
| anything else | managed C#: the same results, slower |

The package has two builds:
- `net8.0`, for .NET 8 and later;
- `netstandard2.0`, for .NET Framework 4.6.1+, Mono and Unity.

If the native library cannot be loaded, the managed core takes over silently. `FULLINT_FORCE_MANAGED=1` forces it.

Source, documentation and benchmarks: https://github.com/denis-king/FullInt

License: MIT, Copyright (c) 2026 Denys Omelianenko.
