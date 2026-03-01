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

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Certificates.Platform;

/// <summary>
/// Installs CA certificates on Linux using the ca-certificates system.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxCATrustInstaller : ICATrustInstaller
{
    /// <summary>
    /// Default certificate directory path.
    /// </summary>
    public const string DEFAULT_CERT_DIR = "/usr/local/share/ca-certificates";
    /// <summary>
    /// The certificate filename prefix.
    /// </summary>
    private const string CERT_FILENAME_PREFIX = "duplicati-local-ca";
    /// <summary>
    /// The timeout for the update-ca-certificates command.
    /// </summary>
    private static readonly TimeSpan PROCESS_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the certificate directory path.
    /// Defaults to /usr/local/share/ca-certificates.
    /// </summary>
    public string CertDirectory { get; set; } = DEFAULT_CERT_DIR;

    /// <summary>
    /// Generates a unique certificate filename based on the certificate thumbprint.
    /// </summary>
    /// <param name="caCertificate">The CA certificate to generate the filename for.</param>
    /// <returns>A unique filename for the certificate.</returns>
    private static string GetCertFilename(X509Certificate2 caCertificate)
    {
        // Use first 8 chars of thumbprint for uniqueness while keeping filename readable
        var thumbprintPrefix = caCertificate.Thumbprint[..8].ToLowerInvariant();
        return $"{CERT_FILENAME_PREFIX}-{thumbprintPrefix}.crt";
    }

    /// <inheritdoc />
    public string Name => "Linux ca-certificates";

    /// <inheritdoc />
    public bool CanInstall() => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool IsInstalled(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            var certPath = Path.Combine(CertDirectory, GetCertFilename(caCertificate));
            if (!File.Exists(certPath))
                return false;

            // Read and compare certificate (file is stored in PEM format)
            var pemText = File.ReadAllText(certPath);
            var installedCert = CertificateStorageHelper.ImportFromPem(pemText);
            return installedCert.Thumbprint.Equals(caCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public TrustInstallationResult Install(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsLinux())
            return TrustInstallationResult.Failed;

        // Check if already installed
        if (IsInstalled(caCertificate))
            return TrustInstallationResult.AlreadyInstalled;

        // Check if cert directory exists - fail if it doesn't
        if (!Directory.Exists(CertDirectory))
            return TrustInstallationResult.Failed;

        var certPath = Path.Combine(CertDirectory, GetCertFilename(caCertificate));

        try
        {
            // Export certificate as PEM
            var pem = CertificateStorageHelper.ExportToPem(caCertificate);
            File.WriteAllText(certPath, pem);

            // Run update-ca-certificates
            var result = RunProcess("update-ca-certificates", string.Empty);

            if (result.ExitCode != 0)
            {
                // Check for permission errors
                if (result.Error.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("could not open", StringComparison.OrdinalIgnoreCase))
                {
                    // Clean up the file we created
                    try
                    {
                        File.Delete(certPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                    return TrustInstallationResult.RequiresElevation;
                }

                // Certificate file was written but update-ca-certificates failed,
                // meaning the certificate is NOT actually trusted by the system.
                // Clean up and report failure.
                try
                {
                    File.Delete(certPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                return TrustInstallationResult.Failed;
            }

            return TrustInstallationResult.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return TrustInstallationResult.RequiresElevation;
        }
        catch
        {
            return TrustInstallationResult.Failed;
        }
    }

    /// <inheritdoc />
    public TrustUninstallationResult Uninstall(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsLinux())
            return TrustUninstallationResult.Failed;

        // Check if installed
        if (!IsInstalled(caCertificate))
            return TrustUninstallationResult.NotInstalled;

        var certPath = Path.Combine(CertDirectory, GetCertFilename(caCertificate));

        try
        {
            // Delete the certificate file
            File.Delete(certPath);

            // Run update-ca-certificates to remove from trust store
            var result = RunProcess("update-ca-certificates", "--fresh");

            if (result.ExitCode != 0)
            {
                if (result.Error.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("denied", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustUninstallationResult.RequiresElevation;
                }
            }

            return TrustUninstallationResult.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return TrustUninstallationResult.RequiresElevation;
        }
        catch (FileNotFoundException)
        {
            return TrustUninstallationResult.NotInstalled;
        }
        catch
        {
            return TrustUninstallationResult.Failed;
        }
    }

    private static (int ExitCode, string Output, string Error) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, $"Failed to start process: {fileName}");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit(PROCESS_TIMEOUT);
        if (!process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors
            }
            return (-1, string.Empty, $"Process timed out after {PROCESS_TIMEOUT}");
        }

        var output = outputTask.Result;
        var error = errorTask.Result;

        return (process.ExitCode, output, error);
    }
}
