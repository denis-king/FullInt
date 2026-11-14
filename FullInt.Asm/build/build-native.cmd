@echo off
rem ---------------------------------------------------------------------------
rem  Builds the FullInt native kernels on Windows.
rem
rem  Usage: build-native.cmd <Debug|Release> <x64|x86|arm64> <objDir> <binDir>
rem
rem  Toolchain:
rem    NASM      - bundled in Tools\nasm (override with FULLINT_NASM)
rem    link.exe  - found with vswhere (override with FULLINT_LINK, or put link on PATH)
rem
rem  Normally invoked by FullInt.AsmForNet.csproj; can be run by hand.
rem ---------------------------------------------------------------------------
setlocal EnableExtensions

set "CONFIG=%~1"
set "ARCH=%~2"
set "OBJ=%~3"
set "BIN=%~4"

if "%CONFIG%"=="" goto :usage
if "%ARCH%"=="" goto :usage
if "%OBJ%"=="" goto :usage
if "%BIN%"=="" goto :usage

set "ASM_ROOT=%~dp0.."
for %%i in ("%ASM_ROOT%") do set "ASM_ROOT=%%~fi"

if /I "%ARCH%"=="x64" goto :x64
if /I "%ARCH%"=="x86" goto :x86
if /I "%ARCH%"=="arm64" goto :arm64
echo build-native: error: unknown architecture "%ARCH%" 1>&2
exit /b 1

rem ===========================================================================
:arm64
rem TODO(arm64): AArch64 kernels in Sources\arm64 (GNU syntax .S), assembled with
rem   clang --target=aarch64-pc-windows-msvc, linked with link.exe /machine:arm64.
echo build-native: error: win-arm64 native backend is not implemented yet 1>&2
exit /b 1

rem ===========================================================================
:x86
rem 32-bit kernels (Sources\x86, cdecl). The same NASM with -f win32; --prefix _ gives the
rem C names of 32-bit COFF (_name), and the .def exports them undecorated.
set "SRC=%ASM_ROOT%\Sources\x86"
set "DEF=%ASM_ROOT%\exports_x86.def"
set "LIBNAME=FullIntOperations_win_x86"
set "NASMFORMAT=-f win32 --prefix _"
set "MACHINE=x86"
set "LINKHOSTDIR=Hostx64\x86"
set "EXTRALINKFLAGS=/safeseh:no"
goto :build

rem ===========================================================================
:x64
set "SRC=%ASM_ROOT%\Sources\x64"
set "DEF=%ASM_ROOT%\exports.def"
set "LIBNAME=FullIntOperations_win_x64"
set "NASMFORMAT=-f win64"
set "MACHINE=x64"
set "LINKHOSTDIR=Hostx64\x64"
set "EXTRALINKFLAGS="

:build

rem --- NASM -----------------------------------------------------------------
if defined FULLINT_NASM (set "NASM=%FULLINT_NASM%") else (set "NASM=%ASM_ROOT%\..\Tools\nasm\nasm.exe")
if not exist "%NASM%" (
    echo build-native: error: NASM not found at "%NASM%" 1>&2
    exit /b 1
)

rem --- link.exe -------------------------------------------------------------
call :find_link
if errorlevel 1 exit /b 1

echo build-native: win-%MACHINE% %CONFIG%
echo   nasm: %NASM%
echo   link: %LINK_EXE%
echo   out : %BIN%\%LIBNAME%.dll

rem --- clean object dir so deleted/renamed sources never get linked ---------
if exist "%OBJ%" rmdir /s /q "%OBJ%"
mkdir "%OBJ%" || exit /b 1
if not exist "%BIN%" mkdir "%BIN%" || exit /b 1

set "NASMFLAGS=%NASMFORMAT% -Xvc"
if /I "%CONFIG%"=="Debug" set "NASMFLAGS=%NASMFLAGS% -g"

rem --- assemble -------------------------------------------------------------
set "RSP=%OBJ%\objects.rsp"
type nul > "%RSP%"
for %%f in ("%SRC%\*.asm") do (
    echo   asm  %%~nxf
    "%NASM%" %NASMFLAGS% -I "%SRC%\\" "%%~ff" -o "%OBJ%\%%~nf.obj" || exit /b 1
    echo "%OBJ%\%%~nf.obj">> "%RSP%"
)

rem --- link -----------------------------------------------------------------
set "LINKFLAGS=/nologo /dll /noentry /machine:%MACHINE% /incremental:no %EXTRALINKFLAGS%"
set "DEBUGFLAGS=/release"
if /I "%CONFIG%"=="Debug" set DEBUGFLAGS=/debug /pdb:"%BIN%\%LIBNAME%.pdb"

echo   link %LIBNAME%.dll
"%LINK_EXE%" %LINKFLAGS% %DEBUGFLAGS% /def:"%DEF%" /implib:"%OBJ%\%LIBNAME%.lib" /out:"%BIN%\%LIBNAME%.dll" @"%RSP%" || exit /b 1

if /I not "%CONFIG%"=="Debug" if exist "%BIN%\%LIBNAME%.pdb" del /q "%BIN%\%LIBNAME%.pdb"

echo build-native: done
exit /b 0

rem ===========================================================================
:find_link
rem 1) explicit override
if defined FULLINT_LINK (
    set "LINK_EXE=%FULLINT_LINK%"
    goto :check_link
)

rem 2) vswhere (ships with every VS 2017+ / Build Tools install)
set "LINK_EXE="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :link_from_path
for /f "usebackq delims=" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find "VC\Tools\MSVC\**\bin\%LINKHOSTDIR%\link.exe"`) do set "LINK_EXE=%%i"
if defined LINK_EXE goto :check_link

:link_from_path
rem 3) already on PATH (Developer Command Prompt)
for /f "delims=" %%i in ('where link.exe 2^>nul') do (
    set "LINK_EXE=%%i"
    goto :check_link
)
echo build-native: error: MSVC link.exe not found. 1>&2
echo   Install Visual Studio or Build Tools with the "Desktop development with C++" workload, 1>&2
echo   or set FULLINT_LINK to the full path of link.exe. 1>&2
exit /b 1

:check_link
if not exist "%LINK_EXE%" (
    echo build-native: error: link.exe not found at "%LINK_EXE%" 1>&2
    exit /b 1
)
exit /b 0

rem ===========================================================================
:usage
echo usage: build-native.cmd ^<Debug^|Release^> ^<x64^|arm64^> ^<objDir^> ^<binDir^> 1>&2
exit /b 1
