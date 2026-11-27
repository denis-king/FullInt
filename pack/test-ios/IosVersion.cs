// The app's one use of an iOS API: without a reference the build leaves Microsoft.iOS out of the app,
// and the runtime aborts at start ("Could not find the assembly 'Microsoft.iOS.dll'"). Before Main:
// the iOS version into the log.
static class IosVersion
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Print() =>
        System.Console.WriteLine("iOS " + Foundation.NSProcessInfo.ProcessInfo.OperatingSystemVersionString);
}
