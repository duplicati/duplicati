// Copyright (C) 2026, The Duplicati Team
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


using System;
using System.IO;
using System.Runtime.InteropServices;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Entry point for the update helper
/// </summary>
public static class Program
{
    /// <summary>
    /// The helper version
    /// </summary>
    public static string Version
        => typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Runs the helper
    /// </summary>
    /// <param name="args">The command line arguments</param>
    /// <returns>The exit code</returns>
    public static int Main(string[] args)
    {
        using var log = new HelperLog();
        log.Redirect(Path.Combine(AppContext.BaseDirectory, UpdateHelperProtocol.HelperLogFilename));

        if (args.Length == 0)
        {
            WriteUsage();
            return 100;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "version":
                Console.WriteLine(Version);
                return 0;

            case "install":
                {
                    var pipe = ParseOption(args, "--pipe") ?? Environment.GetEnvironmentVariable(UpdateHelperProtocol.PipeEnvironmentVariable);
                    if (string.IsNullOrWhiteSpace(pipe))
                    {
                        Console.Error.WriteLine("Missing --pipe argument");
                        return 100;
                    }

                    // The secret is read from stdin so it never shows in a process listing
                    var secret = Console.In.ReadLine();
                    if (string.IsNullOrWhiteSpace(secret))
                    {
                        Console.Error.WriteLine("No secret was supplied on stdin");
                        return 100;
                    }

                    log.Write($"Update helper {Version} starting, pid {Environment.ProcessId}");
                    DetachFromProcessGroup(log);
                    try
                    {
                        return new HelperSession(log).Run(pipe, secret.Trim());
                    }
                    catch (Exception ex)
                    {
                        log.Write($"Unhandled error: {ex}");
                        return HelperSession.ExitFailed;
                    }
                }

            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                WriteUsage();
                return 100;
        }
    }

    [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static extern int setsid();

    /// <summary>
    /// Moves the helper into its own session and process group. launchd kills every process in a job's
    /// process group when the job's main process exits, which would take the helper down with the server.
    /// This must happen before the server is told to go, which is after the handshake.
    /// </summary>
    private static void DetachFromProcessGroup(HelperLog log)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            if (setsid() < 0)
                log.Write($"setsid failed with error {Marshal.GetLastPInvokeError()}; the helper stays in the parent process group");
        }
        catch (Exception ex)
        {
            log.Write($"setsid failed: {ex.Message}; the helper stays in the parent process group");
        }
    }

    private static string? ParseOption(string[] args, string name)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i].Substring(name.Length + 1);
        }
        return null;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Duplicati update helper");
        Console.WriteLine();
        Console.WriteLine("This program is started by the Duplicati server to install an update.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  install --pipe <name>   Connect to the server on the named pipe; the secret is read from stdin");
        Console.WriteLine("  version                 Print the helper version");
    }
}
