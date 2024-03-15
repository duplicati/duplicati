using System;
using System.Collections.Generic;
using Crypto = System.Security.Cryptography;
using System.Text;

namespace Duplicati.Library.Utility
{
    public static class HashFactory
    {
        public const string MD5 = "MD5";
        public const string SHA1 = "SHA1";
        public const string SHA256 = "SHA256";
        public const string SHA384 = "SHA384";
        public const string SHA512 = "SHA512";

        public static string[] GetSupportedHashes()
        {
            var r = new List<string>();
            foreach (var h in new string[] { "SHA1", "MD5", "SHA256", "SHA384", "SHA512" })
            {
                try
                {
                    using (var p = CreateHasher(h)) { 
                        if (p != null)
                            r.Add(h);
                    }
                }
                catch
                {
                }
            }

            return r.ToArray();
        }

        public static Crypto.HashAlgorithm CreateHasher(string algorithm)
        {
            switch (algorithm.ToUpperInvariant())
            {
                case "MD5":
                    return Crypto.MD5.Create();
                case "SHA1":
                    return Crypto.SHA1.Create();
                case "SHA256":
                    return Crypto.SHA256.Create();
                case "SHA384":
                    return Crypto.SHA384.Create();
                case "SHA512":
                    return Crypto.SHA512.Create();
                default:
                    throw new ArgumentException("Unknown algorithm: " + algorithm, nameof(algorithm));
            }
        }

        public static int HashSizeBytes(string algorithm)
        {
            using (var hasher = CreateHasher(algorithm))
            {
                return hasher.HashSize / 8;
            }
        }
    }
}
