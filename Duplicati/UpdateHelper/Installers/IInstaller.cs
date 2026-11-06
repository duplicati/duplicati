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


using System.Collections.Generic;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs one package format
/// </summary>
internal interface IInstaller
{
    /// <summary>
    /// Checks that the install can be performed, before the server exits
    /// </summary>
    /// <param name="job">The job</param>
    /// <returns>Null if the install can proceed, otherwise the reason it cannot</returns>
    string? Prepare(UpdateJob job);

    /// <summary>
    /// Performs the install; throws on failure
    /// </summary>
    /// <param name="job">The job</param>
    void Install(UpdateJob job);

    /// <summary>
    /// True if <see cref="Rollback"/> can restore the previous installation
    /// </summary>
    bool CanRollback { get; }

    /// <summary>
    /// Restores the previous installation after a failed install or start
    /// </summary>
    /// <param name="job">The job</param>
    void Rollback(UpdateJob job);

    /// <summary>
    /// True if the installer already started the new instance, so the restart instruction must be skipped
    /// </summary>
    bool RestartHandled { get; }

    /// <summary>
    /// Paths the new instance should delete, because the helper could not
    /// </summary>
    IReadOnlyList<string> CleanupPaths { get; }
}

/// <summary>
/// Creates the installer for a package type id
/// </summary>
internal static class InstallerFactory
{
    /// <summary>
    /// Gets the package format suffix of a package type id, e.g. <c>deb</c> for <c>linux-x64-gui.deb</c>
    /// </summary>
    public static string GetFormat(string packageTypeId)
    {
        var idx = packageTypeId.LastIndexOf('.');
        return idx < 0 ? "" : packageTypeId.Substring(idx + 1).ToLowerInvariant();
    }

    /// <summary>
    /// Creates the installer, or returns null if the format is not supported
    /// </summary>
    public static IInstaller? Create(HelperLog log, string packageTypeId)
        => GetFormat(packageTypeId) switch
        {
            "zip" => new ZipInstaller(log),
            "appimage" => new AppImageInstaller(log),
            "msi" => new MsiInstaller(log),
            "deb" => new DpkgInstaller(log),
            "rpm" => new RpmInstaller(log),
            "pkg" => new MacPkgInstaller(log),
            "dmg" => new DmgInstaller(log),
            _ => null
        };
}
