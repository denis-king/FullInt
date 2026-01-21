@echo off
rem Сборка ASM файлов в объектные
nasm -f win64 ..\..\Sources\x64\FromDecString.asm -o FromDecString.obj
nasm -f win64 runFromDecString.asm -o runFromDecString.obj

rem Линковка в exe, точка входа main
link runFromDecString.obj FromDecString.obj /SUBSYSTEM:CONSOLE /ENTRY:main /OUT:RunDec.exe /DEBUG

rem Запуск WinDbg с паузой на загрузке модуля
"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\windbg.exe" -o "RunDec.exe" -c "sxe ld:RunDec.exe; bp @$exentry; .reload /f; g"
