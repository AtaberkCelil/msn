using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Client
{
    public static class EncryptionHelper
    {
        private static byte[] GetBytes(string input, int length)
        {
            byte[] bytes = new byte[length];
            if (string.IsNullOrEmpty(input)) return bytes;
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            Array.Copy(inputBytes, bytes, Math.Min(inputBytes.Length, length));
            return bytes;
        }

        public static string Encrypt(string plainText, string key, string iv)
        {
            try
            {
                byte[] keyBytes = GetBytes(key, 32);
                byte[] ivBytes = GetBytes(iv, 16);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = keyBytes;
                    aes.IV = ivBytes;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            using (StreamWriter sw = new StreamWriter(cs, Encoding.UTF8))
                            {
                                sw.Write(plainText);
                            }
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Encryption Error] {ex.Message}");
                return string.Empty;
            }
        }

        public static string Decrypt(string cipherText, string key, string iv)
        {
            try
            {
                byte[] keyBytes = GetBytes(key, 32);
                byte[] ivBytes = GetBytes(iv, 16);
                byte[] cipherBytes = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = keyBytes;
                    aes.IV = ivBytes;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (MemoryStream ms = new MemoryStream(cipherBytes))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                        {
                            using (StreamReader sr = new StreamReader(cs, Encoding.UTF8))
                            {
                                return sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Decryption Error] {ex.Message}");
                return string.Empty;
            }
        }
    }
}
