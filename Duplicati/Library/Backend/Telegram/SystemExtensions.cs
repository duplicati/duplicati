using System;
using System.Linq;

namespace Duplicati.Library.Backend
{
    public static class SystemExtensions
    {
        public static byte[] ToBytes(this string hex)
        {
            return Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();
        }

        public static string ToHex(this byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}