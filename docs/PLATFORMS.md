# Platforms

The goal is the widest reach: every .NET runtime and every CPU runs the library; native kernels
speed it up where they exist. The managed core (C#) is the guarantee, asm is the accelerator.

## Target frameworks

One code base, two builds of `FullInt.Core`. NuGet gives each app the closest one:

| Build | Runs on | Differences |
|---|---|---|
| `netstandard2.0` | .NET Framework 4.6.1+, Xamarin.iOS / Xamarin.Android, Mono, Unity, UWP, .NET Core 2.0–7 | `System.Memory` and `Microsoft.Bcl.HashCode` packages; native calls always with the GC transition |
| `net8.0` | .NET 8, 9, 10 and later (Windows, Linux, macOS, Android, iOS) | short native calls skip the GC transition (`SuppressGCTransition`, ~3–7 ns per call) |

The differences are a few `#if NET5_0_OR_GREATER` / `#if NETSTANDARD2_0` spots; everything else
is the same source. A newer build (`net10.0`) is added only when it brings something .NET 8 lacks.

Core selection at run time (`FullIntCoreFactory`): by OS and process architecture, then one probe
call into the native library; if it is missing or unusable, the managed core is used silently.
A 32-bit Windows process uses the x86 asm core (`FullIntOperations_win_x86.dll`); other 32-bit processes (ARM32) use the managed core.

Tested on Windows: .NET 8 x64 (asm and managed), .NET Framework 4.8 x64 (asm and managed),
.NET Framework 4.8 x86 (x86 asm): the whole suite passes on each.

## Support matrix

| RID | Native backend | Toolchain | Status |
|---|---|---|---|
| `win-x64` | x86-64 asm | NASM `-f win64` + MSVC `link.exe` | ✅ builds and runs; see ANALYSIS A1–A3 |
| `linux-x64` | x86-64 asm (same sources, System V entries of `abi.inc`) | NASM `-f elf64` + `cc -shared -nostdlib -z defs` → `libFullIntOperations_linux_x64.so` | ✅ C# (the x64 core through a DllImport resolver) and C++ (CMake + GCC); all tests pass in Docker, see below |
| `osx-x64` | x86-64 asm (same sources) | NASM `-f macho64 --prefix _` + `cc -dynamiclib` | ⏳ stub, same as linux-x64 |
| `linux-arm64` | AArch64 asm (`Sources/arm64`, GNU syntax) + portable C++ kernels for the rest | gcc / clang (`enable_language(ASM)`) | ⏳ C++ library: AddN / SubN / Mult in asm, the rest in C++ (`kernels_generic.cpp`); tested in Docker under QEMU. C#: the managed core |
| `osx-arm64` (Apple Silicon) | AArch64 asm | `clang` | ❌ not started |
| `win-arm64` | AArch64 asm | `clang --target=aarch64-pc-windows-msvc` or `armasm64` | ❌ not started |
| `win-x86` | x86 (32-bit) asm, a port of the x64 kernels (`Sources/x86`) | NASM `-f win32 --prefix _` + MSVC `link.exe` (`Hostx64\x86`) | ✅ all kernels; built next to win-x64 |
| `linux-arm` (32-bit) | managed fallback | — | ✅ complete; no asm planned |
| `linux-musl-*`, `browser-wasm`, `ios-*`, `android-*`, others | managed fallback | — | ✅ complete (all tests pass on it) |
| `ios-arm64` | AArch64 asm, later | static library + `DllImport("__Internal")` (iOS forbids loading dynamic libraries) | managed until then |

## x86 (32-bit Windows)

`Sources/x86` ports every x64 kernel to 32-bit registers, cdecl, keeping the algorithms: the same three runs
in Add/Sub, the same columns and column bounds in Mult, the same window, digit estimate (q_min / q_max +
interpolation) and corrections in Div, base 10^19 in the decimal conversions. What is 64-bit on x64 is built
from 32-bit operations: a 64-bit word is two dwords (low first); `mul r64` is four `mul r32`; `div r64` is
`fi_div128` (Div128.asm, Knuth with a two-digit divisor); `shld r64` is a small helper. Register maps are in
the file headers; what does not fit the 7 registers lives on the stack.

On .NET Framework x86 a native call costs ~40-45 ns, so `FullIntCoreWindowsX86` sends Compare and
add/sub/logic/shifts up to 4 words to the managed core. Measured in a 32-bit process (.NET Framework 4.8),
x86 asm vs the managed core:

| Op | 16 limbs | 64 | 256 |
|---|---|---|---|
| Mult | 12.6x | 17.8x | 17.8x |
| Div 2N/N | 4.9x | 7.8x | 10.6x |
| Add / Sub | 3.1x / 3.4x | 8.4x / 7.6x | 12.4x / 13.9x |
| Div by word | 3.2x | 3.8x | 3.7x |
| ToDecString / FromDecString | 4.2x / 5.7x | 4.1x / 5.4x | |

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

NASM cannot assemble ARM code, so ARM64 needs its own sources. One GNU-syntax `.s` file serves Linux, Android, macOS and Windows on ARM.

- **Directives only.** It uses assembler directives (`.macro`, `.ifdef`, `--defsym FULLINT_MACHO=1` for the `_` symbol prefix on macOS), not the C preprocessor.
- **Toolchains.** It builds the same with clang, GNU `as` and `llvm-mc`.
- **Bundled assembler.** For the Windows x64 development machine, `Tools/llvm/llvm-mc.exe` (LLVM 23.1.6, standalone) emits ELF, Mach-O and COFF objects for AArch64.
- **ARM hosts** (Termux, Linux, macOS) use their native clang.

Checked in Docker `linux/arm64` (QEMU): the same source runs correctly from clang, GNU `as`, and an `llvm-mc` object built on Windows.

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

**Current implementation** (`FullInt64CoreDefault`, same code in both builds): 64×64 → 128 via four 32-bit products, 128/64 via Hacker's Delight `divlu`, multi-word division is a 1:1 port of `Div.asm` (same digit estimate and buffer contract, so both cores give identical results). Speed was not a goal yet.

## Testing on other platforms

### Linux x64 (Docker)

`docker/linux-x64` is an Ubuntu 24.04 image (gcc, cmake, ninja, nasm, libgmp-dev, .NET 8 SDK). From Windows:

```powershell
powershell -File docker/test-linux-x64.ps1                    # C++ and .NET tests
powershell -File docker/test-linux-x64.ps1 -NoCSharp -Bench   # + the C++ benchmark against GMP (CSV in out/linux-x64)
```

The script mounts the repository read-only and `run-tests.sh` copies it into the container. It then:
- builds the C++ library and tests (NASM elf64, GCC -O3);
- runs the tests with the CPU's kernels and with `FULLINT_CPU=baseline`;
- builds the `.so` and runs the .NET tests on the x64 asm core, in both CPU modes.

CPU detection and kernel choice are the same as on Windows. First runs (Ryzen 7 7730U, WSL2): every test passed. C++ against Ubuntu's GMP (GCC, fat build) is within about 10% everywhere except short division at 4–32 words (1.14–1.58, open).

### Linux ARM64 (Docker, QEMU emulation)

`docker/linux-arm64` is the same Ubuntu 24.04 setup for `linux/arm64`: gcc, clang, cmake, ninja, GMP and .NET 8. On an x64 machine Docker Desktop runs it under QEMU, so the results show correctness only and the timings mean nothing.

```powershell
powershell -File docker/test-linux-arm64.ps1             # C++ (gcc) and .NET
powershell -File docker/test-linux-arm64.ps1 -NoCSharp -Clang
```

**How the C++ library builds on AArch64:**
- `src/kernels_generic.cpp` holds every kernel entry in portable C++, mostly a port of the C# managed core with the same contracts.
- The asm kernels in `FullInt.Asm/Sources/arm64/*.s` replace them one at a time. The C++ version of a replaced kernel stays under the name `..._generic`. The tests compare the kernel against it word for word, and the kernel falls back to it for the cases it does not handle.
- `-DFULLINT_ARM64_ASM=OFF` builds the C++ kernels only, as the reference for A/B timing.
- The engine's code paths are selected with `FULLINT_X64` / `FULLINT_X86` / `FULLINT_ARM64`, `FULLINT_X86_FAMILY` (asm with cpuid dispatch) and `FULLINT_WORD64` (the 64-bit paths and thresholds) in `kernels.h`.

Under QEMU, `tar` cannot extract into the container (`Cannot open: Invalid argument`), so `run-tests.sh` copies the sources with `cp`.

### Managed fallback

`FULLINT_FORCE_MANAGED=1` forces the managed core on any machine:

```powershell
$env:FULLINT_FORCE_MANAGED = "1"; dotnet test FullInt.Tests -c Release --filter "FullyQualifiedName!~BigTestDec"
```

Planned for ARM64:

```powershell
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
├── lib/netstandard2.0/FullInt.dll            (.NET Framework, Xamarin, Mono, Unity)
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
- .NET Framework does not read `runtimes/<rid>/native` by itself: the package needs a small `build/FullInt.targets` that copies the native library next to the app (for x64 processes).
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
