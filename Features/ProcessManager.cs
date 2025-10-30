using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    public static class ProcessManager
    {
        public static async Task ListProcesses()
        {
            try
            {
                var processes = Process.GetProcesses();
                var sb = new StringBuilder();

                sb.AppendLine(string.Format("{0,-6} {1,-30} {2,10}", "PID", "Name", "Memory (MB)"));
                sb.AppendLine(new string('-', 52));

                foreach (var proc in processes)
                {
                    try
                    {
                        long memMb = proc.PrivateMemorySize64 / (1024 * 1024);
                        sb.AppendLine(string.Format("{0,-6} {1,-30} {2,10}", proc.Id, proc.ProcessName, memMb));
                    }
                    catch
                    {
                        // Some protected/system processes might throw exceptions
                    }
                }

                await Communicator.PostResponse("processes", sb.ToString());
                Logger.Info("ProcessManager", "Sent process list.");
            }
            catch (Exception ex)
            {
                Logger.Error("ProcessManager", $"Error listing processes: {ex.Message}");
                await Communicator.PostResponse("processes", $"Error listing processes: {ex.Message}");
            }
        }

        public static async Task KillProcess(string input)
        {
            input = input.Trim();
            bool success = false;

            try
            {
                if (int.TryParse(input, out int pid))
                {
                    // Kill by PID
                    var proc = Process.GetProcessById(pid);
#if NET5_0_OR_GREATER
                    proc.Kill(true); // Kill including child processes
#else
                    proc.Kill();
#endif
                    success = true;
                }
                else
                {
                    // Kill by name
                    var found = Process.GetProcessesByName(input);
                    foreach (var proc in found)
                    {
#if NET5_0_OR_GREATER
                        proc.Kill(true);
#else
                        proc.Kill();
#endif
                        success = true;
                    }
                }

                string message = success ? $"Killed: {input}" : $"Process not found: {input}";
                Logger.Info("ProcessManager", message);
                await Communicator.PostResponse("kill", message);
            }
            catch (Exception ex)
            {
                string error = $"Error killing {input}: {ex.Message}";
                Logger.Error("ProcessManager", error);
                await Communicator.PostResponse("kill", error);
            }
        }
    }
}
