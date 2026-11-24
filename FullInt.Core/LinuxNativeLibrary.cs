#if NET5_0_OR_GREATER
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace FullInt.Core
{
    /// <summary>
    /// Linux: the 64-bit core's DllImports name the Windows library (FullIntOperations_win_x64.dll);
    /// the same entries are built for Linux as
    ///   x64:   libFullIntOperations_linux_x64.so   (the x64 sources, NASM elf64, the System V
    ///          entries of abi.inc),
    ///   arm64: libFullIntOperations_linux_arm64.so (the AArch64 sources, Sources/arm64: the same
    ///          names and contracts, AAPCS64; no x86 feature bits, so the core takes the baseline
    ///          entries and the division by reciprocal).
    /// The resolver maps the one name to the process's library for this assembly; any other
    /// library loads as usual.
    /// </summary>
    internal static class LinuxNativeLibrary
    {
        private const string WindowsName = "FullIntOperations_win_x64.dll";
        private static readonly string LinuxName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "libFullIntOperations_linux_arm64.so"
            : "libFullIntOperations_linux_x64.so";
        private static int _registered;

        public static void Register()
        {
            if (Interlocked.Exchange(ref _registered, 1) == 0)
                NativeLibrary.SetDllImportResolver(typeof(LinuxNativeLibrary).Assembly, Resolve);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != WindowsName) return IntPtr.Zero;   // the default probing
            return NativeLibrary.TryLoad(LinuxName, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
        }
    }
}
#endif
