#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Builds the FullInt native kernels on Linux and macOS.
#
#  Usage: build-native.sh <Debug|Release> <linux|osx> <x64|arm64> <objDir> <binDir>
#
#  Status: skeleton. No Unix target is enabled yet; FullInt.AsmForNet.csproj
#  only calls this script for RIDs listed in FullIntNativeSupportedRids.
#  Each target below documents what is missing before it can be enabled.
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
        # TODO(linux-x64):
        #   - guard the *WIN wrappers with %ifidn __OUTPUT_FORMAT__, win64
        #   - make internal calls PIC-safe (call sym wrt ..plt) or drop the wrappers
        #   - preserve rbx/rbp/r12-r15 (System V callee-saved)
        #   - nasm -f elf64 [-g -F dwarf]  →  cc -shared -o libFullIntOperations_linux_x64.so
        #   - C#: FullIntCoreLinuxX64 bound to the SysV entry points
        not_implemented "see TODO(linux-x64) in FullInt.Asm/build/build-native.sh" ;;
    osx-x64)
        # TODO(osx-x64): same as linux-x64, plus `nasm -f macho64 --prefix _`
        #   and `cc -dynamiclib -o libFullIntOperations_osx_x64.dylib`
        not_implemented "see TODO(osx-x64) in FullInt.Asm/build/build-native.sh" ;;
    linux-arm64|osx-arm64)
        # TODO(arm64): AArch64 kernels in Sources/arm64 (GNU .S), built with cc -shared / -dynamiclib
        not_implemented "AArch64 kernels do not exist yet (Sources/arm64)" ;;
    *)
        echo "build-native: error: unknown target $OS-$ARCH" >&2
        exit 1 ;;
esac
