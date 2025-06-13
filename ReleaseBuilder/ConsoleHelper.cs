// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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
