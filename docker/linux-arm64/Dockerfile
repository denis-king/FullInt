# FullInt on Linux ARM64 (AArch64): the toolchain for the C++ library (its tests against GMP) and the
# .NET tests. On an x64 machine it runs under QEMU emulation (Docker Desktop): correctness only, the
# speed means nothing there.
#   docker build --platform linux/arm64 -t fullint-linux-arm64 docker/linux-arm64
# Runs through docker/test-linux-arm64.ps1 (the repository mounted read-only at /src).
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 LC_ALL=C.UTF-8 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        build-essential clang cmake ninja-build libgmp-dev \
        dotnet-sdk-8.0 \
        ca-certificates git \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /work
