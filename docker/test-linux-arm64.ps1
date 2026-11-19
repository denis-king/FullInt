# Linux ARM64 tests in Docker (on an x64 machine: QEMU emulation, correctness only): builds the image
# (cached after the first time) and runs docker/linux-arm64/run-tests.sh on this repository,
# mounted read-only (nothing is written into it).
#   powershell -File docker/test-linux-arm64.ps1            # C++ and .NET
#   powershell -File docker/test-linux-arm64.ps1 -NoCSharp  # C++ only
#   powershell -File docker/test-linux-arm64.ps1 -Clang     # C++ with clang instead of g++
param([switch]$NoCSharp, [switch]$NoCpp, [switch]$Clang)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
docker build --platform linux/arm64 -t fullint-linux-arm64 "$PSScriptRoot/linux-arm64"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$envs = @()
if ($NoCSharp) { $envs += '-e', 'CSHARP=0' }
if ($NoCpp) { $envs += '-e', 'CPP=0' }
if ($Clang) { $envs += '-e', 'CXX=clang++' }
docker run --rm --platform linux/arm64 @envs -v "${root}:/src:ro" fullint-linux-arm64 bash /src/docker/linux-arm64/run-tests.sh
exit $LASTEXITCODE
