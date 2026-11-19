#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Builds the FullInt native kernels on Linux and macOS.
#
#  Usage: build-native.sh <Debug|Release> <linux|osx> <x64|arm64> <objDir> <binDir>
#
#  linux-x64: the x64 sources with NASM -f elf64 (abi.inc gives every entry the System V
#  arguments: the same names and contracts as Windows), linked into
#  libFullIntOperations_linux_x64.so. The others: not yet (each documents what is missing).
#  FullInt.AsmForNet.csproj calls this script for the RIDs in FullIntNativeSupportedRids.
#  FULLINT_NASM / CC override the tools.
# ---------------------------------------------------------------------------
set -euo pipefail

if [[ $# -ne 5 ]]; then
    echo "usage: build-native.sh <Debug|Release> <linux|osx> <x64|arm64> <objDir> <binDir>" >&2
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
    linux-arm64|osx-arm64)
        # TODO(arm64): AArch64 kernels in Sources/arm64 (GNU .S), built with cc -shared / -dynamiclib
        not_implemented "AArch64 kernels do not exist yet (Sources/arm64)" ;;
    *)
        echo "build-native: error: unknown target $OS-$ARCH" >&2
        exit 1 ;;
esac
