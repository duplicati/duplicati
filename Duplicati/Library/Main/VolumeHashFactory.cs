using System.Security.Cryptography;

namespace Duplicati.Library.Main
{
    static class VolumeHashFactory
    {
        public static HashAlgorithm CreateHasher()
        {
            return SHA256.Create();
        }
    }
}
