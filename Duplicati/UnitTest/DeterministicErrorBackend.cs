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

using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class DeterministicErrorBackend : IBackend, IStreamingBackend
    {
        public sealed record BackendAction(string Code)
        {
            public static readonly BackendAction GetBefore = new BackendAction("get_0");
            public static readonly BackendAction GetAfter = new BackendAction("get_1");
            public static readonly BackendAction PutBefore = new BackendAction("put_0");
            public static readonly BackendAction PutAfter = new BackendAction("put_1");
            public static readonly BackendAction DeleteBefore = new BackendAction("delete_0");
            public static readonly BackendAction DeleteAfter = new BackendAction("delete_1");

            public bool IsGetOperation => this == GetBefore || this == GetAfter;
            public bool IsPutOperation => this == PutBefore || this == PutAfter;
            public bool IsDeleteOperation => this == DeleteBefore || this == DeleteAfter;
        }

        public class DeterministicErrorBackendException(string message) : Exception(message) { };

        static DeterministicErrorBackend() { WrappedBackend = "file"; }

        // return true to throw exception, parameters: (action, remotename)
        public static Func<BackendAction, string, bool> ErrorGenerator = null;

        public static string WrappedBackend { get; set; }

        private IStreamingBackend m_backend;
        public DeterministicErrorBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public DeterministicErrorBackend(string url, Dictionary<string, string> options)
        {
            var u = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
        }

        private void ThrowError(BackendAction action, string remotename)
        {
            if (ErrorGenerator != null && ErrorGenerator(action, remotename))
            {
                throw new DeterministicErrorBackendException("Backend error");
            }
        }
        #region IStreamingBackend implementation
        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            ThrowError(BackendAction.PutBefore, remotename);
            await m_backend.PutAsync(remotename, stream, cancelToken).ConfigureAwait(false);
            ThrowError(BackendAction.PutAfter, remotename);
        }
        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancellationToken)
        {
            ThrowError(BackendAction.GetBefore, remotename);
            await m_backend.GetAsync(remotename, stream, cancellationToken).ConfigureAwait(false);
            ThrowError(BackendAction.GetAfter, remotename);
        }
        #endregion

        #region IBackend implementation
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken)
        {
            return m_backend.ListAsync(cancelToken);
        }
        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            ThrowError(BackendAction.PutBefore, remotename);
            await m_backend.PutAsync(remotename, filename, cancelToken).ConfigureAwait(false);
            ThrowError(BackendAction.PutAfter, remotename);
        }
        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            ThrowError(BackendAction.GetBefore, remotename);
            await m_backend.GetAsync(remotename, filename, cancelToken).ConfigureAwait(false);
            ThrowError(BackendAction.GetAfter, remotename);
        }
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            ThrowError(BackendAction.DeleteBefore, remotename);
            await m_backend.DeleteAsync(remotename, cancelToken).ConfigureAwait(false);
            ThrowError(BackendAction.DeleteAfter, remotename);
        }
        public Task TestAsync(CancellationToken cancelToken)
        {
            return m_backend.TestAsync(cancelToken);
        }
        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return m_backend.CreateFolderAsync(cancelToken);
        }
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        {
            return m_backend.GetDNSNamesAsync(cancelToken);
        }
        public string DisplayName
        {
            get
            {
                return "Deterministic Error Backend";
            }
        }
        public string ProtocolKey
        {
            get
            {
                return "deterror";
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
                return "A testing backend that randomly fails";
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

