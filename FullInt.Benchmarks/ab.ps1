<#
.SYNOPSIS
  Speed regression check for the native kernels: current working tree vs a git ref.

.DESCRIPTION
  1. Builds the asm sources of <Base> (git ref, default HEAD) into a temp folder,
     using the current build script and the bundled NASM.
  2. Builds the current working tree's asm (Release).
  3. Runs "FullInt.Benchmarks ab base.dll cur.dll": both DLLs in one process,
     alternating rounds, median ns/call, '!' on slowdowns above the 5% noise floor.

.EXAMPLE
  .\FullInt.Benchmarks\ab.ps1                    # uncommitted asm edits vs HEAD
  .\FullInt.Benchmarks\ab.ps1 -Base HEAD~1       # last commit vs the one before
  .\FullInt.Benchmarks\ab.ps1 -Filter Div -Rounds 21
#>
param(
    [string]$Base = "HEAD",
    [string]$Filter = "",
    [int]$Rounds = 15
)
$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$sha = (git -C $repo rev-parse --short $Base).Trim()
if ($LASTEXITCODE -ne 0) { throw "unknown git ref '$Base'" }

$work = Join-Path ([IO.Path]::GetTempPath()) "fullint-ab-$sha"
$baseAsm = Join-Path $work "FullInt.Asm"
$baseDll = Join-Path $work "bin\FullIntOperations_win_x64.dll"

# --- 1. base: sources of the ref + today's build script ------------------------------------------
if (-not (Test-Path $baseDll)) {
    Write-Host "== building base $Base ($sha) in $work"
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory -Force (Join-Path $baseAsm "build") | Out-Null

    $tar = Join-Path $work "src.tar"
    git -C $repo archive --format=tar -o $tar $Base FullInt.Asm/Sources FullInt.Asm/exports.def
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
    tar -xf $tar -C $work
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }

    Copy-Item (Join-Path $repo "FullInt.Asm\build\build-native.cmd") (Join-Path $baseAsm "build")
    $env:FULLINT_NASM = Join-Path $repo "Tools\nasm\nasm.exe"
    & cmd /c "`"$baseAsm\build\build-native.cmd`" Release x64 `"$work\obj`" `"$work\bin`""
    Remove-Item Env:FULLINT_NASM
    if ($LASTEXITCODE -ne 0) { throw "base native build failed" }
} else {
    Write-Host "== base $Base ($sha): cached $baseDll"
}

# --- 2. current working tree ------------------------------------------------------------------------
Write-Host "== building current (Release)"
dotnet build (Join-Path $repo "FullInt.Benchmarks\FullInt.Benchmarks.csproj") -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$curDll = Join-Path $repo "FullInt.Asm\bin\win-x64\Release\FullIntOperations_win_x64.dll"

# --- 3. compare -------------------------------------------------------------------------------------
# Windows PowerShell drops empty-string arguments to native commands, so "all ops" is '*'
if ($Filter -eq "") { $Filter = "*" }
$runArgs = @("ab", $baseDll, $curDll, $Filter, $Rounds)
dotnet run --project (Join-Path $repo "FullInt.Benchmarks\FullInt.Benchmarks.csproj") -c Release --no-build -- @runArgs
