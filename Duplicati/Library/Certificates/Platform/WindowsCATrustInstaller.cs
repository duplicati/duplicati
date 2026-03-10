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

using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Certificates.Platform;

/// <summary>
/// Installs CA certificates in the Windows certificate store.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsCATrustInstaller : ICATrustInstaller
{
    /// <summary>
    /// The store location to use.
    /// </summary>
    private readonly StoreLocation _storeLocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsCATrustInstaller"/> class.
    /// </summary>
    /// <param name="storeLocation">The store location to use (default is CurrentUser).</param>
    public WindowsCATrustInstaller(StoreLocation storeLocation = StoreLocation.CurrentUser)
    {
        _storeLocation = storeLocation;
    }

    /// <inheritdoc />
    public string Name => $"Windows ({_storeLocation})";

    /// <inheritdoc />
    public bool CanInstall() => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public bool IsInstalled(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var store = new X509Store(StoreName.Root, _storeLocation);
        try
        {
            store.Open(OpenFlags.ReadOnly);
            var thumbprint = caCertificate.Thumbprint;
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            return found.Count > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            store.Close();
        }
    }

    /// <inheritdoc />
    public TrustInstallationResult Install(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsWindows())
            return TrustInstallationResult.Failed;

        // Check if already installed
        if (IsInstalled(caCertificate))
            return TrustInstallationResult.AlreadyInstalled;

        using var store = new X509Store(StoreName.Root, _storeLocation);
        try
        {
            // Try to open with read/write access
            store.Open(OpenFlags.ReadWrite);

            // Add the certificate without private key
            var certWithoutKey = X509CertificateLoader.LoadCertificate(caCertificate.RawData);
            store.Add(certWithoutKey);

            return TrustInstallationResult.Success;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // Access denied typically means we need elevation
            if (ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return TrustInstallationResult.RequiresElevation;
            }
            return TrustInstallationResult.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            return TrustInstallationResult.RequiresElevation;
        }
        catch
        {
            return TrustInstallationResult.Failed;
        }
        finally
        {
            store.Close();
        }
    }

    /// <inheritdoc />
    public TrustUninstallationResult Uninstall(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsWindows())
            return TrustUninstallationResult.Failed;

        // Check if installed
        if (!IsInstalled(caCertificate))
            return TrustUninstallationResult.NotInstalled;

        using var store = new X509Store(StoreName.Root, _storeLocation);
        try
        {
            store.Open(OpenFlags.ReadWrite);

            var thumbprint = caCertificate.Thumbprint;
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);

            if (found.Count == 0)
                return TrustUninstallationResult.NotInstalled;

            store.RemoveRange(found);
            return TrustUninstallationResult.Success;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            if (ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return TrustUninstallationResult.RequiresElevation;
            }
            return TrustUninstallationResult.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            return TrustUninstallationResult.RequiresElevation;
        }
        catch
        {
            return TrustUninstallationResult.Failed;
        }
        finally
        {
            store.Close();
        }
    }
}
