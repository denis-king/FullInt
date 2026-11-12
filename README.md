# FullInt

Arbitrary-precision integer arithmetic for .NET, built on 64-bit limbs with hand-written assembly kernels.

> **Status: pre-alpha.** The library is not yet published to NuGet. Only **Windows x64** has a native backend, and the full test suite does not pass yet. See [docs/ANALYSIS.md](docs/ANALYSIS.md) for known issues and the roadmap.

## Why another big-integer library?

`System.Numerics.BigInteger` stores numbers as 32-bit words. FullInt uses **64-bit limbs** and runs the hot loops in native assembly (`mul`, `adc`, `div` on x86-64), so each multiply step handles four times as many bits per word product. The goals:

- **Speed on large numbers.** Native kernels plus sub-quadratic algorithms (Karatsuba and Toom-Cook are planned).
- **Allocation-free low-level API.** Operations on `Span<ulong>` that write into caller-provided buffers.
- **A foundation for big decimals and floats.** `BigDecimal` and `BigFloat` are planned on top of the same integer core.

## Quick start

```csharp
using FullInt.Core;

FullInt64 a = "123456789012345678901234567890";      // decimal string
FullInt64 b = "0xFFFFFFFFFFFFFFFFFFFF";              // hex string
FullInt64 c = 42;                                    // from int/long/byte

var sum  = a + b;
var prod = a * b;
var quot = a / c;
var rem  = a % c;
var pow  = c ^ 100;          // NOTE: '^' is power here, not XOR (will become Pow() — see roadmap)

Console.WriteLine(prod.ToString(10));   // decimal
Console.WriteLine(prod.ToString(16));   // hex with 0x prefix
```

The current public API is listed in [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Supported operations (current)

| Operation | Windows x64 (asm) | Managed fallback |
|---|---|---|
| `+`, `-`, unary `-` | ✅ | ✅ |
| `*` (schoolbook) | ✅ | ✅ |
| `/`, `%` | ✅ | ✅ |
| `&`, `\|`, `Xor`, `~`, `<<`, `>>` (BigInteger semantics) | ✅ | ✅ |
| Compare, `==`, `<`, … | ✅ | ✅ |
| `^` (power, square-and-multiply) | ✅ | ✅ |
| Parse decimal string | ✅ | ✅ |
| `ToString(10)` | ✅ | ✅ |
| Decimal storage mode (`toBase.dec`) | ❌ wrong results | partial |

## Platforms

| RID | Backend | Status |
|---|---|---|
| `win-x64` | NASM x86-64 | working, has known bugs |
| `linux-x64`, `osx-x64` | NASM x86-64 (System V ABI) | sources exist, build scripts are not functional |
| `win-arm64`, `linux-arm64`, `osx-arm64` | AArch64 assembly | planned |
| anything else (incl. 32-bit) | managed C# | ✅ complete, slower than asm |

Details: [docs/PLATFORMS.md](docs/PLATFORMS.md).

## Building

Requirements (Windows x64):

- .NET SDK 8.0 or later
- Visual Studio or Build Tools 2017+ with the **Desktop development with C++** workload (only `link.exe` is used; it is located automatically with `vswhere`)
- NASM is bundled in `Tools/nasm`

```powershell
dotnet build FullInt.sln -c Release
dotnet test  FullInt.Tests/FullInt.Tests.csproj
.\FullInt.Benchmarks\ab.ps1      # after an asm edit: speed vs HEAD
```

`dotnet build` builds the native library too. `FullInt.AsmForNet` runs `FullInt.Asm/build/build-native.cmd`, which assembles `Sources/x64/*.asm` and links `FullIntOperations_win_x64.dll` into `FullInt.Asm/bin/<rid>/<Configuration>/`. The DLL (and its `.pdb` in Debug) is then copied to the output of every project that references `FullInt.Core`. The native step is incremental: it only reruns when a source, `exports.def` or a build script changes.

| Option | Effect |
|---|---|
| `FULLINT_LINK=<path>` (env) | Use this `link.exe` instead of searching with `vswhere` |
| `FULLINT_NASM=<path>` (env) | Use this `nasm.exe` instead of the bundled one |
| `-p:FullIntNativeRid=<rid>` | Build for another RID (non-Windows targets are stubs for now) |
| `-p:FullIntSkipNative=true` | Skip the native build; the managed fallback is used |

On platforms without a native backend yet (`linux-*`, `osx-*`, `win-arm64`), the build succeeds with warning `FULLINT001` and the managed fallback core is used.

## Repository layout

```
FullInt.Core/        main library: FullInt64 type, engine, core abstraction, P/Invoke bindings
FullInt.Asm/         native kernels (NASM, x86-64) and build scripts
FullInt.AsmForNet/   MSBuild glue that builds the native DLL and copies it next to managed outputs
FullInt.Tests/       MSTest suite; randomized differential tests against System.Numerics.BigInteger
FullInt.Benchmarks/  asm regression check (ab.ps1) and BenchmarkDotNet suites, see its README
FullInt.Net/         legacy byte-based implementation (to be removed)
FullInt/             legacy SQL CLR wrapper, .NET Framework 3.5 (to be removed)
Tools/nasm/          bundled NASM assembler
docs/                documentation
```

## Documentation

| Document | Contents |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers, data representation, native interop, build pipeline |
| [OPERATIONS.md](docs/OPERATIONS.md) | Public API, semantics, edge cases |
| [ALGORITHMS.md](docs/ALGORITHMS.md) | Current and planned algorithms, complexity, thresholds |
| [PLATFORMS.md](docs/PLATFORMS.md) | ABIs, assembler toolchains, ARM64 port, NuGet packaging |
| [USE_CASES.md](docs/USE_CASES.md) | Where arbitrary-precision arithmetic is used and where FullInt fits |
| [ANALYSIS.md](docs/ANALYSIS.md) | Project review, known defects, roadmap (in Russian) |

## Roadmap (short)

1. **Stabilize:** fix the division and ABI bugs, make the build portable, make tests deterministic.
2. **Core 2.0:** binary-only little-endian limbs, small native primitive layer (GMP `mpn`-style), complete managed fallback, immutable public type with generic math.
3. **NuGet preview:** `win-x64`, `linux-x64`, `osx-x64` native assets plus the managed fallback.
4. **ARM64 and speed:** AArch64 kernels, Karatsuba and Toom-3 multiplication, divide-and-conquer base conversion.
5. **Extensions:** GCD, ModPow, roots, primality tests, `BigDecimal`, `BigFloat`.

## License

Not chosen yet. MIT or Apache-2.0 is recommended before the first NuGet release.
