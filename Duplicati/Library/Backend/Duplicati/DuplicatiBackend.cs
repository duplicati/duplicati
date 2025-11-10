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

using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend;

public class DuplicatiBackend : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
{

    public string DisplayName => Strings.DuplicatiBackend.DisplayName;
    public string ProtocolKey => "duplicati";
    public bool SupportsStreaming => true;
    private readonly HttpClient _client;
    public string Description => Strings.DuplicatiBackend.Description;

    public DuplicatiBackend(string url, Dictionary<string, string?> options)
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(url),
        };
    }

    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {

    }

    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {

    }

    public async Task GetAsync(string remotename, Stream destination, CancellationToken token)
    {

    }

    public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {

    }

    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

    public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
    {

    }

    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {

    }

    public async Task PutAsync(string remotename, Stream source, CancellationToken token)
    {

    }

    public async Task PutAsync(string targetFilename, string sourceFilePath, CancellationToken cancelToken)
    {

    }

    public Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
    {

    }

    public IList<ICommandLineArgument> SupportedCommands
    {
        get
        {
            var lst = new List<ICommandLineArgument>();

            return lst;
        }
    }

    public Task TestAsync(CancellationToken cancelToken)
    {

    }

    public void Dispose()
    {
        _client.Dispose();
    }

}