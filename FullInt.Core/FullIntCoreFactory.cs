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
					return new FullIntCoreWindowsX64();
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
    }
}
