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
using System.IO;
using System.Linq;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// The verdict on whether updates can be installed automatically on this installation
/// </summary>
/// <param name="Supported">True if the install can be performed</param>
/// <param name="Reason">The reason it cannot, when not supported</param>
/// <param name="PackageFormat">The package format, e.g. <c>deb</c></param>
public sealed record UpdateInstallSupport(bool Supported, string? Reason, string PackageFormat)
{
    /// <summary>
    /// The package formats the helper can install
    /// </summary>
    public static readonly string[] SupportedFormats = ["zip", "appimage", "msi", "deb", "rpm", "pkg", "dmg"];

    /// <summary>
    /// The package formats that need root to install
    /// </summary>
    private static readonly string[] RootFormats = ["deb", "rpm", "pkg"];

    /// <summary>
    /// Gets the package format suffix of a package type id, e.g. <c>deb</c> for <c>linux-x64-gui.deb</c>
    /// </summary>
    public static string GetFormat(string? packageTypeId)
    {
        if (string.IsNullOrWhiteSpace(packageTypeId))
            return "";
        var idx = packageTypeId.LastIndexOf('.');
        return idx < 0 ? "" : packageTypeId.Substring(idx + 1).ToLowerInvariant();
    }

    /// <summary>
    /// The path of the helper executable in the installation
    /// </summary>
    public static string HelperPath
        => Path.Combine(UpdaterManager.INSTALLATIONDIR, PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.UpdateHelper));

    /// <summary>
    /// Evaluates the support for the running installation
    /// </summary>
    /// <param name="host">The host environment</param>
    /// <returns>The verdict</returns>
    public static UpdateInstallSupport Evaluate(HostEnvironment host)
        => Evaluate(UpdaterManager.PackageTypeId, host, File.Exists(HelperPath), UpdaterManager.DISABLE_UPDATE_CHECK, ProcessInfo.IsPrivileged());

    /// <summary>
    /// Evaluates the support from the given facts
    /// </summary>
    /// <param name="packageTypeId">The package type id of the installation</param>
    /// <param name="host">The host environment</param>
    /// <param name="helperExists">True if the helper executable is present</param>
    /// <param name="updatesDisabled">True if update checks are disabled</param>
    /// <param name="isPrivileged">True if the process runs as root or an elevated administrator</param>
    /// <returns>The verdict</returns>
    public static UpdateInstallSupport Evaluate(string? packageTypeId, HostEnvironment host, bool helperExists, bool updatesDisabled, bool isPrivileged)
    {
        var format = GetFormat(packageTypeId);

        if (updatesDisabled)
            return new UpdateInstallSupport(false, "Update checks are disabled", format);

        if (string.IsNullOrWhiteSpace(packageTypeId) || string.Equals(packageTypeId, "debug", StringComparison.OrdinalIgnoreCase))
            return new UpdateInstallSupport(false, "This instance does not run from an installed package", format);

        if (!SupportedFormats.Contains(format))
            return new UpdateInstallSupport(false, format switch
            {
                "docker" => "Docker images are updated by pulling a new image",
                "spk" => "Synology packages are updated through the Package Center",
                "qpkg" => "QNAP packages are updated through the App Center",
                _ => $"Automatic installation is not supported for {format} packages"
            }, format);

        if (!helperExists)
            return new UpdateInstallSupport(false, "The update helper is missing from this installation", format);

        if (format == "appimage" && string.IsNullOrWhiteSpace(host.AppImagePath))
            return new UpdateInstallSupport(false, "The AppImage path is not known; start the AppImage directly", format);

        if (RootFormats.Contains(format) && !isPrivileged && !host.InteractiveSession)
            return new UpdateInstallSupport(false, "Installing the package requires root privileges and no interactive session is available to ask for them", format);

        if (host.Supervisor == SupervisorKind.Systemd && string.IsNullOrWhiteSpace(host.SystemdRunPath))
            return new UpdateInstallSupport(false, "systemd-run is required to update a systemd managed instance", format);

        if (host.Supervisor == SupervisorKind.WindowsService && format != "msi" && format != "zip")
            return new UpdateInstallSupport(false, $"Automatic installation is not supported for {format} packages when running as a Windows service", format);

        return new UpdateInstallSupport(true, null, format);
    }
}
