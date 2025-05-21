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


using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend
{
    public class SSHv2 : IStreamingBackend, IRenameEnabledBackend, IFolderEnabledBackend
    {
        private const string SSH_KEYFILE_OPTION = "ssh-keyfile";
        private const string SSH_KEYFILE_INLINE = "ssh-key";
        private const string SSH_FINGERPRINT_OPTION = "ssh-fingerprint";
        private const string SSH_RELATIVE_PATH_OPTION = "ssh-relative-path";
        private const string SSH_FINGERPRINT_ACCEPT_ANY_OPTION = "ssh-accept-any-fingerprints";
        public const string KEYFILE_URI = "sshkey://";
        private const string SSH_TIMEOUT_OPTION = "ssh-operation-timeout";
        private const string SSH_KEEPALIVE_OPTION = "ssh-keepalive";

        readonly Dictionary<string, string?> m_options;

        private readonly string m_server;
        private readonly string m_path;
        private readonly string m_username;
        private readonly string? m_password;
        private readonly string? m_fingerprint;
        private readonly bool m_fingerprintallowall;
        private readonly TimeSpan m_operationtimeout;
        private readonly TimeSpan m_keepaliveinterval;

        private readonly int m_port = 22;
        private readonly bool m_useRelativePath;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;
        private string? m_initialDirectory;

        private SftpClient? m_con;

        public SSHv2()
        {
            m_server = null!;
            m_path = null!;
            m_username = null!;
            m_options = null!;
            m_timeouts = null!;
            m_con = null!;
        }

        public SSHv2(string url, Dictionary<string, string?> options)
        {
            m_options = options;
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            var auth = AuthOptionsHelper.Parse(options, uri);
            if (!auth.HasUsername)
                throw new UserInformationException(Strings.SSHv2Backend.UsernameRequired, "UsernameNotSpecified");

            m_username = auth.Username!;
            m_password = auth.Password;
            m_fingerprint = options.GetValueOrDefault(SSH_FINGERPRINT_OPTION);
            m_fingerprintallowall = Utility.Utility.ParseBoolOption(options, SSH_FINGERPRINT_ACCEPT_ANY_OPTION);
            m_useRelativePath = Utility.Utility.ParseBoolOption(options, SSH_RELATIVE_PATH_OPTION);
            m_timeouts = TimeoutOptionsHelper.Parse(options);

            m_path = uri.Path;

            if (!string.IsNullOrWhiteSpace(m_path))
                m_path = Util.AppendDirSeparator(m_path, "/");

            if (m_useRelativePath)
            {
                m_path = m_path.TrimStart('/');
            }
            else
            {
                if (!m_path.StartsWith("/", StringComparison.Ordinal))
                    m_path = "/" + m_path;
            }

            m_server = uri.Host;

            if (uri.Port > 0)
                m_port = uri.Port;

            m_keepaliveinterval = Utility.Utility.ParseTimespanOption(options, SSH_KEEPALIVE_OPTION, "0s");
            m_operationtimeout = Utility.Utility.ParseTimespanOption(options, SSH_TIMEOUT_OPTION, "0s");
        }

        #region IBackend Members

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var con = await CreateConnection(cancelToken).ConfigureAwait(false);

            // Since the SftpClient.CreateDirectory method does not create all the parent directories
            // as needed, this has to be done manually.
            var partialPath = string.Empty;
            foreach (string part in m_path.Split('/').Where(x => !String.IsNullOrEmpty(x)))
            {
                partialPath += $"/{part}";
                if (await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.ExistsAsync(partialPath, ct)).ConfigureAwait(false))
                {
                    if (!await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.GetAttributes(partialPath).IsDirectory).ConfigureAwait(false))
                    {
                        throw new ArgumentException($"The path {partialPath} already exists and is not a directory.");
                    }
                }
                else
                {
                    await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.CreateDirectoryAsync(partialPath, ct)).ConfigureAwait(false);
                }
            }

        }

        public string DisplayName => Strings.SSHv2Backend.DisplayName;

        public string ProtocolKey => "ssh";

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.Open(filename, FileMode.Open,
                FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.Open(filename, FileMode.Create,
                FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var con = await CreateConnection(cancelToken).ConfigureAwait(false);
                await SetWorkingDirectory(con, cancelToken).ConfigureAwait(false);
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.DeleteFileAsync(remotename, ct)).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public IList<ICommandLineArgument> SupportedCommands => [
            .. AuthOptionsHelper.GetOptions(),
            new CommandLineArgument(SSH_FINGERPRINT_OPTION, CommandLineArgument.ArgumentType.String,
                Strings.SSHv2Backend.DescriptionFingerprintShort,
                Strings.SSHv2Backend.DescriptionFingerprintLong),
            new CommandLineArgument(SSH_FINGERPRINT_ACCEPT_ANY_OPTION, CommandLineArgument.ArgumentType.Boolean,
                Strings.SSHv2Backend.DescriptionAnyFingerprintShort,
                Strings.SSHv2Backend.DescriptionAnyFingerprintLong),
            new CommandLineArgument(SSH_KEYFILE_OPTION, CommandLineArgument.ArgumentType.Path,
                Strings.SSHv2Backend.DescriptionSshkeyfileShort,
                Strings.SSHv2Backend.DescriptionSshkeyfileLong),
            new CommandLineArgument(SSH_KEYFILE_INLINE, CommandLineArgument.ArgumentType.Password,
                Strings.SSHv2Backend.DescriptionSshkeyShort,
                Strings.SSHv2Backend.DescriptionSshkeyLong(KEYFILE_URI)),
            new CommandLineArgument(SSH_TIMEOUT_OPTION, CommandLineArgument.ArgumentType.Timespan,
                Strings.SSHv2Backend.DescriptionSshtimeoutShort, Strings.SSHv2Backend.DescriptionSshtimeoutLong,
                "0", null,null, Strings.SSHv2Backend.TimeoutDeprecated(SSH_TIMEOUT_OPTION, TimeoutOptionsHelper.ShortTimeoutOption, TimeoutOptionsHelper.ListTimeoutOption, TimeoutOptionsHelper.ReadWriteTimeoutOption)),
            new CommandLineArgument(SSH_KEEPALIVE_OPTION, CommandLineArgument.ArgumentType.Timespan,
                Strings.SSHv2Backend.DescriptionSshkeepaliveShort,
                Strings.SSHv2Backend.DescriptionSshkeepaliveLong, "0"),
            new CommandLineArgument(SSH_RELATIVE_PATH_OPTION, CommandLineArgument.ArgumentType.Boolean,
                Strings.SSHv2Backend.DescriptionRelativePathShort,
                Strings.SSHv2Backend.DescriptionRelativePathLong),
            .. TimeoutOptionsHelper.GetOptions(),
        ];

        public string Description => Strings.SSHv2Backend.Description;

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_con != null)
            {
                try
                {
                    m_con.Dispose();
                }
                catch (System.Net.Sockets.SocketException)
                {
                    //If the operating system sometimes close socket before disposal of connection following exception is thrown
                    //System.Net.Sockets.SocketException (0x80004005): An existing connection was forcibly closed by the remote host 
                }
                finally
                {
                    m_con = null;
                }
            }
        }

        #endregion

        #region IStreamingBackend Implementation

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var con = await CreateConnection(cancelToken).ConfigureAwait(false);
            await SetWorkingDirectory(con, cancelToken).ConfigureAwait(false);
            try
            {
                using var ts = stream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
                await Task.Factory.FromAsync(
                    (cb, state) => con.BeginUploadFile(ts, remotename, cb, state, _ => cancelToken.ThrowIfCancellationRequested()),
                    con.EndUploadFile,
                    null);
            }
            catch (SftpPathNotFoundException ex)
            {
                throw new FolderMissingException(ex);
            }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var con = await CreateConnection(cancelToken).ConfigureAwait(false);
            await SetWorkingDirectory(con, cancelToken).ConfigureAwait(false);

            try
            {
                using var ts = stream.ObserveWriteTimeout(m_timeouts.ReadWriteTimeout, false);
                await Task.Factory.FromAsync(
                    (cb, state) => con.BeginDownloadFile(remotename, ts, cb, state, _ => cancelToken.ThrowIfCancellationRequested()),
                    con.EndDownloadFile,
                    null).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        #endregion

        #region IRenameEnabledBackend Implementation

        public async Task RenameAsync(string source, string target, CancellationToken cancelToken)
        {
            var con = await CreateConnection(cancelToken).ConfigureAwait(false);
            await SetWorkingDirectory(con, cancelToken).ConfigureAwait(false);
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.RenameFileAsync(source, target, ct).ConfigureAwait(false));
        }

        #endregion

        #region Implementation

        internal async Task<SftpClient> CreateConnection(CancellationToken cancelToken)
        {
            if (m_con != null && m_con.IsConnected)
                return m_con;

            if (m_con != null && !m_con.IsConnected)
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_con.ConnectAsync(ct)).ConfigureAwait(false);
                return m_con;
            }

            SftpClient con;

            m_options.TryGetValue(SSH_KEYFILE_OPTION, out var keyFile);
            if (string.IsNullOrWhiteSpace(keyFile))
                m_options.TryGetValue(SSH_KEYFILE_INLINE, out keyFile);

            if (!string.IsNullOrWhiteSpace(keyFile))
                con = new SftpClient(m_server, m_port, m_username, ValidateKeyFile(keyFile, m_password));
            else
                con = new SftpClient(m_server, m_port, m_username, m_password ?? string.Empty);

            con.HostKeyReceived += (sender, e) =>
            {
                e.CanTrust = false;

                if (m_fingerprintallowall)
                {
                    e.CanTrust = true;
                    return;
                }

                var hostFingerprint =
                    $"{e.HostKeyName} {e.KeyLength} {BitConverter.ToString(e.FingerPrint).Replace('-', ':')}";

                if (string.IsNullOrEmpty(m_fingerprint))
                    throw new HostKeyException(
                        Strings.SSHv2Backend.FingerprintNotSpecifiedManagedError(
                            hostFingerprint.ToLower(CultureInfo.InvariantCulture), SSH_FINGERPRINT_OPTION,
                            SSH_FINGERPRINT_ACCEPT_ANY_OPTION),
                        hostFingerprint, m_fingerprint);

                if (!string.Equals(hostFingerprint, m_fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new HostKeyException(
                        Strings.SSHv2Backend.FingerprintNotMatchManagedError(
                            hostFingerprint.ToLower(CultureInfo.InvariantCulture)),
                        hostFingerprint, m_fingerprint);

                e.CanTrust = true;
            };

            if (m_operationtimeout.TotalSeconds > 0)
                con.OperationTimeout = m_operationtimeout;
            if (m_keepaliveinterval.TotalSeconds > 0)
                con.KeepAliveInterval = m_keepaliveinterval;

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => con.ConnectAsync(ct)).ConfigureAwait(false);
            if (m_useRelativePath && (string.IsNullOrWhiteSpace(con.WorkingDirectory) || !con.WorkingDirectory.StartsWith("/")))
                throw new UserInformationException("Server does not report absolute initial directory, please switch to absolute paths", "RelativePathNotSupported");
            m_initialDirectory = con.WorkingDirectory;

            return m_con = con;
        }

        private async Task SetWorkingDirectory(SftpClient connection, CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(m_path))
                return;

            var targetPath = m_useRelativePath
                ? Util.AppendDirSeparator(m_initialDirectory ?? "", "/") + m_path
                : m_path;

            var workingDir = Util.AppendDirSeparator(connection.WorkingDirectory, "/");
            if (workingDir == targetPath)
                return;

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => connection.ChangeDirectoryAsync(targetPath, ct)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new FolderMissingException(
                    Strings.SSHv2Backend.FolderNotFoundManagedError(m_path, ex.Message), ex);
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var path = ".";

            var con = await CreateConnection(cancelToken).ConfigureAwait(false);
            await SetWorkingDirectory(con, cancelToken).ConfigureAwait(false);

            await foreach (var ls in await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct => con.ListDirectoryAsync(path, ct)).ConfigureAwait(false))
            {
                if (ls.Name.ToString() == "." || ls.Name.ToString() == "..") continue;
                yield return new FileEntry(ls.Name, ls.Length,
                    ls.LastAccessTime, ls.LastWriteTime)
                { IsFolder = ls.Attributes.IsDirectory };
            }
        }

        private static PrivateKeyFile ValidateKeyFile(string filename, string? password)
        {
            try
            {
                if (!filename.StartsWith(KEYFILE_URI, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrEmpty(password)
                        ? new PrivateKeyFile(filename)
                        : new PrivateKeyFile(filename, password);
                }

                using (var ms = new MemoryStream())
                using (var sr = new StreamWriter(ms))
                {
                    sr.Write(Utility.Uri.UrlDecode(filename.Substring(KEYFILE_URI.Length)));
                    sr.Flush();

                    ms.Position = 0;

                    return string.IsNullOrEmpty(password) ? new PrivateKeyFile(ms) : new PrivateKeyFile(ms, password);
                }
            }
            catch (Exception ex)
            {
                throw new UserInformationException(
                    "Failed to parse the keyfile, check the key format and passphrase. " +
                    $"Error message was {ex.Message}",
                    "SSHFailedToParseKeyFile", ex);
            }
        }

        #endregion

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { m_server });

        /// <inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var con = await CreateConnection(cancellationToken).ConfigureAwait(false);
            await SetWorkingDirectory(con, cancellationToken).ConfigureAwait(false);

            var prefixPath = m_useRelativePath ? "" : m_path;
            if (!string.IsNullOrWhiteSpace(prefixPath))
                prefixPath = Util.AppendDirSeparator(prefixPath, "/");

            var filterPath = prefixPath + BackendSourceFileEntry.NormalizePathTo(path, '/');
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            if (string.IsNullOrWhiteSpace(filterPath))
                filterPath = ".";

            await foreach (var x in await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancellationToken, ct => con.ListDirectoryAsync(filterPath, ct)).ConfigureAwait(false))
                yield return new FileEntry(
                    x.Name,
                    x.Attributes.Size,
                    x.Attributes.LastAccessTimeUtc,
                    x.Attributes.LastWriteTimeUtc)
                {
                    IsFolder = x.Attributes.IsDirectory
                };
        }

        /// <inheritdoc/>
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(null);
    }
}