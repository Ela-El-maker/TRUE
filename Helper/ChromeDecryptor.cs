using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace True.Helper
{
    public static class ChromeDecryptor
    {
        public static byte[] GetMasterKey(string localStatePath)
        {
            string localStateJson = File.ReadAllText(localStatePath);
            var json = JObject.Parse(localStateJson);
            string encryptedKeyB64 = json["os_crypt"]?["encrypted_key"]?.ToString();

            if (string.IsNullOrEmpty(encryptedKeyB64))
                throw new Exception("Encrypted key not found in Local State.");

            byte[] encryptedKey = Convert.FromBase64String(encryptedKeyB64);

            // First 5 bytes are "DPAPI" prefix
            byte[] dpapiKey = new byte[encryptedKey.Length - 5];
            Array.Copy(encryptedKey, 5, dpapiKey, 0, dpapiKey.Length);

            return ProtectedData.Unprotect(dpapiKey, null, DataProtectionScope.CurrentUser);
        }

        public static string Decrypt(byte[] encryptedData, byte[] masterKey)
        {
            string header = Encoding.UTF8.GetString(encryptedData, 0, 3);

            // AES-GCM format: "v10" header
            if (header == "v10" || header == "v11")
            {
                byte[] nonce = new byte[12];
                byte[] ciphertextTag = new byte[encryptedData.Length - 15];

                Array.Copy(encryptedData, 3, nonce, 0, 12);
                Array.Copy(encryptedData, 15, ciphertextTag, 0, ciphertextTag.Length);

                byte[] plaintext = new byte[ciphertextTag.Length - 16]; // AES-GCM tag is 16 bytes

                using (var aesGcm = new AesGcm(masterKey))
                {
                    aesGcm.Decrypt(nonce, ciphertextTag, ciphertextTag[^16..], plaintext);
                }

                return Encoding.UTF8.GetString(plaintext);
            }

            // Older format — DPAPI
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser));
        }
    }
}
