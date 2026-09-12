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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A test backend that wraps the built-in <c>file</c> backend and behaves like a
    /// destination that does not distinguish letter case, the way NTFS and APFS do:
    /// a put, get or delete of a name that differs only in case from an existing file
    /// is applied to that existing file, and the listing reports the name the file was
    /// first stored under. This gives the tests the same behaviour on every CI
    /// operating system, including a case-sensitive Linux file system.
    /// </summary>
    public class CaseInsensitiveFileBackend : IBackend, IStreamingBackend
    {
        /// <summary>
        /// The protocol key this backend is registered under.
        /// </summary>
        public const string Key = "cifile";

        private IStreamingBackend m_backend;

        public CaseInsensitiveFileBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public CaseInsensitiveFileBackend(string url, Dictionary<string, string> options)
        {
            var wrappedUrl = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(wrappedUrl, options);
        }

        /// <summary>
        /// Returns the name an existing file is stored under when it matches
        /// <paramref name="remotename"/> ignoring case, or the name itself otherwise.
        /// </summary>
        private async Task<string> ResolveAsync(string remotename, CancellationToken cancelToken)
        {
            await foreach (var entry in m_backend.ListAsync(cancelToken).ConfigureAwait(false))
                if (!entry.IsFolder && string.Equals(entry.Name, remotename, StringComparison.OrdinalIgnoreCase))
                    return entry.Name;
            return remotename;
        }

        #region IStreamingBackend implementation
        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => await m_backend.PutAsync(await ResolveAsync(remotename, cancelToken).ConfigureAwait(false), stream, cancelToken).ConfigureAwait(false);
        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => await m_backend.GetAsync(await ResolveAsync(remotename, cancelToken).ConfigureAwait(false), stream, cancelToken).ConfigureAwait(false);
        #endregion

        #region IBackend implementation
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => m_backend.ListAsync(cancellationToken);

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
            => await m_backend.PutAsync(await ResolveAsync(remotename, cancelToken).ConfigureAwait(false), filename, cancelToken).ConfigureAwait(false);

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
            => await m_backend.GetAsync(await ResolveAsync(remotename, cancelToken).ConfigureAwait(false), filename, cancelToken).ConfigureAwait(false);

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
            => await m_backend.DeleteAsync(await ResolveAsync(remotename, cancelToken).ConfigureAwait(false), cancelToken).ConfigureAwait(false);

        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken)
            => m_backend.TestAsync(alsoWrite, cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
            => m_backend.CreateFolderAsync(cancelToken);

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
            => m_backend.GetDNSNamesAsync(cancelToken);

        public string DisplayName => "Case-insensitive file backend";
        public string ProtocolKey => Key;

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

        public string Description => "A testing backend that does not distinguish letter case in file names";
        public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;
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
