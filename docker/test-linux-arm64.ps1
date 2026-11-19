# Linux ARM64 tests in Docker (on an x64 machine: QEMU emulation, correctness only): builds the image
# (cached after the first time) and runs docker/linux-arm64/run-tests.sh on this repository,
# mounted read-only (nothing is written into it); the build lives in the volume fullint-arm64-cache
# (incremental between runs; docker volume rm fullint-arm64-cache starts afresh).
#   powershell -File docker/test-linux-arm64.ps1            # C++ and .NET
#   powershell -File docker/test-linux-arm64.ps1 -NoCSharp  # C++ only
#   powershell -File docker/test-linux-arm64.ps1 -Clang     # C++ with clang instead of g++
#   powershell -File docker/test-linux-arm64.ps1 -Kernels   # only the AArch64 kernels against the C++ ones
param([switch]$NoCSharp, [switch]$NoCpp, [switch]$Clang, [switch]$Kernels)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
docker build --platform linux/arm64 -t fullint-linux-arm64 "$PSScriptRoot/linux-arm64"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$envs = @()
if ($NoCSharp) { $envs += '-e', 'CSHARP=0' }
if ($NoCpp) { $envs += '-e', 'CPP=0' }
if ($Clang) { $envs += '-e', 'CXX=clang++' }
if ($Kernels) { $envs += '-e', 'KERNELS=1' }
docker run --rm --platform linux/arm64 @envs -v "${root}:/src:ro" -v fullint-arm64-cache:/cache fullint-linux-arm64 bash /src/docker/linux-arm64/run-tests.sh
exit $LASTEXITCODE
