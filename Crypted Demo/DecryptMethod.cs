using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Crypted_Demo
{
    internal class DecryptMethod
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        public static void DecryptStringArray(string type, string arrayName, byte[] key, byte[] iv)
        {
            if ((type == "EncryptedStrings.EncryptedStrings") && (arrayName == "Strings"))
            {
                for (int i = 0; i < EncryptedStrings.EncryptedStrings.Strings.Length; i++)
                {
                    EncryptedStrings.EncryptedStrings.Strings[i] = DecryptString(EncryptedStrings.EncryptedStrings.Strings[i], key, iv);
                }
            }
        }

        public static void DecryptToMethod(int encryptedStringIndex, int ilOffset, int ilSize, byte[] key, byte[] iv)
        {
            var virtualAddress = IntPtr.Add(Process.GetCurrentProcess().MainModule.BaseAddress, ilOffset);

            const uint PAGE_EXECUTE_READWRITE = 0x40;
            uint oldProtect;
            byte[] decryptedBuff = DecryptToBytes(PLStrings.PLStrings.Strings[encryptedStringIndex], key, iv);
            if (!VirtualProtect(virtualAddress, (UIntPtr)ilSize, PAGE_EXECUTE_READWRITE, out oldProtect))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            Marshal.Copy(decryptedBuff, 0, virtualAddress, ilSize);

            // restore original protection
            if (!VirtualProtect(virtualAddress, (UIntPtr)ilSize, oldProtect, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static byte[] Decrypt(byte[] encr, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(encr, 0, encr.Length);
                    }

                    return memoryStream.ToArray();
                }
            }
        }

        public static byte[] DecryptToBytes(string encryptedstr, byte[] key, byte[] iv)
        {
            if (string.IsNullOrEmpty(encryptedstr)) return null;
            byte[] encrypted = Convert.FromBase64String(encryptedstr);
            if (encrypted == null) return null;
            if (encrypted.Length == 0) return null;


            return Decrypt(encrypted, key, iv);
        }

        public static string DecryptString(string encryptedstr, byte[] key, byte[] iv)
        {
            if (string.IsNullOrEmpty(encryptedstr)) return "";
            byte[] encrypted = Convert.FromBase64String(encryptedstr);
            if (encrypted == null) return "";
            if (encrypted.Length == 0) return "";


            return DecryptStringFromBytes(encrypted, key, iv);
        }

        static string DecryptStringFromBytes(byte[] cipherText, byte[] key, byte[] iv)
        {
            // Check arguments
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException(nameof(cipherText));
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException(nameof(key));
            if (iv == null || iv.Length <= 0)
                throw new ArgumentNullException(nameof(iv));

            return Encoding.UTF8.GetString(Decrypt(cipherText, key, iv));
        }
    }
}
