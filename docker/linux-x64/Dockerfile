# FullInt on Linux x64: the toolchain for the C++ library (its tests against GMP) and the .NET tests.
#   docker build -t fullint-linux-x64 docker/linux-x64
# Runs through docker/test-linux-x64.ps1 (the repository mounted read-only at /src).
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 LC_ALL=C.UTF-8 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        build-essential cmake ninja-build nasm libgmp-dev \
        dotnet-sdk-8.0 \
        ca-certificates git \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /work
