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
using System.Text;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Privilege elevation helpers; privilege checks live in <see cref="ProcessInfo"/>
/// </summary>
internal static class Privileges
{
    /// <summary>
    /// Runs a command as root, using the privileges at hand or an interactive prompt
    /// </summary>
    /// <param name="log">The log</param>
    /// <param name="executable">The executable</param>
    /// <param name="arguments">The arguments</param>
    /// <param name="environment">Environment variables to set for the command</param>
    /// <param name="interactive">True if a graphical prompt may be shown</param>
    /// <param name="timeout">The maximum time to wait</param>
    /// <returns>The result</returns>
    public static ShellResult RunAsRoot(HelperLog log, string executable, IReadOnlyList<string> arguments, IDictionary<string, string>? environment, bool interactive, TimeSpan timeout)
    {
        if (ProcessInfo.IsPrivileged())
            return Shell.Run(log, executable, arguments, environment, timeout);

        if (!interactive)
            throw new InvalidOperationException("Root privileges are required and no interactive session is available to ask for them");

        if (OperatingSystem.IsMacOS())
        {
            // osascript shows the standard administrator password dialog
            var cmd = new StringBuilder();
            if (environment != null)
                foreach (var kv in environment)
                    cmd.Append(ShellQuote(kv.Key + "=" + kv.Value)).Append(' ');
            cmd.Append(ShellQuote(executable));
            foreach (var a in arguments)
                cmd.Append(' ').Append(ShellQuote(a));

            var script = "do shell script " + AppleScriptQuote(cmd.ToString()) + " with administrator privileges";
            return Shell.Run(log, "/usr/bin/osascript", ["-e", script], null, timeout);
        }

        if (OperatingSystem.IsLinux())
        {
            var pkexec = Shell.Find("pkexec") ?? throw new InvalidOperationException("Root privileges are required but pkexec was not found");
            var args = new List<string>();
            if (environment != null && environment.Count > 0)
            {
                args.Add("env");
                foreach (var kv in environment)
                    args.Add(kv.Key + "=" + kv.Value);
            }
            args.Add(executable);
            args.AddRange(arguments);
            return Shell.Run(log, pkexec, args, null, timeout);
        }

        throw new PlatformNotSupportedException("Privilege elevation is not supported on this platform");
    }

    /// <summary>
    /// Quotes a string for /bin/sh
    /// </summary>
    public static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Quotes a string as an AppleScript string literal
    /// </summary>
    public static string AppleScriptQuote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
