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

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// The options of the PostgreSQL source provider, read from the source URL and the backup options.
/// A root certificate given as text is kept in a temporary file while the options are in use, as the
/// client programs read it from a file; disposing the options removes it.
/// </summary>
public sealed class PostgreSQLOptions : IDisposable
{
    /// <summary>
    /// The URL scheme that selects the provider
    /// </summary>
    public const string Scheme = "postgresql";

    /// <summary>The option with the server host name</summary>
    public const string HostOption = "postgresql-host";
    /// <summary>The option with the server port</summary>
    public const string PortOption = "postgresql-port";
    /// <summary>The option with the name of the database to back up</summary>
    public const string DatabaseOption = "postgresql-database";
    /// <summary>The option with the role to connect as</summary>
    public const string UsernameOption = "postgresql-username";
    /// <summary>The option with the password of the role</summary>
    public const string PasswordOption = "postgresql-password";
    /// <summary>The option with the TLS mode of the connection</summary>
    public const string SslModeOption = "postgresql-sslmode";
    /// <summary>The option with the path to the root certificate that the server certificate is verified against</summary>
    public const string SslRootCertOption = "postgresql-sslrootcert";
    /// <summary>
    /// The option with the root certificate itself, as PEM text, for where there is no file to point to
    /// </summary>
    public const string SslRootCertContentOption = "postgresql-sslrootcert-content";
    /// <summary>What a certificate in PEM text starts with</summary>
    private const string PemCertificateHeader = "-----BEGIN CERTIFICATE-----";
    /// <summary>The option that includes large objects in the backup</summary>
    public const string IncludeLargeObjectsOption = "postgresql-include-large-objects";
    /// <summary>
    /// The option with the largest database a backup may cover. A backup of a larger database
    /// fails before anything is read, so the size a backup may grow to can be capped.
    /// </summary>
    public const string MaxDatabaseSizeOption = "postgresql-max-database-size";
    /// <summary>The option with the folder that holds the PostgreSQL client programs</summary>
    public const string ClientPathOption = "postgresql-client-path";

    /// <summary>
    /// The option that says the source is listed to be shown, not backed up. The tables are then read
    /// from the catalog of the database and no dump is started.
    /// </summary>
    public const string EnumerationModeOption = "enumeration-mode";
    /// <summary>
    /// The option a backup hands its filter to the source in, as the filter expressions with their
    /// plus or minus prefix, joined by the path separator. The rows of the tables it leaves out
    /// are not dumped, so they are never read from the server.
    /// </summary>
    public const string FilterOption = "filter";

    /// <summary>
    /// The name of the backup option with the block size. The row segments are padded to this size,
    /// so the provider must use the value the backup itself uses.
    /// </summary>
    public const string BlockSizeOption = "blocksize";
    /// <summary>
    /// The block size a backup uses when the option is not set
    /// </summary>
    public const string DefaultBlockSize = "1mb";

    /// <summary>The port used when none is given</summary>
    public const int DefaultPort = 5432;
    /// <summary>The TLS mode used when none is given</summary>
    public const string DefaultSslMode = "require";
    /// <summary>The TLS modes libpq accepts</summary>
    public static readonly string[] SslModes = ["disable", "allow", "prefer", DefaultSslMode, "verify-ca", "verify-full"];

    /// <summary>The server host name</summary>
    public required string Host { get; init; }
    /// <summary>The server port</summary>
    public required int Port { get; init; }
    /// <summary>The database to back up</summary>
    public required string Database { get; init; }
    /// <summary>The role to connect as</summary>
    public required string Username { get; init; }
    /// <summary>The password of the role, or null to connect without one</summary>
    public required string? Password { get; init; }
    /// <summary>The TLS mode</summary>
    public required string SslMode { get; init; }
    /// <summary>
    /// The root certificate file, or null. When the certificate was given as text, this is the
    /// temporary file that holds it.
    /// </summary>
    public required string? SslRootCert { get; init; }

    /// <summary>The temporary file with a root certificate that was given as text, or null</summary>
    private Library.Utility.TempFile? m_rootCertFile;
    /// <summary>True when large objects are part of the backup</summary>
    public required bool IncludeLargeObjects { get; init; }
    /// <summary>True when the source is listed to be shown, and no dump is started</summary>
    public required bool EnumerationMode { get; init; }
    /// <summary>The filter of the backup, or null when it has none</summary>
    public required IFilter? Filter { get; init; }
    /// <summary>The largest database a backup may cover, in bytes, or null when there is no limit</summary>
    public required long? MaxDatabaseSize { get; init; }
    /// <summary>The folder with the client programs, or null to look in the standard places</summary>
    public required string? ClientPath { get; init; }
    /// <summary>The block size of the backup, in bytes</summary>
    public required int BlockSize { get; init; }

    /// <summary>
    /// The command line arguments the provider supports
    /// </summary>
    public static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(HostOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLSourceProvider.HostShort, Strings.PostgreSQLSourceProvider.HostLong),
        new CommandLineArgument(PortOption, CommandLineArgument.ArgumentType.Integer, Strings.PostgreSQLSourceProvider.PortShort, Strings.PostgreSQLSourceProvider.PortLong, DefaultPort.ToString()),
        new CommandLineArgument(DatabaseOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLSourceProvider.DatabaseShort, Strings.PostgreSQLSourceProvider.DatabaseLong),
        new CommandLineArgument(UsernameOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLSourceProvider.UsernameShort, Strings.PostgreSQLSourceProvider.UsernameLong),
        new CommandLineArgument(PasswordOption, CommandLineArgument.ArgumentType.Password, Strings.PostgreSQLSourceProvider.PasswordShort, Strings.PostgreSQLSourceProvider.PasswordLong),
        new CommandLineArgument(SslModeOption, CommandLineArgument.ArgumentType.Enumeration, Strings.PostgreSQLSourceProvider.SslModeShort, Strings.PostgreSQLSourceProvider.SslModeLong, DefaultSslMode, [], SslModes),
        new CommandLineArgument(SslRootCertOption, CommandLineArgument.ArgumentType.Path, Strings.PostgreSQLSourceProvider.SslRootCertShort, Strings.PostgreSQLSourceProvider.SslRootCertLong),
        new CommandLineArgument(SslRootCertContentOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLSourceProvider.SslRootCertContentShort, Strings.PostgreSQLSourceProvider.SslRootCertContentLong),
        new CommandLineArgument(IncludeLargeObjectsOption, CommandLineArgument.ArgumentType.Boolean, Strings.PostgreSQLSourceProvider.IncludeLargeObjectsShort, Strings.PostgreSQLSourceProvider.IncludeLargeObjectsLong, "false"),
        new CommandLineArgument(MaxDatabaseSizeOption, CommandLineArgument.ArgumentType.Size, Strings.PostgreSQLSourceProvider.MaxDatabaseSizeShort, Strings.PostgreSQLSourceProvider.MaxDatabaseSizeLong),
        new CommandLineArgument(ClientPathOption, CommandLineArgument.ArgumentType.Path, Strings.PostgreSQLSourceProvider.ClientPathShort, Strings.PostgreSQLSourceProvider.ClientPathLong),
    ];

    /// <summary>
    /// Reads the options. The loader of the provider has already added the options in the URL,
    /// where a value in the URL wins over the same option given to the operation.
    /// </summary>
    /// <param name="options">The options, with those of the URL</param>
    /// <returns>The parsed options</returns>
    public static PostgreSQLOptions Parse(IReadOnlyDictionary<string, string?> options)
    {
        string? Get(string name)
            => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        string Required(string name)
            => Get(name) ?? throw new UserInformationException(Strings.PostgreSQLSourceProvider.MissingOption(name), PostgreSQLEventIds.MissingOption);

        var port = DefaultPort;
        var portText = Get(PortOption);
        if (portText != null && (!int.TryParse(portText, out port) || port <= 0 || port > 65535))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(PortOption, portText), PostgreSQLEventIds.InvalidOption);

        var sslMode = Get(SslModeOption) ?? DefaultSslMode;
        if (!SslModes.Contains(sslMode, StringComparer.Ordinal))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(SslModeOption, sslMode), PostgreSQLEventIds.InvalidOption);

        var blockSize = Sizeparser.ParseSize(Get(BlockSizeOption) ?? DefaultBlockSize, "kb");
        if (blockSize <= 0 || blockSize > int.MaxValue)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(BlockSizeOption, blockSize.ToString()), PostgreSQLEventIds.InvalidOption);

        long? maxDatabaseSize = null;
        var maxDatabaseSizeText = Get(MaxDatabaseSizeOption);
        if (maxDatabaseSizeText != null)
        {
            try
            {
                maxDatabaseSize = Sizeparser.ParseSize(maxDatabaseSizeText, "mb");
            }
            catch (Exception ex)
            {
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(MaxDatabaseSizeOption, maxDatabaseSizeText), PostgreSQLEventIds.InvalidOption, ex);
            }

            if (maxDatabaseSize <= 0)
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(MaxDatabaseSizeOption, maxDatabaseSizeText), PostgreSQLEventIds.InvalidOption);
        }

        var rootCert = Get(SslRootCertOption);
        var rootCertContent = Get(SslRootCertContentOption);
        if (rootCert != null && rootCertContent != null)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.RootCertGivenTwice(SslRootCertOption, SslRootCertContentOption), PostgreSQLEventIds.InvalidOption);
        if (rootCertContent != null && !rootCertContent.Contains(PemCertificateHeader, StringComparison.Ordinal))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.RootCertNotPem(SslRootCertContentOption), PostgreSQLEventIds.InvalidOption);

        // The rest is read first, so no file is left behind when an option is refused
        var host = Required(HostOption);
        var database = Required(DatabaseOption);
        var username = Required(UsernameOption);

        Library.Utility.TempFile? rootCertFile = null;
        if (rootCertContent != null)
        {
            rootCertFile = new Library.Utility.TempFile();
            try
            {
                File.WriteAllText(rootCertFile, rootCertContent.Trim() + "\n");
            }
            catch
            {
                rootCertFile.Dispose();
                throw;
            }
        }

        return new PostgreSQLOptions
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = Get(PasswordOption),
            SslMode = sslMode,
            SslRootCert = rootCertFile != null ? (string)rootCertFile : rootCert,
            IncludeLargeObjects = Library.Utility.Utility.ParseBool(Get(IncludeLargeObjectsOption), false),
            MaxDatabaseSize = maxDatabaseSize,
            EnumerationMode = Library.Utility.Utility.ParseBool(Get(EnumerationModeOption), false),
            Filter = ParseFilter(Get(FilterOption)),
            ClientPath = Get(ClientPathOption),
            BlockSize = (int)blockSize,
            m_rootCertFile = rootCertFile,
        };
    }

    /// <summary>
    /// Reads the filter of a backup. The expressions tell names apart by their casing, as the
    /// provider asks the backup to do, so a table is only taken to be left out when the backup
    /// leaves out exactly that table.
    /// </summary>
    /// <param name="filter">The filter expressions, joined by the path separator, or null</param>
    /// <returns>The filter, or null when there is none</returns>
    public static IFilter? ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        IFilter? result = null;
        foreach (var expression in filter.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var include = expression.StartsWith('+');
            if (!include && !expression.StartsWith('-'))
                continue;

            result = FilterExpression.Combine(result, new FilterExpression(expression.Substring(1), include, true));
        }

        return result;
    }

    /// <summary>
    /// Removes the temporary file of a root certificate that was given as text
    /// </summary>
    public void Dispose()
    {
        m_rootCertFile?.Dispose();
        m_rootCertFile = null;
    }
}
