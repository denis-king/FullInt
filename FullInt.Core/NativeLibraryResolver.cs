using System;
#if NET5_0_OR_GREATER
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endif

namespace FullNumerics
{
    /// <summary>
    /// The names the DllImports use. On .NET 5+ they are logical names, mapped to the file of the
    /// process's OS and architecture by NativeLibraryResolver:
    ///   FullIntOperations64: Windows x64  FullIntOperations_win_x64.dll
    ///                        Linux x64    libFullIntOperations_linux_x64.so   (the x64 sources, NASM
    ///                                     elf64, the System V entries of abi.inc)
    ///                        Linux arm64  libFullIntOperations_linux_arm64.so (Sources/arm64: the
    ///                                     same names and contracts, AAPCS64; no x86 feature bits,
    ///                                     so the core takes the baseline entries)
    ///   FullIntOperations32: Windows x86  FullIntOperations_win_x86.dll
    /// netstandard2.0 has no resolver API (the .NET Framework build, Windows only): the Windows
    /// file names themselves.
    /// </summary>
    internal static class NativeNames
    {
#if NET5_0_OR_GREATER
        public const string Lib64 = "FullIntOperations64";
        public const string Lib32 = "FullIntOperations32";
#else
        public const string Lib64 = "FullIntOperations_win_x64.dll";
        public const string Lib32 = "FullIntOperations_win_x86.dll";
#endif
    }

#if NET5_0_OR_GREATER
    internal static class NativeLibraryResolver
    {
        // registered when the assembly loads, before any DllImport of it can bind
#pragma warning disable CA2255   // a library on purpose: the resolver must be in place before any DllImport binds
        [ModuleInitializer]
        internal static void Register()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
        }

        // the file of a logical name on this OS and architecture, or null (no such library here)
        internal static string? FileName(string name)
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (name == NativeNames.Lib64 && arch == Architecture.X64) return "FullIntOperations_win_x64.dll";
                if (name == NativeNames.Lib32 && arch == Architecture.X86) return "FullIntOperations_win_x86.dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && name == NativeNames.Lib64)
            {
                if (arch == Architecture.X64) return "libFullIntOperations_linux_x64.so";
                if (arch == Architecture.Arm64) return "libFullIntOperations_linux_arm64.so";
            }
            return null;
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != NativeNames.Lib64 && name != NativeNames.Lib32) return IntPtr.Zero;   // the default probing
            string? file = FileName(name);
            if (file == null) return IntPtr.Zero;   // DllNotFoundException: the factory falls back to the managed core
            return NativeLibrary.TryLoad(file, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
        }
    }
#endif
}
