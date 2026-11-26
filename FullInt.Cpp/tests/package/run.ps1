# The C++ library as a user takes it, on Windows (MSVC, the VS developer shell of the architecture):
#   1. builds FullInt.Cpp (Debug and Release) and installs both into out/package/<arch>/install;
#   2. builds tests/package against the install (find_package), Debug and Release, and runs them;
#   3. the same through add_subdirectory;
#   4. with -Fetch: through FetchContent of this repository's committed master (a local clone).
#   powershell -File FullInt.Cpp/tests/package/run.ps1 [-Arch x64|x86] [-Fetch]
param([ValidateSet('x64', 'x86')][string]$Arch = 'x64', [switch]$Fetch)
$ErrorActionPreference = 'Stop'
$cpp = (Resolve-Path "$PSScriptRoot/../..").Path
$repo = Split-Path -Parent $cpp
$out = "$cpp/out/package/$Arch"

$vs = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\Launch-VsDevShell.ps1'
if ($Arch -eq 'x64') { & $vs -Arch amd64 -SkipAutomaticLocation | Out-Null }
else { & $vs -Arch x86 -HostArch amd64 -SkipAutomaticLocation | Out-Null }

function Invoke-Checked([scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "failed ($LASTEXITCODE): $cmd" }
}

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
$gen = @('-G', 'Ninja Multi-Config')

Write-Host "== $Arch`: the library, Debug and Release, installed"
Invoke-Checked { cmake -S $cpp -B "$out/lib" @gen }
foreach ($c in 'Debug', 'Release') {
    Invoke-Checked { cmake --build "$out/lib" --config $c }
    Invoke-Checked { cmake --install "$out/lib" --config $c --prefix "$out/install" }
}
Get-ChildItem "$out/install" -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($out.Length + 1) }

Write-Host "== $Arch`: find_package"
Invoke-Checked { cmake -S "$cpp/tests/package" -B "$out/find" @gen -DFULLINT_CONSUME=find_package "-DCMAKE_PREFIX_PATH=$out/install" }
foreach ($c in 'Debug', 'Release') {
    Invoke-Checked { cmake --build "$out/find" --config $c }
    Write-Host "  $c`: " -NoNewline; Invoke-Checked { & "$out/find/$c/package_test.exe" }
}

Write-Host "== $Arch`: add_subdirectory"
Invoke-Checked { cmake -S "$cpp/tests/package" -B "$out/subdir" @gen -DFULLINT_CONSUME=subdirectory }
Invoke-Checked { cmake --build "$out/subdir" --config Release }
Write-Host '  Release: ' -NoNewline; Invoke-Checked { & "$out/subdir/Release/package_test.exe" }

if ($Fetch) {
    Write-Host "== $Arch`: FetchContent (the committed master of $repo)"
    Invoke-Checked { cmake -S "$cpp/tests/package" -B "$out/fetch" @gen -DFULLINT_CONSUME=fetchcontent "-DFULLINT_GIT_REPOSITORY=$repo" -DFULLINT_GIT_TAG=master }
    Invoke-Checked { cmake --build "$out/fetch" --config Release }
    Write-Host '  Release: ' -NoNewline; Invoke-Checked { & "$out/fetch/Release/package_test.exe" }
}
