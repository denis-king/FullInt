using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    public class FullIntCoreFactory
    {
		// FULLINT_FORCE_MANAGED=1 forces the managed fallback core on every platform
		// (used to run the whole test suite against it and compare with the native core).
		public const string ForceManagedEnvVar = "FULLINT_FORCE_MANAGED";

		public static IFullIntCore Create()
		{
			if (Environment.GetEnvironmentVariable(ForceManagedEnvVar) == "1")
			{
				return new FullInt64CoreDefault();
			}

			var os = Environment.OSVersion.Platform;
			var arch = RuntimeInformation.ProcessArchitecture;
			
			if(os == PlatformID.Win32NT)
			{
				if (arch == Architecture.X64)
				{
					return NativeOrManaged(() => new FullIntCoreWindowsX64());
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
						
					}
					else if (arch == Architecture.Arm64)
					{
						
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

			return new FullInt64CoreDefault();
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
				Span<ulong> a = stackalloc ulong[] { 2 }, b = stackalloc ulong[] { 3 }, r = stackalloc ulong[2];
				core.Mult(a, b, r);
				if (r[0] == 0 && r[1] == 6)
				{
					return core;
				}
			}
			catch (DllNotFoundException) { }
			catch (EntryPointNotFoundException) { }
			catch (BadImageFormatException) { }
			return new FullInt64CoreDefault();
		}
    }
}
