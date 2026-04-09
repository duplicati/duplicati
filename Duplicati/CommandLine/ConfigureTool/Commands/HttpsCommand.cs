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

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Certificates;
using Duplicati.Library.Certificates.Platform;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using ServerSettings = Duplicati.Server.Database.ServerSettings;

namespace Duplicati.CommandLine.ConfigureTool.Commands;

/// <summary>
/// Commands for managing HTTPS certificates.
/// </summary>
public static class HttpsCommand
{
    /// <summary>
    /// Adds platform-specific CA options to the command.
    /// </summary>
    /// <param name="cmd">The command to add options to.</param>
    /// <returns>The command with added options.</returns>
    private static Command AddPlatformSpecificCAOptions(Command cmd)
    {
        // Add platform-specific options
        if (OperatingSystem.IsWindows())
            cmd.AddOption(new Option<string>("--store", getDefaultValue: () => OperatingSystem.IsWindows() ? CATrustInstallerFactory.GetDefaultWindowsStoreLocation() == StoreLocation.LocalMachine ? "local" : "user" : "", description: "Certificate store location (local|user). Defaults to 'local' if admin, otherwise 'user')"));
        else if (OperatingSystem.IsLinux())
            cmd.AddOption(new Option<string>("--cert-dir", getDefaultValue: () => OperatingSystem.IsLinux() ? LinuxCATrustInstaller.DEFAULT_CERT_DIR : "", description: "Custom certificate directory for installing CA certificate"));
        else if (OperatingSystem.IsMacOS())
            cmd.AddOption(new Option<string>("--keychain", getDefaultValue: () => OperatingSystem.IsMacOS() ? MacOSCATrustInstaller.DEFAULT_KEYCHAIN_PATH : "", description: "Custom keychain path for installing CA certificate"));

        return cmd;
    }

    /// <summary>
    /// Adds common database options to the command.
    /// </summary>
    /// <param name="cmd">The command to add options to.</param>
    /// <returns>The command</returns>
    private static Command AddDatabaseOptions(Command cmd)
    {
        cmd.AddOption(new Option<string>("--data-folder", "Path to the Duplicati data folder (defaults to standard location)"));
        cmd.AddOption(new Option<string>("--settings-encryption-key", "Settings encryption key for the database (if settings are encrypted)"));
        return cmd;
    }

    /// <summary>
    /// Creates the 'generate' command.
    /// </summary>
    public static Command CreateGenerateCommand()
    {
        var cmd = new Command("generate", "Generate a new CA and server certificate for HTTPS")
        {
            new Option<string>("--hostnames", "Comma-separated list of hostnames to include in the certificate (defaults to auto-detected hostnames)"),
            new Option<bool>("--no-trust", "Skip installing the CA certificate in the system trust store"),
            new Option<bool>("--auto-create-database", "Create the database if it does not exist"),
        };

        AddDatabaseOptions(cmd);
        AddPlatformSpecificCAOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, bool, string?, string?, bool, string?, string?, string?>(HandleGenerate);
        return cmd;
    }

    /// <summary>
    /// Creates the 'renew' command.
    /// </summary>
    public static Command CreateRenewCommand()
    {
        var cmd = new Command("renew", "Renew the server certificate using the existing CA");

        AddDatabaseOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, string?>(HandleRenew);
        return cmd;
    }

    /// <summary>
    /// Creates the 'regenerate-ca' command.
    /// </summary>
    public static Command CreateRegenerateCaCommand()
    {
        var cmd = new Command("regenerate-ca", "Regenerate the CA and server certificate (removes old CA from trust store)")
        {
            new Option<string>("--hostnames", "Comma-separated list of hostnames to include in the certificate (defaults to auto-detected hostnames)"),
            new Option<bool>("--no-trust", "Skip installing the CA certificate in the system trust store"),
        };

        AddDatabaseOptions(cmd);
        AddPlatformSpecificCAOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, bool, string?, string?, string?, string?, string?>(HandleRegenerateCa);
        return cmd;
    }

    /// <summary>
    /// Creates the 'remove' command.
    /// </summary>
    public static Command CreateRemoveCommand()
    {
        var cmd = new Command("remove", "Remove the CA from trust store and delete certificates from database");

        AddDatabaseOptions(cmd);
        AddPlatformSpecificCAOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, string?, string?, string?, string?>(HandleRemove);
        return cmd;
    }

    /// <summary>
    /// Creates the 'show' command.
    /// </summary>
    public static Command CreateShowCommand()
    {
        var cmd = new Command("show", "Display current certificate status");

        AddDatabaseOptions(cmd);
        AddPlatformSpecificCAOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, string?, string?, string?, string?>(HandleShow);
        return cmd;
    }

    /// <summary>
    /// Creates the 'export' command.
    /// </summary>
    public static Command CreateExportCommand()
    {
        var cmd = new Command("export", "Export the server certificate (public key only) to a file")
        {
            new Option<string>("--file", "Output file path (defaults to duplicati-server.crt in current directory)"),
        };

        AddDatabaseOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, string?, string?>(HandleExport);
        return cmd;
    }

    /// <summary>
    /// Creates the 'export-ca' command.
    /// </summary>
    public static Command CreateExportCaCommand()
    {
        var cmd = new Command("export-ca", "Export the CA certificate (public key only) to a file")
        {
            new Option<string>("--file", "Output file path (defaults to duplicati-ca.crt in current directory)"),
        };

        AddDatabaseOptions(cmd);
        cmd.Handler = CommandHandler.Create<string?, string?, string?>(HandleExportCa);
        return cmd;
    }

    /// <summary>
    /// Gets the data folder path, either from the option or using the default.
    /// </summary>
    private static string GetDataFolder(string? dataFolderOption)
    {
        if (!string.IsNullOrWhiteSpace(dataFolderOption))
            return Path.GetFullPath(dataFolderOption);

        return DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);
    }

    /// <summary>
    /// Gets the database path for the given data folder.
    /// </summary>
    private static string GetDatabasePath(string dataFolder)
        => Path.Combine(dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);

    /// <summary>
    /// Opens a connection to the server database.
    /// </summary>
    private static Connection OpenDatabase(string dataFolder, string? settingsEncryptionKey, bool autoCreateDatabase)
    {
        var databasePath = GetDatabasePath(dataFolder);

        if (!File.Exists(databasePath) && !autoCreateDatabase)
            throw new UserInformationException($"Database not found: {databasePath}", "DatabaseNotFound");

        var opts = new Dictionary<string, string>();

        // Add settings encryption key if provided
        if (!string.IsNullOrWhiteSpace(settingsEncryptionKey))
            opts["settings-encryption-key"] = settingsEncryptionKey;

        // Create application settings with the specified data folder
        var appSettings = new DataFolderApplicationSettings(dataFolder);

        return Server.Program.GetDatabaseConnection(appSettings, opts, true, false);
    }

    /// <summary>
    /// Application settings implementation that uses a specific data folder.
    /// </summary>
    private class DataFolderApplicationSettings : IApplicationSettings
    {
        private readonly CancellationTokenSource _applicationExitEvent = new();

        public DataFolderApplicationSettings(string dataFolder)
        {
            DataFolder = dataFolder;
        }

        public bool SettingsEncryptionKeyProvidedExternally { get; set; }
        public Action? StartOrStopUsageReporter { get; set; }
        public string DataFolder { get; }
        public string Origin { get; set; } = "ConfigureTool";
        public CancellationToken ApplicationExit => _applicationExitEvent.Token;
        public ISecretProvider? SecretProvider { get; set; }

        public void SignalApplicationExit() => _applicationExitEvent.Cancel();
    }

    /// <summary>
    /// Parses the store location option and returns the appropriate StoreLocation value.
    /// </summary>
    /// <param name="storeOption">The store option string ("local", "user", or null).</param>
    /// <returns>The StoreLocation value, or null to use auto-detection.</returns>
    private static StoreLocation? ParseStoreLocation(string? storeOption)
    {
        if (string.IsNullOrWhiteSpace(storeOption))
            return null; // Auto-detect

        return storeOption.ToLowerInvariant() switch
        {
            "local" or "machine" or "localmachine" => StoreLocation.LocalMachine,
            "user" or "currentuser" => StoreLocation.CurrentUser,
            _ => null // Invalid value, will use auto-detection
        };
    }

    /// <summary>
    /// Reads CA certificate data from the database connection.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <returns>The CA certificate data, or null if not available.</returns>
    private static CACertificateData? ReadCaData(Connection connection)
    {
        var caCert = connection.ApplicationSettings.ServerCACertificate;
        var caKey = connection.ApplicationSettings.ServerCACertificateKey;
        var caPassword = connection.ApplicationSettings.ServerCACertificatePassword;

        if (string.IsNullOrWhiteSpace(caCert) || string.IsNullOrWhiteSpace(caKey) || string.IsNullOrWhiteSpace(caPassword))
            return null;

        return new CACertificateData
        {
            CACertificate = caCert,
            CAKey = caKey,
            CAPassword = caPassword
        };
    }

    /// <summary>
    /// Stores generated CA and server certificates in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="result">The certificate generation result.</param>
    private static void StoreGeneratedCertificates(Connection connection, CertificateGenerationResult result)
    {
        var settings = new Dictionary<string, string?>
        {
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE] = result.CACertificate!.CACertificate,
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE_KEY] = result.CACertificate.CAKey,
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE_PASSWORD] = result.CACertificate.CAPassword,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE] = result.ServerCertificate!.ServerCertificate,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD] = result.ServerCertificate.Password,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE_AUTOGENERATED] = "true"
        };

        connection.ApplicationSettings.UpdateSettings(settings, false);
    }

    /// <summary>
    /// Stores a renewed server certificate in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="result">The certificate renewal result.</param>
    private static void StoreRenewedCertificate(Connection connection, CertificateRenewalResult result)
    {
        var settings = new Dictionary<string, string?>
        {
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE] = result.RenewedCertificate!.ServerCertificate,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD] = result.RenewedCertificate.Password,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE_AUTOGENERATED] = "true"
        };

        connection.ApplicationSettings.UpdateSettings(settings, false);
    }

    /// <summary>
    /// Prints the trust installation status to the console.
    /// </summary>
    /// <param name="status">The trust installation status.</param>
    /// <returns>True if the status indicates a fatal error that should abort the operation.</returns>
    private static bool PrintTrustInstallationStatus(CATrustInstallationStatus? status)
    {
        switch (status)
        {
            case CATrustInstallationStatus.Success:
                Console.WriteLine("CA certificate installed successfully.");
                return false;
            case CATrustInstallationStatus.AlreadyInstalled:
                Console.WriteLine("CA certificate was already installed.");
                return false;
            case CATrustInstallationStatus.NotSupported:
                Console.WriteLine("Warning: No trust installer available for this platform.");
                return false;
            case CATrustInstallationStatus.RequiresElevation:
                Console.WriteLine("Error: Administrator/root privileges required to install CA certificate.");
                Console.WriteLine("Run with elevated permissions or use --no-trust to skip CA installation.");
                return true;
            case CATrustInstallationStatus.Failed:
                Console.WriteLine("Error: Failed to install CA certificate.");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Prints a database encryption warning if encryption is not enabled.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    private static void PrintEncryptionWarning(Connection connection)
    {
        if (!connection.IsEncryptingFields)
        {
            Console.WriteLine();
            Console.WriteLine("WARNING: Database field encryption is not enabled.");
            Console.WriteLine("Since a generated CA is stored in the database this can enable an attacker to issues certificates and enable a man-in-the-middle attack on all HTTPS connections.");
        }
    }

    /// <summary>
    /// Captures log messages generated in the library and forwards them to the console
    /// </summary>
    /// <returns>A disposable log scope</returns>
    private static IDisposable StartConsoleLogScope()
        => Log.StartScope(entry =>
        {
            if (entry.Level == LogMessageType.Information)
                Console.WriteLine(entry.FormattedMessage);
            else
                Console.WriteLine($"{entry.Level}: {entry.FormattedMessage}");
        }, entry => entry.Level >= LogMessageType.Information);        

    /// <summary>
    /// Handles the 'generate' command.
    /// Delegates certificate generation to <see cref="CertificateConfigurationHelper.GenerateCertificates"/>.
    /// </summary>
    private static int HandleGenerate(string? hostnames, bool noTrust, string? dataFolder, string? settingsEncryptionKey, bool autoCreateDatabase, string? store, string? certDir, string? keychain)
    {
        var storeLocation = ParseStoreLocation(store);
        var dataFolderPath = GetDataFolder(dataFolder);

        using var _  = StartConsoleLogScope();
        Console.WriteLine($"Using data folder: {dataFolderPath}");

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, autoCreateDatabase);

        var result = CertificateConfigurationHelper.GenerateCertificates(
            connection.ApplicationSettings.ServerSSLCertificateAutogenerated,
            connection.ApplicationSettings.ServerSSLCertificate,
            ReadCaData(connection),
            hostnames,
            noTrust,
            storeLocation,
            certDir,
            keychain);

        if (!result.Success)
        {
            Console.WriteLine($"Error: Failed to generate HTTPS certificates.");
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                Console.WriteLine($"Reason: {result.ErrorMessage}");
            return 1;
        }

        // Check if certificates were already valid (no new certs generated)
        if (result.CACertificate == null || result.ServerCertificate == null)
        {
            Console.WriteLine("Valid certificates already exist.");
            Console.WriteLine("Use 'regenerate-ca' to force regeneration or 'renew' to renew the server certificate.");
            return 0;
        }

        // Check trust installation status
        if (PrintTrustInstallationStatus(result.TrustInstallationStatus))
            return 1;

        // Store in database
        Console.WriteLine("Storing certificates in database...");
        StoreGeneratedCertificates(connection, result);

        PrintEncryptionWarning(connection);

        Console.WriteLine();
        Console.WriteLine("HTTPS certificates generated and stored successfully.");

        return 0;
    }

    /// <summary>
    /// Handles the 'renew' command.
    /// Delegates certificate renewal to <see cref="CertificateConfigurationHelper.RenewServerCertificate"/>.
    /// </summary>
    private static int HandleRenew(string? dataFolder, string? settingsEncryptionKey)
    {
        var dataFolderPath = GetDataFolder(dataFolder);

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        // Read existing CA data
        var caData = ReadCaData(connection);
        if (caData == null)
            throw new UserInformationException("No existing CA certificate found in database. Use 'generate' to create new certificates.", "CANotFound");

        Console.WriteLine("Renewing server certificate...");

        var result = CertificateConfigurationHelper.RenewServerCertificate(caData);

        if (!result.Renewed)
        {
            Console.WriteLine($"Error: Failed to renew server certificate.");
            if (!string.IsNullOrWhiteSpace(result.RenewalFailedReason))
                Console.WriteLine($"Reason: {result.RenewalFailedReason}");
            return 1;
        }

        // Store new server certificate in database
        Console.WriteLine("Storing new server certificate in database...");
        StoreRenewedCertificate(connection, result);

        Console.WriteLine();
        Console.WriteLine("Server certificate renewed successfully.");

        return 0;
    }

    /// <summary>
    /// Handles the 'regenerate-ca' command.
    /// Delegates to <see cref="CertificateConfigurationHelper.RegenerateCACertificates"/>.
    /// </summary>
    private static int HandleRegenerateCa(string? hostnames, bool noTrust, string? dataFolder, string? settingsEncryptionKey, string? store, string? certDir, string? keychain)
    {
        var storeLocation = ParseStoreLocation(store);
        var dataFolderPath = GetDataFolder(dataFolder);

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        var existingCaCertBase64 = connection.ApplicationSettings.ServerCACertificate;

        var result = CertificateConfigurationHelper.RegenerateCACertificates(
            existingCaCertBase64,
            hostnames,
            noTrust,
            storeLocation,
            certDir,
            keychain);

        if (!result.Success)
        {
            Console.WriteLine($"Error: Failed to regenerate HTTPS certificates.");
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                Console.WriteLine($"Reason: {result.ErrorMessage}");
            return 1;
        }

        // Check trust installation status
        if (PrintTrustInstallationStatus(result.TrustInstallationStatus))
            return 1;

        // Store in database
        Console.WriteLine("Storing certificates in database...");
        StoreGeneratedCertificates(connection, result);

        Console.WriteLine();
        Console.WriteLine("CA and server certificates regenerated successfully.");

        return 0;
    }

    /// <summary>
    /// Handles the 'remove' command.
    /// </summary>
    private static int HandleRemove(string? dataFolder, string? settingsEncryptionKey, string? store, string? certDir, string? keychain)
    {
        var storeLocation = ParseStoreLocation(store);
        var dataFolderPath = GetDataFolder(dataFolder);

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        // Get existing CA certificate and remove from trust store
        var caCertBase64 = connection.ApplicationSettings.ServerCACertificate;

        if (!string.IsNullOrWhiteSpace(caCertBase64))
        {
            try
            {
                var caCert = CertificateStorageHelper.DeserializeCertificate(caCertBase64);
                Console.WriteLine("Removing CA certificate from system trust store...");

                if (CertificateConfigurationHelper.IsCATrustInstalled(caCert, storeLocation, certDir, keychain))
                {
                    if (CertificateConfigurationHelper.RemoveCATrust(caCert, storeLocation, certDir, keychain))
                        Console.WriteLine("CA certificate removed from trust store.");
                    else
                        Console.WriteLine("Warning: Failed to remove CA certificate from trust store.");
                }
                else
                {
                    Console.WriteLine("CA certificate was not found in trust store.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not remove CA from trust store: {ex.Message}");
            }
        }

        // Remove all certificate data from database
        Console.WriteLine("Removing certificate data from database...");
        var settings = new Dictionary<string, string?>
        {
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE] = null,
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE_KEY] = null,
            [ServerSettings.CONST.SERVER_CA_CERTIFICATE_PASSWORD] = null,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE] = null,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD] = null,
            [ServerSettings.CONST.SERVER_SSL_CERTIFICATE_AUTOGENERATED] = null
        };

        connection.ApplicationSettings.UpdateSettings(settings, false);

        Console.WriteLine();
        Console.WriteLine("HTTPS certificates removed successfully.");

        return 0;
    }

    /// <summary>
    /// Handles the 'show' command.
    /// Delegates status retrieval to <see cref="CertificateConfigurationHelper.GetCertificateStatus"/>.
    /// </summary>
    private static int HandleShow(string? dataFolder, string? settingsEncryptionKey, string? store, string? certDir, string? keychain)
    {
        var storeLocation = ParseStoreLocation(store);
        var dataFolderPath = GetDataFolder(dataFolder);

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        Console.WriteLine();
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        var caCertBase64 = connection.ApplicationSettings.ServerCACertificate;
        var serverCertCollection = connection.ApplicationSettings.ServerSSLCertificate;
        var isAutogenerated = connection.ApplicationSettings.ServerSSLCertificateAutogenerated;

        // Check if certificates exist
        if (string.IsNullOrWhiteSpace(caCertBase64) && serverCertCollection == null)
        {
            Console.WriteLine("No HTTPS certificates configured.");
            Console.WriteLine("Use 'generate' command to create certificates.");
            return 0;
        }

        var status = CertificateConfigurationHelper.GetCertificateStatus(
            caCertBase64,
            serverCertCollection,
            isAutogenerated,
            storeLocation,
            certDir,
            keychain);

        // Display CA certificate info
        Console.WriteLine("=== CA Certificate ===");
        if (status.CACert == null)
        {
            Console.WriteLine("No CA certificate stored.");
        }
        else
        {
            Console.WriteLine($"Subject: {status.CACert.Subject}");
            Console.WriteLine($"Issuer: {status.CACert.Issuer}");
            Console.WriteLine($"Valid from: {status.CACert.NotBefore:yyyy-MM-dd}");
            Console.WriteLine($"Valid until: {status.CACert.NotAfter:yyyy-MM-dd}");

            var caStatus = status.CaDaysUntilExpiry <= 0 ? "EXPIRED"
                : status.CaDaysUntilExpiry <= CertificateRenewalChecker.RENEWAL_THRESHOLD_DAYS ? "EXPIRING SOON"
                : "Valid";
            Console.WriteLine($"Status: {caStatus}");
            Console.WriteLine($"Trust store: {(status.IsCATrusted ? "Installed" : "Not installed")}");
        }

        Console.WriteLine();

        // Display server certificate info
        Console.WriteLine("=== Server Certificate ===");
        if (status.ServerCert == null)
        {
            if (serverCertCollection != null)
                Console.WriteLine("Server certificate collection exists but no certificate with private key found.");
            else
                Console.WriteLine("No server certificate stored.");
        }
        else
        {
            Console.WriteLine($"Subject: {status.ServerCert.Subject}");
            Console.WriteLine($"Issuer: {status.ServerCert.Issuer}");
            Console.WriteLine($"Valid from: {status.ServerCert.NotBefore:yyyy-MM-dd}");
            Console.WriteLine($"Valid until: {status.ServerCert.NotAfter:yyyy-MM-dd}");

            var serverStatus = status.ServerDaysUntilExpiry <= 0 ? "EXPIRED"
                : status.ServerDaysUntilExpiry <= CertificateRenewalChecker.RENEWAL_THRESHOLD_DAYS ? "EXPIRING SOON"
                : "Valid";
            Console.WriteLine($"Status: {serverStatus}");

            if (status.DnsNames.Any())
                Console.WriteLine($"DNS names: {string.Join(", ", status.DnsNames)}");
            if (status.IpAddresses.Any())
                Console.WriteLine($"IP addresses: {string.Join(", ", status.IpAddresses)}");

            Console.WriteLine($"Autogenerated: {status.IsAutogenerated}");
        }

        Console.WriteLine();

        // Display database encryption status
        Console.WriteLine("=== Security ===");
        Console.WriteLine($"Database field encryption: {(connection.IsEncryptingFields ? "Enabled" : "Disabled")}");

        return 0;
    }

    /// <summary>
    /// Handles the 'export' command.
    /// </summary>
    private static int HandleExport(string? file, string? dataFolder, string? settingsEncryptionKey)
    {
        var dataFolderPath = GetDataFolder(dataFolder);
        var outputFile = string.IsNullOrWhiteSpace(file) ? "duplicati-server.crt" : file;

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        Console.WriteLine($"Exporting server certificate to: {Path.GetFullPath(outputFile)}");
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        var serverCertCollection = connection.ApplicationSettings.ServerSSLCertificate;
        if (serverCertCollection == null || serverCertCollection.Count == 0)
        {
            Console.WriteLine("Error: No server certificate found in database.");
            return 1;
        }

        try
        {
            var serverCert = serverCertCollection.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey) ?? serverCertCollection[0];
            var pem = CertificateStorageHelper.ExportToPem(serverCert);
            File.WriteAllText(outputFile, pem);
            Console.WriteLine($"Server certificate exported successfully.");
            Console.WriteLine($"Subject: {serverCert.Subject}");
            Console.WriteLine($"Valid until: {serverCert.NotAfter:yyyy-MM-dd}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting certificate: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles the 'export-ca' command.
    /// </summary>
    private static int HandleExportCa(string? file, string? dataFolder, string? settingsEncryptionKey)
    {
        var dataFolderPath = GetDataFolder(dataFolder);
        var outputFile = string.IsNullOrWhiteSpace(file) ? "duplicati-ca.crt" : file;

        Console.WriteLine($"Using data folder: {dataFolderPath}");
        Console.WriteLine($"Exporting CA certificate to: {Path.GetFullPath(outputFile)}");
        using var _  = StartConsoleLogScope();

        using var connection = OpenDatabase(dataFolderPath, settingsEncryptionKey, false);

        var caCertBase64 = connection.ApplicationSettings.ServerCACertificate;
        if (string.IsNullOrWhiteSpace(caCertBase64))
        {
            Console.WriteLine("Error: No CA certificate found in database.");
            return 1;
        }

        try
        {
            var caCert = CertificateStorageHelper.DeserializeCertificate(caCertBase64);
            var pem = CertificateStorageHelper.ExportToPem(caCert);
            File.WriteAllText(outputFile, pem);
            Console.WriteLine($"CA certificate exported successfully.");
            Console.WriteLine($"Subject: {caCert.Subject}");
            Console.WriteLine($"Valid until: {caCert.NotAfter:yyyy-MM-dd}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting certificate: {ex.Message}");
            return 1;
        }
    }
}
