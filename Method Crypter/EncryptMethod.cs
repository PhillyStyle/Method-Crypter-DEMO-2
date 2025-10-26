﻿using dnlib.DotNet;
using dnlib.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.XPath;

namespace Method_Crypter
{
    internal class EncryptMethod
    {
        public static int GetOverloadMethodCount(ModuleDefMD module, string typeName, string methodName)
        {
            TypeDef type = module.Find(typeName, isReflectionName: true);

            if (type == null)
                throw new Exception($"Type '{typeName}' not found.");

            // Collect all methods that match methodName (in case of overloaded methods)
            var methods = string.IsNullOrEmpty(methodName)
                ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                : type.Methods.Where(m => m.Name == methodName).ToList();

            return methods.Count;
        }

        public static (long, int, int, bool) GetMethodILOffset(ModuleDefMD module, int index, string typeName, string methodName)
        {
            TypeDef type = module.Find(typeName, isReflectionName: true);

            if (type == null)
                throw new Exception($"Type '{typeName}' not found.");

            // Collect all methods that match methodName (in case of overloaded methods)
            var methods = string.IsNullOrEmpty(methodName)
                ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                : type.Methods.Where(m => m.Name == methodName).ToList();

            int i = 0;
            foreach (var method in methods)
            {
                if (i != index)
                {
                    i++;
                    continue;
                }
                return ((long)GetMethodILStartFileOffset(module, method), GetMethodILOffset(module, method), GetMethodILSize(module, method), method.IsConstructor);
            }
            return (-1, -1, -1, false);
        }

        public static string GetEncryptedMethod(ModuleDefMD module, string pathName, int index, string typeName, string methodName, byte[] key, byte[] iv)
        {
            TypeDef type = module.Find(typeName, isReflectionName: true);

            if (type == null)
                throw new Exception($"Type '{typeName}' not found.");

            // Collect all methods that match methodName (in case of overloaded methods)
            var methods = string.IsNullOrEmpty(methodName)
                ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                : type.Methods.Where(m => m.Name == methodName).ToList();

            if (methods.Count == 0)
                throw new Exception($"No methods named '{methodName}' found in type '{typeName}'.");

            int i = 0;
            foreach (var method in methods)
            {
                if (i != index) {
                    i++;
                    continue;
                }
                int cryptSize = GetMethodILSize(module, method);
                var fileoffset = GetMethodILStartFileOffset(module, method);

                byte[] buffer = new byte[cryptSize];
                string encryptedData = "";
                using (FileStream fs = new FileStream(pathName, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(fileoffset, SeekOrigin.Begin);
                    fs.Read(buffer, 0, cryptSize);
                    encryptedData = EncryptArrayToString(buffer, key, iv);
                }
                return encryptedData;
            }
            return "";
        }

        public static int GetMethodILSize(ModuleDefMD module, MethodDef method)
        {
            if (method == null || method.RVA == 0 || !method.HasBody || method.Body == null)
                return 0;

            var peImage = module.Metadata.PEImage;

            try
            {
                var reader = peImage.CreateReader(method.RVA);
                byte first = reader.ReadByte();

                // Tiny header
                if ((first & 0x3) == 2)
                {
                    return first >> 2; // code size only
                }
                else
                {
                    // Fat header
                    reader.Position--;
                    ushort flagsAndSize = reader.ReadUInt16();
                    int headerSize = (flagsAndSize >> 12) * 4;

                    reader.ReadUInt16(); // maxStack
                    int codeSize = reader.ReadInt32();
                    reader.ReadUInt32(); // localVarSigTok

                    // NO EH sections
                    return codeSize;
                }
            }
            catch
            {
                return 0;
            }
        }

        public static int GetMethodILOffset(ModuleDefMD module, MethodDef method)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (method.RVA == 0 || !method.HasBody) return -1;

            var reader = module.Metadata.PEImage.CreateReader(method.RVA);

            byte first = reader.ReadByte();

            if ((first & 3) == 2) // tiny header
                return (int)method.RVA + 1;
            else // fat header
                return (int)method.RVA + method.Body.HeaderSize;

        }

        /// Returns the file offset (bytes from start of file) where the method body header begins.
        /// If you want the offset where the IL code starts, add method.Body.HeaderSize.
        /// Returns -1 if not available.
        public static long GetMethodBodyFileOffset(ModuleDefMD module, MethodDef method)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (method.RVA == 0) return -1;           // no RVA (P/Invoke, extern, etc.)
            if (module.Metadata?.PEImage == null) return -1;

            // Convert RVA -> file offset
            uint rva = (uint)method.RVA;
            long fileOffset = (uint)module.Metadata.PEImage.ToFileOffset((dnlib.PE.RVA)rva);

            return fileOffset;
        }

        /// Returns the file offset where the IL instruction stream starts (header + code offset).
        /// Returns -1 if not available.
        public static long GetMethodILStartFileOffset(ModuleDefMD module, MethodDef method)
        {
            var baseOffset = GetMethodBodyFileOffset(module, method);
            if (baseOffset < 0) return -1;
            if (!method.HasBody || method.Body == null)
            {
                // Still valid to compute header location even if dnlib hasn't parsed a body,
                // but method.Body may be null in some scenarios.
                return baseOffset;
            }

            // The header size (tiny vs fat) is available from dnlib's parsed body:
            int headerSize = method.Body.HeaderSize; // number of bytes of method header
            return baseOffset + headerSize;
        }

        public static byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = key;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                        cs.Close();
                    }
                    return ms.ToArray();
                }
            }
        }

        public static string EncryptString(string plainText, byte[] Key, byte[] IV)
        {
            // Check arguments
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException(nameof(plainText));
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException(nameof(Key));
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException(nameof(IV));

            byte[] encrypted;

            // Create an Aes object with the specified key and IV
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create an encryptor to perform the stream transform
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            // Write all data to the stream
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes
            return Convert.ToBase64String(encrypted);
        }

        public static string EncryptArrayToString(byte[] unencrypted, byte[] Key, byte[] IV)
        {
            // Check arguments
            if (unencrypted == null || unencrypted.Length <= 0)
                throw new ArgumentNullException(nameof(unencrypted));
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException(nameof(Key));
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException(nameof(IV));

            byte[] encrypted = Encrypt(unencrypted, Key, IV);

            // Return the encrypted bytes
            return Convert.ToBase64String(encrypted);
        }

        public static class RandomNameGenerator
        {
            private static readonly HashSet<string> usedNames = new HashSet<string>();

            public static string GetUniqueName(int length = 12)
            {
                string candidate;
                do
                {
                    candidate = Guid.NewGuid().ToString("N").Substring(0, length);
                } while (!usedNames.Add(candidate));
                return candidate;
            }
        }
    }
}
