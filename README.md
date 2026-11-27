# FullInt

[![CI](https://github.com/denis-king/FullInt/actions/workflows/ci.yml/badge.svg)](https://github.com/denis-king/FullInt/actions/workflows/ci.yml)

Fast arbitrary-precision integers for .NET and C++. In .NET, `FullInt` is an alternative to `System.Numerics.BigInteger` with the same API and a faster engine: 64-bit limbs, hand-written assembly kernels for x64, x86, ARM64 and ARMv7, and sub-quadratic algorithms. In C++, `fullnumerics::FullInt` runs on the same kernels.

> **Status: preview before 1.0.** The API is settled and tested against `BigInteger` and GMP. The NuGet package builds and installs on every supported platform, but it is not published yet; see [Installing](#installing).

## Why not BigInteger

`BigInteger` is fine for small numbers and slow for big ones:
- it uses 32-bit words and plain C#;
- its multiplication and decimal conversion slow down much faster than the numbers grow: at 4096 words on .NET 10, a product takes 5.8 ms against FullInt's 0.3 ms, and `ToString` 65 ms against 2.3 ms.

.NET 10 made its division much faster than .NET 8's, but FullInt is still 11–23× faster on large numbers.

FullInt keeps the API and replaces the engine:

| | `FullInt` |
|---|---|
| Word | 64 bits; assembly kernels per CPU (BMI2 / ADX and AVX2 on x64, NEON on ARM64 and ARMv7), chosen at start-up |
| Multiplication | schoolbook, Karatsuba, Toom-3 / 4 / 6, NTT |
| Division | schoolbook, Burnikel–Ziegler, Newton |
| Decimal text | divide and conquer, both ways |
| Loops without allocation | `FullInt.Add(result, a, b)` and its siblings |

How many times faster than `BigInteger` on .NET 10 (Windows x64, Ryzen 7 7730U; a word is 64 bits; above 1 means FullInt is faster):

| Operation | 1 word | 4 | 16 | 64 | 256 | 1024 | 4096 | 16384 |
|---|---|---|---|---|---|---|---|---|
| `a * b` | 1.8 | 2.7 | 4.4 | 5.0 | 4.8 | 8.5 | 18 | 35 |
| `c / b` | 1.9 | 1.6 | 4.6 | 6.8 | 5.2 | 6.9 | 11 | 23 |
| `ToString()` | 2.4 | 1.1 | 1.5 | 2.9 | 7.1 | 14 | 28 | 97 |
| `Parse` | 1.7 | 2.1 | 2.6 | 2.8 | 3.9 | 4.7 | 6.9 | 10 |
| `a + b` | 1.5 | 1.4 | 2.3 | 2.4 | 3.7 | 3.4 | 2.7 | 2.6 |

FullInt is at least as fast in every measured cell, from a single word up. The gain grows with the size, because the algorithms differ in complexity. Against .NET 8's `BigInteger` the division gains are far larger (219× at 4096 words), because .NET 10 sped its division up. On ARM64 (Neoverse-N1, .NET 10) the gains are smaller: multiplication 1.2–25×, division 1.1–15×, `ToString` up to 53×; a few operations on 2–4 words are on par (0.96–1.0). On 32-bit ARM (ARMv7) multiplication is 3–20× and division 3–11× faster than `BigInteger` from 8 words, but operations on 1–8 words that allocate their result are slower (0.34–0.8), because the runtime allocates 64-bit arrays slowly there. The full tables, the other operations and the raw times: [docs/benchmarks/2026-11-26-net-vs-biginteger.md](docs/benchmarks/2026-11-26-net-vs-biginteger.md) (.NET 8 and .NET 10) and [docs/benchmarks/2026-11-27-all-platforms.md](docs/benchmarks/2026-11-27-all-platforms.md) (every platform, C++ and .NET).

For values that fit in 64 or 128 bits, `long` and `Int128` beat both.

The C++ library on the same kernels runs at or ahead of GMP 6.3 on x64 and x86 (0.11–1.0 of its time in almost every cell) and on ARMv7 for most operations; on ARM64 it is behind in division (1.1–1.4×) and `ToString` (up to 1.5×). The tables: [docs/benchmarks/2026-11-27-all-platforms.md](docs/benchmarks/2026-11-27-all-platforms.md); the way there: [docs/HISTORY.md](docs/HISTORY.md).

## Quick start

```csharp
using FullNumerics;

FullInt a = FullInt.Parse("123456789012345678901234567890");
FullInt b = 42;                                     // from any integer type

var p = FullInt.Pow(a, 100) * b;
var q = p / a;                                      // truncates toward zero, like BigInteger
var x = a ^ b;                                      // XOR, as for int and BigInteger

Console.WriteLine(p);                               // decimal
Console.WriteLine(p.ToString("X"));                 // hexadecimal
byte[] bytes = p.ToByteArray();                     // the BigInteger format
var big = (System.Numerics.BigInteger)p;            // and back: (FullInt)big
```

```cpp
#include <fullnumerics/FullInt.h>
using fullnumerics::FullInt;

FullInt p = fullnumerics::Pow(FullInt::Parse("123456789012345678901234567890"), 100);
std::cout << p.ToString() << "\n";
```

The whole API, with the rules for division, signs and bit operations, is in [docs/OPERATIONS.md](docs/OPERATIONS.md). It covers:
- operators;
- `Parse` / `ToString` with formats;
- bytes and words in either order;
- conversions to and from every number type;
- System.Text.Json;
- generic math (`IBinaryInteger<FullInt>`, .NET 7+).

## Installing

**.NET.** One NuGet package, `FullInt`, with two builds:
- `net8.0`, for .NET 8 and later;
- `netstandard2.0`, for .NET Framework 4.6.1+, Mono and Unity.

The native libraries come inside the package. Until it is published, build it locally:

```powershell
powershell -File pack/pack-nuget.ps1                # out/nuget/FullInt.<version>.nupkg (needs Docker for the Linux libraries)
dotnet add package FullInt --prerelease --source out/nuget
```

**C++.** CMake; three ways, described in [docs/CPP_INSTALL.md](docs/CPP_INSTALL.md):
- `cmake --install` and then `find_package(FullInt)`;
- `add_subdirectory`;
- `FetchContent` from this repository.

In every case the project links `FullInt::FullInt`.

## Platforms

| | Kernels | .NET | C++ |
|---|---|---|---|
| Windows x64 | x86-64 assembly | ✅ | ✅ MSVC |
| Windows x86 | x86 assembly | ✅ | ✅ MSVC |
| Linux x64 | x86-64 assembly | ✅ | ✅ GCC, Clang |
| Linux ARM64 | AArch64 assembly | ✅ | ✅ GCC, Clang |
| Linux ARMv7 (32-bit, armhf) | ARMv7 assembly | ✅ | ✅ GCC |
| macOS, Windows ARM64, Android, iOS, others | managed C# (.NET only) | ✅ same results, slower | — |

If the native library cannot be loaded, the .NET library switches to its managed core without a word. `FULLINT_FORCE_MANAGED=1` forces it. The details, including the ABIs and the ARM ports, are in [docs/PLATFORMS.md](docs/PLATFORMS.md).

## Correctness

Every operation is checked differentially, on random and edge-case numbers from 1 word to millions of bits:
- **.NET:** against `BigInteger`, on the native and the managed core, on .NET 8 and .NET Framework 4.8, x64 and x86;
- **C++:** against GMP, with every kernel variant: the CPU's own and `FULLINT_CPU=baseline`.

The suites run on Windows x64 and x86, Linux x64, Linux ARM64 and Linux ARMv7 on our machines; CI (GitHub Actions, on every push) runs the suites on Windows x64, x86 and ARM64, Linux x64, ARM64 and ARMv7 (under QEMU), and macOS (Apple Silicon and Intel). The runs: https://github.com/denis-king/FullInt/actions

## Building from source

Requirements on Windows:
- .NET SDK 8 or later;
- Visual Studio 2022+ (or the Build Tools) with the **Desktop development with C++** workload, for `link.exe`;
- NASM, which is bundled in `Tools/nasm`.

```powershell
dotnet build FullInt.sln -c Release                 # builds the native library of this machine too
dotnet test  FullInt.Tests -c Release               # net8.0 and net48
powershell -File docker/test-linux-x64.ps1          # Linux x64 in Docker: C++ against GMP and .NET
```

How the native build works:
- `FullInt.AsmForNet` assembles the kernels for the build machine. On Windows x64 it builds the x86 library as well.
- The result is copied next to every project that references `FullInt.Core`.

| Option | Effect |
|---|---|
| `FULLINT_LINK=<path>`, `FULLINT_NASM=<path>` (environment) | The linker and assembler to use |
| `-p:FullIntNativeRid=<rid>` | Build the native library for another RID |
| `-p:FullIntSkipNative=true` | No native build; the managed core is used |

The C++ library and its tests against GMP: [docs/CPP_INSTALL.md](docs/CPP_INSTALL.md) and `FullInt.Cpp/CMakePresets.json` (`x64-bench`, `x86-bench`).

## Repository

```
FullInt.Core/        the .NET library (assembly and package FullInt, namespace FullNumerics)
FullInt.Asm/         assembly kernels: Sources/x64, Sources/x86 (NASM), Sources/arm64, Sources/arm32 (GNU as); build scripts
FullInt.AsmForNet/   MSBuild glue: builds the native library and copies it next to the outputs
FullInt.Cpp/         the C++ library (CMake), its tests and benchmarks against GMP
FullInt.Tests/       MSTest: differential tests against BigInteger
FullInt.Benchmarks/  benchmarks: against BigInteger (cross), asm A/B (ab.ps1), BenchmarkDotNet suites
pack/                the NuGet package: build script, .targets, package README, consumer test
docker/              Linux x64 / ARM64 / ARMv7 test images
docs/                documentation
```

## Documentation

| Document | Contents |
|---|---|
| [OPERATIONS.md](docs/OPERATIONS.md) | The public API and its semantics |
| [CPP_INSTALL.md](docs/CPP_INSTALL.md) | Using the C++ library |
| [USE_CASES.md](docs/USE_CASES.md) | Where big integers are used and where FullInt fits (not for production cryptography) |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers, data representation, native interop, build pipeline |
| [ALGORITHMS.md](docs/ALGORITHMS.md) | Algorithms, complexity, thresholds |
| [PLATFORMS.md](docs/PLATFORMS.md) | ABIs, toolchains, ARM64, ARMv7, NuGet packaging |
| [HISTORY.md](docs/HISTORY.md) | From byte arithmetic to the race with GMP |
| [benchmarks/](docs/benchmarks/) | Measurements |

## Roadmap

- **1.0:** publishing the NuGet package.
- **After 1.0:**
  - `ModPow`, `GreatestCommonDivisor`, `Sqrt`;
  - per-CPU self-tuning of the thresholds;
  - closing the ARM64 gaps to GMP;
  - macOS (Apple Silicon) and Windows ARM64 kernels;
  - a vcpkg port.
- **Later:** `FullDecimal`, a big float.

## License

[MIT](LICENSE), Copyright (c) 2026 Denys Omelianenko. Free to use, change and distribute, in open and closed products alike; keep the copyright notice and the license text with the code.

The bundled tools keep their own licenses:
- `Tools/nasm`: NASM, BSD 2-clause;
- `Tools/llvm`: `llvm-mc`, Apache-2.0 with the LLVM exception (`Tools/llvm/LICENSE.TXT`).

They are used only to build the library and are not part of the package. GMP is used only by the C++ tests and benchmarks, as the reference; it is not linked into the library.
