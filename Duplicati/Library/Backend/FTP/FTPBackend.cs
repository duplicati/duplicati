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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using FluentFTP;
using FluentFTP.Client.BaseClient;
using FluentFTP.Exceptions;
using CoreUtility = Duplicati.Library.Utility.Utility;
using Uri = System.Uri;

namespace Duplicati.Library.Backend
{

    /// <summary>
    /// The unified FTP backend which uses the FluentFTP library.
    ///
    /// In previous versions, this was being exposed as AlternateFTPBackend, whist the FTP backend was
    /// using the System.Net.FtpWebRequest class which is now deprecated.
    ///
    /// To provide a transparent upgrade path, the AlternateFTPBackend now inherits from this class,
    /// but overides the default configuration values to match the old names(prefixed with a) and the backedn
    /// name being "aftp" rather than ftp.
    /// </summary>
    public class FTP : IStreamingBackend
    {
        /// <summary>
        /// The credentials used to authenticate with the FTP server
        /// </summary>
        private readonly NetworkCredential? _userInfo;
        /// <summary>
        /// Th option used to accept a specific SSL certificate hash
        /// </summary>
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash"; // Global option
        /// <summary>
        /// The option used to accept any SSL certificate
        /// </summary>
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate"; // Global option

        /// <summary>
        /// The default data connection type
        /// </summary>
        private const FtpDataConnectionType DEFAULT_DATA_CONNECTION_TYPE = FtpDataConnectionType.AutoPassive;
        /// <summary>
        /// The default encryption mode
        /// </summary>
        private const FtpEncryptionMode DEFAULT_ENCRYPTION_MODE = FtpEncryptionMode.None;
        /// <summary>
        /// The default SSL protocols
        /// </summary>
        private static readonly SslProtocols DEFAULT_SSL_PROTOCOLS = SslProtocols.None; // NOTE: None means "use system default"        

        /// <summary>
        /// The configuration key for the FTP encryption mode
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_ENCRYPTION_MODE => "ftp-encryption-mode";
        /// <summary>
        /// The configuration key for the FTP data connection type
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_DATA_CONNECTION_TYPE => "ftp-data-connection-type";
        /// <summary>
        /// The configuration key for the FTP SSL protocols
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_SSL_PROTOCOLS => "ftp-ssl-protocols";
        /// <summary>
        /// The configuration key for the FTP upload delay
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_UPLOAD_DELAY => "ftp-upload-delay";
        /// <summary>
        /// The configuration key for the FTP log to console
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_LOGTOCONSOLE => "ftp-log-to-console";
        /// <summary>
        /// The configuration key for the FTP log private info to console
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE => "ftp-log-privateinfo-to-console";
        /// <summary>
        /// The configuration key for the FTP log to console
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_LOGDIAGNOSTICS => "ftp-log-diagnostics";
        /// <summary>
        /// The configuration key for the FTP absolute paths option
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_ABSOLUTE_PATH => "ftp-absolute-path";
        /// <summary>
        /// The configuration key for the FTP relative path option
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_RELATIVE_PATH => "ftp-relative-path";
        /// <summary>
        /// The configuration key for the FTP use CWD names option
        /// </summary>
        protected virtual string CONFIG_KEY_FTP_USE_CWD_NAMES => "ftp-use-cwd-names";

        /// <summary>
        /// The configuration key for the disable upload verify option
        /// </summary>
        protected virtual string CONFIG_KEY_DISABLE_UPLOAD_VERIFY => "disable-upload-verify";

        // The following keys are private because they are irrelevant for inheritors and are here for backwards compatibility
        /// <summary>
        /// The configuration key for the legacy FTP passive mode
        /// </summary>
        private static string CONFIG_KEY_FTP_LEGACY_FTPPASSIVE => "ftp-passive";
        /// <summary>
        /// The configuration key for the legacy FTP active mode
        /// </summary>
        private static string CONFIG_KEY_FTP_LEGACY_FTPREGULAR => "ftp-regular";
        /// <summary>
        /// The configuration key for the legacy use SSL option
        /// </summary>
        private static string CONFIG_KEY_FTP_LEGACY_USESSL => "use-ssl";

        /// <summary>
        /// The test file name used to test access permissions
        /// </summary>
        private const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";
        /// <summary>
        /// The test file content used to test access permissions
        /// </summary>
        private const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

        /// <summary>
        /// The default data connection type as a string
        /// </summary>
        protected static readonly string DEFAULT_DATA_CONNECTION_TYPE_STRING = DEFAULT_DATA_CONNECTION_TYPE.ToString();
        /// <summary>
        /// The default encryption mode as a string
        /// </summary>
        protected static readonly string DEFAULT_ENCRYPTION_MODE_STRING = DEFAULT_ENCRYPTION_MODE.ToString();
        /// <summary>
        /// The default SSL protocols as a string
        /// </summary>
        protected static readonly string DEFAULT_SSL_PROTOCOLS_STRING = DEFAULT_SSL_PROTOCOLS.ToString();
        /// <summary>
        /// The default upload delay as a string
        /// </summary>
        protected static readonly string DEFAULT_UPLOAD_DELAY_STRING = "0s";

        /// <summary>
        /// The URL of the FTP server
        /// </summary>
        private readonly Uri _url;
        /// <summary>
        /// The flag to indicate if the list verify option is enabled
        /// </summary>
        private readonly bool _listVerify = true;
        /// <summary>
        /// The flag to indicate if relative paths are used
        /// </summary>
        private readonly bool _relativePaths = true;
        /// <summary>
        /// The flag to indicate if the CWD strategy is used
        /// </summary>
        private readonly bool _useCwdNames = false;
        /// <summary>
        /// The FTP configuration
        /// </summary>
        private readonly FtpConfig _ftpConfig;
        /// <summary>
        /// The wait time after each upload before checking the file size
        /// </summary>
        private readonly TimeSpan _uploadWaitTime;

        /// <summary>
        /// The flag to indicate if the dialog should be logged to the console
        /// </summary>
        private readonly bool _logToConsole;
        /// <summary>
        /// The flag to indicate if private information should be logged to the console
        /// </summary>
        private readonly bool _logPrivateInfoToConsole;
        /// <summary>
        /// The flag to indicate if diagnostics information should be logged
        /// </summary>
        private readonly bool _diagnosticsLog;
        /// <summary>
        /// The flag to indicate if all certificates should be accepted
        /// </summary>
        private readonly bool _accepAllCertificates;
        /// <summary>
        /// The valid certificate hashes
        /// </summary>
        private readonly string[] _validHashes;

        /// <summary>
        /// The localized name to display for this backend
        /// </summary>
        public virtual string DisplayName => Strings.DisplayName;

        /// <summary>
        /// The protocol key, e.g. ftp, http or ssh
        /// </summary>
        public virtual string ProtocolKey => "ftp";

        /// <summary>
        /// The client instance
        /// </summary>
        private AsyncFtpClient? _client;

        /// <summary>
        /// The server initial working directory
        /// </summary>
        private string? _initialCwd;

        /// <inheritdoc />
        public virtual IList<ICommandLineArgument> SupportedCommands =>
            new List<ICommandLineArgument>([
                new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.DescriptionAuthPasswordShort, Strings.DescriptionAuthPasswordLong),
                new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.DescriptionAuthUsernameShort, Strings.DescriptionAuthUsernameLong),
                new CommandLineArgument(CONFIG_KEY_DISABLE_UPLOAD_VERIFY, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionDisableUploadVerifyShort, Strings.DescriptionDisableUploadVerifyLong),
                new CommandLineArgument(CONFIG_KEY_FTP_ABSOLUTE_PATH, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionAbsolutePathShort, Strings.DescriptionAbsolutePathLong),
                new CommandLineArgument(CONFIG_KEY_FTP_USE_CWD_NAMES, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionUseCwdNamesShort, Strings.DescriptionUseCwdNamesLong),
                new CommandLineArgument(CONFIG_KEY_FTP_DATA_CONNECTION_TYPE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpDataConnectionTypeShort, Strings.DescriptionFtpDataConnectionTypeLong, DEFAULT_DATA_CONNECTION_TYPE_STRING, null, Enum.GetNames(typeof(FtpDataConnectionType))),
                new CommandLineArgument(CONFIG_KEY_FTP_ENCRYPTION_MODE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpEncryptionModeShort, Strings.DescriptionFtpEncryptionModeLong, DEFAULT_ENCRYPTION_MODE_STRING, null, Enum.GetNames(typeof(FtpEncryptionMode))),
                new CommandLineArgument(CONFIG_KEY_FTP_SSL_PROTOCOLS, CommandLineArgument.ArgumentType.Flags, Strings.DescriptionSslProtocolsShort, Strings.DescriptionSslProtocolsLong, DEFAULT_SSL_PROTOCOLS_STRING, null, Enum.GetNames(typeof(SslProtocols))),
                new CommandLineArgument(CONFIG_KEY_FTP_UPLOAD_DELAY, CommandLineArgument.ArgumentType.Timespan, Strings.DescriptionUploadDelayShort, Strings.DescriptionUploadDelayLong, DEFAULT_UPLOAD_DELAY_STRING),
                new CommandLineArgument(CONFIG_KEY_FTP_LOGTOCONSOLE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionLogToConsoleShort, Strings.DescriptionLogToConsoleLong),
                new CommandLineArgument(CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionLogPrivateInfoToConsoleShort, Strings.DescriptionLogPrivateInfoToConsoleLong, "false"),
                new CommandLineArgument(CONFIG_KEY_FTP_LOGDIAGNOSTICS, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionLogDiagnosticsShort, Strings.DescriptionLogDiagnosticsLong),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_FTPPASSIVE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionFTPPassiveShort, Strings.DescriptionFTPPassiveLong, "false", null, null, Strings.FtpPassiveDeprecated),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_FTPREGULAR, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionFTPActiveShort, Strings.DescriptionFTPActiveLong, "true", null, null, Strings.FtpActiveDeprecated),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_USESSL, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionUseSSLShort, Strings.DescriptionUseSSLLong, "false", null, null, Strings.UseSslDeprecated),
            ]);

        /// <summary>
        /// Initialize a new instance.
        /// </summary>
        public FTP()
        {
            // TODO: Remove this constructor once static properties are introduced on IBackend
            _validHashes = null!;
            _ftpConfig = null!;
            _url = null!;
            _client = null!;
        }

        /// <summary>
        /// Initialize a new instance/
        /// </summary>
        /// <param name="url">Configured url.</param>
        /// <param name="options">Configured options. cannot be null.</param>
        [SuppressMessage("ReSharper", "VirtualMemberCallInConstructor")] // The behavior of accessing the virtual properties is as expected
        public FTP(string url, Dictionary<string, string?> options)
        {
            _accepAllCertificates = CoreUtility.ParseBoolOption(options, OPTION_ACCEPT_ANY_CERTIFICATE);

            options.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out var certHash);

            _validHashes = certHash?.Split([",", ";"], StringSplitOptions.RemoveEmptyEntries) ?? [];

            var u = new Utility.Uri(url);
            u.RequireHost();

            if (!string.IsNullOrEmpty(u.Username))
            {
                _userInfo = new NetworkCredential
                {
                    UserName = u.Username
                };
                if (!string.IsNullOrEmpty(u.Password))
                    _userInfo.Password = u.Password;
                else if (options.ContainsKey("auth-password"))
                    _userInfo.Password = options["auth-password"];
            }
            else
            {
                if (options.ContainsKey("auth-username"))
                {
                    _userInfo = new NetworkCredential();
                    _userInfo.UserName = options["auth-username"];
                    if (options.ContainsKey("auth-password"))
                        _userInfo.Password = options["auth-password"];
                }
            }

            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (_userInfo != null)
                _userInfo.Domain = "";

            var parsedurl = u.SetScheme("ftp").SetQuery(null).SetCredentials(null, null).ToString();
            parsedurl = Util.AppendDirSeparator(parsedurl, "/");
            _url = new Uri(parsedurl);

            _listVerify = !CoreUtility.ParseBoolOption(options, CONFIG_KEY_DISABLE_UPLOAD_VERIFY);
            _relativePaths = ProtocolKey == "ftp"
                ? !CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_ABSOLUTE_PATH)
                : CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_RELATIVE_PATH);

            _useCwdNames = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_USE_CWD_NAMES);
            if (options.TryGetValue(CONFIG_KEY_FTP_UPLOAD_DELAY, out var uploadWaitTimeString) && !string.IsNullOrWhiteSpace(uploadWaitTimeString))
                _uploadWaitTime = Timeparser.ParseTimeSpan(uploadWaitTimeString);

            var dataConnectionType = CoreUtility.ParseEnumOption(options, CONFIG_KEY_FTP_DATA_CONNECTION_TYPE, DEFAULT_DATA_CONNECTION_TYPE);
            var encryptionMode = CoreUtility.ParseEnumOption(options, CONFIG_KEY_FTP_ENCRYPTION_MODE, DEFAULT_ENCRYPTION_MODE);
            var sslProtocols = CoreUtility.ParseFlagsOption(options, CONFIG_KEY_FTP_SSL_PROTOCOLS, DEFAULT_SSL_PROTOCOLS);

            // Process options of the legacy FTP backend
            if (ProtocolKey == "ftp")
            {
                // To mirror the behavior of existing backups, we need to check the legacy options

                // This flag takes precedence over ftp-data-connection-type
                if (CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LEGACY_FTPPASSIVE))
                    dataConnectionType = FtpDataConnectionType.AutoPassive;

                // This flag takes precedence over the ftp-passive flag
                if (CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LEGACY_FTPREGULAR))
                    dataConnectionType = FtpDataConnectionType.AutoActive;

                // When using legacy useSSL option, the encryption is set to automatic and the SSL protocols are set to none
                // (None meaning the OS will choose the appropriate protocol)
                if (CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LEGACY_USESSL))
                {
                    sslProtocols = SslProtocols.None;
                    encryptionMode = FtpEncryptionMode.Explicit;
                }
            }

            _logToConsole = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LOGTOCONSOLE);
            _logPrivateInfoToConsole = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE);
            _diagnosticsLog = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LOGDIAGNOSTICS);

            _ftpConfig = new FtpConfig
            {
                DataConnectionType = dataConnectionType,
                EncryptionMode = encryptionMode,
                SslProtocols = sslProtocols,
                LogToConsole = _logToConsole,
                ValidateAnyCertificate = _accepAllCertificates,
                Noop = true
            };

            if (_logPrivateInfoToConsole) _ftpConfig.LogHost = _ftpConfig.LogPassword = _ftpConfig.LogUserName = true;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            FtpListItem[] items;
            try
            {
                var client = CreateClient(CancellationToken.None).Await();
                var remotePath = PreparePathForClient(null);
                items = await client.GetListing(remotePath, FtpListOption.Modify | FtpListOption.Size).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (TranslateException(null, ref e))
                    throw e;

                throw;
            }

            foreach (var item in items)
            {
                switch (item.Type)
                {
                    case FtpObjectType.Directory:
                        if (item.Name == "." || item.Name == "..")
                            continue;

                        yield return new FileEntry(item.Name, -1, new DateTime(), item.Modified)
                        {
                            IsFolder = true,
                        };
                        break;

                    case FtpObjectType.File:
                        yield return new FileEntry(item.Name, item.Size, new DateTime(), item.Modified);
                        break;

                    case FtpObjectType.Link:
                        {
                            if (item.Name == "." || item.Name == "..")
                                continue;

                            if (item.LinkObject != null)
                            {
                                switch (item.LinkObject.Type)
                                {
                                    case FtpObjectType.Directory:
                                        if (item.Name == "." || item.Name == "..")
                                            continue;

                                        yield return new FileEntry(item.Name, -1, new DateTime(), item.Modified)
                                        {
                                            IsFolder = true,
                                        };
                                        break;

                                    case FtpObjectType.File:
                                        yield return new FileEntry(item.Name, item.Size, new DateTime(), item.Modified);
                                        break;
                                }
                            }
                            break;
                        }

                }
            }
        }

        /// <inheritdoc />
        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            try
            {
                var streamLen = -1L;
                var client = await CreateClient(cancelToken).ConfigureAwait(false);
                var clientRemoteName = PreparePathForClient(remotename);

                try { streamLen = input.Length; }
                catch (NotSupportedException) { }

                var status = await client.UploadStream(input, clientRemoteName, createRemoteDir: false, token: cancelToken, progress: null).ConfigureAwait(false);
                if (status != FtpStatus.Success)
                    throw new UserInformationException(Strings.ErrorWriteFile(remotename, $"Status is {status}"), "FtpPutFailure");

                // Wait for the upload, if required
                if (_uploadWaitTime.Ticks > 0)
                    Thread.Sleep(_uploadWaitTime);

                if (_listVerify)
                {
                    // check remote file size; matching file size indicates completion
                    var remoteSize = await client.GetFileSize(clientRemoteName, -1, cancelToken);
                    if (streamLen != remoteSize)
                        throw new UserInformationException(Strings.ListVerifySizeFailure(remotename, remoteSize, streamLen), "FtpListVerifySizeFailure");
                }
            }
            catch (Exception e)
            {
                if (TranslateException(remotename, ref e))
                    throw e;

                throw;
            }
        }

        /// <inheritdoc />
        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using var fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read);
            await PutAsync(remotename, fs, cancelToken);
        }

        /// <inheritdoc />
        public async Task GetAsync(string remotename, Stream output, CancellationToken cancelToken)
        {
            try
            {
                var client = await CreateClient(cancelToken).ConfigureAwait(false);
                var clientRemoteName = PreparePathForClient(remotename);
                await using var inputStream = await client.OpenRead(clientRemoteName, token: cancelToken);
                await CoreUtility.CopyStreamAsync(inputStream, output, false, cancelToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (TranslateException(remotename, ref e))
                    throw e;

                throw;
            }
        }

        /// <inheritdoc />
        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using FileStream fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None);
            await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var client = await CreateClient(cancelToken).ConfigureAwait(false);
                var clientRemoteName = PreparePathForClient(remotename);
                await client.DeleteFile(clientRemoteName, cancelToken);
            }
            catch (Exception e)
            {
                if (TranslateException(remotename, ref e))
                    throw e;

                throw;
            }

        }

        /// <inheritdoc />
        public virtual string Description => Strings.Description;

        /// <inheritdoc />
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { _url.Host });

        /// <inheritdoc />
        public async Task TestAsync(CancellationToken cancellationToken)
        {
            // Try to set the working directory to trigger a folder-not-found exception
            try
            {
                var client = await CreateClient(cancellationToken).ConfigureAwait(false);
                if (!_useCwdNames)
                {
                    var folderpath = PreparePathForClient(null);
                    await client.SetWorkingDirectory(folderpath, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                if (TranslateException(null, ref e))
                    throw e;

                throw;
            }

            // Remove the file if it exists
            try
            {
                if (await ListAsync(cancellationToken).AnyAsync(entry => entry.Name == TEST_FILE_NAME).ConfigureAwait(false))
                    await DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (TranslateException(TEST_FILE_NAME, ref e))
                    throw e;

                throw new UserInformationException(Strings.ErrorDeleteFile(TEST_FILE_NAME, e.Message), "TestPreparationError");
            }

            // Test write permissions
            using (var testStream = new MemoryStream(Encoding.UTF8.GetBytes(TEST_FILE_CONTENT)))
            {
                try
                {
                    await PutAsync(TEST_FILE_NAME, testStream, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Do not pass the filename here because a not-found should be treated as folder-not-found
                    if (TranslateException(null, ref e))
                        throw e;

                    throw new UserInformationException(Strings.ErrorWriteFile(TEST_FILE_NAME, e.Message), "TestWriteError");
                }
            }

            // Test read permissions
            using (var testStream = new MemoryStream())
            {
                try
                {
                    await GetAsync(TEST_FILE_NAME, testStream, cancellationToken).ConfigureAwait(false);
                    var readValue = Encoding.UTF8.GetString(testStream.ToArray());
                    if (readValue != TEST_FILE_CONTENT)
                        throw new Exception("Test file corrupted.");
                }
                catch (Exception e)
                {
                    if (TranslateException(TEST_FILE_NAME, ref e))
                        throw e;

                    throw new UserInformationException(Strings.ErrorReadFile(TEST_FILE_NAME, e.Message), "TestReadError");
                }
            }

            // Cleanup
            try
            {
                await DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (TranslateException(TEST_FILE_NAME, ref e))
                    throw e;

                throw new UserInformationException(Strings.ErrorDeleteFile(TEST_FILE_NAME, e.Message), "TestCleanupError");
            }
        }

        /// <inheritdoc />
        public async Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Try to create the directory 
                var client = await CreateClient(cancellationToken, false).ConfigureAwait(false);
                var clientPath = PreparePathForClient(null, false);
                if (_useCwdNames && clientPath.Contains('/') && clientPath != "/")
                {
                    // Go to the parent folder and create the folder
                    var parentPath = clientPath.Substring(0, clientPath.LastIndexOf('/'));
                    var folderName = clientPath.Substring(clientPath.LastIndexOf('/') + 1);
                    await client.SetWorkingDirectory(parentPath, cancellationToken).ConfigureAwait(false);
                    await client.CreateDirectory(folderName, true, cancellationToken).ConfigureAwait(false);

                    // Reset the client and check that it works
                    _client = null;
                    client = await CreateClient(cancellationToken).ConfigureAwait(false);
                    var cwd = await client.GetWorkingDirectory(cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(cwd?.TrimEnd('/'), clientPath, StringComparison.OrdinalIgnoreCase))
                        throw new UserInformationException(Strings.ErrorCreateFolder(clientPath, cwd), "CreateFolderError");
                }
                else
                {
                    await client.CreateDirectory(clientPath, true, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                if (TranslateException(null, ref ex))
                    throw ex;
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _client?.Dispose();
        }

        /// <summary>
        /// Create a new FTP client, or return the existing one if it already exists.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="cwdFlag">A flag to override the CWD strategy.</param>
        /// <returns>The FTP client.</returns>
        private async Task<AsyncFtpClient> CreateClient(CancellationToken cancellationToken, bool? cwdFlag = null)
        {
            if (_client == null)
            {
                var client = new AsyncFtpClient
                {
                    Host = _url.Host,
                    Port = _url.Port == -1 ? 21 : _url.Port,
                    Credentials = _userInfo,
                    Config = _ftpConfig,
                    Logger = _diagnosticsLog ? new DiagnosticsLogger() : null
                };

                client.ValidateCertificate += HandleValidateCertificate;
                await client.Connect(cancellationToken).ConfigureAwait(false);

                // Set up for relative paths
                if (_relativePaths)
                {
                    _initialCwd = await client.GetWorkingDirectory(cancellationToken).ConfigureAwait(false);
                    _initialCwd = _initialCwd?.TrimEnd('/');
                }

                // Setup the initial working directory, if needed
                if (cwdFlag ?? _useCwdNames)
                {
                    var clientPath = PreparePathForClient(null, false, client);
                    await client.SetWorkingDirectory(clientPath, cancellationToken).ConfigureAwait(false);
                }

                _client = client;
            }
            return _client;
        }

        /// <summary>
        /// Handle the certificate validation event.
        /// </summary>
        /// <param name="control">The FTP client.</param>
        /// <param name="e">The event arguments.</param>
        private void HandleValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
        {
            if (e.PolicyErrors == SslPolicyErrors.None || _accepAllCertificates)
            {
                e.Accept = true;
                return;
            }

            e.Accept = false;
            try
            {
                var certHash = (_validHashes != null && _validHashes.Length > 0) ? e.Certificate?.GetCertHashString() : null;
                if (certHash != null && _validHashes != null && _validHashes.Any(hash => !string.IsNullOrEmpty(hash) && certHash.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                    e.Accept = true;
            }
            catch
            {
            }

            if (e.Accept == false && e.Certificate != null)
                throw new SslCertificateValidator.InvalidCertificateException(e.Certificate?.GetCertHashString(), e.PolicyErrors);
        }

        /// <summary>
        /// Prepare the path for the client.
        /// </summary>
        /// <param name="path">The path to prepare.</param>
        /// <param name="cwdFlag">A flag to override the CWD strategy.</param>
        /// <param name="client">The FTP client, if not using the class instance.</param>
        /// <returns>The prepared path.</returns>
        private string PreparePathForClient(string? path, bool? cwdFlag = null, AsyncFtpClient? client = null)
        {
            client = client ?? _client;
            if (cwdFlag ?? _useCwdNames)
                return string.IsNullOrWhiteSpace(path)
                    ? string.Empty
                    : Uri.UnescapeDataString(path);

            var remotePath = _url.AbsolutePath.TrimEnd('/');
            if (_relativePaths)
            {
                if (client == null)
                    throw new InvalidOperationException("Client not initialized");

                if (!string.IsNullOrWhiteSpace(_initialCwd))
                    remotePath = _initialCwd + "/" + remotePath.TrimStart('/');
            }

            if (string.IsNullOrEmpty(path))
                return Uri.UnescapeDataString(remotePath);

            if (path.StartsWith("/", StringComparison.Ordinal))
                return Uri.UnescapeDataString(path);

            return Uri.UnescapeDataString(remotePath + "/" + path);
        }

        /// <summary>
        /// Translate an exception to a more user-friendly exception.
        /// </summary>
        /// <param name="filename">The filename provided.</param>
        /// <param name="ex">The exception to translate.</param>
        /// <returns>True if the exception was translated, otherwise false.</returns>
        private bool TranslateException(string? filename, ref Exception ex)
        {
            if (ex.InnerException != null && (ex.InnerException is FtpCommandException || ex.InnerException is SslCertificateValidator.InvalidCertificateException))
                ex = ex.InnerException;

            if (ex is FtpCommandException ftpEx && (ftpEx.CompletionCode == "550" || ftpEx.CompletionCode == "450"))
            {
                ex = string.IsNullOrWhiteSpace(filename)
                    ? new FolderMissingException(Strings.MissingFolderError(_url.AbsolutePath, ftpEx.Message), ftpEx)
                    : new FileMissingException(Strings.FileMissingError(filename, ftpEx.Message), ftpEx);

                return true;
            }

            if (ex is SslCertificateValidator.InvalidCertificateException)
                return true;

            return false;
        }

        private sealed class DiagnosticsLogger : IFtpLogger
        {
            private static readonly string LOGTAG = Logging.Log.LogTagFromType<DiagnosticsLogger>();
            public void Log(FtpLogEntry entry)
            {
                var type = entry.Severity switch
                {
                    FtpTraceLevel.Verbose => Logging.LogMessageType.Verbose,
                    FtpTraceLevel.Info => Logging.LogMessageType.Information,
                    FtpTraceLevel.Warn => Logging.LogMessageType.Warning,
                    FtpTraceLevel.Error => Logging.LogMessageType.Error,
                    _ => Logging.LogMessageType.Information
                };
                Logging.Log.WriteMessage(type, LOGTAG, "FtpLogMessage", entry.Exception, entry.Message);
            }
        }
    }
}
