#!/usr/bin/env bash
# Inside the container (Linux ARM64; on an x64 machine under QEMU): copies the sources from /src
# (the repository, read-only) to /work, builds the C++ library and its tests against GMP (the
# kernels in portable C++ so far: src/kernels_generic.cpp) and runs them; then the .NET tests (the
# managed core: no native library for linux-arm64 yet).
#   CPP=0 / CSHARP=0 skip a part; CXX=clang++ builds with clang (default: g++).
set -euo pipefail

SRC=/src
WORK=/work/fullint
rm -rf "$WORK"
mkdir -p "$WORK"
# (cp, not tar: tar's extraction fails under QEMU's user emulation - "Cannot open: Invalid argument")
shopt -s dotglob
for e in "$SRC"/*; do
    case "$(basename "$e")" in .git|.vs|out|vcpkg_installed|TestResults|BenchmarkDotNet.Artifacts) continue ;; esac
    cp -r "$e" "$WORK/"
done
find "$WORK" -type d \( -name bin -o -name Bin -o -name obj -o -name out -o -name TestResults \
    -o -name BenchmarkDotNet.Artifacts -o -name vcpkg_installed \) -prune -exec rm -rf {} +

echo "== $(uname -srm), $(${CXX:-g++} --version | head -1), dotnet $(dotnet --version)"

if [[ "${CPP:-1}" == 1 ]]; then
    echo "== C++: configure / build"
    cmake -S "$WORK/FullInt.Cpp" -B "$WORK/build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DFULLINT_WITH_GMP=ON \
        -DCMAKE_CXX_COMPILER="${CXX:-g++}"
    cmake --build "$WORK/build" --target fullint_tests fullint_bench_api
    echo "== C++ tests"
    "$WORK/build/fullint_tests"
fi

if [[ "${CSHARP:-1}" == 1 ]]; then
    echo "== .NET tests (net8.0, the managed core)"
    dotnet test "$WORK/FullInt.Tests" -c Release -f net8.0
fi
