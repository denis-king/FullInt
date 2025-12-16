@echo off
set SRC=Sources\x64
set OBJ=Bin\Obj
set DST=Bin

echo === Compiling asm files ===
for %%f in (%SRC%\*.asm) do (
	echo NASM %%f
	nasm -f win64 "%%f" -o "%OBJ%\%%~nf.obj"
)

echo === Linking ===
link /dll /noentry /out:%DST%\FullIntOperations.dll %OBJ%\*.obj  /def:exports.def