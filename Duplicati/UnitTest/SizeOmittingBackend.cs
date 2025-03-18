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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class SizeOmittingBackend : IBackend, IStreamingBackend
    {
        static SizeOmittingBackend() { WrappedBackend = "file"; }

        public static string WrappedBackend { get; set; }

        private IStreamingBackend m_backend;
        public SizeOmittingBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public SizeOmittingBackend(string url, Dictionary<string, string> options)
        {
            var u = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
        }

        #region IStreamingBackend implementation
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, stream, cancelToken);
        }
        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            await m_backend.GetAsync(remotename, stream, cancelToken).ConfigureAwait(false);
        }
        #endregion

        #region IBackend implementation
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
        {
            return
                from n in m_backend.ListAsync(cancellationToken)
                where !n.IsFolder
                select new FileEntry(n.Name);
        }
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, filename, cancelToken);
        }
        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await m_backend.GetAsync(remotename, filename, cancelToken).ConfigureAwait(false);
        }
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            await m_backend.DeleteAsync(remotename, cancelToken).ConfigureAwait(false);
        }
        public Task TestAsync(CancellationToken cancelToken)
        {
            return m_backend.TestAsync(cancelToken);
        }
        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return m_backend.CreateFolderAsync(cancelToken);
        }
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend.GetDNSNamesAsync(cancelToken);
        public string DisplayName
        {
            get
            {
                return "Size Omitting Backend";
            }
        }
        public string ProtocolKey
        {
            get
            {
                return "omitsize";
            }
        }
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_backend == null)
                    try { return Duplicati.Library.DynamicLoader.BackendLoader.GetSupportedCommands(WrappedBackend + "://").ToList(); }
                    catch { }

                return m_backend.SupportedCommands;
            }
        }
        public string Description
        {
            get
            {
                return "A testing backend that does not return size information";
            }
        }
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

