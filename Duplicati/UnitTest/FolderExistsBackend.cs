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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A test backend that reports the folder as missing, and then reports that the
    /// folder already existed when it is asked to create it. That is what a backend
    /// does when the folder appears between the test and the create, and what the
    /// file and Dropbox backends do whenever their existence check and their create
    /// disagree. Used to check that creating a destination folder that is already
    /// there is treated as the desired outcome.
    /// </summary>
    public class FolderExistsBackend : IBackend
    {
        /// <summary>
        /// The number of times a folder creation was requested, across all instances
        /// </summary>
        public static int CreateFolderCalls;

        /// <summary>
        /// True once the folder has been reported as already existing
        /// </summary>
        private bool m_folderReported;

        public FolderExistsBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public FolderExistsBackend(string url, Dictionary<string, string> options)
        {
        }

        /// <summary>
        /// Forgets the recorded folder creations
        /// </summary>
        public static void Reset()
            => CreateFolderCalls = 0;

        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken)
        {
            if (!m_folderReported)
                throw new FolderMissingException();

            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            CreateFolderCalls++;
            m_folderReported = true;
            throw new FolderAreadyExistedException();
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => EmptyListAsync();

        private static async IAsyncEnumerable<IFileEntry> EmptyListAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
            => Task.FromResult(Array.Empty<string>());

        public string DisplayName => "Existing Folder Backend";
        public string ProtocolKey => "folderexists";
        public string Description => "A testing backend that reports the folder as already existing";

        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

        public void Dispose()
        {
        }
    }
}
