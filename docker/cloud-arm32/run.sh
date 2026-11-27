#!/usr/bin/env bash
# ARMv7 (armhf) natively on an AArch64 machine whose cores run AArch32 (Neoverse-N1: `lscpu` lists
# "32-bit, 64-bit"), in a container pinned to one core. The image is made once, in a container under
# the same limits (docker build would take every core):
#   docker run --name fa32 --platform linux/arm/v7 --cpuset-cpus=1 --memory=6g -e DEBIAN_FRONTEND=noninteractive \
#       ubuntu:24.04 sh -c "apt-get update && apt-get install -y --no-install-recommends build-essential clang cmake \
#       ninja-build libgmp-dev libicu74 libssl3t64 zlib1g ca-certificates git && useradd -u 1001 -m bench"
#   docker commit fa32 fullint-arm32-bench && docker rm fa32
# .NET: the linux-arm SDK unpacked into build-cloud-dotnet/dotnet10-arm (the tests target net8.0 and
# run on the SDK's .NET 10 runtime: DOTNET_ROLL_FORWARD=Major).
# Then, on the machine:
#   docker run --rm --platform linux/arm/v7 --cpuset-cpus=1 --memory=6g --user 1001:1001 -e HOME=/tmp \
#       -v ~/projects/fullint:/src -w /src fullint-arm32-bench bash docker/cloud-arm32/run.sh
#   NET=0 skips .NET; BENCH=1 adds the benchmarks (C++ against GMP, .NET against BigInteger) into results/
set -euo pipefail
cmake -S FullInt.Cpp -B build-arm32 -G Ninja -DCMAKE_BUILD_TYPE=Release -DFULLINT_WITH_GMP=ON > /tmp/cmake.log
cmake --build build-arm32 --target fullint_arm64_kernels fullint_tests fullint_bench_api > /tmp/build.log 2>&1 \
    || { tail -30 /tmp/build.log; exit 1; }
./build-arm32/fullint_arm64_kernels | tail -1
./build-arm32/fullint_tests | tail -1
if [[ "${NET:-1}" == 1 ]]; then
    export DOTNET_ROOT=/src/build-cloud-dotnet/dotnet10-arm PATH=/src/build-cloud-dotnet/dotnet10-arm:$PATH
    export DOTNET_CLI_HOME=/src/build-cloud-dotnet/home-arm NUGET_PACKAGES=/src/build-cloud-dotnet/nuget-arm
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
    mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
    DOTNET_ROLL_FORWARD=Major dotnet test FullInt.Tests -c Release -f net8.0 -o /tmp/tests 2>&1 | grep -E "Passed!|Failed!|  Failed "
fi
if [[ "${BENCH:-0}" == 1 ]]; then
    OUT=results/$(date -u +%Y%m%d-%H%M%S)-arm32; mkdir -p "$OUT"
    ./build-arm32/fullint_bench_api --cpu 1 --ops add,sub,mul,div,todec,parse --max 1024 > "$OUT/cpp.txt"
    if [[ "${NET:-1}" == 1 ]]; then
        dotnet build FullInt.Benchmarks -c Release -p:BenchTfm=net10.0 > /tmp/nb.log 2>&1 || { tail -20 /tmp/nb.log; exit 1; }
        dotnet run --no-build -c Release -p:BenchTfm=net10.0 --project FullInt.Benchmarks -- cross \
            --ops add,sub,mul,div,todec,parse --max 1024 > "$OUT/net.txt"
    fi
    echo "== results: $OUT"
fi
