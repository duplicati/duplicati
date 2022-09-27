#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using FluentFTP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using CoreUtility = Duplicati.Library.Utility.Utility;
using Uri = System.Uri;

namespace Duplicati.Library.Backend.AlternativeFTP
{
    // ReSharper disable once RedundantExtendsListEntry
    public class AlternativeFtpBackend : IBackend, IStreamingBackend
    {
        private System.Net.NetworkCredential _userInfo;
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash"; // Global option
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate"; // Global option

        private const FtpDataConnectionType DEFAULT_DATA_CONNECTION_TYPE = FtpDataConnectionType.AutoPassive;
        private const FtpEncryptionMode DEFAULT_ENCRYPTION_MODE = FtpEncryptionMode.None;
        private const SslProtocols DEFAULT_SSL_PROTOCOLS = SslProtocols.Default;
        private const string CONFIG_KEY_AFTP_ENCRYPTION_MODE = "aftp-encryption-mode";
        private const string CONFIG_KEY_AFTP_DATA_CONNECTION_TYPE = "aftp-data-connection-type";
        private const string CONFIG_KEY_AFTP_SSL_PROTOCOLS = "aftp-ssl-protocols";
        private const string CONFIG_KEY_AFTP_UPLOAD_DELAY = "aftp-upload-delay";

        private const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";
        private const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

        // ReSharper disable InconsistentNaming
        private static readonly string DEFAULT_DATA_CONNECTION_TYPE_STRING = DEFAULT_DATA_CONNECTION_TYPE.ToString();
        private static readonly string DEFAULT_ENCRYPTION_MODE_STRING = DEFAULT_ENCRYPTION_MODE.ToString();
        private static readonly string DEFAULT_SSL_PROTOCOLS_STRING = DEFAULT_SSL_PROTOCOLS.ToString();
        private static readonly string DEFAULT_UPLOAD_DELAY_STRING = "0s";
        // ReSharper restore InconsistentNaming

        private readonly string _url;
        private readonly bool _listVerify = true;
        private readonly FtpEncryptionMode _encryptionMode;
        private readonly FtpDataConnectionType _dataConnectionType;
        private readonly SslProtocols _sslProtocols;
        private readonly TimeSpan _uploadWaitTime;

        private readonly byte[] _copybuffer = new byte[CoreUtility.DEFAULT_BUFFER_SIZE];
        private readonly bool _accepAllCertificates;
        private readonly string[] _validHashes;

        /// <summary>
        /// The localized name to display for this backend
        /// </summary>
        public string DisplayName
        {
            get { return Strings.DisplayName; }
        }

        /// <summary>
        /// The protocol key, eg. ftp, http or ssh
        /// </summary>
        public string ProtocolKey
        {
            get { return "aftp"; }
        }

        private FtpClient Client
        { get; set; }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                          new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.DescriptionAuthPasswordShort, Strings.DescriptionAuthPasswordLong),
                          new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.DescriptionAuthUsernameShort, Strings.DescriptionAuthUsernameLong),
                          new CommandLineArgument("disable-upload-verify", CommandLineArgument.ArgumentType.Boolean, Strings.DescriptionDisableUploadVerifyShort, Strings.DescriptionDisableUploadVerifyLong),
                          new CommandLineArgument(CONFIG_KEY_AFTP_DATA_CONNECTION_TYPE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpDataConnectionTypeShort, Strings.DescriptionFtpDataConnectionTypeLong, DEFAULT_DATA_CONNECTION_TYPE_STRING, null, Enum.GetNames(typeof(FtpDataConnectionType))),
                          new CommandLineArgument(CONFIG_KEY_AFTP_ENCRYPTION_MODE, CommandLineArgument.ArgumentType.Enumeration, Strings.DescriptionFtpEncryptionModeShort, Strings.DescriptionFtpEncryptionModeLong, DEFAULT_ENCRYPTION_MODE_STRING, null, Enum.GetNames(typeof(FtpEncryptionMode))),
                          new CommandLineArgument(CONFIG_KEY_AFTP_SSL_PROTOCOLS, CommandLineArgument.ArgumentType.Flags, Strings.DescriptionSslProtocolsShort, Strings.DescriptionSslProtocolsLong, DEFAULT_SSL_PROTOCOLS_STRING, null, Enum.GetNames(typeof(SslProtocols))),
                          new CommandLineArgument(CONFIG_KEY_AFTP_UPLOAD_DELAY, CommandLineArgument.ArgumentType.Timespan, Strings.DescriptionUploadDelayShort, Strings.DescriptionUploadDelayLong, DEFAULT_UPLOAD_DELAY_STRING),
                     });
            }
        }

        /// <summary>
        /// Initialize a new instance.
        /// </summary>
        public AlternativeFtpBackend()
        {

        }

        /// <summary>
        /// Initialize a new instance/
        /// </summary>
        /// <param name="url">Configured url.</param>
        /// <param name="options">Configured options. cannot be null.</param>
        public AlternativeFtpBackend(string url, Dictionary<string, string> options)
        {
            _accepAllCertificates = CoreUtility.ParseBoolOption(options, OPTION_ACCEPT_ANY_CERTIFICATE);

            string certHash;
            options.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            _validHashes = certHash == null ? null : certHash.Split(new[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries);

            var u = new Utility.Uri(url);
            u.RequireHost();

            if (!string.IsNullOrEmpty(u.Username))
            {
                _userInfo = new System.Net.NetworkCredential();
                _userInfo.UserName = u.Username;
                if (!string.IsNullOrEmpty(u.Password))
                    _userInfo.Password = u.Password;
                else if (options.ContainsKey("auth-password"))
                    _userInfo.Password = options["auth-password"];
            }
            else
            {
                if (options.ContainsKey("auth-username"))
                {
                    _userInfo = new System.Net.NetworkCredential();
                    _userInfo.UserName = options["auth-username"];
                    if (options.ContainsKey("auth-password"))
                        _userInfo.Password = options["auth-password"];
                }
            }

            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (_userInfo != null)
                _userInfo.Domain = "";

            _url = u.SetScheme("ftp").SetQuery(null).SetCredentials(null, null).ToString();
            _url = Common.IO.Util.AppendDirSeparator(_url, "/");
            _listVerify = !CoreUtility.ParseBoolOption(options, "disable-upload-verify");
            if (options.TryGetValue(CONFIG_KEY_AFTP_UPLOAD_DELAY, out var uploadWaitTimeString) && !string.IsNullOrWhiteSpace(uploadWaitTimeString))
                _uploadWaitTime = Duplicati.Library.Utility.Timeparser.ParseTimeSpan(uploadWaitTimeString);

            // Process the aftp-data-connection-type option
            string dataConnectionTypeString;

            if (!options.TryGetValue(CONFIG_KEY_AFTP_DATA_CONNECTION_TYPE, out dataConnectionTypeString) || string.IsNullOrWhiteSpace(dataConnectionTypeString))
            {
                dataConnectionTypeString = null;
            }

            if (dataConnectionTypeString == null || !Enum.TryParse(dataConnectionTypeString, true, out _dataConnectionType))
            {
                _dataConnectionType = DEFAULT_DATA_CONNECTION_TYPE;
            }

            // Process the aftp-encryption-mode option
            string encryptionModeString;

            if (!options.TryGetValue(CONFIG_KEY_AFTP_ENCRYPTION_MODE, out encryptionModeString) || string.IsNullOrWhiteSpace(encryptionModeString))
            {
                encryptionModeString = null;
            }

            if (encryptionModeString == null || !Enum.TryParse(encryptionModeString, true, out _encryptionMode))
            {
                _encryptionMode = DEFAULT_ENCRYPTION_MODE;
            }

            // Process the aftp-ssl-protocols option
            string sslProtocolsString;

            if (!options.TryGetValue(CONFIG_KEY_AFTP_SSL_PROTOCOLS, out sslProtocolsString) || string.IsNullOrWhiteSpace(sslProtocolsString))
            {
                sslProtocolsString = null;
            }

            if (sslProtocolsString == null || !Enum.TryParse(sslProtocolsString, true, out _sslProtocols))
            {
                _sslProtocols = DEFAULT_SSL_PROTOCOLS;
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            return List("");
        }

        public IEnumerable<IFileEntry> List(string filename)
        {
            return List(filename, false);
        }

        private IEnumerable<IFileEntry> List(string filename, bool stripFile)
        {
            var list = new List<IFileEntry>();
            string remotePath = filename;

            try
            {
                var ftpClient = CreateClient();

                // Get the remote path
                var url = new Uri(this._url);
                remotePath = "/" + this.GetUnescapedAbsolutePath(url);

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

                foreach (FtpListItem item in ftpClient.GetListing(remotePath, FtpListOption.Modify | FtpListOption.Size | FtpListOption.DerefLinks))
                {
                    switch (item.Type)
                    {
                        case FtpFileSystemObjectType.Directory:
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
                        case FtpFileSystemObjectType.File:
                            {
                                list.Add(new FileEntry(item.Name, item.Size, new DateTime(), item.Modified));

                                break;
                            }
                        case FtpFileSystemObjectType.Link:
                            {
                                if (item.Name == "." || item.Name == "..")
                                {
                                    continue;
                                }

                                if (item.LinkObject != null)
                                {
                                    switch (item.LinkObject.Type)
                                    {
                                        case FtpFileSystemObjectType.Directory:
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
                                        case FtpFileSystemObjectType.File:
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
            }//         Message    "Directory not found."    string
            catch (FtpCommandException ex)
            {
                if (ex.Message == "Directory not found.")
                {
                    throw new FolderMissingException(Strings.MissingFolderError(remotePath, ex.Message), ex);
                }

                throw;
            }

            return list;
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            string remotePath = remotename;
            long streamLen;

            try
            {
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

                var success = await ftpClient.UploadAsync(input, remotePath, FtpExists.Overwrite, createRemoteDir: false, token: cancelToken, progress: null).ConfigureAwait(false);
                if (!success)
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
                    var remoteSize = await ftpClient.GetFileSizeAsync(remotePath, cancelToken);
                    if (streamLen != remoteSize)
                    {
                        throw new UserInformationException(Strings.ListVerifySizeFailure(remotename, remoteSize, streamLen), "AftpListVerifySizeFailure");
                    }
                }
            }
            catch (FtpCommandException ex)
            {
                if (ex.Message == "Directory not found.")
                {
                    throw new FolderMissingException(Strings.MissingFolderError(remotePath, ex.Message), ex);
                }

                throw;
            }
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await PutAsync(remotename, fs, cancelToken);
            }
        }

        public void Get(string remotename, Stream output)
        {
            var ftpClient = CreateClient();

            // Get the remote path
            var remotePath = "";

            if (!string.IsNullOrEmpty(remotename))
            {
                // Append the filename
                remotePath += remotename;
            }

            using (var inputStream = ftpClient.OpenRead(remotePath))
            {
                try
                {
                    CoreUtility.CopyStream(inputStream, output, false, _copybuffer);
                }
                finally
                {
                    inputStream.Close();
                }
            }

        }

        public void Get(string remotename, string localname)
        {
            using (FileStream fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Get(remotename, fs);
            }
        }

        public void Delete(string remotename)
        {
            var ftpClient = CreateClient();

            // Get the remote path
            var remotePath = "";

            if (!string.IsNullOrEmpty(remotename))
            {
                // Append the filename
                remotePath += remotename;
            }

            ftpClient.DeleteFile(remotePath);

        }

        /// <summary>
        /// A localized description of the backend, for display in the usage information
        /// </summary>
        public string Description
        {
            get
            {
                return Strings.Description;
            }
        }

        public string[] DNSName
        {
            get { return new string[] { new Uri(_url).Host }; }
        }

        /// <summary>
        /// Test FTP access permissions.
        /// </summary>
        public void Test()
        {
            var list = List();

            // Delete test file if exists
            if (list.Any(entry => entry.Name == TEST_FILE_NAME))
            {
                try
                {
                    Delete(TEST_FILE_NAME);
                }
                catch (Exception e)
                {
                    throw new Exception(string.Format(Strings.ErrorDeleteFile, e.Message), e);
                }
            }

            // Test write permissions
            using (var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TEST_FILE_CONTENT)))
            {
                try
                {
                    PutAsync(TEST_FILE_NAME, testStream, CancellationToken.None).Wait();
                }
                catch (Exception e)
                {
                    throw new Exception(string.Format(Strings.ErrorWriteFile, e.Message), e);
                }
            }

            // Test read permissions
            using (var testStream = new MemoryStream())
            {
                try
                {
                    Get(TEST_FILE_NAME, testStream);
                    var readValue = System.Text.Encoding.UTF8.GetString(testStream.ToArray());
                    if (readValue != TEST_FILE_CONTENT)
                        throw new Exception("Test file corrupted.");
                }
                catch (Exception e)
                {
                    throw new Exception(string.Format(Strings.ErrorReadFile, e.Message), e);
                }
            }

            // Cleanup
            try
            {
                Delete(TEST_FILE_NAME);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format(Strings.ErrorDeleteFile, e.Message), e);
            }
        }

        public void CreateFolder()
        {
            var client = CreateClient();

            var url = new Uri(_url);

            // Get the remote path
            var remotePath = this.GetUnescapedAbsolutePath(url);

            // Try to create the directory 
            client.CreateDirectory(remotePath, true);

        }

        public void Dispose()
        {
            if (Client != null)
                Client.Dispose();

            Client = null;
            _userInfo = null;
        }

        private FtpClient CreateClient()
        {
            var uri = new Uri(_url);

            if (this.Client == null) // Create connection if it doesn't exist yet
            {
                var ftpClient = new FtpClient
                {
                    Host = uri.Host,
                    Port = uri.Port == -1 ? 21 : uri.Port,
                    Credentials = _userInfo,
                    EncryptionMode = _encryptionMode,
                    DataConnectionType = _dataConnectionType,
                    SslProtocols = _sslProtocols,

                    // We do not support parallel uploads, and the feature is buggy
                    EnableThreadSafeDataConnections = false,
                };

                ftpClient.ValidateCertificate += HandleValidateCertificate;

                this.Client = ftpClient;
            } // else reuse existing connection

            // Change working directory to the remote path
            // Do this every time to prevent issues when FtpClient silently reconnects after failure.
            var remotePath = this.GetUnescapedAbsolutePath(uri);
            this.Client.SetWorkingDirectory(remotePath);

            return this.Client;
        }

        private string GetUnescapedAbsolutePath(Uri uri)
        {
            string absolutePath = Uri.UnescapeDataString(uri.AbsolutePath);
            return absolutePath.EndsWith("/", StringComparison.Ordinal) ? absolutePath.Substring(0, absolutePath.Length - 1) : absolutePath;
        }

        private void HandleValidateCertificate(FtpClient control, FtpSslValidationEventArgs e)
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
