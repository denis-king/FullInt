# Linux x64 tests in Docker: builds the image (cached after the first time) and runs
# docker/linux-x64/run-tests.sh on this repository, mounted read-only (nothing is written into it).
#   powershell -File docker/test-linux-x64.ps1            # C++ and .NET
#   powershell -File docker/test-linux-x64.ps1 -NoCSharp  # C++ only
#   powershell -File docker/test-linux-x64.ps1 -NoCSharp -Bench [-BenchArgs "--max 1024"]
#       + the C++ benchmark against GMP; its CSV in out/linux-x64/ (out/ is not committed)
param([switch]$NoCSharp, [switch]$NoCpp, [switch]$Bench, [string]$BenchArgs = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
docker build -t fullint-linux-x64 "$PSScriptRoot/linux-x64"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$envs = @()
if ($NoCSharp) { $envs += '-e', 'CSHARP=0' }
if ($NoCpp) { $envs += '-e', 'CPP=0' }
if ($Bench) { $envs += '-e', 'BENCH=1', '-e', "BENCH_ARGS=$BenchArgs" }
$out = Join-Path $root 'out/linux-x64'
New-Item -ItemType Directory -Force $out | Out-Null
docker run --rm @envs -v "${root}:/src:ro" -v "${out}:/out" fullint-linux-x64 bash /src/docker/linux-x64/run-tests.sh
exit $LASTEXITCODE
