using System.Text;

namespace Duplicati.WebserverCore.Extensions;

public static class StringExtensions
{
    public static byte[] GetBytes(this string str)
    {
        return Encoding.Default.GetBytes(str);
    }
}