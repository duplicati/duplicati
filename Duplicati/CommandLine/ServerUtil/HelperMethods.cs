namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Various helper methods.
/// </summary>
public static class HelperMethods
{
    /// <summary>
    /// Reads a password from the console.
    /// </summary>
    /// <param name="prompt">The prompt to display.</param>
    public static string ReadPasswordFromConsole(string prompt)
    {
        Console.Write(prompt);
        string password = "";
        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
            }
            else if (keyInfo.KeyChar != '\u0000') // Only accept if the key maps to a Unicode character (e.g., ignore F1 or Home).
            {
                password += keyInfo.KeyChar;
                Console.Write("*");
            }
        }

        Console.WriteLine();
        return password;
    }
}
