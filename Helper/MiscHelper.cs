using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace True.Helper
{
    public static class MiscHelper
    {
        public static string GetFriendlyOSName()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    return obj["Caption"]?.ToString() ?? "Unknown OS";
                }
            }
            catch { }

            return "Unknown OS";
        }

        public static string GetMachineGuid()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString() ?? "unknown";
            }
            catch { return "unknown"; }
        }

    }
}
