#!/usr/bin/env bash
# Inside the container: copies the sources from /src (the repository, read-only) to /work, builds
# the C++ library and its tests against GMP (NASM elf64: the System V entries of abi.inc) and runs
# them with the CPU's kernels and with FULLINT_CPU=baseline; then the .NET tests.
#   CPP=0 / CSHARP=0 skip a part.
set -euo pipefail

SRC=/src
WORK=/work/fullint
rm -rf "$WORK"
mkdir -p "$WORK"
tar -C "$SRC" \
    --exclude=.git --exclude=out --exclude=bin --exclude=Bin --exclude=obj \
    --exclude=vcpkg_installed --exclude=BenchmarkDotNet.Artifacts --exclude=TestResults \
    -cf - . | tar -C "$WORK" -xf -

echo "== $(uname -srm), $(gcc --version | head -1), $(nasm -v), dotnet $(dotnet --version)"
grep -m1 "model name" /proc/cpuinfo || true

if [[ "${CPP:-1}" == 1 ]]; then
    echo "== C++: configure / build"
    cmake -S "$WORK/FullInt.Cpp" -B "$WORK/build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DFULLINT_WITH_GMP=ON
    cmake --build "$WORK/build" --target fullint_tests
    echo "== C++ tests (the CPU's kernels)"
    "$WORK/build/fullint_tests"
    echo "== C++ tests (FULLINT_CPU=baseline)"
    FULLINT_CPU=baseline "$WORK/build/fullint_tests"
fi

if [[ "${CSHARP:-1}" == 1 ]]; then
    echo "== .NET tests (net8.0)"
    dotnet test "$WORK/FullInt.Tests" -c Release -f net8.0
fi
