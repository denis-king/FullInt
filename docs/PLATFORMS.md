# Platforms

## Support matrix

| RID | Native backend | Toolchain | Status |
|---|---|---|---|
| `win-x64` | x86-64 asm | NASM `-f win64` + MSVC `link.exe` | ✅ builds and runs; see ANALYSIS A1–A3 |
| `linux-x64` | x86-64 asm (same sources) | NASM `-f elf64` + `cc -shared` | ⏳ stub in `build-native.sh`: needs PIC-safe calls, wrapper guards, a C# core class |
| `osx-x64` | x86-64 asm (same sources) | NASM `-f macho64 --prefix _` + `cc -dynamiclib` | ⏳ stub, same as linux-x64 |
| `linux-arm64` | AArch64 asm | GNU `as` / `clang` | ❌ not started |
| `osx-arm64` (Apple Silicon) | AArch64 asm | `clang` | ❌ not started |
| `win-arm64` | AArch64 asm | `clang --target=aarch64-pc-windows-msvc` or `armasm64` | ❌ not started |
| `linux-musl-*`, `browser-wasm`, `ios-*`, `android-*`, others | managed fallback | — | ✅ complete (all tests pass on it) |

## x86-64

### Calling conventions

| | Windows x64 | System V AMD64 (Linux, macOS) |
|---|---|---|
| Integer args | `rcx, rdx, r8, r9`, then stack at `[rsp+28h]`… (after the 32-byte shadow space) | `rdi, rsi, rdx, rcx, r8, r9`, then stack |
| Return | `rax` | `rax` (`rdx:rax` for 128-bit) |
| Callee-saved (must preserve) | `rbx, rbp, rdi, rsi, r12–r15, xmm6–xmm15` | `rbx, rbp, r12–r15` |
| Caller-saved (free to clobber) | `rax, rcx, rdx, r8–r11, xmm0–5` | `rax, rcx, rdx, rsi, rdi, r8–r11, all xmm` |
| Stack alignment at `call` | 16 bytes | 16 bytes |
| Red zone | none | 128 bytes below `rsp` |
| Unwind info | **required** for non-leaf functions and any function that changes `rsp` or saves registers (`.pdata`/`.xdata`, `[SECTION .pdata]` in NASM or `PROC_FRAME` macros) | `.cfi_*` directives, recommended |
| Symbol names | `Fullint64Add` | Linux: `Fullint64Add`; macOS: `_Fullint64Add` |

**Current design.** Kernel bodies use System V registers. The `…WIN` wrappers translate the arguments and `call` the body. This is a reasonable way to share code, but the wrapper **must** push and pop `rdi`, `rsi` and every other non-volatile register the body touches. A cleaner layout is a NASM macro that emits a prologue and epilogue per ABI:

```nasm
%ifidn __OUTPUT_FORMAT__, win64
  %define ARG1 rcx
  %define ARG2 rdx
  ; ...
%else
  %define ARG1 rdi
  %define ARG2 rsi
  ; ...
%endif
```

GMP, OpenSSL and x264 all use this approach, so there are no wrapper functions and no extra `call`.

### ISA levels

| Feature | Instructions | Gain | Availability |
|---|---|---|---|
| Baseline x86-64 | `mul`, `adc`, `div` | | all |
| BMI2 | `mulx` (multiply without touching flags) | lets multiply and carry chains interleave | Intel Haswell (2013)+, AMD Zen+ |
| ADX | `adcx`, `adox` (two independent carry chains) | about 1.5–2× in `addmul_1` and `mul_basecase` | Intel Broadwell (2014)+, AMD Zen+ |

Select at load time: check `System.Runtime.Intrinsics.X86.Bmi2.X64.IsSupported` (and CPUID for ADX, which .NET does not expose) in C#, then pick between exported `…_adx` and `…_base` entry points.

## AArch64 (ARM64)

NASM cannot assemble ARM code, so ARM64 needs its own sources. A single GNU-syntax `.S` file can serve Linux, macOS and Windows on ARM when built with clang, with a few macros for symbol naming and section directives.

### Calling convention (AAPCS64; Apple and Windows ARM64 are close variants)

| | |
|---|---|
| Integer args / return | `x0–x7` / `x0` (`x0:x1` for 128-bit) |
| Callee-saved | `x19–x28`, `x29` (FP), `x30` (LR), low 64 bits of `v8–v15` |
| Free to use | `x0–x17` |
| Reserved | `x18`: platform register. **Never touch** on macOS and Windows. |
| Stack | 16-byte aligned at all times |

### Key instructions

| Purpose | x86-64 | AArch64 |
|---|---|---|
| 64×64 → 128 | `mul r64` → `rdx:rax` | `mul xLo, a, b` + `umulh xHi, a, b` |
| Add with carry chain | `add` / `adc` | `adds` / `adcs` |
| Subtract with borrow | `sub` / `sbb` | `subs` / `sbcs`. **Inverted carry**: C = 1 means *no* borrow. |
| Capture carry | `setc` | `cset x, cs` (or `adc x, xzr, xzr`) |
| 128/64 divide | `div r64` | **none.** `udiv` is 64/64 only, so use the reciprocal method (Möller–Granlund). |
| Double shift | `shld`/`shrd` | `extr` |
| Count leading zeros | `lzcnt` / `bsr` | `clz` |
| Load pair | — | `ldp`/`stp`: load and store two limbs per instruction, a large win in limb loops |

### Example: `add_n` (r = a + b, n limbs, little-endian limbs, returns carry)

```asm
// x0 = r, x1 = a, x2 = b, x3 = n (n >= 1)   →  x0 = carry
    .globl  fi_add_n
fi_add_n:
    cmn     xzr, xzr            // C = 0
1:  ldr     x4, [x1], #8
    ldr     x5, [x2], #8
    adcs    x4, x4, x5
    str     x4, [x0], #8
    sub     x3, x3, #1          // sub (not subs) keeps the carry flag
    cbnz    x3, 1b
    cset    x0, cs
    ret
```

A production version unrolls 4× with `ldp`/`stp`. Note that the snippet assumes little-endian limb order, which is one more reason to switch (see [ANALYSIS.md](ANALYSIS.md) §4.3).

## Managed fallback

The fallback must be **complete and correct on every platform**. It is both the reference implementation and the safety net when a native library is missing.

| Primitive | Implementation |
|---|---|
| 64×64 → 128 | `Math.BigMul(ulong, ulong, out ulong low)` (.NET 5+, compiles to `mul`/`mulx` or `umulh`) |
| 128/64 divide | `UInt128` division (.NET 7+), or `X86Base.X64.DivRem` where available, otherwise the reciprocal method |
| Add with carry | `ulong s = a + b; carry = s < a ? 1UL : 0UL;` (the JIT does not emit `adc` chains, so this is the main gap with native code) |
| Count leading zeros | `BitOperations.LeadingZeroCount` |

On .NET 8+ the managed kernels typically reach 30–60% of the speed of good asm for multiplication. The gap is mostly carry propagation.

**Current implementation** (`FullInt64CoreDefault`, `netstandard2.1`): 64×64 → 128 via four 32-bit products, 128/64 via Hacker's Delight `divlu`, multi-word division is a 1:1 port of `Div.asm` (same digit estimate and buffer contract, so both cores give identical results). Speed was not a goal yet.

## Testing on other platforms

> **TODO** — not done yet. So far the fallback has only been tested on Windows x64 with `FULLINT_FORCE_MANAGED=1`.

`FULLINT_FORCE_MANAGED=1` forces the managed core on any machine:

```powershell
$env:FULLINT_FORCE_MANAGED = "1"; dotnet test FullInt.Tests -c Release --filter "FullyQualifiedName!~BigTestDec"
```

On Linux the factory picks the fallback by itself, and the native build only emits warning `FULLINT001`. Planned Docker runs from the repository root:

```powershell
# linux x64 (native speed)
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 `
  dotnet test FullInt.Tests -c Release --filter "FullyQualifiedName!~BigTestDec"

# linux arm64 (QEMU emulation in Docker Desktop, slow)
docker run --rm --platform linux/arm64 -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 `
  dotnet test FullInt.Tests -c Release --filter "FullyQualifiedName!~BigTestDec"
```

Mounting the Windows working tree shares `bin/` and `obj/` between Windows and Linux builds. If restore or build gets confused, copy the sources into the container or build with `-p:BaseIntermediateOutputPath=/tmp/obj/` instead. macOS, `win-arm64` and 32-bit targets (`win-x86`, `linux-arm`) need CI runners or real machines.

## Native packaging for NuGet

### Package layout

```
FullInt.nupkg
├── lib/net8.0/FullInt.dll
├── lib/netstandard2.1/FullInt.dll            (optional, without generic math)
└── runtimes/
    ├── win-x64/native/fullint_native.dll
    ├── win-arm64/native/fullint_native.dll
    ├── linux-x64/native/libfullint_native.so
    ├── linux-arm64/native/libfullint_native.so
    ├── osx-x64/native/libfullint_native.dylib
    └── osx-arm64/native/libfullint_native.dylib
```

- Use **one logical library name** (`fullint_native`) on every platform, with `[LibraryImport("fullint_native")]`. The runtime resolves the `lib` prefix and the extension, and NuGet picks the folder that matches the app's RID. No per-platform `DllImport` classes or a `FullIntCoreFactory` OS switch are needed.
- On load failure (`DllNotFoundException`, `EntryPointNotFoundException`), switch silently to the managed kernels. Let users opt out with an `AppContext` switch, for example `FullInt.DisableNative`.
- macOS: build a universal binary (`lipo`) or separate `osx-x64` and `osx-arm64` files. Sign it if it is distributed outside NuGet.

### CI (GitHub Actions sketch)

```yaml
jobs:
  native:
    strategy:
      matrix:
        include:
          - { os: windows-latest,     rid: win-x64 }
          - { os: windows-11-arm,     rid: win-arm64 }
          - { os: ubuntu-latest,      rid: linux-x64 }
          - { os: ubuntu-24.04-arm,   rid: linux-arm64 }
          - { os: macos-13,           rid: osx-x64 }
          - { os: macos-latest,       rid: osx-arm64 }
    steps: [checkout, build native (nasm/clang), dotnet test (native + managed), upload artifact runtimes/<rid>/native]
  pack:
    needs: native
    steps: [download all artifacts, dotnet pack, push to NuGet on tag]
```

Runner labels change over time, so check GitHub's current list when you set this up. Every native build should run the full test suite on its own platform before it is packed.

### Build scripts

The native build lives in `FullInt.Asm/build/`: `build-native.cmd` for Windows (it finds `link.exe` with `vswhere`) and `build-native.sh` for Linux and macOS. `FullInt.AsmForNet.csproj` calls the right script for the target RID (see [ARCHITECTURE.md](ARCHITECTURE.md#build-pipeline)). CI can call the same scripts directly. If the number of targets grows, consider CMake (`enable_language(ASM_NASM)` for x86-64, `ASM` for AArch64) behind the same MSBuild entry point.
