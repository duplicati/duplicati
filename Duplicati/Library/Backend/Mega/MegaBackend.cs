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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtpNet;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend.Mega
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class MegaBackend : IBackend, IStreamingBackend
    {
        private readonly string m_username = null;
        private readonly string m_password = null;
        private readonly string m_twoFactorKey = null;
        private Dictionary<string, List<INode>> m_filecache;
        private INode m_currentFolder = null;
        private readonly string m_prefix = null;

        private MegaApiClient m_client;

        public MegaBackend()
        {
        }

        private MegaApiClient Client
        {
            get
            {
                if (m_client == null)
                {
                    var cl = new MegaApiClient();
                    if (m_twoFactorKey == null)
                        cl.Login(m_username, m_password);
                    else
                    {
                        var totp = new Totp(Base32Encoding.ToBytes(m_twoFactorKey)).ComputeTotp();
                        cl.Login(m_username, m_password, totp);
                    }
                    m_client = cl;
                }

                return m_client;
            }
        }

        public MegaBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];
            if (options.ContainsKey("auth-two-factor-key"))
                m_twoFactorKey = options["auth-two-factor-key"];

            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            if (string.IsNullOrEmpty(m_username))
                throw new UserInformationException(Strings.MegaBackend.NoUsernameError, "MegaNoUsername");
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.MegaBackend.NoPasswordError, "MegaNoPassword");

            m_prefix = uri.HostAndPath ?? "";
        }

        private async Task FetchCurrentFolderAsync(bool autocreate, CancellationToken cancelToken)
        {
            var parts = m_prefix.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = Client.GetNodes();
            INode parent = nodes.First(x => x.Type == NodeType.Root);

            foreach (var n in parts)
            {
                var item = nodes.FirstOrDefault(x => x.Name == n && x.Type == NodeType.Directory && x.ParentId == parent.Id);
                if (item == null)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    item = await Client.CreateFolderAsync(n, parent).ConfigureAwait(false);
                }

                parent = item;
            }

            m_currentFolder = parent;

            await ResetFileCacheAsync(nodes, cancelToken).ConfigureAwait(false);
        }

        private async Task<INode> GetCurrentFolderAsync(CancellationToken cancelToken)
        {
            if (m_currentFolder == null)
                await FetchCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);

            return m_currentFolder;
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

        private async Task ResetFileCacheAsync(IEnumerable<INode> list, CancellationToken cancelToken)
        {
            if (m_currentFolder == null)
            {
                await FetchCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);
            }
            else
            {
                var currentFolder = await GetCurrentFolderAsync(cancelToken).ConfigureAwait(false);
                m_filecache =
                    (list ?? Client.GetNodes()).Where(x => x.Type == NodeType.File && x.ParentId == currentFolder.Id)
                        .GroupBy(x => x.Name, x => x, (k, g) => new KeyValuePair<string, List<INode>>(k, g.ToList()))
                        .ToDictionary(x => x.Key, x => x.Value);
            }
        }

        #region IStreamingBackend implementation

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                if (m_filecache == null)
                    await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

                var currentFolder = await GetCurrentFolderAsync(cancelToken).ConfigureAwait(false);
                var el = await Client.UploadAsync(stream, remotename, currentFolder, new Progress(), null, cancelToken).ConfigureAwait(false);
                if (m_filecache.ContainsKey(remotename))
                    await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);

                m_filecache[remotename] = [el];
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var node = await GetFileNodeAsync(remotename, cancelToken).ConfigureAwait(false);
            using (var s = Client.Download(node))
                await Library.Utility.Utility.CopyStreamAsync(s, stream, cancelToken).ConfigureAwait(false);
        }

        #endregion

        #region IBackend implementation

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            if (m_filecache == null)
                await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

            foreach (var n in m_filecache.Values)
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
                if (m_filecache == null || !m_filecache.ContainsKey(remotename))
                    await ResetFileCacheAsync(null, cancelToken).ConfigureAwait(false);

                if (!m_filecache.ContainsKey(remotename))
                    throw new FileMissingException();

                foreach (var n in m_filecache[remotename])
                    await Client.DeleteAsync(n, false).ConfigureAwait(false);

                m_filecache.Remove(remotename);
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

        public string DisplayName
        {
            get
            {
                return Strings.MegaBackend.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                return "mega";
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.MegaBackend.AuthPasswordDescriptionShort, Strings.MegaBackend.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.MegaBackend.AuthUsernameDescriptionShort, Strings.MegaBackend.AuthUsernameDescriptionLong),
                    new CommandLineArgument("auth-two-factor-key", CommandLineArgument.ArgumentType.Password, Strings.MegaBackend.AuthTwoFactorKeyDescriptionShort, Strings.MegaBackend.AuthTwoFactorKeyDescriptionLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.MegaBackend.Description;
            }
        }

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
