# Architecture

This document describes the code as it is today. The target design is summarized at the end; the reasoning behind it is in [ANALYSIS.md](ANALYSIS.md) §4.

## Layers

```
┌───────────────────────────────────────────────────────────────┐
│ FullInt  (FullInt.Core/FullInt.cs)                         │  public value type: operators,
│   sign + ulong[] magnitude + IsDec flag                        │  implicit conversions, ToString
└──────────────────────────────┬────────────────────────────────┘
                               │ static singleton
┌──────────────────────────────▼────────────────────────────────┐
│ FullIntEngine  (FullIntEngine.cs)                          │  sign handling, buffer sizing,
│   add/sub/mult/div/mod/pow/compare, base conversion, parsing   │  normalization, string formats
└──────────────────────────────┬────────────────────────────────┘
                               │ IFullIntCore (magnitude-only, Span<ulong>)
             ┌─────────────────┴───────────────────┐
┌────────────▼─────────────┐          ┌────────────▼─────────────┐
│ FullIntCoreBase          │          │ FullIntCoreManaged       │
│  pins spans → IntPtr     │          │  managed C# fallback     │
│ ├ FullIntCoreNative64    │          └──────────────────────────┘
│ └ FullIntCoreNative32    │
│    [DllImport] ...WIN    │
└────────────┬─────────────┘
             │ P/Invoke, logical names FullIntOperations64 / 32
             │ (NativeLibraryResolver maps them to the platform's file)
┌────────────▼─────────────────────────────────────────────────┐
│ FullIntOperations_win_x64.dll        (Sources/x64)           │
│ FullIntOperations_win_x86.dll        (Sources/x86)           │
│ libFullIntOperations_linux_x64.so    (Sources/x64, System V) │
│ libFullIntOperations_linux_arm64.so  (Sources/arm64)         │
│  the same entry names and contracts on every platform        │
└──────────────────────────────────────────────────────────────┘
```

`FullIntCoreFactory.Create()` picks the core from `Environment.OSVersion` and `RuntimeInformation.ProcessArchitecture`: Windows x64 and Linux x64 / arm64 get `FullIntCoreNative64`, Windows x86 gets `FullIntCoreNative32`. Every other combination, a library that fails to load, or `FULLINT_FORCE_MANAGED=1` falls back to `FullIntCoreManaged`.

## Data representation

| Aspect | Current choice | Notes |
|---|---|---|
| Limb size | `ulong` (64 bits) | |
| Limb order | **big-endian**: `value[0]` is the most significant limb | The opposite of GMP and `BigInteger`. Normalizing means slicing off leading zeros and copying (`FullHelper.DeleteZeros(...).ToArray()`). |
| Sign | separate `bool Sing` (sign-magnitude) | Zero can end up negative (see ANALYSIS C7). |
| Radix | 2⁶⁴ (`toBase.hex`), or 10¹⁹ per limb (`toBase.dec`, `IsDec = true`) | Decimal mode only works in managed code. The asm kernels are binary-only. |
| Minimum length | 1 limb (zero is `{ 0 }`) | |
| Mutability | mutable class with a public `ulong[] value` setter | The copy constructor shares the array with the original. |

## Core contract (`IFullIntCore`)

All core methods work on **magnitudes** (unsigned limb spans, MSW first). Signs are handled in the engine.

| Method | Contract |
|---|---|
| `ulong Add(a, b, r)` | `r = a + b`. `r.Length ≥ max(len) + 1` for a full result. Returns the final carry. |
| `int Sub(a, b, r)` | `r = |a − b|`. Returns `sign(a − b)` as −1, 0 or +1, so the engine can derive the result sign. |
| `void Mult(a, b, r)` | `r = a × b`, `r.Length ≥ lenA + lenB`. |
| `void Div(a, b, q, rem)` | Long division. `q.Length ≥ lenA − lenB + 1`; `rem.Length = lenB + 2`. The first two limbs of `rem` are scratch space for the partial dividend. |
| `void Div(a, ulong d, q, out r)` | Division by a single limb. `q` may alias `a` (in-place). |
| `int Compare(a, b)` | Compares magnitudes, ignoring leading zero limbs. |
| `ToDecString(value, bytes, out start)` | Writes ASCII digits right-aligned into `bytes`; `start` is the index of the first digit. |
| `FromDecString(bytes, limbs, out start)` | Parses ASCII digits into right-aligned limbs. |

## Native interop

- **Calling convention bridge.** The kernels are written for the System V AMD64 ABI (arguments in `rdi, rsi, rdx, rcx, r8, r9`). Each `…WIN` entry point moves the Windows x64 arguments (`rcx, rdx, r8, r9`, then the stack at `[rsp+28h]`, `[rsp+30h]`) into System V registers and `call`s the shared body. The same body is meant to be exported unchanged on Linux and macOS.
- **Register preservation is missing.** See ANALYSIS A3. Windows treats `rdi`, `rsi`, `rbx`, `rbp` and `r12–r15` as non-volatile, and System V treats `rbx`, `rbp` and `r12–r15` as non-volatile. The kernels must save every one of these they use.
- **Pinning.** `FullIntCoreBase` pins each span with `fixed` and passes raw pointers as `IntPtr`, together with lengths as `int`.
- **Library loading.** The `DllImport`s use the logical names `FullIntOperations64` / `FullIntOperations32` (`NativeNames`). On .NET 5+ `NativeLibraryResolver`, registered by a module initializer, maps them to the file of the OS and architecture. The netstandard2.0 build (.NET Framework, Windows only) has no resolver API and uses the Windows file names directly. The build copies the libraries flat into the output folder.

## Build pipeline

```
dotnet build
  └─ FullInt.Core → ProjectReference → FullInt.AsmForNet (netstandard2.0, no sources)
        ├─ resolve FullIntNativeRid = $(RuntimeIdentifier) or host (e.g. win-x64)
        ├─ supported (FullIntNativeSupportedRids = win-x64)?
        │     no  → warning FULLINT001, managed fallback
        │     yes → Target FullIntBuildNative (BeforeBuild, incremental on sources/def/scripts)
        │             Windows: FullInt.Asm/build/build-native.cmd <Config> <arch> <objDir> <binDir>
        │               ├─ nasm -f win64 [-g]  Sources/x64/*.asm → obj/<rid>/<Config>/*.obj  (obj dir wiped first)
        │               ├─ link.exe (via FULLINT_LINK | vswhere | PATH) /dll /noentry /def:exports.def
        │               └─ → bin/<rid>/<Config>/FullIntOperations_win_x64.dll (+ .pdb in Debug)
        │             Unix:    FullInt.Asm/build/build-native.sh <Config> <os> <arch> <objDir> <binDir>  (skeleton)
        └─ <None CopyToOutputDirectory> flows the library to every consumer's output folder
```

To enable a new platform: implement its branch in the build script, add its RID to `FullIntNativeSupportedRids` in `FullInt.AsmForNet.csproj`, and add a matching core class in `FullIntCoreFactory`. The `TODO(...)` notes in `build-native.sh` list what each Unix target still needs.

`FullInt.Asm.vcxproj` is browse-only: it lists the sources and scripts for Visual Studio but is not built by the solution.

### Debugging the native code

Build in `Debug`. NASM then emits CodeView symbols (`-g`) and `link /DEBUG` produces a `.pdb` that is copied next to the DLL. In Visual Studio, enable **Native code debugging** (mixed mode) for the test project. You can then set breakpoints in the `.asm` files. `FullInt.Tests/debugTestBigData.bat` shows a WinDbg alternative.

## Legacy code

| Item | What it is | Plan |
|---|---|---|
| `FullInt.Core/FullInt.cs`, `FullIntMath.cs`, `FullIntConvertor.cs` | First version: one byte per limb, radix 256 or 100 | Removed 2026-11-25 (in git history) |
| `FullInt.Net/` | Duplicate of the byte-based version | Removed 2026-11-25 |
| `FullInt/SQL_FullInt.cs` | SQL Server CLR procedures (commented out), .NET Framework 3.5 | Removed 2026-11-25; a SQL Server integration on the new engine is an idea for later |
| `toBase` / `IsDec` in `FullInt`, `FullIntEngine` | The decimal mode (10^19 a word) of the first versions; its add / sub never used the base (the core adds binary words), so it held only for one-word values | Removed 2026-11-25: FullInt is binary only; fast decimal text through ToString(10) / Parse |

## Target architecture

```
FullInteger (readonly struct, generic math)      BigDecimal / BigFloat (later)
        │                                                 │
        └────────────── FullIntOps (Span API, algorithms in C#) ──────────────┘
                     Karatsuba · Toom-3 · Knuth D · Burnikel–Ziegler · D&C radix conversion
                                         │
                         IMpnKernels (≈10 primitives, little-endian limbs)
                ┌────────────────────────┼──────────────────────────┐
         x86-64 asm (SysV + Win64)   AArch64 asm (AAPCS64)      Managed (Math.BigMul / UInt128)
         + BMI2/ADX variant           Linux / macOS / Windows    always available, reference
```

The native layer only accelerates the code. The managed kernels define correctness, and every test runs against both.
