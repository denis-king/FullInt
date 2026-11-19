#!/usr/bin/env bash
# Inside the container (Linux ARM64; on an x64 machine under QEMU): copies the sources from /src
# (the repository, read-only) to /cache/fullint (a Docker volume: the build stays between runs,
# ninja rebuilds what changed), builds the C++ library with the AArch64 kernels and its tests
# against GMP and runs them; then the .NET tests (the managed core: no native library for
# linux-arm64 yet).
#   CPP=0 / CSHARP=0 skip a part; CXX=clang++ builds with clang (default: g++);
#   KERNELS=1 only the AArch64 kernels against the portable C++ ones (fullint_arm64_kernels).
set -euo pipefail

SRC=/src
WORK=/cache/fullint
mkdir -p "$WORK"
# (cp, not tar: tar's extraction fails under QEMU's user emulation - "Cannot open: Invalid argument";
# -a keeps the times: an unchanged source is not rebuilt. The copy is refreshed, not cleared.)
shopt -s dotglob
for e in "$SRC"/*; do
    case "$(basename "$e")" in .git|.vs|out|vcpkg_installed|TestResults|BenchmarkDotNet.Artifacts) continue ;; esac
    cp -a "$e" "$WORK/"
done
find "$WORK" -mindepth 2 -type d \( -name bin -o -name Bin -o -name obj -o -name out -o -name TestResults \
    -o -name BenchmarkDotNet.Artifacts -o -name vcpkg_installed \) -prune -exec rm -rf {} +
BUILD="$WORK/build-$(basename "${CXX:-g++}")"

echo "== $(uname -srm), $(${CXX:-g++} --version | head -1), dotnet $(dotnet --version)"

if [[ "${CPP:-1}" == 1 || "${KERNELS:-0}" == 1 ]]; then
    cmake -S "$WORK/FullInt.Cpp" -B "$BUILD" -G Ninja -DCMAKE_BUILD_TYPE=Release -DFULLINT_WITH_GMP=ON \
        -DCMAKE_CXX_COMPILER="${CXX:-g++}" > /dev/null
fi

if [[ "${KERNELS:-0}" == 1 ]]; then
    echo "== the AArch64 kernels against the portable C++ ones"
    cmake --build "$BUILD" --target fullint_arm64_kernels
    timeout 1800 "$BUILD/fullint_arm64_kernels"
    exit 0
fi

if [[ "${CPP:-1}" == 1 ]]; then
    echo "== C++: build"
    cmake --build "$BUILD" --target fullint_arm64_kernels fullint_tests fullint_bench_api
    echo "== the AArch64 kernels against the portable C++ ones"
    timeout 1800 "$BUILD/fullint_arm64_kernels"
    echo "== C++ tests against GMP (the AArch64 kernels)"
    "$BUILD/fullint_tests"
    echo "== C++ tests against GMP, FULLINT_CPU=baseline (the baseline division entries)"
    FULLINT_CPU=baseline "$BUILD/fullint_tests"
fi

if [[ "${CSHARP:-1}" == 1 ]]; then
    echo "== .NET tests (net8.0, the managed core)"
    dotnet test "$WORK/FullInt.Tests" -c Release -f net8.0
fi
