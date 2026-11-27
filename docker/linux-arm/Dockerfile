# FullInt on Linux ARMv7 (armhf): the toolchain for the ARMv7 kernels' .so (pack/pack-nuget.ps1) and
# the C++ library. On an x64 machine it runs under QEMU emulation (Docker Desktop); on an AArch64 host
# with 32-bit support (Neoverse-N1, docker/cloud-arm32) natively.
#   docker build --platform linux/arm/v7 -t fullint-linux-arm docker/linux-arm
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8 LC_ALL=C.UTF-8

RUN apt-get update \
 && apt-get install -y --no-install-recommends build-essential cmake ninja-build libgmp-dev ca-certificates \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /work
