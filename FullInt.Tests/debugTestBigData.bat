@echo off
dotnet build FullInt.Tests.csproj -c Debug -v detailed
if %errorlevel%==0 (
    "C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\windbg.exe" -o -c "
        .sympath+ C:\Users\Denis\source\repos\FullInt\FullInt.Tests\bin\Debug\net8.0
        .reload /f
        bp Fullint64FromDecString:36
        g
    " "C:\Program Files\Microsoft Visual Studio\2022\TestAgent\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" ^
    "C:\Users\Denis\source\repos\FullInt\FullInt.Tests\bin\Debug\net8.0\FullInt.Tests.dll" ^
    "/TestCaseFilter:FullyQualifiedName~FullInt.Tests.FullIntBigTests.BigTestHex" ^
    "/Logger:console;verbosity=detailed"
)
