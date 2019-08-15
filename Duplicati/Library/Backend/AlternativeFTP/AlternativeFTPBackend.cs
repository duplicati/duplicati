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

        private const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";
        private const string TEST_FILE_CONTENT = "This file used by Duplicati to test access permissions and could be safely deleted.";

        // ReSharper disable InconsistentNaming
        private static readonly string DEFAULT_DATA_CONNECTION_TYPE_STRING = DEFAULT_DATA_CONNECTION_TYPE.ToString();
        private static readonly string DEFAULT_ENCRYPTION_MODE_STRING = DEFAULT_ENCRYPTION_MODE.ToString();
        private static readonly string DEFAULT_SSL_PROTOCOLS_STRING = DEFAULT_SSL_PROTOCOLS.ToString();
        // ReSharper restore InconsistentNaming

        private readonly List<string> m_fileCache = new List<string>();
        private static readonly List<string> m_folderCache = new List<string>();

        private readonly string m_rootPath;

        private readonly string _url;
        private readonly bool _listVerify = true;
        private readonly FtpEncryptionMode _encryptionMode;
        private readonly FtpDataConnectionType _dataConnectionType;
        private readonly SslProtocols _sslProtocols;

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

            var rootUrl = new Uri(this._url);
            m_rootPath = "/" + (rootUrl.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ? rootUrl.AbsolutePath.Substring(0, rootUrl.AbsolutePath.Length - 1) : rootUrl.AbsolutePath);

            _listVerify = !CoreUtility.ParseBoolOption(options, "disable-upload-verify");

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

        public IEnumerable<IFileEntry> List(string remoteFolder)
        {
            List<IFileEntry> list;

            try
            {
                var ftpClient = CreateClient();

                var remoteFolderPath = string.IsNullOrEmpty(remoteFolder)
                    ? m_rootPath
                    : $"{m_rootPath}/{remoteFolder}";

                list = GetRemoteList(remoteFolderPath, ftpClient);

            } // Message "Directory not found." string
            catch (FtpCommandException ex)
            {
                if (ex.Message == "Directory not found.")
                {
                    throw new FolderMissingException(Strings.MissingFolderError(remoteFolder, ex.Message), ex);
                }

                throw;
            }

            return list.Where(x => x.IsFolder == false);
        }

        private List<IFileEntry> GetRemoteList(string remoteFolderPath, FtpClient ftpClient)
        {
            var items = new List<IFileEntry>();

            try
            {
                var relativePath = remoteFolderPath.Remove(0, m_rootPath.Length);

                while (relativePath.StartsWith("/"))
                {
                    relativePath = relativePath.Remove(0, 1);
                }

                foreach (FtpListItem item in ftpClient.GetListing(remoteFolderPath, FtpListOption.Modify | FtpListOption.Size | FtpListOption.DerefLinks))
                {
                    string itemFullPath = string.IsNullOrEmpty(remoteFolderPath)
                        ? item.Name
                        : $"{remoteFolderPath}/{item.Name}";

                    string itemRelativePath = string.IsNullOrEmpty(relativePath)
                        ? item.Name
                        : $"{relativePath}/{item.Name}";

                    switch (item.Type)
                    {
                        case FtpFileSystemObjectType.Directory:
                            {
                                if (item.Name == "." || item.Name == "..")
                                {
                                    continue;
                                }

                                items.Add(new FileEntry(itemRelativePath, -1, new DateTime(), item.Modified)
                                {
                                    IsFolder = true,
                                });

                                lock (m_folderCache)
                                {
                                    m_folderCache.Add(itemRelativePath);
                                }

                                items.AddRange(GetRemoteList(itemFullPath, ftpClient));

                                break;
                            }

                        case FtpFileSystemObjectType.File:
                            {
                                items.Add(new FileEntry(itemRelativePath,
                                    item.Size, new DateTime(), item.Modified));

                                lock (m_fileCache)
                                {
                                    m_fileCache.Add(itemRelativePath);
                                }

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

                                                items.Add(new FileEntry(itemRelativePath, -1, new DateTime(), item.Modified)
                                                {
                                                    IsFolder = true,
                                                });

                                                lock (m_folderCache)
                                                {
                                                    m_folderCache.Add(itemRelativePath);
                                                }

                                                items.AddRange(GetRemoteList(itemFullPath, ftpClient));

                                                break;
                                            }

                                        case FtpFileSystemObjectType.File:
                                            {
                                                items.Add(new FileEntry(itemRelativePath, item.Size, new DateTime(), item.Modified));

                                                lock (m_fileCache)
                                                {
                                                    m_fileCache.Add(itemRelativePath);
                                                }

                                                break;
                                            }

                                        case FtpFileSystemObjectType.Link:
                                            break;

                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }
                                }

                                break;
                            }

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            return items;
        }

        public FileEntry GetFileInfo(string remotename)
        {
            var ftpClient = CreateClient();

            var filename = SystemIO.IO_OS.PathGetFileName(remotename);
            var relativePath = remotename.Substring(0, remotename.Length - filename.Length);

            IEnumerable<FileEntry> items = from n in ftpClient.GetListing(relativePath, FtpListOption.Modify | FtpListOption.Size | FtpListOption.DerefLinks).Where(x => x.Name == remotename)
                                           select new FileEntry
                                           {
                                               Name = n.Name,
                                               Size = n.Size,
                                               IsFolder = n.Type == FtpFileSystemObjectType.Directory,
                                               LastAccess = n.Modified,
                                               LastModification = n.Modified
                                           };

            return items.First();
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            string remotePath = remotename;

            var path = SystemIO.IO_OS.PathGetDirectoryName(remotename);

            try
            {
                var ftpClient = CreateClient();

                long streamLen;

                try
                {
                    streamLen = input.Length;
                }
                catch (NotSupportedException)
                {
                    streamLen = -1;
                }

                CreateFolder(path);

                bool success = await ftpClient.UploadAsync(input, $"{m_rootPath}/{remotename}", FtpExists.Overwrite, createRemoteDir: true, token: cancelToken, progress: null).ConfigureAwait(false);

                if (!success)
                {
                    throw new UserInformationException(string.Format(Strings.ErrorWriteFile, remotename), "AftpPutFailure");
                }

                if (_listVerify)
                {
                    FileEntry remoteFileInfo = GetFileInfo(remotename);

                    if (remoteFileInfo.Size != streamLen)
                    {
                        throw new UserInformationException(Strings.ListVerifySizeFailure(remotename, remoteFileInfo.Size, streamLen), "AftpListVerifySizeFailure");
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

        public Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return PutAsync(remotename, fs, cancelToken);
            }
        }

        public void Get(string remotename, Stream output)
        {
            var ftpClient = CreateClient();

            if (string.IsNullOrEmpty(remotename))
            {
                return;
            }

            using (var inputStream = ftpClient.OpenRead(remotename))
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

            if (string.IsNullOrEmpty(remotename))
            {
                return;
            }

            var remotePath = $"{m_rootPath}/{remotename}";

            ftpClient.DeleteFile(remotePath);

            lock (m_fileCache)
            {
                m_fileCache.Remove(remotePath);
            }
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

        private static Stream StringToStream(string str)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream) { AutoFlush = true };
            writer.Write(str);
            return stream;
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
            using (var testStream = StringToStream(TEST_FILE_CONTENT))
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
            using (var stream = new MemoryStream())
            {
                try
                {
                    Get(TEST_FILE_NAME, stream);
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

        public void CreateFolder(string folderToCreate)
        {
            if (string.IsNullOrEmpty(folderToCreate))
            {
                return;
            }

            // Get the remote path
            var pathToCreate = $"{m_rootPath}/{folderToCreate}";

            lock (folderToCreate)
            {
                if (m_folderCache.Contains(pathToCreate))
                {
                    return;
                }

                var ftpClient = CreateClient();

                if (ftpClient.DirectoryExists(pathToCreate)) return;

                ftpClient.CreateDirectory(pathToCreate);

                m_folderCache.Add(pathToCreate);
            }
        }

        public void CreateFolder()
        {
            CreateFolder(m_rootPath);
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
                    EnableThreadSafeDataConnections = true, // Required to work properly but can result in up to 3 connections being used even when you expect just one..
                };

                ftpClient.ValidateCertificate += HandleValidateCertificate;

                this.Client = ftpClient;
            } // else reuse existing connection

            // Change working directory to the remote path
            // Do this every time to prevent issues when FtpClient silently reconnects after failure.
            var remotePath = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ? uri.AbsolutePath.Substring(0, uri.AbsolutePath.Length - 1) : uri.AbsolutePath;
            this.Client.SetWorkingDirectory(remotePath);

            return this.Client;
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
