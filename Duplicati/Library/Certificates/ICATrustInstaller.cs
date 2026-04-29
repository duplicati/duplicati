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

using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Certificates;

/// <summary>
/// Result of a CA trust installation operation.
/// </summary>
public enum TrustInstallationResult
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The CA is already installed.
    /// </summary>
    AlreadyInstalled,

    /// <summary>
    /// The operation requires elevation (administrator/root privileges).
    /// </summary>
    RequiresElevation,

    /// <summary>
    /// The operation failed.
    /// </summary>
    Failed
}

/// <summary>
/// Result of a CA trust uninstallation operation.
/// </summary>
public enum TrustUninstallationResult
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The CA was not installed.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// The operation requires elevation (administrator/root privileges).
    /// </summary>
    RequiresElevation,

    /// <summary>
    /// The operation failed.
    /// </summary>
    Failed
}

/// <summary>
/// Interface for installing and uninstalling CA certificates in the system trust store.
/// </summary>
public interface ICATrustInstaller
{
    /// <summary>
    /// Gets the name of the installer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this installer can be used on the current platform.
    /// </summary>
    /// <returns>True if the installer can be used; otherwise, false.</returns>
    bool CanInstall();

    /// <summary>
    /// Checks whether the CA certificate is already installed in the trust store.
    /// </summary>
    /// <param name="caCertificate">The CA certificate to check.</param>
    /// <returns>True if the CA is installed; otherwise, false.</returns>
    bool IsInstalled(X509Certificate2 caCertificate);

    /// <summary>
    /// Installs the CA certificate in the system trust store.
    /// </summary>
    /// <param name="caCertificate">The CA certificate to install.</param>
    /// <returns>The result of the installation operation.</returns>
    TrustInstallationResult Install(X509Certificate2 caCertificate);

    /// <summary>
    /// Uninstalls the CA certificate from the system trust store.
    /// </summary>
    /// <param name="caCertificate">The CA certificate to uninstall.</param>
    /// <returns>The result of the uninstallation operation.</returns>
    TrustUninstallationResult Uninstall(X509Certificate2 caCertificate);
}

/// <summary>
/// Exception thrown when CA trust installation fails.
/// </summary>
public class CATrustInstallerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CATrustInstallerException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public CATrustInstallerException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CATrustInstallerException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CATrustInstallerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
