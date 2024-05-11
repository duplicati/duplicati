using System.Text;

namespace ReleaseBuilder;

public static class EncryptionHelper
{
    /// <summary>
    /// Decrypts the contents of the password file, using the given password and returns the file contents as a string
    /// </summary>
    /// <param name="passwordfile">The password file to decrypt</param>
    /// <param name="password">The password to decrypt with</param>
    /// <returns>The file contents</returns>
    public static string DecryptPasswordFile(string passwordfile, string password)
    {
        using var ms = new MemoryStream();
        using var fs = File.OpenRead(passwordfile);
        SharpAESCrypt.SharpAESCrypt.Decrypt(password, fs, ms);

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
