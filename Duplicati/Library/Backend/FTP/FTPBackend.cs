// Copyright (C) 2024, The Duplicati Team
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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
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
        private NetworkCredential _userInfo;
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash"; // Global option
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate"; // Global option

        private const FtpDataConnectionType DEFAULT_DATA_CONNECTION_TYPE = FtpDataConnectionType.AutoPassive;
        private const FtpEncryptionMode DEFAULT_ENCRYPTION_MODE = FtpEncryptionMode.None;
        private static readonly SslProtocols DEFAULT_SSL_PROTOCOLS = SslProtocols.None; // NOTE: None means "use system default"

        protected virtual string CONFIG_KEY_FTP_ENCRYPTION_MODE => "ftp-encryption-mode";
        protected virtual string CONFIG_KEY_FTP_DATA_CONNECTION_TYPE => "ftp-data-connection-type";
        protected virtual string CONFIG_KEY_FTP_SSL_PROTOCOLS => "ftp-ssl-protocols";
        protected virtual string CONFIG_KEY_FTP_UPLOAD_DELAY => "ftp-upload-delay";
        protected virtual string CONFIG_KEY_FTP_LOGTOCONSOLE => "ftp-log-to-console";
        protected virtual string CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE => "ftp-log-privateinfo-to-console";
        
        // The following keys are private because they are irrelevant for inheritors and are here for backwards compatibility
        private static string CONFIG_KEY_FTP_LEGACY_FTPPASSIVE => "ftp-passive";
        private static string CONFIG_KEY_FTP_LEGACY_FTPREGULAR => "ftp-regular";
        private static string CONFIG_KEY_FTP_LEGACY_USESSL => "use-ssl";

        private const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";
        private const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

        protected static readonly string DEFAULT_DATA_CONNECTION_TYPE_STRING = DEFAULT_DATA_CONNECTION_TYPE.ToString();
        protected static readonly string DEFAULT_ENCRYPTION_MODE_STRING = DEFAULT_ENCRYPTION_MODE.ToString();
        protected static readonly string DEFAULT_SSL_PROTOCOLS_STRING = DEFAULT_SSL_PROTOCOLS.ToString();
        protected static readonly string DEFAULT_UPLOAD_DELAY_STRING = "0s";

        private readonly string _url;
        private readonly bool _listVerify = true;
        private readonly FtpConfig _ftpConfig;
        private readonly TimeSpan _uploadWaitTime;

        private readonly bool _logToConsole;
        private readonly bool _logPrivateInfoToConsole;
        private readonly bool _accepAllCertificates;
        private readonly string[] _validHashes;

        /// <summary>
        /// The localized name to display for this backend
        /// </summary>
        public virtual string DisplayName => Strings.DisplayName;

        /// <summary>
        /// The protocol key, e.g. ftp, http or ssh
        /// </summary>
        public virtual string ProtocolKey => "ftp";

        private AsyncFtpClient Client
        { get; set; }

        public virtual IList<ICommandLineArgument> SupportedCommands =>
            new List<ICommandLineArgument>([
                new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.DescriptionAuthPasswordShort, Strings.DescriptionAuthPasswordLong),
                new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.DescriptionAuthUsernameShort, Strings.DescriptionAuthUsernameLong),
                new CommandLineArgument("disable-upload-verify", CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionDisableUploadVerifyShort, Strings.DescriptionDisableUploadVerifyLong),
                new CommandLineArgument(CONFIG_KEY_FTP_DATA_CONNECTION_TYPE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpDataConnectionTypeShort, Strings.DescriptionFtpDataConnectionTypeLong, DEFAULT_DATA_CONNECTION_TYPE_STRING, null, Enum.GetNames(typeof(FtpDataConnectionType))),
                new CommandLineArgument(CONFIG_KEY_FTP_ENCRYPTION_MODE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpEncryptionModeShort, Strings.DescriptionFtpEncryptionModeLong, DEFAULT_ENCRYPTION_MODE_STRING, null, Enum.GetNames(typeof(FtpEncryptionMode))),
                new CommandLineArgument(CONFIG_KEY_FTP_SSL_PROTOCOLS, CommandLineArgument.ArgumentType.Flags, Strings.DescriptionSslProtocolsShort, Strings.DescriptionSslProtocolsLong, DEFAULT_SSL_PROTOCOLS_STRING, null, Enum.GetNames(typeof(SslProtocols))),
                new CommandLineArgument(CONFIG_KEY_FTP_UPLOAD_DELAY, CommandLineArgument.ArgumentType.Timespan, Strings.DescriptionUploadDelayShort, Strings.DescriptionUploadDelayLong, DEFAULT_UPLOAD_DELAY_STRING),
                new CommandLineArgument(CONFIG_KEY_FTP_LOGTOCONSOLE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionLogToConsoleShort, Strings.DescriptionLogToConsoleLong),
                new CommandLineArgument(CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionLogPrivateInfoToConsoleShort, Strings.DescriptionLogPrivateInfoToConsoleLong, "false"),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_FTPPASSIVE, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionFTPPassiveShort, Strings.DescriptionFTPPassiveLong, "false", null, null, Strings.FtpPassiveDeprecated),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_FTPREGULAR, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionFTPActiveShort, Strings.DescriptionFTPActiveLong, "true", null, null, Strings.FtpActiveDeprecated),
                new CommandLineArgument(CONFIG_KEY_FTP_LEGACY_USESSL, CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionUseSSLShort, Strings.DescriptionUseSSLLong, "false", null, null, Strings.UseSslDeprecated),
            ]);

        /// <summary>
        /// Initialize a new instance.
        /// </summary>
        public FTP()
        {

        }

        /// <summary>
        /// Initialize a new instance/
        /// </summary>
        /// <param name="url">Configured url.</param>
        /// <param name="options">Configured options. cannot be null.</param>
        [SuppressMessage("ReSharper", "VirtualMemberCallInConstructor")] // The behavior of accessing the virtual properties is as expected
        public FTP(string url, Dictionary<string, string> options)
        {
            _accepAllCertificates = CoreUtility.ParseBoolOption(options, OPTION_ACCEPT_ANY_CERTIFICATE);

            string certHash;
            options.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            _validHashes = certHash?.Split([",", ";"], StringSplitOptions.RemoveEmptyEntries);

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

            _url = u.SetScheme("ftp").SetQuery(null).SetCredentials(null, null).ToString();
            _url = Util.AppendDirSeparator(_url, "/");
            _listVerify = !CoreUtility.ParseBoolOption(options, "disable-upload-verify");
            if (options.TryGetValue(CONFIG_KEY_FTP_UPLOAD_DELAY, out var uploadWaitTimeString) && !string.IsNullOrWhiteSpace(uploadWaitTimeString))
                _uploadWaitTime = Timeparser.ParseTimeSpan(uploadWaitTimeString);

            // Process the aftp-data-connection-type option
            string dataConnectionTypeString;
            FtpDataConnectionType dataConnectionType;

            if (!options.TryGetValue(CONFIG_KEY_FTP_DATA_CONNECTION_TYPE, out dataConnectionTypeString) || string.IsNullOrWhiteSpace(dataConnectionTypeString))
                dataConnectionTypeString = null;

            if (dataConnectionTypeString == null || !Enum.TryParse(dataConnectionTypeString, true, out dataConnectionType))
                dataConnectionType = DEFAULT_DATA_CONNECTION_TYPE;

            // Process the aftp-encryption-mode option
            FtpEncryptionMode encryptionMode;
            if (!options.TryGetValue(CONFIG_KEY_FTP_ENCRYPTION_MODE, out var encryptionModeString) || string.IsNullOrWhiteSpace(encryptionModeString))
                encryptionModeString = null;

            if (encryptionModeString == null || !Enum.TryParse(encryptionModeString, true, out encryptionMode))
                encryptionMode = DEFAULT_ENCRYPTION_MODE;
            
            // Process the aftp-ssl-protocols option
            SslProtocols sslProtocols;
            if (!options.TryGetValue(CONFIG_KEY_FTP_SSL_PROTOCOLS, out var sslProtocolsString) || string.IsNullOrWhiteSpace(sslProtocolsString))
                sslProtocolsString = null;

            if (sslProtocolsString == null || !Enum.TryParse(sslProtocolsString, true, out sslProtocols)) sslProtocols = DEFAULT_SSL_PROTOCOLS;

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
                    encryptionMode = FtpEncryptionMode.Auto;
                }
            }
            
            _logToConsole = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LOGTOCONSOLE);
            _logPrivateInfoToConsole = CoreUtility.ParseBoolOption(options, CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE);
            
            _ftpConfig = new FtpConfig
            {
                DataConnectionType = dataConnectionType,
                EncryptionMode = encryptionMode,
                SslProtocols = sslProtocols,
                LogToConsole = _logToConsole,
            };

            if (_logPrivateInfoToConsole) _ftpConfig.LogHost = _ftpConfig.LogPassword = _ftpConfig.LogUserName = true;
        }

        public IEnumerable<IFileEntry> List()
        {
            return List(string.Empty);
        }

        public IEnumerable<IFileEntry> List(string filename)
        {
            return List(filename, false);
        }

        private IEnumerable<IFileEntry> List(string filename, bool stripFile)
        {
            var list = new List<IFileEntry>();
            string remotePath = filename;

            var ftpClient = CreateClient();

            // Get the remote path
            var url = new Uri(_url);
            remotePath = "/" + GetUnescapedAbsolutePath(url);

            if (!string.IsNullOrEmpty(filename))
            {
                if (!stripFile)
                {
                    // Append the filename
                    remotePath += filename;
                }
                else if (filename.Contains("/"))
                {
                    remotePath += filename.Substring(0, filename.LastIndexOf("/", StringComparison.Ordinal));
                }
                // else: stripping the filename in this case ignoring it
            }

            foreach (FtpListItem item in ftpClient.GetListing(remotePath, FtpListOption.Modify | FtpListOption.Size).Await())
            {
                switch (item.Type)
                {
                    case FtpObjectType.Directory:
                        {
                            if (item.Name == "." || item.Name == "..")
                            {
                                continue;
                            }

                            list.Add(new FileEntry(item.Name, -1, new DateTime(), item.Modified)
                            {
                                IsFolder = true,
                            });

                            break;
                        }
                    case FtpObjectType.File:
                        {
                            list.Add(new FileEntry(item.Name, item.Size, new DateTime(), item.Modified));

                            break;
                        }
                    case FtpObjectType.Link:
                        {
                            if (item.Name == "." || item.Name == "..")
                            {
                                continue;
                            }

                            if (item.LinkObject != null)
                            {
                                switch (item.LinkObject.Type)
                                {
                                    case FtpObjectType.Directory:
                                        {
                                            if (item.Name == "." || item.Name == "..")
                                            {
                                                continue;
                                            }

                                            list.Add(new FileEntry(item.Name, -1, new DateTime(), item.Modified)
                                            {
                                                IsFolder = true,
                                            });

                                            break;
                                        }
                                    case FtpObjectType.File:
                                        {
                                            list.Add(new FileEntry(item.Name, item.Size, new DateTime(), item.Modified));

                                            break;
                                        }
                                }
                            }
                            break;
                        }

                }
            }

            return list;
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            string remotePath = remotename;
            long streamLen;

            var ftpClient = CreateClient();

            try
            {
                streamLen = input.Length;
            }
            catch (NotSupportedException) { streamLen = -1; }

            // Get the remote path
            remotePath = "";

            if (!string.IsNullOrEmpty(remotename))
            {
                // Append the filename
                remotePath += remotename;
            }

            var status = await ftpClient.UploadStream(input, remotePath, createRemoteDir: false, token: cancelToken, progress: null).ConfigureAwait(false);
            if (status != FtpStatus.Success)
            {
                throw new UserInformationException(string.Format(Strings.ErrorWriteFile, remotename), "AftpPutFailure");
            }

            // Wait for the upload, if required
            if (_uploadWaitTime.Ticks > 0)
            {
                Thread.Sleep(_uploadWaitTime);
            }

            if (_listVerify)
            {
                // check remote file size; matching file size indicates completion
                var remoteSize = await ftpClient.GetFileSize(remotePath, -1, cancelToken);
                if (streamLen != remoteSize)
                {
                    throw new UserInformationException(Strings.ListVerifySizeFailure(remotename, remoteSize, streamLen), "AftpListVerifySizeFailure");
                }
            }
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read);
            await PutAsync(remotename, fs, cancelToken);
        }

        public async Task GetAsync(string remotename, Stream output, CancellationToken cancelToken)
        {
            var ftpClient = CreateClient();

            // Get the remote path
            var remotePath = "";

            if (!string.IsNullOrEmpty(remotename))
            {
                // Append the filename
                remotePath += remotename;
            }

            await using var inputStream = await ftpClient.OpenRead(remotePath, token: cancelToken);
            try
            {
                await CoreUtility.CopyStreamAsync(inputStream, output, false, cancelToken).ConfigureAwait(false);
            }
            finally
            {
                inputStream.Close();
            }
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using FileStream fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None);
            await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            var ftpClient = CreateClient();

            // Get the remote path
            var remotePath = "";

            if (!string.IsNullOrEmpty(remotename))
            {
                // Append the filename
                remotePath += remotename;
            }

            await ftpClient.DeleteFile(remotePath, cancelToken);

        }

        /// <summary>
        /// A localized description of the backend, for display in the usage information
        /// </summary>
        public virtual string Description => Strings.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { new Uri(_url).Host });

        /// <summary>
        /// Test FTP access permissions.
        /// </summary>
        public async Task TestAsync(CancellationToken cancellationToken)
        {
            var list = List();

            // Delete test file if exists
            if (list.Any(entry => entry.Name == TEST_FILE_NAME))
            {
                try
                {
                    await DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (e.InnerException != null) { e = e.InnerException; }
                    throw new Exception(string.Format(Strings.ErrorDeleteFile, e.Message), e);
                }
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
                    if (e.InnerException != null) { e = e.InnerException; }
                    throw new Exception(string.Format(Strings.ErrorWriteFile, e.Message), e);
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
                    if (e.InnerException != null) { e = e.InnerException; }
                    throw new Exception(string.Format(Strings.ErrorReadFile, e.Message), e);
                }
            }

            // Cleanup
            try
            {
                await DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (e.InnerException != null) { e = e.InnerException; }
                throw new Exception(string.Format(Strings.ErrorDeleteFile, e.Message), e);
            }
        }

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            var client = CreateClient(false);

            var url = new Uri(_url);

            // Get the remote path
            var remotePath = GetUnescapedAbsolutePath(url);

            // Try to create the directory 
            return client.CreateDirectory(remotePath, true, cancellationToken);
        }

        public void Dispose()
        {
            if (Client != null)
                Client.Dispose();

            Client = null;
            _userInfo = null;
        }

        private AsyncFtpClient CreateClient(bool setWorkingDirectory = true)
        {
            var uri = new Uri(_url);

            if (Client == null) // Create connection if it doesn't exist yet
            {
                var ftpClient = new AsyncFtpClient
                {
                    Host = uri.Host,
                    Port = uri.Port == -1 ? 21 : uri.Port,
                    Credentials = _userInfo,
                    Config = _ftpConfig,
                };

                ftpClient.ValidateCertificate += HandleValidateCertificate;

                Client = ftpClient;

            } // else reuse existing connection

            if (setWorkingDirectory)
            {
                // Change working directory to the remote path
                // Do this every time to prevent issues when FtpClient silently reconnects after failure.
                var remotePath = GetUnescapedAbsolutePath(uri);
                try
                {
                    Client.SetWorkingDirectory(remotePath).Await();
                }
                catch (FtpCommandException ex)
                {
                    if (ex.CompletionCode == "550")
                    {
                        throw new FolderMissingException(Strings.MissingFolderError(remotePath, ex.Message), ex);
                    }

                    throw;
                }
            }

            return Client;
        }

        private string GetUnescapedAbsolutePath(Uri uri)
        {
            string absolutePath = Uri.UnescapeDataString(uri.AbsolutePath);
            return absolutePath.EndsWith("/", StringComparison.Ordinal) ? absolutePath.Substring(0, absolutePath.Length - 1) : absolutePath;
        }

        private void HandleValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
        {
            if (e.PolicyErrors == SslPolicyErrors.None || _accepAllCertificates)
            {
                e.Accept = true;
                return;
            }

            try
            {
                var certHash = (_validHashes != null && _validHashes.Length > 0) ? CoreUtility.ByteArrayAsHexString(e.Certificate.GetCertHash()) : null;
                if (certHash != null)
                {
                    if (_validHashes.Any(hash => !string.IsNullOrEmpty(hash) && certHash.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        e.Accept = true;
                    }
                }
            }
            catch
            {
                e.Accept = false;
            }
        }
    }
}
