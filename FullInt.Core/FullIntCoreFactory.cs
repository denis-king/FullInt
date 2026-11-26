using System;
using System.Runtime.InteropServices;

namespace FullNumerics
{
    internal class FullIntCoreFactory
    {
		// FULLINT_FORCE_MANAGED=1 forces the managed fallback core on every platform
		// (used to run the whole test suite against it and compare with the native core).
		public const string ForceManagedEnvVar = "FULLINT_FORCE_MANAGED";

		public static IFullIntCore Create()
		{
			if (Environment.GetEnvironmentVariable(ForceManagedEnvVar) == "1")
			{
				return new FullIntCoreManaged();
			}

			var os = Environment.OSVersion.Platform;
			var arch = RuntimeInformation.ProcessArchitecture;
			
			if(os == PlatformID.Win32NT)
			{
				if (arch == Architecture.X64)
				{
					return NativeOrManaged(() => new FullIntCoreNative64());
				}
				else if (arch == Architecture.X86)
				{
					return NativeOrManaged(() => new FullIntCoreNative32());
				}
				else if (arch == Architecture.Arm64)
				{
					
				}
			}
			else if (os == PlatformID.Unix)
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					if (arch == Architecture.X64)
					{
#if NET5_0_OR_GREATER
						// the same x64 kernels (their entries take the System V arguments on Linux,
						// abi.inc): the 64-bit core (NativeLibraryResolver maps its library to the .so)
						return NativeOrManaged(() => new FullIntCoreNative64());
#endif
					}
					else if (arch == Architecture.Arm64)
					{
#if NET5_0_OR_GREATER
						// the AArch64 kernels (Sources/arm64: the x64 entries' names and contracts,
						// libFullIntOperations_linux_arm64.so); no x86 feature bits, so the 64-bit
						// core takes the baseline entries and the division by reciprocal
						return NativeOrManaged(() => new FullIntCoreNative64());
#endif
					}
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					if (arch == Architecture.X64)
					{
						
					}
					else if (arch == Architecture.Arm64)
					{
						
					}
				}
			}

			return new FullIntCoreManaged();
		}

		// The native library may be missing or unusable on a given machine (another platform
		// than it was built for, a package without the native asset, a blocked file). One cheap
		// call proves it loads and runs; if it does not, the managed core is used instead, so
		// the library keeps working everywhere, only slower.
		private static IFullIntCore NativeOrManaged(Func<IFullIntCore> createNative)
		{
			try
			{
				var core = createNative();
				Span<ulong> a = stackalloc ulong[] { ulong.MaxValue }, b = stackalloc ulong[] { 3 }, r = stackalloc ulong[2];
				core.Add(a, b, r);
				if (r[0] == 1 && r[1] == 2)
				{
					return core;
				}
			}
			catch (DllNotFoundException) { }
			catch (EntryPointNotFoundException) { }
			catch (BadImageFormatException) { }
			return new FullIntCoreManaged();
		}
    }
}
