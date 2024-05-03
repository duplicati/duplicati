namespace ReleaseBuilder;

public static class ConsoleHelper
{
    /// <summary>
    /// Helper method to request that the user chooses an option
    /// </summary>
    /// <param name="prompt">The prompt to display</param>
    /// <param name="options">The allowed options</param>
    /// <returns>The selected option</returns>
    public static string ReadInput(string prompt, params string[] options)
    {
        while (true)
        {
            Console.WriteLine($"{prompt} [{string.Join("/", options)}]:");
            var r = Console.ReadLine();
            if (r == null)
                throw new TaskCanceledException();
            r = r.Trim();

            var m = options.FirstOrDefault(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase));
            if (m != null)
                return m;

            Console.WriteLine($"Input not accepted: {r}");
        }
    }

    /// <summary>
    /// Read a password from the console
    /// </summary>
    /// <param name="prompt">The text to show the user</param>
    /// <returns>The password</returns>
    public static string ReadPassword(string prompt)
    {
        Console.WriteLine(prompt);

        // From: https://stackoverflow.com/a/3404522
        var pass = string.Empty;
        ConsoleKey key;
        do
        {
            var keyInfo = Console.ReadKey(intercept: true);
            key = keyInfo.Key;

            if (key == ConsoleKey.Backspace && pass.Length > 0)
            {
                Console.Write("\b \b");
                pass = pass[0..^1];
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                if (OperatingSystem.IsWindows())
                    Console.Write("*");

                pass += keyInfo.KeyChar;
            }
        } while (key != ConsoleKey.Enter);

        return pass;
    }

}
