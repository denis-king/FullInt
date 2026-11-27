# Platforms

The goal is the widest reach: every .NET runtime and every CPU runs the library; native kernels
speed it up where they exist. The managed core (C#) is the guarantee, asm is the accelerator.

## Target frameworks

One code base, two builds of the `FullInt` assembly (project `FullInt.Core`). NuGet gives each app the closest one:

| Build | Runs on | Differences |
|---|---|---|
| `netstandard2.0` | .NET Framework 4.6.1+, Xamarin.iOS / Xamarin.Android, Mono, Unity, UWP, .NET Core 2.0–7 | `System.Memory` and `Microsoft.Bcl.HashCode` packages; native calls always with the GC transition |
| `net8.0` | .NET 8, 9, 10 and later (Windows, Linux, macOS, Android, iOS) | short native calls skip the GC transition (`SuppressGCTransition`, ~3–7 ns per call) |

The differences are a few `#if NET5_0_OR_GREATER` / `#if NETSTANDARD2_0` spots; everything else
is the same source. A newer build (`net10.0`) is added only when it brings something .NET 8 lacks.

Core selection at run time (`FullIntCoreFactory`): by OS and process architecture, then one probe
call into the native library; if it is missing or unusable, the managed core is used silently.
A 32-bit Windows process uses the x86 asm core (`FullIntOperations_win_x86.dll`); a Linux ARMv7 process the managed core with the ARMv7 kernels (`FullIntCoreNativeArm32`, `libFullIntOperations_linux_arm.so`); other 32-bit processes the managed core.

Tested on Windows: .NET 8 x64 (asm and managed), .NET Framework 4.8 x64 (asm and managed),
.NET Framework 4.8 x86 (x86 asm): the whole suite passes on each.

## Support matrix

| RID | Native backend | Toolchain | Status |
|---|---|---|---|
| `win-x64` | x86-64 asm | NASM `-f win64` + MSVC `link.exe` | ✅ builds and runs; see ANALYSIS A1–A3 |
| `linux-x64` | x86-64 asm (same sources, System V entries of `abi.inc`) | NASM `-f elf64` + `cc -shared -nostdlib -z defs` → `libFullIntOperations_linux_x64.so` | ✅ C# (the x64 core through a DllImport resolver) and C++ (CMake + GCC); all tests pass in Docker, see below |
| `osx-x64` | x86-64 asm (same sources) | NASM `-f macho64 --prefix _` + `cc -dynamiclib` | ⏳ stub, same as linux-x64 |
| `linux-arm64` | AArch64 asm (`Sources/arm64`, GNU syntax) + portable C++ kernels for the rest | gcc / clang (`enable_language(ASM)`) | ✅ C++ library: every baseline kernel in AArch64 asm plus the NTT on NEON; tested natively (Galaxy S21+ Cortex-X1, Oracle Ampere A1 Neoverse-N1) against the C++ kernels and GMP. C#: the 64-bit core through `libFullIntOperations_linux_arm64.so` (the same entry names; `Fullint64CpuFeatures` 0, so the baseline entries; `X86Names.s` gives the x86 versions' names) |
| `osx-arm64` (Apple Silicon) | AArch64 asm | `clang` | ❌ not started |
| `win-arm64` | AArch64 asm | `clang --target=aarch64-pc-windows-msvc` or `armasm64` | ❌ not started |
| `win-x86` | x86 (32-bit) asm, a port of the x64 kernels (`Sources/x86`) | NASM `-f win32 --prefix _` + MSVC `link.exe` (`Hostx64\x86`) | ✅ all kernels; built next to win-x64 |
| `linux-arm` (ARMv7, armhf) | ARMv7 asm (`Sources/arm32`, GNU syntax, ARM state, `umaal`): every kernel but the squaring and the approximate quotient (the managed core / the portable C++ ones there) | gcc (`cc -shared -nostdlib`) → `libFullIntOperations_linux_arm.so`; the C++ library adds `CppGeneric.s` (jumps to the portable kernels) | ✅ C# (145 tests) and C++ (kernels against the portable ones, the library against GMP), natively on Neoverse-N1 in AArch32 (an armhf container on the cloud machine) |
| `linux-musl-*`, `browser-wasm`, `ios-*`, `android-*`, others | managed fallback | — | ✅ complete (all tests pass on it) |
| `ios-arm64` | AArch64 asm, later | static library + `DllImport("__Internal")` (iOS forbids loading dynamic libraries) | managed until then |

## ARMv7 (32-bit Linux, armhf)

`Sources/arm32` holds the ARMv7 kernels (GNU syntax, ARM state, `.eabi_attribute 28, 1`: without the hard-float marking the armhf loader refuses the `.so`). They work in 32-bit digits, the words staying big-endian 64-bit limbs as everywhere else:
- `AddSubN.s`: `Fullint64AddNWIN`, `SubNWIN`, `SubCmpNWIN` - two words (four digits) a pass with `ldmdb` / `stmdb`, the carry kept in C across passes (the loop counts with `sub` and `teq #0`, neither touches C);
- `Mult.s`: `Fullint64MultWIN` - one pass per word of the shorter operand, its two digits at once (two rows, two carry chains): four `umaal` per word of the longer one, one load / store of r per word; any `lenR` (a truncated product stops at r's top word);
- `Div.s`: `Fullint64Div2longWin` (by a word), `ToDecStringWin`, `FromDecStringWin` (and their `_recip` names) - 3-by-2 division steps by the reciprocal of the normalized divisor (Möller–Granlund, two `umull` a digit, the corrections branch-free but a rare one). ARMv7 need not have `udiv`: v comes from a 32-step restoring division once a call; for 10^19 it is a constant. A remainder below 10^19 splits into 1 + 9 + 9 digits by 2-by-1 divisions by 10^9 (as 4·10^9, normalized); the digits by the reciprocal of 10. Parse: base-10^19 blocks (one `umaal` a digit), then each pass the running remainder times 10^19 plus the next block (four `umaal` a block);
- `DivLong.s`: `Fullint64DivWIN_recip` - Knuth's algorithm D in 32-bit digits: U = A << s in `work` (lenA + 1 words are enough), D = B << s in r until the remainder goes there; a quotient digit by the 3-by-2 step on U's top three digits, then U -= q·D on the nd - 2 below (`umaal` and a borrow), a rare add-back; U's top two equal to D's: q = 2^32 - 1, the full multiply-subtract, D added back while negative;
- `Logic.s`, `Shift.s`, `Compare.s`: And / Or / Xor (non-negative word by word; negative operands digit by digit with the three two's-complement carry chains), Shl / Shr (a funnel shift of three digits by s mod 32, s < 32 and s >= 32 two copies of the loop; Shr in place), Compare / CmpN.

Left in portable C++ (the C++ library) / the managed core (.NET): the squaring (the product a·a), the approximate quotient (`DivQ`: the exact division instead), the Toom exact division, the header forms.

The C++ library builds them over the portable C++ kernels (`kernels_generic.cpp`, `..._generic` names when `FULLINT_ARM32_ASM` is on); `CppGeneric.s` gives the other entries' names as jumps to those. The .NET library's `.so` is the kernels alone; `FullIntCoreNativeArm32` is the managed core with them. CMake tells ARMv7 by the pointer size: an armhf userland on a 64-bit kernel reports `aarch64`. `DecimalConversion.ShortWords` is 0 on ARMv7: the ToString kernel beats the C# short path there (Math.BigMul is four multiplies on 32-bit).

Tested natively on the cloud machine's Neoverse-N1 in AArch32 (an `ubuntu:24.04` armhf container, `--platform linux/arm/v7`, core 1): the kernels against the portable ones (`fullint_arm64_kernels`, 145166 checks), the library against GMP 6.3 (276150), the .NET suite on .NET 10 (145 tests; the native core picked).

Measured there (2026-11-27, [the report](benchmarks/2026-11-27-arm32.md)). C++ against GMP (FullInt / GMP time): multiplication 0.58-0.78 up to 128 words, on par at 256-1024 (the portable NTT pays off only from 4096 words on this CPU: `NttThreshold` 4096); add / sub 0.27-0.98; compare 0.57-0.95; ToString 0.67-0.80 up to 16 words, 1.0-1.4 above; Parse 0.59-0.98 from 16 words; `mod` 1.0-1.5; shifts 0.9-1.5; `div` 1.1-2.8 (GMP's quotient by an approximate division: the gap left). .NET 10 against `BigInteger` (BigInteger / FullInt time): multiplication 2.9-5.2 from 8 words, division 2-4.6 from 4, ToString 1.7-312, Parse 1-4.4, compare 1.7-2.8 from 16, add / sub from 32 words 1.1-3.5. Operations on 1-8 words that allocate their result are slower than `BigInteger` (0.36-0.9): the runtime allocates a `ulong[]` on 32-bit ARM through its 8-byte-alignment path - `new ulong[2]` 101 ns against `new uint[4]` 37 ns there - and that, not the kernel, sets the time of a short result.

## x86 (32-bit Windows)

`Sources/x86` ports every x64 kernel to 32-bit registers, cdecl, keeping the algorithms: the same three runs
in Add/Sub, the same columns and column bounds in Mult, the same window, digit estimate (q_min / q_max +
interpolation) and corrections in Div, base 10^19 in the decimal conversions. What is 64-bit on x64 is built
from 32-bit operations: a 64-bit word is two dwords (low first); `mul r64` is four `mul r32`; `div r64` is
`fi_div128` (Div128.asm, Knuth with a two-digit divisor); `shld r64` is a small helper. Register maps are in
the file headers; what does not fit the 7 registers lives on the stack.

On .NET Framework x86 a native call costs ~40-45 ns, so `FullIntCoreNative32` sends Compare and
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

**Current implementation** (`FullIntCoreManaged`, same code in both builds): 64×64 → 128 via four 32-bit products, 128/64 via Hacker's Delight `divlu`, multi-word division is a 1:1 port of `Div.asm` (same digit estimate and buffer contract, so both cores give identical results). Speed was not a goal yet.

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

### Linux ARM64 on a cloud machine (native)

`docker/cloud-arm64/run.sh` runs the C++ tests and the API benchmark against GMP (and with `NET=1` the .NET half: one table of GMP / C++ / .NET / BigInteger) inside a container on a native ARM64 machine, e.g. an Oracle Ampere A1 (Neoverse-N1). The script's header has the `docker run` line (one core, `--user`); `SET="kara=12 toom3=96"` sets thresholds without a rebuild. The image needs a user with the host's uid: the .NET SDK's named mutexes fail for an unknown uid.

What the two ARM64 cores showed:
- **Cortex-X1** (the phone). A 64 x 64 -> 128 product (`mul` + `umulh`) costs about 2 cycles, and the thresholds are Karatsuba 24 and Toom-3 192.
- **Neoverse-N1** (a Cortex-A76 relative). The scalar multiplier takes 3 cycles for `mul` and 4 for `umulh`, so 7 a word product (`fullint_mul_probe`). Karatsuba pays from 12 words and Toom-3 from 96; NEON `umull` does two 32 x 32 products a cycle.
- So the ARM64 thresholds should go by the core type: a table by MIDR plus the planned one-time tuner. N1-class cores also want a NEON basecase and decimal conversion.

### Linux ARM64 (Docker, QEMU emulation)

`docker/linux-arm64` is the same Ubuntu 24.04 setup for `linux/arm64`: gcc, clang, cmake, ninja, GMP and .NET 8. On an x64 machine Docker Desktop runs it under QEMU, so the results show correctness only and the timings mean nothing.

```powershell
powershell -File docker/test-linux-arm64.ps1             # C++ (gcc) and .NET
powershell -File docker/test-linux-arm64.ps1 -NoCSharp -Clang
powershell -File docker/test-linux-arm64.ps1 -Kernels    # only the AArch64 kernels against the C++ ones
```

The build lives in the Docker volume `fullint-arm64-cache`, so a second run rebuilds only what changed (`docker volume rm fullint-arm64-cache` starts afresh). A full C++ run executes `fullint_arm64_kernels` (each AArch64 kernel against its C++ version, word for word, no GMP) and then `fullint_tests` against GMP twice: with the kernels the engine picks, and with `FULLINT_CPU=baseline` (the baseline division entries).

First full run (gcc 13, clang 18): `fullint_arm64_kernels` 145166 checks, `fullint_tests` 230679 checks against GMP in both modes, 0 failed. Under QEMU an endless loop in a kernel looks like silence, so `fullint_arm64_kernels` prints each section before it runs and has a watchdog: if nothing moves for a minute, it prints `STUCK at <kernel and its sizes>` and exits; `run-tests.sh` also caps the run with `timeout 1800`.

**How the C++ library builds on AArch64:**
- `FullInt.Asm/Sources/arm64/*.s` has every kernel the x64 baseline set has, with the same algorithms and contracts: add / sub (general, equal-length, the header forms, `SubCmpN`), compare, and / or / xor, shifts, schoolbook multiply, square, low product (`MulLo`), the divisions (one word, long, approximate quotient), exact division for Toom-Cook, and decimal conversion both ways (including the fraction digits). `arith.inc` holds the shared macros.
- AArch64 has no 128 / 64 division, so every division divides by a reciprocal of the normalized divisor (Möller–Granlund). The reciprocal is computed once per division in 32-bit digits by `udiv`. The baseline entries (`Div2longWin`, `DivWin`, `ToDecStringWin`) use the same arithmetic as the `_recip` ones, so `Cpu::Recip` is set on ARM too.
- `src/kernels_generic.cpp` holds every kernel entry in portable C++, mostly a port of the C# managed core with the same contracts, under the names `..._generic`. It is the reference `fullint_arm64_kernels` checks the asm against. `-DFULLINT_ARM64_ASM=OFF` builds it as the kernels themselves, as the baseline for A/B timing.
- Not ported: the x86 vector kernels (NTT on AVX2; AND / shift / compare on AVX2) and the object kernels (`CmpObj`, `AndObj`, `ShlObj`), where the C++ paths do the work. The FFT helpers (`FftAddSub`, `FftShlMod`) also stay in C++. NEON versions are the next step once timings on real ARM hardware show where they pay off.
- The engine's code paths are selected with `FULLINT_X64` / `FULLINT_X86` / `FULLINT_ARM64`, `FULLINT_X86_FAMILY` (asm with cpuid dispatch) and `FULLINT_WORD64` (the 64-bit paths and thresholds) in `kernels.h`.

Under QEMU, `tar` cannot extract into the container (`Cannot open: Invalid argument`), so `run-tests.sh` copies the sources with `cp -a`. Keeping the timestamps lets ninja skip the unchanged sources.

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
FullInt.<version>.nupkg
├── lib/net8.0/FullInt.dll, FullInt.xml
├── lib/netstandard2.0/FullInt.dll, FullInt.xml      (.NET Framework, Mono, Unity)
├── runtimes/
│   ├── win-x64/native/FullIntOperations_win_x64.dll
│   ├── win-x86/native/FullIntOperations_win_x86.dll
│   ├── linux-x64/native/libFullIntOperations_linux_x64.so
│   └── linux-arm64/native/libFullIntOperations_linux_arm64.so
├── build/FullInt.targets, buildTransitive/FullInt.targets
└── README.md                                        (pack/README.md)
```

- The package and the assembly are `FullInt` (the project folder stays `FullInt.Core`), the namespace `FullNumerics`. `FullInt.AsmForNet` is not a dependency (`PrivateAssets="all"`).
- The DllImports use the logical names `FullIntOperations64` / `FullIntOperations32`. On .NET 5+ `NativeLibraryResolver` maps them to the file of the process and loads it with `NativeLibrary.TryLoad(file, assembly, …)`, which finds `runtimes/<rid>/native` through the app's `deps.json`.
- .NET Framework does not read `runtimes/`. `FullInt.targets` copies both Windows libraries next to the app: an AnyCPU exe gets `RuntimeIdentifier = win-x86` from the SDK but may run as a 64-bit process. The library that the RID already brings is not added twice.
- If a library is missing or does not load, the managed core is used.

### Building the package

```powershell
powershell -File pack/pack-nuget.ps1              # out/native/<rid>/ and out/nuget/*.nupkg, *.snupkg
powershell -File pack/pack-nuget.ps1 -SkipLinux   # reuse out/native/linux-* from an earlier run
```

The script builds:
- `win-x64` and `win-x86` here;
- `linux-x64` in Docker (the `docker/linux-x64` image);
- `linux-arm64` in Docker under QEMU (`docker/linux-arm64`).

Then it runs `dotnet pack`. A plain `dotnet pack` without the four libraries stops with `FULLINT002`. Nothing is published. The license is not set yet: `PackageLicenseExpression` comes before the first push.

### Checking the package

`pack/test` is a consumer project. It takes the package from `out/nuget`, keeps its own packages folder in `out/pkgtest/packages` (a rebuilt package of the same version is never taken from the cache), prints the core that was loaded, fails unless it is the native one (`EXPECT_CORE` overrides), and checks the arithmetic against `BigInteger`.

```powershell
dotnet run --project pack/test -c Release -f net8.0             # win-x64
dotnet run --project pack/test -c Release -f net8.0 -a x86      # win-x86
dotnet build pack/test -c Release -f net48 -o out/pkgtest/any; out/pkgtest/any/PackageTest.exe
dotnet build pack/test -c Release -f net48 -p:Platform=x86 -o out/pkgtest/x86; out/pkgtest/x86/PackageTest.exe
```

On Linux: `dotnet run -c Release -p:TargetFrameworks=net8.0 -f net8.0` in `pack/test`.

`1.0.0-preview.1` (2026-11-26) loaded the native core everywhere:
- .NET 8 on Windows x64 and x86;
- .NET Framework 4.8 AnyCPU (64-bit process) and x86;
- .NET 8 on Linux x64 (Docker) and Linux ARM64 (Neoverse-N1).

`FULLINT_FORCE_MANAGED=1` gives the managed core.

macOS (`osx-arm64`, `osx-x64`) and `win-arm64` would add their own `runtimes/<rid>/native` folders. They need a Mac or CI runners.

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
