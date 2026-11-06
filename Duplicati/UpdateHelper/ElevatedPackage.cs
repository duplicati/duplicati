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
using System.Text;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Builds shell scripts that install a package with root privileges from a copy the user cannot change.
/// The downloaded package lives in a folder the user can write to, so a check done before elevating
/// does not cover the file the root process later opens. The script copies the package into a folder
/// only root can access, checks the SHA256 of that copy, and installs from it.
/// </summary>
internal static class ElevatedPackage
{
    /// <summary>
    /// The shell variable holding the path of the verified copy; it lives in the root-only folder <c>$WORKDIR</c>
    /// </summary>
    public const string PackageVariable = "\"$PKG\"";

    /// <summary>
    /// Builds the script
    /// </summary>
    /// <param name="job">The job, with the package path and the verified hash</param>
    /// <param name="extension">The file extension for the copy, without a dot</param>
    /// <param name="installCommand">The install command; refers to the copy as <see cref="PackageVariable"/></param>
    /// <param name="cleanupCommand">Commands run on exit before the copy is removed, or an empty string</param>
    /// <returns>The script for <c>/bin/sh -c</c></returns>
    public static string BuildScript(UpdateJob job, string extension, string installCommand, string cleanupCommand)
    {
        var hex = Convert.ToHexStringLower(Convert.FromBase64String(job.PackageSha256));
        var hashCommand = OperatingSystem.IsMacOS() ? "/usr/bin/shasum -a 256" : "sha256sum";
        var cleanup = string.IsNullOrWhiteSpace(cleanupCommand) ? "" : cleanupCommand + "; ";

        // One line, joined with semicolons, so it survives being embedded in an AppleScript string
        var sb = new StringBuilder();
        sb.Append("WORKDIR=$(mktemp -d /tmp/duplicati-update.XXXXXXXX) || exit 1; ");
        sb.Append("PKG=\"$WORKDIR/package.").Append(extension).Append("\"; ");
        // Only plain removals; nothing recursive, so a mount left behind is never emptied
        sb.Append("trap '").Append(cleanup.Replace("'", "'\\''")).Append("rm -f \"$PKG\"; rmdir \"$WORKDIR\" 2>/dev/null' EXIT; ");
        sb.Append("cp -- ").Append(Privileges.ShellQuote(job.PackagePath)).Append(" \"$PKG\" || exit 1; ");
        sb.Append("HASH=$(").Append(hashCommand).Append(" \"$PKG\") || exit 1; ");
        sb.Append("HASH=${HASH%% *}; ");
        sb.Append("[ \"$HASH\" = \"").Append(hex).Append("\" ] || { echo 'The package changed after it was verified' >&2; exit 1; }; ");
        sb.Append(installCommand);
        return sb.ToString();
    }

    /// <summary>
    /// Runs an install command as root against a verified, root-only copy of the package
    /// </summary>
    /// <param name="log">The log</param>
    /// <param name="job">The job</param>
    /// <param name="extension">The file extension for the copy, without a dot</param>
    /// <param name="installCommand">The install command; refers to the copy as <see cref="PackageVariable"/></param>
    /// <param name="cleanupCommand">Commands run on exit before the copy is removed, or an empty string</param>
    /// <param name="environment">Environment variables for the command, or null</param>
    /// <param name="interactive">True if a graphical prompt may be shown</param>
    /// <param name="timeout">The maximum time to wait</param>
    /// <returns>The result</returns>
    public static ShellResult Run(HelperLog log, UpdateJob job, string extension, string installCommand, string cleanupCommand, System.Collections.Generic.IDictionary<string, string>? environment, bool interactive, TimeSpan timeout)
        => Privileges.RunAsRoot(log, "/bin/sh", ["-c", BuildScript(job, extension, installCommand, cleanupCommand)], environment, interactive, timeout);
}
