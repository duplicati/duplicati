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
using Duplicati.Library.Utility;

namespace Duplicati.Library.Certificates.Platform;

/// <summary>
/// Installs CA certificates in the macOS Keychain using the security CLI tool.
/// </summary>
[SupportedOSPlatform("macos")]
public class MacOSCATrustInstaller : ICATrustInstaller
{
    /// <summary>
    /// Default keychain path.
    /// </summary>
    public const string DEFAULT_KEYCHAIN_PATH = "~/Library/Keychains/login.keychain-db";

    /// <summary>
    /// The timeout for the security command.
    /// </summary>
    private static readonly TimeSpan PROCESS_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the keychain path.
    /// Defaults to ~/Library/Keychains/login.keychain-db.
    /// </summary>
    public string KeychainPath { get; set; } = DEFAULT_KEYCHAIN_PATH;

    /// <inheritdoc />
    public string Name => "macOS Keychain";

    /// <inheritdoc />
    public bool CanInstall() => OperatingSystem.IsMacOS();

    /// <inheritdoc />
    public bool IsInstalled(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS())
            return false;

        try
        {
            // Find certificate by SHA-1 hash
            var hash = caCertificate.GetCertHashString();
            var result = RunSecurityCommand("find-certificate", "-Z", "-c", caCertificate.Subject, KeychainPath);

            // Check if the hash is in the output
            if (result.ExitCode == 0)
                return result.Output.Contains(hash, StringComparison.OrdinalIgnoreCase);

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public TrustInstallationResult Install(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsMacOS())
            return TrustInstallationResult.Failed;

        // Check if already installed
        if (IsInstalled(caCertificate))
            return TrustInstallationResult.AlreadyInstalled;

        // Export certificate to a temporary file
        using var tempFile = new TempFile();
        try
        {
            // Export as PEM
            var pem = CertificateStorageHelper.ExportToPem(caCertificate);
            File.WriteAllText(tempFile, pem);

            // Add certificate to keychain
            var addResult = RunSecurityCommand("add-certificates", KeychainPath, tempFile);
            if (addResult.ExitCode != 0)
            {
                if (addResult.Error.Contains("User interaction is not allowed", StringComparison.OrdinalIgnoreCase) ||
                    addResult.Error.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustInstallationResult.RequiresElevation;
                }
                return TrustInstallationResult.Failed;
            }

            // Trust the certificate
            var trustResult = RunSecurityCommand(
                "add-trusted-cert", "-d", "-r", "trustRoot", "-k", KeychainPath, tempFile);

            if (trustResult.ExitCode != 0)
            {
                if (trustResult.Error.Contains("User interaction is not allowed", StringComparison.OrdinalIgnoreCase) ||
                    trustResult.Error.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustInstallationResult.RequiresElevation;
                }
                // Non-fatal - certificate is added but may need manual trust setting
            }

            return TrustInstallationResult.Success;
        }
        catch
        {
            return TrustInstallationResult.Failed;
        }
    }

    /// <inheritdoc />
    public TrustUninstallationResult Uninstall(X509Certificate2 caCertificate)
    {
        if (!OperatingSystem.IsMacOS())
            return TrustUninstallationResult.Failed;

        // Check if installed
        if (!IsInstalled(caCertificate))
            return TrustUninstallationResult.NotInstalled;

        try
        {
            // Find and delete certificate
            var result = RunSecurityCommand("delete-certificate", "-c", caCertificate.Subject, KeychainPath);

            if (result.ExitCode == 0)
                return TrustUninstallationResult.Success;

            if (result.Error.Contains("User interaction is not allowed", StringComparison.OrdinalIgnoreCase) ||
                result.Error.Contains("authorization", StringComparison.OrdinalIgnoreCase))
            {
                return TrustUninstallationResult.RequiresElevation;
            }

            return TrustUninstallationResult.Failed;
        }
        catch
        {
            return TrustUninstallationResult.Failed;
        }
    }

    private static (int ExitCode, string Output, string Error) RunSecurityCommand(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList to safely pass arguments (handles escaping automatically)
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, "Failed to start security process");

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
            return (-1, string.Empty, $"Security process timed out after {PROCESS_TIMEOUT}");
        }

        var output = outputTask.Result;
        var error = errorTask.Result;

        return (process.ExitCode, output, error);
    }
}
