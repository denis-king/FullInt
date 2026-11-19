#if NET5_0_OR_GREATER
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace FullInt.Core
{
    /// <summary>
    /// Linux x64: the x64 core's DllImports name the Windows library (FullIntOperations_win_x64.dll);
    /// the same kernels are built for Linux as libFullIntOperations_linux_x64.so (NASM elf64, the
    /// System V entries of abi.inc: the same names and contracts). The resolver maps the one name
    /// to the other for this assembly; any other library loads as usual.
    /// </summary>
    internal static class LinuxX64Library
    {
        private const string WindowsName = "FullIntOperations_win_x64.dll";
        private const string LinuxName = "libFullIntOperations_linux_x64.so";
        private static int _registered;

        public static void Register()
        {
            if (Interlocked.Exchange(ref _registered, 1) == 0)
                NativeLibrary.SetDllImportResolver(typeof(LinuxX64Library).Assembly, Resolve);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != WindowsName) return IntPtr.Zero;   // the default probing
            return NativeLibrary.TryLoad(LinuxName, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
        }
    }
}
#endif
