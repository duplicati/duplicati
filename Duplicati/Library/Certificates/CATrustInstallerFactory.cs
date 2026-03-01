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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Certificates.Platform;

namespace Duplicati.Library.Certificates;

/// <summary>
/// Factory for creating the appropriate CA trust installer for the current platform.
/// </summary>
public static class CATrustInstallerFactory
{
    /// <summary>
    /// Creates a CA trust installer appropriate for the current platform.
    /// </summary>
    /// <param name="storeLocation">The store location to use (Windows only). If null, uses LocalMachine if admin, otherwise CurrentUser.</param>
    /// <param name="linuxCertDirectory">The certificate directory to use (Linux only). If null, uses the default system location.</param>
    /// <param name="macOSKeychainPath">The keychain path to use (macOS only). If null, uses the default login keychain.</param>
    /// <returns>An ICATrustInstaller instance for the current platform, or null if not supported.</returns>
    public static ICATrustInstaller? CreateInstaller(StoreLocation? storeLocation, string? linuxCertDirectory, string? macOSKeychainPath)
    {
        if (OperatingSystem.IsWindows())
        {
            // If no store location specified, auto-detect based on privileges
            var location = storeLocation ?? GetDefaultWindowsStoreLocation();
            return new WindowsCATrustInstaller(location);
        }

        if (OperatingSystem.IsMacOS())
        {
            var installer = new MacOSCATrustInstaller();
            if (!string.IsNullOrWhiteSpace(macOSKeychainPath))
                installer.KeychainPath = macOSKeychainPath;
            return installer;
        }

        if (OperatingSystem.IsLinux())
        {
            var installer = new LinuxCATrustInstaller();
            if (!string.IsNullOrWhiteSpace(linuxCertDirectory))
                installer.CertDirectory = linuxCertDirectory;
            return installer;
        }

        return null;
    }

    /// <summary>
    /// Gets the default Windows store location based on current privileges.
    /// Uses LocalMachine if running as admin/LocalSystem, otherwise CurrentUser.
    /// </summary>
    /// <returns>The default store location for Windows.</returns>
    [SupportedOSPlatform("windows")]
    public static StoreLocation GetDefaultWindowsStoreLocation()
    {
        if (Utility.PermissionHelper.IsRunningAsAdministratorOrLocalSystem())
            return StoreLocation.LocalMachine;
        return StoreLocation.CurrentUser;
    }

    /// <summary>
    /// Determines whether the current platform is supported.
    /// </summary>
    /// <returns>True if the platform is supported; otherwise, false.</returns>
    public static bool IsPlatformSupported()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
