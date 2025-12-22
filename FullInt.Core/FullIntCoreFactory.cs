using System;
using System.Runtime.InteropServices;

namespace FullInt.Core
{
    public class FullIntCoreFactory
    {
		public static IFullIntCore Create()
		{
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
