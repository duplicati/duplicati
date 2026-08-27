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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Stores files in a local folder, and reports that the destination has no room left.
    ///
    /// A read-only mount does exactly this: macOS mounting an NTFS volume reports zero bytes
    /// available while the volume is perfectly readable, which is what issue #3672 was about.
    /// </summary>
    public class NoFreeSpaceBackend : IBackend, IQuotaEnabledBackend
    {
        /// <summary>
        /// What the destination claims its capacity is.
        /// </summary>
        private const long TotalSpace = 4_000_000_000_000;

        /// <summary>
        /// Where the files actually go. Set by the test, so the url does not have to carry a
        /// path and this does not have to parse one.
        /// </summary>
        public static string Folder { get; set; } = "";

        private readonly string m_folder = Folder;

        public NoFreeSpaceBackend()
        {
        }

        public NoFreeSpaceBackend(string url, Dictionary<string, string> options)
        {
            if (!string.IsNullOrEmpty(m_folder) && !Directory.Exists(m_folder))
                Directory.CreateDirectory(m_folder);
        }

        public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
            => Task.FromResult<IQuotaInfo?>(new QuotaInfo(TotalSpace, 0));

        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(m_folder);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<IFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            foreach (var path in Directory.EnumerateFiles(m_folder))
            {
                var info = new FileInfo(path);
                yield return new FileEntry(info.Name, info.Length, info.LastAccessTimeUtc, info.LastWriteTimeUtc);
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
        {
            await using var src = File.OpenRead(filename);
            await using var dst = File.Create(Path.Combine(m_folder, remotename));
            await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
        {
            await using var src = File.OpenRead(Path.Combine(m_folder, remotename));
            await using var dst = File.Create(filename);
            await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            File.Delete(Path.Combine(m_folder, remotename));
            return Task.CompletedTask;
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
            => Task.FromResult(Array.Empty<string>());

        public string DisplayName => "No Free Space Backend";
        public string ProtocolKey => "nofreespace";
        public string Description => "A testing backend that stores files locally but reports no free space";
        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

        public void Dispose()
        {
        }
    }
}
