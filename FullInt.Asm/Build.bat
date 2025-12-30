@echo off
setlocal

:: Читаем параметры
set OS=%1
set ARCH=%2
set OBJ=Bin\Obj

:: Если пусто → считаем что all
if "%OS%"=="" set OS=all
if "%ARCH%"=="" set ARCH=all

:: Windows
if /I "%OS%"=="win" (
    if /I "%ARCH%"=="x64" (
        call BuidNasm.bat win64
        link /dll /noentry /out:Bin\FullIntOperations_win_x64.dll %OBJ%\*.obj /def:exports.def
    )
    if /I "%ARCH%"=="arm64" (
        call BuidNasm.bat win64
        link /dll /noentry /out:Bin\FullIntOperations_win_arm64.dll %OBJ%\*.obj /def:exports.def
    )
)

:: Linux
if /I "%OS%"=="linux" (
    if /I "%ARCH%"=="x64" (
        call BuidNasm.bat elf64
        gcc -shared -o Bin/FullIntOperations_linux_x64.so %OBJ%\*.o
    )
    if /I "%ARCH%"=="arm64" (
        call BuidNasm.bat elf64
        gcc -shared -o Bin/FullIntOperations_linux_arm64.so %OBJ%\*.o
    )
)

:: macOS
if /I "%OS%"=="mac" (
    if /I "%ARCH%"=="x64" (
        call BuidNasm.bat macho64
        clang -shared -o Bin/FullIntOperations_mac_x64.dylib %OBJ%\*.o
    )
    if /I "%ARCH%"=="arm64" (
        call BuidNasm.bat macho64
        clang -shared -o Bin/FullIntOperations_mac_arm64.dylib %OBJ%\*.o
    )
)

echo === Build finished ===
endlocal
