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

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A test backend that wraps the built-in <c>file</c> backend and reports folder
    /// entries with a trailing slash in their name, the way several real backends
    /// (S3, Google Drive, Box, OneDrive, Dropbox, SMB) do by convention. It lets the
    /// sync handler's handling of such folder names be exercised without a network.
    /// </summary>
    public class TrailingSlashFolderBackend : IBackend, IStreamingBackend, IFolderEnabledBackend
    {
        private IFolderEnabledBackend m_backend;

        public TrailingSlashFolderBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public TrailingSlashFolderBackend(string url, Dictionary<string, string> options)
        {
            var wrappedUrl = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
            m_backend = (IFolderEnabledBackend)Library.DynamicLoader.BackendLoader.GetBackend(wrappedUrl, options);
        }

        /// <summary>
        /// Returns the entry with a trailing slash appended to the name if it is a folder.
        /// </summary>
        private static IFileEntry WithTrailingSlash(IFileEntry entry)
            => entry.IsFolder && !string.IsNullOrEmpty(entry.Name) && !entry.Name.EndsWith("/")
                ? new FileEntry(entry.Name + "/", entry.Size, entry.LastAccess, entry.LastModification, true, entry.IsArchived)
                : entry;

        #region IStreamingBackend implementation
        public Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => ((IStreamingBackend)m_backend).PutAsync(remotename, stream, cancelToken);
        public Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => ((IStreamingBackend)m_backend).GetAsync(remotename, stream, cancelToken);
        #endregion

        #region IFolderEnabledBackend implementation
        public async IAsyncEnumerable<IFileEntry> ListAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var entry in m_backend.ListAsync(path, cancellationToken).ConfigureAwait(false))
                yield return WithTrailingSlash(entry);
        }

        public async Task<IFileEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
        {
            var entry = await m_backend.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
            return entry == null ? null : WithTrailingSlash(entry);
        }
        #endregion

        #region IBackend implementation
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var entry in m_backend.ListAsync(cancellationToken).ConfigureAwait(false))
                yield return WithTrailingSlash(entry);
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
            => m_backend.PutAsync(remotename, filename, cancelToken);

        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
            => m_backend.GetAsync(remotename, filename, cancelToken);

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
            => m_backend.DeleteAsync(remotename, cancelToken);

        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken)
            => m_backend.TestAsync(alsoWrite, cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
            => m_backend.CreateFolderAsync(cancelToken);

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
            => m_backend.GetDNSNamesAsync(cancelToken);

        public string DisplayName => "Trailing-Slash Folder Backend";
        public string ProtocolKey => "slashfolder";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_backend == null)
                    try { return Library.DynamicLoader.BackendLoader.GetSupportedCommands("file://").ToList(); }
                    catch { }

                return m_backend.SupportedCommands;
            }
        }

        public string Description => "A testing backend that names folder entries with a trailing slash";
        public bool SupportsStreaming => (m_backend as IStreamingBackend)?.SupportsStreaming ?? false;
        #endregion

        #region IDisposable implementation
        public void Dispose()
        {
            if (m_backend != null)
                try { m_backend.Dispose(); }
                finally { m_backend = null; }
        }
        #endregion
    }
}
