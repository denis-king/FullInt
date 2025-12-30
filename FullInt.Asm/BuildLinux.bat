@echo off
setlocal

:: Читаем параметры
set OBJ=Bin\Obj

:: Linux
if /I "%OS%"=="linux" (
    call BuidNasm.bat elf64
    gcc -shared -o Bin/FullIntOperations_linux_x64.so %OBJ%\*.o
)

echo === Build finished ===
endlocal
