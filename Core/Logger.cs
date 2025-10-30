using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;


namespace True.Core
{
  

    public static class Logger
    {
        public enum Level { DEBUG, INFO, WARN, ERROR }

        private static readonly string logPath = "client.log";
        private static Level currentLevel = Level.INFO;

        public static void Init()
        {
            string level = ConfigManager.Get("Logging", "level", "info").ToUpper();
            if (Enum.TryParse(level, out Level parsed))
                currentLevel = parsed;
        }

        public static void Log(Level level, string message)
        {
            if (level < currentLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string output = $"[{timestamp}] [{level}] {message}";

            Console.WriteLine(output);
            try
            {
                File.AppendAllText(logPath, output + Environment.NewLine);
            }
            catch { /* Silent fail to avoid crash */ }
        }

        // Single-parameter versions
        public static void Debug(string msg) => Log(Level.DEBUG, msg);
        public static void Info(string msg) => Log(Level.INFO, msg);
        public static void Warn(string msg) => Log(Level.WARN, msg);
        public static void Error(string msg) => Log(Level.ERROR, msg);

        // Two-parameter versions with tag
        public static void Debug(string tag, string msg) => Log(Level.DEBUG, $"[{tag}] {msg}");
        public static void Info(string tag, string msg) => Log(Level.INFO, $"[{tag}] {msg}");
        public static void Warn(string tag, string msg) => Log(Level.WARN, $"[{tag}] {msg}");
        public static void Error(string tag, string msg) => Log(Level.ERROR, $"[{tag}] {msg}");

    }

}
