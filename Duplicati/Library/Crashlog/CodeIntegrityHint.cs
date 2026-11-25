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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Crashlog;
#nullable enable

/// <summary>
/// Explains a crash caused by Windows code integrity blocking a library.
///
/// With Smart App Control or App Control for Business turned on, Windows refuses
/// to load unsigned libraries that it does not trust. Duplicati lists the
/// unsigned third-party libraries in a signature catalog, which Windows only
/// uses once it is registered. Without the registration, the first unsigned
/// library fails to load with ERROR_SYSTEM_INTEGRITY_POLICY, and the stack
/// trace does not tell the user what to do.
/// </summary>
internal static class CodeIntegrityHint
{
    /// <summary>
    /// ERROR_SYSTEM_INTEGRITY_POLICY as an HRESULT
    /// </summary>
    private const int SystemIntegrityPolicyHResult = unchecked((int)0x800711C7);

    /// <summary>
    /// The error code as it appears in the messages of load failures
    /// </summary>
    private const string SystemIntegrityPolicyCode = "0x800711C7";

    /// <summary>
    /// The name of the signature catalog, next to the executables
    /// </summary>
    private const string CatalogFileName = "Duplicati.cat";

    /// <summary>
    /// The executable that registers the signature catalog
    /// </summary>
    private const string RegistrationTool = "Duplicati.WindowsService.exe";

    /// <summary>
    /// Matches the library name in the message of a failed native library load
    /// </summary>
    private static readonly Regex NativeLibraryName = new Regex(@"DLL '(?<name>[^']+)'", RegexOptions.Compiled);

    /// <summary>
    /// Gets all the exceptions in the exception tree
    /// </summary>
    /// <param name="ex">The exception to start from</param>
    /// <returns>The exception and all its inner exceptions</returns>
    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        pending.Push(ex);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            else if (current.InnerException != null)
                pending.Push(current.InnerException);
        }
    }

    /// <summary>
    /// Finds the exception that reports a library blocked by code integrity
    /// </summary>
    /// <param name="ex">The exception to examine</param>
    /// <returns>The exception, or <c>null</c> if the crash has another cause</returns>
    private static Exception? FindBlockedLoad(Exception ex)
        => Flatten(ex).FirstOrDefault(x =>
            x.HResult == SystemIntegrityPolicyHResult
            || x.Message.Contains(SystemIntegrityPolicyCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the name of the blocked library
    /// </summary>
    /// <param name="ex">The exception that reports the blocked library</param>
    /// <returns>The file name, or <c>null</c> if it is not known</returns>
    private static string? GetLibraryName(Exception ex)
    {
        // Managed assemblies are reported by their display name
        if (ex is FileLoadException fileLoad && !string.IsNullOrWhiteSpace(fileLoad.FileName))
        {
            var name = fileLoad.FileName.Split(',')[0].Trim();
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
        }

        // Native libraries are only named in the message
        var match = NativeLibraryName.Match(ex.Message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>
    /// Creates the explanation for a crash caused by a blocked library
    /// </summary>
    /// <param name="ex">The exception that caused the crash</param>
    /// <param name="installFolder">The folder with the Duplicati files</param>
    /// <returns>The explanation, or <c>null</c> if the crash has another cause</returns>
    internal static string? Create(Exception ex, string installFolder)
    {
        var blocked = FindBlockedLoad(ex);
        if (blocked == null)
            return null;

        var library = GetLibraryName(blocked) ?? "a library that Duplicati needs";
        var reason = $"Windows blocked {library} from loading. This happens when Smart App Control or App Control for Business is turned on.";

        if (!File.Exists(Path.Combine(installFolder, CatalogFileName)))
            return reason + Environment.NewLine + Environment.NewLine
                + "Some of the libraries in Duplicati are published unsigned by their authors, and this build of Duplicati has no signature catalog for them, so it cannot run while App Control is on.";

        return reason + Environment.NewLine + Environment.NewLine
            + "Some of the libraries in Duplicati are published unsigned by their authors. Windows accepts them once the Duplicati signature catalog is registered, which the installer does. To register it, run this from a Command Prompt started as administrator:"
            + Environment.NewLine + Environment.NewLine
            + $"\"{Path.Combine(installFolder, RegistrationTool)}\" register-catalog";
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    /// <summary>MB_ICONERROR</summary>
    private const uint MB_ICONERROR = 0x10;
    /// <summary>MB_TOPMOST</summary>
    private const uint MB_TOPMOST = 0x40000;
    /// <summary>MB_SETFOREGROUND</summary>
    private const uint MB_SETFOREGROUND = 0x10000;

    /// <summary>
    /// Shows the explanation in a message box when the process has no console,
    /// as for the TrayIcon. It uses the Windows message box, because the blocked
    /// library may be the one that the user interface needs.
    /// A process without a desktop, such as a service, shows nothing, as a
    /// message box there would wait forever for a click that cannot come.
    /// </summary>
    /// <param name="hint">The explanation to show</param>
    /// <returns><c>true</c> if the message box was shown</returns>
    [SupportedOSPlatform("windows")]
    internal static bool ShowIfNoConsole(string hint)
    {
        if (!Environment.UserInteractive || GetConsoleWindow() != IntPtr.Zero)
            return false;

        MessageBoxW(IntPtr.Zero, hint, "Duplicati", MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
        return true;
    }
}
