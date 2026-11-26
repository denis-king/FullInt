# FullInt

Arbitrary-precision integers for .NET. `FullInt` follows the API of `System.Numerics.BigInteger`. It stores numbers in 64-bit limbs and runs the hot loops in hand-written assembly.

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
