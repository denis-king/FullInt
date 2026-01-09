dotnet build FullInt.Tests.csproj -v detailed
if %errorlevel%==0 (
    vstest.console.exe C:\Users\Denis\source\repos\FullInt\FullInt.Tests\bin\Debug\net8.0\FullInt.Tests.dll /TestCaseFilter:"FullyQualifiedName~FullInt.Tests.FullIntBigTests.BigTestHex" /Logger:"console;verbosity=detailed"
)