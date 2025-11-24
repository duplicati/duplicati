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

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend;

public class DuplicatiBackend : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
{
    //
    // Constants
    //
    private const string AUTH_API_KEY_OPTION = "duplicati-auth-apikey";
    private const string AUTH_ORG_ID_OPTION = "duplicati-auth-orgid";
    private const string BACKUP_ID_OPTION = "duplicati-backup-id";

    //
    // Fields
    //
    public string DisplayName => Strings.DuplicatiBackend.DisplayName;
    public string ProtocolKey => "duplicati";
    public bool SupportsStreaming => true;
    public string Description => Strings.DuplicatiBackend.Description;

    private readonly string _api_key;
    private readonly string _org_id;
    private readonly HttpClient _client;
    private string BackupId { get; set; } = string.Empty;

    //
    // Constructors
    //
    public DuplicatiBackend()
    {
        // Parameterless constructor for dynamic loading
        _api_key = string.Empty;
        _org_id = string.Empty;
        _client = new HttpClient();
    }

    public DuplicatiBackend(string url, Dictionary<string, string?> options)
    {
        string decoded = Uri.UnescapeDataString(url);
        var uri = new UriBuilder(decoded)
        {
            Scheme = "https"
        };
        _client = new HttpClient
        {
            BaseAddress = uri.Uri,
        };
        BackupId = options.GetValueOrDefault(BACKUP_ID_OPTION) ?? string.Empty;
        _api_key = options.GetValueOrDefault(AUTH_API_KEY_OPTION) ?? string.Empty;
        _org_id = options.GetValueOrDefault(AUTH_ORG_ID_OPTION) ?? string.Empty;
    }

    //
    // Cleanup
    //
    public void Dispose()
    {
        _client.Dispose();
    }

    //
    // Helper methods
    //
    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", _api_key);
        request.Headers.Add("X-Organization-Id", _org_id);
        request.Headers.Add("X-Backup-Id", BackupId);
        return request;
    }

    private record RenameRequest(
        string Source,
        string Destination
    );

    //
    // IBackend methods
    //
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        var request = CreateRequest(HttpMethod.Get, "/createfolder");

        var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        var request = CreateRequest(HttpMethod.Get, $"/delete/{remotename}");

        var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task GetAsync(string remotename, Stream destination, CancellationToken token)
    {
        var request = CreateRequest(HttpMethod.Get, $"/get/{remotename}");

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await responseStream.CopyToAsync(destination, token).ConfigureAwait(false);
    }

    public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        return GetAsync(remotename, File.OpenWrite(filename), cancelToken);
    }

    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

    public async Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
    {
        var request = CreateRequest(HttpMethod.Get, "/quota");

        var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var quotaInfo = await response.Content.ReadFromJsonAsync<QuotaInfo>(cancellationToken: cancelToken).ConfigureAwait(false);

        return quotaInfo;
    }

    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {
        var request = CreateRequest(HttpMethod.Get, "/list");

        var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<FileEntry>>(cancellationToken: cancelToken).ConfigureAwait(false) ?? [];

        foreach (var entry in entries)
        {
            yield return entry;
        }
    }

    public async Task PutAsync(string remotename, Stream source, CancellationToken token)
    {
        var request = CreateRequest(HttpMethod.Post, $"/put/{remotename}");
        request.Content = new StreamContent(source);

        var response = await _client.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutAsync(string targetFilename, string sourceFilePath, CancellationToken cancelToken)
    {
        await using var fileStream = File.OpenRead(sourceFilePath);
        await PutAsync(targetFilename, fileStream, cancelToken).ConfigureAwait(false);
    }

    public Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
    {
        var request = CreateRequest(HttpMethod.Post, "/rename");
        var renameRequest = new RenameRequest(oldname, newname);
        request.Content = JsonContent.Create(renameRequest);

        return _client.SendAsync(request, cancellationToken).ContinueWith(task =>
        {
            var response = task.Result;
            response.EnsureSuccessStatusCode();
        }, cancellationToken);
    }

    public IList<ICommandLineArgument> SupportedCommands
    {
        get
        {
            var lst = new List<ICommandLineArgument>();

            return lst;
        }
    }

    public async Task TestAsync(CancellationToken cancelToken)
    {
        // No specific test operation, just ensure we can list
        await ListAsync(cancelToken).ToListAsync(cancelToken);
    }

}