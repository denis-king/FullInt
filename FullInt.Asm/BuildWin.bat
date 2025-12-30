@echo off
setlocal

:: Читаем параметры
set CONFIG=%1
set OUTDIR=%2
set OBJ=Bin\Obj

:: Windows

set SRC=Sources\x64
set OBJ=Bin\Obj
set DST=Bin

echo === Compiling asm files (win64) %CONFIG% ===

if "%CONFIG%"=="Debug" ( 
	for %%f in (%SRC%\*.asm) do (
		echo NASM %%f
		..\Tools\nasm\nasm.exe -g -f win64 "%%f" -o "%OBJ%\%%~nf.obj"
	)

	link /DEBUG /dll /noentry /out:Bin\FullIntOperations_win_x64.dll %OBJ%\*.obj /def:exports.def
	
	echo copy FullIntOperations_win_x64.dll to %OUTDIR%
	copy /Y Bin\FullIntOperations_win_x64.dll %OUTDIR%\FullIntOperations_win_x64.dll
	copy /Y Bin\FullIntOperations_win_x64.pdb %OUTDIR%\FullIntOperations_win_x64.pdb
) else (

	for %%f in (%SRC%\*.asm) do (
		echo NASM %%f
		..\Tools\nasm\nasm.exe -f win64 "%%f" -o "%OBJ%\%%~nf.obj"
	)

	link /dll /noentry /out:Bin\FullIntOperations_win_x64.dll %OBJ%\*.obj /def:exports.def
	
	echo copy FullIntOperations_win_x64.dll to %OUTDIR%
	copy /Y Bin\FullIntOperations_win_x64.dll %OUTDIR%\FullIntOperations_win_x64.dll
)


echo === Build finished ===
endlocal
