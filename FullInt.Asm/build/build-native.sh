#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Builds the FullInt native kernels on Linux and macOS.
#
#  Usage: build-native.sh <Debug|Release> <linux|osx> <x64|arm64|arm> <objDir> <binDir>
#
#  linux-x64: the x64 sources with NASM -f elf64 (abi.inc gives every entry the System V
#  arguments: the same names and contracts as Windows), linked into
#  libFullIntOperations_linux_x64.so. linux-arm64: the AArch64 sources (Sources/arm64) with cc,
#  linked into libFullIntOperations_linux_arm64.so. The others: not yet (each documents what is
#  missing).
#  FullInt.AsmForNet.csproj calls this script for the RIDs in FullIntNativeSupportedRids.
#  FULLINT_NASM / CC override the tools.
# ---------------------------------------------------------------------------
set -euo pipefail

if [[ $# -ne 5 ]]; then
    echo "usage: build-native.sh <Debug|Release> <linux|osx> <x64|arm64|arm> <objDir> <binDir>" >&2
    exit 1
fi

CONFIG="$1"
OS="$2"
ARCH="$3"
OBJ="$4"
BIN="$5"

ASM_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

not_implemented() {
    echo "build-native: error: $OS-$ARCH native backend is not implemented yet" >&2
    echo "  $1" >&2
    exit 1
}

case "$OS-$ARCH" in
    linux-x64)
        NASM="${FULLINT_NASM:-nasm}"
        DBG=()
        [[ "$CONFIG" == "Debug" ]] && DBG=(-g -F dwarf)
        mkdir -p "$OBJ" "$BIN"
        echo "=== Compiling asm files (elf64) $CONFIG ==="
        for f in "$ASM_ROOT/Sources/x64/"*.asm; do
            name="$(basename "$f" .asm)"
            "$NASM" -f elf64 "${DBG[@]}" -I "$ASM_ROOT/Sources/x64/" "$f" -o "$OBJ/$name.o"
        done
        # the kernels' entries are their global symbols (the calls between kernels go to hidden
        # aliases, abi.inc: no PLT); no C runtime is needed
        "${CC:-cc}" -shared -nostdlib -Wl,-z,noexecstack -Wl,-z,defs             -o "$BIN/libFullIntOperations_linux_x64.so" "$OBJ"/*.o
        echo "=== Build finished: $BIN/libFullIntOperations_linux_x64.so ===" ;;
    osx-x64)
        # TODO(osx-x64): same as linux-x64 with `nasm -f macho64` (the sources add the underscore, abi.inc)
        #   and `cc -dynamiclib -o libFullIntOperations_osx_x64.dylib`
        not_implemented "see TODO(osx-x64) in FullInt.Asm/build/build-native.sh" ;;
    linux-arm64)
        # the AArch64 sources (GNU syntax, directives only: clang or GNU as through cc), linked into
        # libFullIntOperations_linux_arm64.so: the same entry names and contracts as x64, the
        # AAPCS64 arguments; the .NET core takes it for FullIntOperations_win_x64.dll
        # (LinuxNativeLibrary.cs). No C runtime is needed.
        DBG=()
        [[ "$CONFIG" == "Debug" ]] && DBG=(-g)
        mkdir -p "$OBJ" "$BIN"
        echo "=== Compiling asm files (AArch64 ELF) $CONFIG ==="
        for f in "$ASM_ROOT/Sources/arm64/"*.s; do
            name="$(basename "$f" .s)"
            "${CC:-cc}" -c "${DBG[@]}" -Wa,-I"$ASM_ROOT/Sources/arm64" "$f" -o "$OBJ/$name.o"
        done
        "${CC:-cc}" -shared -nostdlib -Wl,-z,noexecstack -Wl,-z,defs \
            -o "$BIN/libFullIntOperations_linux_arm64.so" "$OBJ"/*.o
        echo "=== Build finished: $BIN/libFullIntOperations_linux_arm64.so ===" ;;
    linux-arm)
        # ARMv7 (armhf): the ARMv7 kernels (Sources/arm32, GNU syntax) without CppGeneric.s (the C++
        # library's jumps to its portable kernels), linked into libFullIntOperations_linux_arm.so;
        # the .NET ARMv7 core (FullIntCoreNativeArm32.cs) imports these entries and keeps the
        # managed paths for the rest. CC may be a cross compiler (arm-linux-gnueabihf-gcc, or
        # clang --target=armv7-linux-gnueabihf with -fuse-ld=lld).
        DBG=()
        [[ "$CONFIG" == "Debug" ]] && DBG=(-g)
        mkdir -p "$OBJ" "$BIN"
        echo "=== Compiling asm files (ARMv7 ELF) $CONFIG ==="
        for f in "$ASM_ROOT/Sources/arm32/"*.s; do
            name="$(basename "$f" .s)"
            [[ "$name" == "CppGeneric" ]] && continue
            ${CC:-cc} -c "${DBG[@]}" -Wa,-I"$ASM_ROOT/Sources/arm32" "$f" -o "$OBJ/$name.o"
        done
        ${CC:-cc} -shared -nostdlib -Wl,-z,noexecstack -Wl,-z,defs             -o "$BIN/libFullIntOperations_linux_arm.so" "$OBJ"/*.o
        echo "=== Build finished: $BIN/libFullIntOperations_linux_arm.so ===" ;;
    osx-arm64)
        # TODO(osx-arm64): the same sources with -Wa,--defsym,FULLINT_MACHO=1 (abi.inc adds the
        #   underscore), cc -dynamiclib -o libFullIntOperations_osx_arm64.dylib; untested (no Mac)
        not_implemented "see TODO(osx-arm64) in FullInt.Asm/build/build-native.sh" ;;
    *)
        echo "build-native: error: unknown target $OS-$ARCH" >&2
        exit 1 ;;
esac
