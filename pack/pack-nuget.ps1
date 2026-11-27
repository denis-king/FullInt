# Builds the FullInt NuGet package with the native libraries of every supported platform:
#   win-x64, win-x86   here (FullInt.AsmForNet: NASM + MSVC link.exe)
#   linux-x64          in Docker (docker/linux-x64 image: NASM elf64)
#   linux-arm64        in Docker under QEMU (docker/linux-arm64 image: cc for AArch64)
#   linux-arm          in Docker under QEMU (docker/linux-arm image: cc for ARMv7, armhf)
# The libraries go to out/native/<rid>/, the package (.nupkg and .snupkg) to out/nuget/.
# Nothing is published.
#   powershell -File pack/pack-nuget.ps1
#   powershell -File pack/pack-nuget.ps1 -SkipLinux   # reuse out/native/linux-* from an earlier run
param([switch]$SkipLinux)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$native = Join-Path $root 'out/native'

function Invoke-Checked([scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "failed ($LASTEXITCODE): $cmd" }
}

# ---- Windows: a win-x64 build also builds the x86 library ----
Write-Host '== win-x64, win-x86'
Invoke-Checked { dotnet build "$root/FullInt.AsmForNet" -c Release -p:FullIntNativeRid=win-x64 }
foreach ($rid in 'win-x64', 'win-x86') {
    $file = "FullIntOperations_$($rid.Replace('-', '_')).dll"
    New-Item -ItemType Directory -Force "$native/$rid" | Out-Null
    Copy-Item "$root/FullInt.Asm/bin/$rid/Release/$file" "$native/$rid/$file" -Force
}

# ---- Linux: the sources copied inside the container (the repository is mounted read-only) ----
if (-not $SkipLinux) {
    $script = 'set -e; mkdir -p /work/src; cp -a /src/FullInt.Asm /work/src/; ' +
              'bash /work/src/FullInt.Asm/build/build-native.sh Release linux $ARCH /work/obj /out/linux-$ARCH'
    Write-Host '== linux-x64'
    Invoke-Checked { docker build -t fullint-linux-x64 "$root/docker/linux-x64" }
    Invoke-Checked { docker run --rm -e ARCH=x64 -v "${root}:/src:ro" -v "${native}:/out" fullint-linux-x64 bash -c $script }
    Write-Host '== linux-arm64 (QEMU)'
    Invoke-Checked { docker build --platform linux/arm64 -t fullint-linux-arm64 "$root/docker/linux-arm64" }
    Invoke-Checked { docker run --rm --platform linux/arm64 -e ARCH=arm64 -v "${root}:/src:ro" -v "${native}:/out" fullint-linux-arm64 bash -c $script }
    Write-Host '== linux-arm (QEMU)'
    Invoke-Checked { docker build --platform linux/arm/v7 -t fullint-linux-arm "$root/docker/linux-arm" }
    Invoke-Checked { docker run --rm --platform linux/arm/v7 -e ARCH=arm -v "${root}:/src:ro" -v "${native}:/out" fullint-linux-arm bash -c $script }
}

# ---- the package ----
Write-Host '== pack'
Invoke-Checked { dotnet pack "$root/FullInt.Core" -c Release -o "$root/out/nuget" "-p:FullIntNativePackDir=$native/" }
Get-ChildItem "$native" -Recurse -File | ForEach-Object { '{0,-60} {1,10:N0}' -f $_.FullName.Substring($root.Length + 1), $_.Length }
Get-ChildItem "$root/out/nuget" -File | ForEach-Object { '{0,-60} {1,10:N0}' -f $_.FullName.Substring($root.Length + 1), $_.Length }
