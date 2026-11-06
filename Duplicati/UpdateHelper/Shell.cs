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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.UpdateHelper;

/// <summary>
/// The outcome of running a command
/// </summary>
/// <param name="ExitCode">The exit code</param>
/// <param name="StdOut">The captured standard output</param>
/// <param name="StdErr">The captured standard error</param>
internal sealed record ShellResult(int ExitCode, string StdOut, string StdErr)
{
    /// <summary>
    /// True if the exit code is zero
    /// </summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Runs external commands with captured output
/// </summary>
internal static class Shell
{
    /// <summary>
    /// Runs a command and waits for it to exit
    /// </summary>
    /// <param name="log">The log to write to</param>
    /// <param name="executable">The executable</param>
    /// <param name="arguments">The arguments</param>
    /// <param name="environment">Extra environment variables, or null</param>
    /// <param name="timeout">The maximum time to wait</param>
    /// <returns>The result</returns>
    public static ShellResult Run(HelperLog log, string executable, IEnumerable<string> arguments, IDictionary<string, string>? environment, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        if (environment != null)
            foreach (var kv in environment)
                psi.Environment[kv.Key] = kv.Value;

        log.Write($"Running: {executable} {string.Join(" ", psi.ArgumentList)}");

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outTask = PumpAsync(p.StandardOutput, stdout, log, "out");
        var errTask = PumpAsync(p.StandardError, stderr, log, "err");

        if (!p.WaitForExit(timeout))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException($"{executable} did not complete within {timeout}");
        }

        Task.WaitAll(outTask, errTask);
        log.Write($"Exit code {p.ExitCode} from {executable}");
        return new ShellResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder target, HelperLog log, string tag)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            target.AppendLine(line);
            log.Write($"  [{tag}] {line}");
        }
    }

    /// <summary>
    /// Finds an executable on the PATH or in common system folders
    /// </summary>
    /// <param name="name">The executable name</param>
    /// <returns>The full path, or null if not found</returns>
    public static string? Find(string name)
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        candidates.AddRange(["/usr/local/sbin", "/usr/local/bin", "/usr/sbin", "/usr/bin", "/sbin", "/bin"]);

        foreach (var dir in candidates)
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
                return full;
        }

        return null;
    }
}
