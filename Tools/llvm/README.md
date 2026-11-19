# llvm-mc: the AArch64 assembler

`llvm-mc.exe` is the LLVM machine-code assembler for Windows x64 hosts. It plays the same role for the AArch64 kernels as `Tools/nasm` plays for x86 and x64. The executable is standalone, with no DLLs and no install step.

- **Version:** LLVM 23.1.6. The file is taken unchanged from `clang+llvm-23.1.6-x86_64-pc-windows-msvc.tar.zst` (SHA-256 `7623F582089983C1CB51030CF1996514B19464E5669099571B399E8543770155`) on https://github.com/llvm/llvm-project/releases/tag/llvmorg-23.1.6.
- **`llvm-mc.exe` SHA-256:** `559F2932481EEEAA5DDD0B89910D5FFAFAB8CF2DD0A97AD1815F4AE09ABE50FD`.
- **License:** Apache-2.0 with LLVM Exceptions, see `LICENSE.TXT`.

One GNU-syntax source builds for all three object formats. The sources use assembler directives only (`.macro`, `.ifdef`, `--defsym`), not the C preprocessor, so the same file also builds with GNU `as`, with clang (Linux, Termux, macOS) and with `llvm-mc`:

```
llvm-mc -triple=aarch64-linux-gnu      -filetype=obj x.s -o x.o                          # Linux, Android
llvm-mc -triple=arm64-apple-macos      -filetype=obj --defsym FULLINT_MACHO=1 x.s -o x.o # macOS (symbols get _)
llvm-mc -triple=aarch64-pc-windows-msvc -filetype=obj x.s -o x.obj                       # Windows on ARM
```

On ARM hosts the native toolchain is used instead (clang or gcc). This copy serves cross builds and syntax checks on the Windows x64 development machine.

It was checked on 2026-11-19 with an object built here for `aarch64-linux-gnu`. That object, linked by gcc in Docker `linux/arm64` under QEMU, ran correctly. The same source assembled with GNU `as` and with clang there gave the same result.
