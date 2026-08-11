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

#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Utility;

/// <summary>
/// Shows notifications in the macOS Notification Center by running the
/// AppleScript <c>display notification</c> command through osascript.
/// </summary>
/// <remarks>
/// The notification is posted by the short-lived osascript process, so there
/// is no way to learn that the user activated the notification and
/// <see cref="NotificationClicked"/> is never raised. The notification is
/// also attributed to Script Editor rather than to Duplicati, which is a
/// known limitation of the osascript approach.
/// </remarks>
[SupportedOSPlatform("macOS")]
public sealed class MacOSOSANotifier : INativeNotifier
{
    private static readonly string LOGTAG = Log.LogTagFromType<MacOSOSANotifier>();

    /// <summary>
    /// The time to wait for the osascript process to exit before giving up
    /// </summary>
    private static readonly TimeSpan OsascriptTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public Action? NotificationClicked { get; set; }

    /// <inheritdoc/>
    public void Notify(NativeNotificationLevel level, string title, string message)
    {
        // AppleScript notifications have no severity concept, so the level is ignored
        var script = $"display notification \"{EscapeAppleScriptString(message)}\" with title \"{EscapeAppleScriptString(title)}\"";

        // The osascript process is started and awaited on a background thread
        // so the caller's thread (usually the UI thread) is never blocked.
        // As there is no caller left when the process finishes, failures are
        // logged here instead of being thrown.
        Task.Run(() =>
        {
            try
            {
                RunOsascript(script);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "NotificationFailed", ex, "Failed to show notification: {0}", title);
            }
        });
    }

    /// <summary>
    /// Runs an AppleScript through osascript, waiting for the process to exit
    /// </summary>
    /// <param name="script">The AppleScript source to run</param>
    private static void RunOsascript(string script)
    {
        var psi = new ProcessStartInfo("osascript")
        {
            RedirectStandardOutput = false,
            RedirectStandardInput = false,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the osascript process");

        if (!process.WaitForExit(OsascriptTimeout))
        {
            try { process.Kill(); }
            catch { /* Best effort */ }

            throw new TimeoutException($"The osascript process did not exit within {OsascriptTimeout.TotalSeconds} seconds");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"osascript exited with code {process.ExitCode}: {process.StandardError.ReadToEnd().Trim()}");
    }

    /// <summary>
    /// Escapes a string for use inside an AppleScript string literal
    /// </summary>
    /// <param name="value">The string to escape</param>
    /// <returns>The escaped string</returns>
    private static string EscapeAppleScriptString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
