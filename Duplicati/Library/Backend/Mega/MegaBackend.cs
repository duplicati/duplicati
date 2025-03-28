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

using CG.Web.MegaApiClient;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using OtpNet;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend.Mega
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class MegaBackend : IBackend, IStreamingBackend
    {
        private readonly string m_username;
        private readonly string m_password;
        private readonly string? m_twoFactorKey;
        private Dictionary<string, List<INode>>? m_filecache;
        private INode? m_currentFolder = null;
        private readonly string m_prefix;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        private MegaApiClient? m_client;

        public MegaBackend()
        {
            m_username = null!;
            m_password = null!;
            m_twoFactorKey = null;
            m_prefix = null!;
            m_timeouts = null!;
        }

        private async Task<MegaApiClient> GetClient(CancellationToken cancelToken)
        {
            if (m_client == null)
                m_client = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
                {
                    var cl = new MegaApiClient();
                    if (m_twoFactorKey == null)
                        await cl.LoginAsync(m_username, m_password).ConfigureAwait(false);
                    else
                    {
                        var totp = new Totp(Base32Encoding.ToBytes(m_twoFactorKey)).ComputeTotp();
                        await cl.LoginAsync(m_username, m_password, totp).ConfigureAwait(false);
                    }
                    return cl;
                }).ConfigureAwait(false);

            return m_client;
        }

        public MegaBackend(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);

            var auth = AuthOptionsHelper.Parse(options, uri);
            if (options.ContainsKey("auth-two-factor-key"))
                m_twoFactorKey = options["auth-two-factor-key"];

            if (string.IsNullOrWhiteSpace(auth.Username))
                throw new UserInformationException(Strings.MegaBackend.NoUsernameError, "MegaNoUsername");
            if (string.IsNullOrWhiteSpace(auth.Password))
                throw new UserInformationException(Strings.MegaBackend.NoPasswordError, "MegaNoPassword");

            (m_username, m_password) = auth.GetCredentials();
            m_prefix = uri.HostAndPath ?? "";
            m_timeouts = TimeoutOptionsHelper.Parse(options);
        }

        private async Task<INode> FetchCurrentFolderAsync(bool autocreate, CancellationToken cancelToken)
        {
            var client = await GetClient(cancelToken).ConfigureAwait(false);
            var parts = m_prefix.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => client.GetNodes()).ConfigureAwait(false);
            INode parent = nodes.First(x => x.Type == NodeType.Root);

            foreach (var n in parts)
            {
                var item = nodes.FirstOrDefault(x => x.Name == n && x.Type == NodeType.Directory && x.ParentId == parent.Id);
                if (item == null)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    item = await client.CreateFolderAsync(n, parent).ConfigureAwait(false);
                }

                parent = item;
            }

            m_currentFolder = parent;

            await ResetFileCacheAsync(nodes, cancelToken).ConfigureAwait(false);

            return m_currentFolder;
        }

        private async Task<INode> GetCurrentFolderAsync(CancellationToken cancelToken)
        {
            var folder = m_currentFolder;
            if (folder == null)
                folder = await FetchCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);

            return folder;
        }

        private async Task<INode> GetFileNodeAsync(string name, CancellationToken cancelToken)
        {
            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name].OrderByDescending(x => x.ModificationDate).First();

            await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name].OrderByDescending(x => x.ModificationDate).First();

            throw new FileMissingException();
        }

        private async Task<Dictionary<string, List<INode>>> ResetFileCacheAsync(IEnumerable<INode>? list, CancellationToken cancelToken)
        {
            if (m_currentFolder == null)
            {
                await FetchCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);
                // We build it as part of the fetch step
                return m_filecache!;
            }
            else
            {
                var client = await GetClient(cancelToken).ConfigureAwait(false);

                var currentFolder = await GetCurrentFolderAsync(cancelToken).ConfigureAwait(false);
                if (list == null)
                    list = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => client.GetNodes()).ConfigureAwait(false);
                return m_filecache =
                    list.Where(x => x.Type == NodeType.File && x.ParentId == currentFolder.Id)
                        .GroupBy(x => x.Name, x => x, (k, g) => new KeyValuePair<string, List<INode>>(k, g.ToList()))
                        .ToDictionary(x => x.Key, x => x.Value);
            }
        }

        #region IStreamingBackend implementation

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var filecache = m_filecache;
                if (filecache == null)
                    filecache = await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

                var client = await GetClient(cancelToken).ConfigureAwait(false);
                var currentFolder = await GetCurrentFolderAsync(cancelToken).ConfigureAwait(false);
                using var ts = stream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
                var el = await client.UploadAsync(ts, remotename, currentFolder, new Progress(), null, cancelToken).ConfigureAwait(false);
                if (filecache.ContainsKey(remotename))
                    await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);

                filecache[remotename] = [el];
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var client = await GetClient(cancelToken).ConfigureAwait(false);
            var node = await GetFileNodeAsync(remotename, cancelToken).ConfigureAwait(false);
            using (var s = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => client.Download(node)).ConfigureAwait(false))
            using (var t = s.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                await Utility.Utility.CopyStreamAsync(t, stream, cancelToken).ConfigureAwait(false);
        }

        #endregion

        #region IBackend implementation

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var filecache = m_filecache;
            if (filecache == null)
                filecache = await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

            foreach (var n in filecache.Values)
            {
                var item = n.OrderByDescending(x => x.ModificationDate).First();
                yield return new FileEntry(item.Name, item.Size, item.ModificationDate ?? new DateTime(0), item.ModificationDate ?? new DateTime(0));
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var filecache = m_filecache;
                if (filecache == null || !filecache.ContainsKey(remotename))
                    filecache = await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

                if (!filecache.ContainsKey(remotename))
                    throw new FileMissingException();

                var client = await GetClient(cancelToken).ConfigureAwait(false);
                foreach (var n in filecache[remotename])
                    await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => client.DeleteAsync(n, false)).ConfigureAwait(false);

                filecache.Remove(remotename);
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return FetchCurrentFolderAsync(true, cancelToken);
        }

        public string DisplayName => Strings.MegaBackend.DisplayName;

        public string ProtocolKey => "mega";

        public IList<ICommandLineArgument> SupportedCommands => [
            .. AuthOptionsHelper.GetOptions(),
            new CommandLineArgument("auth-two-factor-key", CommandLineArgument.ArgumentType.Password, Strings.MegaBackend.AuthTwoFactorKeyDescriptionShort, Strings.MegaBackend.AuthTwoFactorKeyDescriptionLong),
            .. TimeoutOptionsHelper.GetOptions()
        ];

        public string Description => Strings.MegaBackend.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        private class Progress : IProgress<double>
        {
            public void Report(double value)
            {
                // No implementation as we have already wrapped the stream in our own progress reporting stream
            }
        }
    }
}
