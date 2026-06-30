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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A test backend that wraps the built-in <c>file</c> backend but does NOT
    /// implement <see cref="IFolderEnabledBackend"/>. It deliberately exposes only
    /// the flat <see cref="IBackend"/> surface (a single listing and put/get/delete
    /// of a filename relative to the backend URL), so that the backend manager's
    /// path-translation logic for non-folder backends can be exercised: relative
    /// paths with sub-folders are split into a sub-folder URL (the backend is
    /// re-created pointing at the sub-folder) plus a flat filename.
    /// </summary>
    public class NonFolderBackend : IBackend, IStreamingBackend
    {
        static NonFolderBackend() { WrappedBackend = "file"; }

        /// <summary>
        /// The protocol key of the underlying backend to wrap (defaults to <c>file</c>).
        /// </summary>
        public static string WrappedBackend { get; set; }

        private IStreamingBackend m_backend;

        public NonFolderBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public NonFolderBackend(string url, Dictionary<string, string> options)
        {
            // Re-create the wrapped backend bound to the exact URL we were given. The
            // backend manager constructs us with a sub-folder URL for non-folder
            // operations, so the wrapped backend ends up pointing at that sub-folder.
            // The caller (sync handler via IBackendManager.EnsureFolderAsync) is
            // responsible for ensuring the bound directory exists before putting a file
            // into it; this wrapper does not pre-create it, mirroring how a real
            // folderless backend would behave (folders are created explicitly via
            // CreateFolderAsync, not implicitly on construction).
            var wrappedUrl = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(wrappedUrl, options);
        }

        #region IStreamingBackend implementation
        public Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => m_backend.PutAsync(remotename, stream, cancelToken);
        public Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
            => m_backend.GetAsync(remotename, stream, cancelToken);
        #endregion

        #region IBackend implementation
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => m_backend.ListAsync(cancellationToken);

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

        public string DisplayName => "Non-Folder Backend";
        public string ProtocolKey => "nofolder";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_backend == null)
                    try { return Library.DynamicLoader.BackendLoader.GetSupportedCommands(WrappedBackend + "://").ToList(); }
                    catch { }

                return m_backend.SupportedCommands;
            }
        }

        public string Description => "A testing backend that does not support folder operations";
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
