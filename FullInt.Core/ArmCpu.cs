using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace FullNumerics
{
    /// <summary>
    /// AArch64: whether the cores multiply narrowly, for the thresholds (C++: Cpu::ArmNarrow). The
    /// scalar multiplier of a narrow core takes several cycles a 64 x 64 -> 128 product -
    /// Neoverse-N1 (a Cortex-A76 relative): mul 3, umulh 4, 7 a word product - so the splitting
    /// algorithms pay off much earlier than on a wide one (Cortex-X1: about 2 cycles).
    /// From MIDR (implementer and part) of every CPU in /proc/cpuinfo (Linux, Android): narrow
    /// only when all of them are known narrow ones (a big.LITTLE phone takes its big cores'
    /// class). Narrow: ARM's Cortex-A53 / A35 / A55 / A57 / A72 .. A77 and Neoverse-N1 (measured:
    /// N1); anything else, another architecture, or no /proc/cpuinfo: not narrow.
    /// FULLINT_ARM_CORE=narrow / wide overrides.
    /// </summary>
    internal static class ArmCpu
    {
        public static readonly bool Narrow = Detect();

        private static bool Detect()
        {
            string? forced = Environment.GetEnvironmentVariable("FULLINT_ARM_CORE");
            if (forced != null) return forced == "narrow";
            if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return false;
            try
            {
                if (!File.Exists("/proc/cpuinfo")) return false;
                int implementer = 0, parts = 0, narrow = 0;
                foreach (string line in File.ReadAllLines("/proc/cpuinfo"))
                {
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    string key = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
                    if (!value.StartsWith("0x", StringComparison.Ordinal)) continue;
                    if (!int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) continue;
                    if (key == "CPU implementer") implementer = v;
                    else if (key == "CPU part")
                    {
                        parts++;
                        // A53, A35, A55, A57; A72, A73, A75, A76, N1, A77, A76AE
                        bool knownNarrow = implementer == 0x41 && (v == 0xd03 || v == 0xd04 || v == 0xd05 || v == 0xd07
                            || (v >= 0xd08 && v <= 0xd0e));
                        if (knownNarrow) narrow++;
                    }
                }
                return parts > 0 && narrow == parts;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
