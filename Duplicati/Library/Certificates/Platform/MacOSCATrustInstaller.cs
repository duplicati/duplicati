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
        if (!OperatingSystem.IsMacOS())
            return false;

        try
        {
            var hash = caCertificate.GetCertHashString();

            // Extract the common name (CN) from the subject for filtering
            // The -c option filters by common name (partial match)
            var commonName = caCertificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (string.IsNullOrEmpty(commonName))
                return false;

            // Find certificates matching the common name, with SHA-1 hashes
            // -a = all matches, -c = filter by common name, -Z = include SHA-1 hashes
            var result = RunSecurityCommand("find-certificate", "-a", "-c", commonName, "-Z", ResolveKeyChainPath(KeychainPath));

            // Search for the SHA-1 hash line containing our specific hash
            if (result.ExitCode == 0)
            {
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("SHA-1 hash:", StringComparison.OrdinalIgnoreCase))
                    {
                        var hashInOutput = line[("SHA-1 hash:".Length)..].Trim();
                        if (hashInOutput.Equals(hash, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

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
            // Export certificate in DER format
            File.WriteAllBytes(tempFile, caCertificate.RawData);

            // Add certificate to keychain using 'security import' with explicit x509 format
            var addResult = RunSecurityCommand("import", tempFile, "-k", ResolveKeyChainPath(KeychainPath), "-t", "cert", "-f", "x509");
            if (addResult.ExitCode != 0)
            {
                if (addResult.Error.Contains("User interaction is not allowed", StringComparison.OrdinalIgnoreCase) ||
                    addResult.Error.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                {
                    return TrustInstallationResult.RequiresElevation;
                }
                return TrustInstallationResult.Failed;
            }

            // Trust the certificate, this requires elevated privileges
            var trustResult = RunSecurityCommand(
                "add-trusted-cert", "-d", "-r", "trustRoot", "-k", ResolveKeyChainPath(KeychainPath), tempFile);

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
            // Delete certificate by SHA-1 hash to avoid accidentally removing
            // other certificates that share the same subject name.
            var hash = caCertificate.GetCertHashString();
            var result = RunSecurityCommand("delete-certificate", "-t", "-Z", hash, ResolveKeyChainPath(KeychainPath));

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

    private static string ResolveKeyChainPath(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home + path.Substring(1);
        }
        return path;
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

        // Read stdout/stderr to completion before waiting for exit to avoid
        // deadlocks when the process output fills the OS pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!Task.WaitAll([outputTask, errorTask], PROCESS_TIMEOUT))
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

        return (process.ExitCode, outputTask.Result, errorTask.Result);
    }
}
