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
using Duplicati.Library.Utility.Options;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.Duplicati;

public class DuplicatiBackend : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
{
    /// <summary>
    /// The authentication API key option name
    /// </summary>
    public const string AUTH_API_KEY_OPTION = "duplicati-auth-apikey";
    /// <summary>
    /// The authentication API id option name
    /// </summary>
    public const string AUTH_API_ID_OPTION = "duplicati-auth-apiid";
    /// <summary>
    /// The backup ID option name
    /// </summary>
    public const string BACKUP_ID_OPTION = "duplicati-backup-id";

    /// <summary>
    /// The protocol key for this backend
    /// </summary>
    public const string PROTOCOL = "duplicati";

    /// <summary>
    /// The default endpoint URL
    /// </summary>
    private const string DEFAULT_ENDPOINT = "https://storage.duplicati.com";

    /// <inheritdoc />
    public string DisplayName => Strings.DuplicatiBackend.DisplayName;
    /// <inheritdoc />
    public string ProtocolKey => PROTOCOL;
    /// <inheritdoc />
    public bool SupportsStreaming => true;
    /// <inheritdoc />
    public string Description => Strings.DuplicatiBackend.Description;

    /// <summary>
    /// The authentication settings
    /// </summary>
    private readonly AuthOptionsHelper.AuthOptions _auth;
    /// <summary>
    /// The timeout settings
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;
    /// <summary>
    /// The backup id
    /// </summary>
    private readonly string _backup_id;

    /// <summary>
    /// The HttpClient used for requests
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Constructor used for dynamic loading
    /// </summary>
    public DuplicatiBackend()
    {
        // Parameterless constructor for dynamic loading
        _auth = null!;
        _timeouts = null!;
        _backup_id = null!;
        _client = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatiBackend"/> class.
    /// </summary>
    /// <param name="url">The backend URL</param>
    /// <param name="options">The backend options</param>
    public DuplicatiBackend(string url, Dictionary<string, string?> options)
    {
        var uri = new Utility.Uri(url);
        if (string.IsNullOrWhiteSpace(uri.HostAndPath))
            uri = new Utility.Uri(DEFAULT_ENDPOINT);

        _client = new HttpClient
        {
            BaseAddress = new System.Uri(uri.SetScheme("https").SetQuery(null).ToString()),
            DefaultRequestHeaders =
            {
                Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") },
            }
        };

        _backup_id = options.GetValueOrDefault(BACKUP_ID_OPTION) ?? string.Empty;
        if (string.IsNullOrEmpty(_backup_id))
            throw new ArgumentException(Strings.DuplicatiBackend.ErrorMissingBackupId, BACKUP_ID_OPTION);
        _auth = AuthOptionsHelper.ParseWithAlias(options, new Utility.Uri(url), AUTH_API_ID_OPTION, AUTH_API_KEY_OPTION)
            .RequireCredentials();

        _client.DefaultRequestHeaders.Add("X-Api-Key", _auth.Password);
        _client.DefaultRequestHeaders.Add("X-Organization-Id", _auth.Username);
        _client.DefaultRequestHeaders.Add("X-Backup-Id", System.Uri.EscapeDataString(_backup_id));

        _timeouts = TimeoutOptionsHelper.Parse(options);
    }

    /// <summary>
    /// Merges the provided arguments into the URL
    /// </summary>
    /// <param name="url">The base URL</param>
    /// <param name="apiId">The API ID</param>
    /// <param name="apiKey">The API key</param>
    /// <param name="endpoint">The endpoint</param>
    /// <returns>The merged URL</returns>
    public static string MergeArgsIntoUrl(string url, string? apiId, string? apiKey, string? endpoint)
    {
        var uri = new Utility.Uri(url);
        var opts = new Dictionary<string, string?>
        {
            [AUTH_API_ID_OPTION] = apiId,
            [AUTH_API_KEY_OPTION] = apiKey,
        };

        var qp = uri.QueryParameters;
        foreach (var kvp in opts)
            if (!string.IsNullOrWhiteSpace(kvp.Value))
                qp[kvp.Key] = Utility.Uri.UrlEncode(kvp.Value);

        var query = Utility.Uri.BuildUriQuery(qp);

        if (string.IsNullOrWhiteSpace(uri.HostAndPath) && !string.IsNullOrWhiteSpace(endpoint))
            uri = new Utility.Uri(endpoint).SetScheme(uri.Scheme).SetQuery(null);

        return uri.SetQuery(query).ToString();
    }

    /// <summary>
    /// Lists the backup folders on the destination
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The list of backup folders</returns>
    public async IAsyncEnumerable<string> ListBackupFolders([EnumeratorCancellation] CancellationToken cancelToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/listbackups");
        using var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            ct => _client.SendAsync(request, ct))
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken: cancelToken).ConfigureAwait(false) ?? [];

        foreach (var entry in entries)
        {
            if (cancelToken.IsCancellationRequested)
                yield break;
            yield return entry;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }

    private record RenameRequest(
        string Source,
        string Destination
    );

    /// <inheritdoc />
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/createfolder");
        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => _client.SendAsync(request, ct))
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/delete/{remotename}");

        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => _client.SendAsync(request, ct))
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, Stream destination, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/get/{remotename}");

        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, token,
            ct => _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);

        using (var timeoutStream = destination.ObserveWriteTimeout(_timeouts.ReadWriteTimeout, false))
            await Utility.Utility.CopyStreamAsync(responseStream, timeoutStream, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        return GetAsync(remotename, File.OpenWrite(filename), cancelToken);
    }

    /// <inheritdoc />
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { _client.BaseAddress?.Host ?? string.Empty });

    /// <inheritdoc />
    public async Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/quota");

        using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => _client.SendAsync(request, ct))
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var quotaInfo = await response.Content.ReadFromJsonAsync<QuotaInfo>(cancellationToken: cancelToken).ConfigureAwait(false);

        return quotaInfo;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/list");

        using var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            ct => _client.SendAsync(request, ct))
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<FileEntry>>(cancellationToken: cancelToken).ConfigureAwait(false) ?? [];

        foreach (var entry in entries)
        {
            if (cancelToken.IsCancellationRequested)
                yield break;
            yield return entry;
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, Stream source, CancellationToken token)
    {
        using var timeoutStream = source.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/put/{remotename}");
        request.Content = new StreamContent(timeoutStream);

        using var response = await _client.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task PutAsync(string targetFilename, string sourceFilePath, CancellationToken cancelToken)
    {
        await using var fileStream = File.OpenRead(sourceFilePath);
        await PutAsync(targetFilename, fileStream, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rename");
        var renameRequest = new RenameRequest(oldname, newname);
        request.Content = JsonContent.Create(renameRequest);

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => [
            new CommandLineArgument(AUTH_API_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.DuplicatiBackend.AuthIdOptionsShort, Strings.DuplicatiBackend.AuthIdOptionsLong, null, [AuthOptionsHelper.AuthUsernameOption]),
            new CommandLineArgument(AUTH_API_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.DuplicatiBackend.AuthKeyOptionsShort, Strings.DuplicatiBackend.AuthKeyOptionsLong, null, [AuthOptionsHelper.AuthPasswordOption]),
            new CommandLineArgument(BACKUP_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.DuplicatiBackend.BackupIdOptionsShort, Strings.DuplicatiBackend.BackupIdOptionsLong),
            .. TimeoutOptionsHelper.GetOptions()
        ];

    /// <inheritdoc />
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestReadWritePermissionsAsync(cancelToken);

}