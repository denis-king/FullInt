@echo off
setlocal

:: Читаем параметры
set OBJ=Bin\Obj

:: macOS
if /I "%OS%"=="mac" (
    call BuidNasm.bat macho64
    clang -shared -o Bin/FullIntOperations_mac_x64.dylib %OBJ%\*.o
)

echo === Build finished ===
endlocal
