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

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Duplicati.Library.Utility;
using System.Text.Json;

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
    /// The endpoint option name
    /// </summary>
    public const string ENDPOINT_OPTION = "duplicati-endpoint";
    /// <summary>
    /// The authentication timeout option name
    /// </summary>
    public const string AUTH_TIMEOUT_OPTION = "duplicati-auth-timeout";

    /// <summary>
    /// The default authentication timeout
    /// </summary>
    public const string DEFAULT_AUTH_TIMEOUT = "120s";

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
    /// The authentication timeout
    /// </summary>
    private readonly TimeSpan _authTimeout;

    /// <summary>
    /// The HttpClient used for requests
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// The mode to use for the backend
    /// </summary>
    private string _mode = "unknown";
    /// <summary>
    /// The S3 backend used for direct mode
    /// </summary>
    private S3? _s3Backend;
    /// <summary>
    /// The credential expiration time for direct mode
    /// </summary>
    private DateTime _credentialsExpiry = DateTime.MinValue;
    /// <summary>
    /// The lock used to protect the credentials
    /// </summary>
    private readonly SemaphoreSlim _credentialsLock = new(1, 1);


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
        var uri = new Utility.CompatUri(url);
        if (string.IsNullOrWhiteSpace(uri.HostAndPath))
            uri = new Utility.CompatUri(DEFAULT_ENDPOINT);
        else
            uri = uri.SetScheme("https").SetQuery(null);

        var endpoint = options.GetValueOrDefault(ENDPOINT_OPTION);
        if (!string.IsNullOrWhiteSpace(endpoint))
            uri = new Utility.CompatUri(endpoint);

        _client = new HttpClient
        {
            BaseAddress = new System.Uri(uri.ToString()),
            DefaultRequestHeaders =
            {
                Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") },
            }
        };

        _backup_id = options.GetValueOrDefault(BACKUP_ID_OPTION) ?? string.Empty;
        if (string.IsNullOrEmpty(_backup_id))
            throw new ArgumentException(Strings.DuplicatiBackend.ErrorMissingBackupId, BACKUP_ID_OPTION);
        _auth = AuthOptionsHelper.ParseWithAlias(options, new Utility.CompatUri(url), AUTH_API_ID_OPTION, AUTH_API_KEY_OPTION)
            .RequireCredentials();

        _authTimeout = Utility.Utility.ParseTimespanOption(options, AUTH_TIMEOUT_OPTION, DEFAULT_AUTH_TIMEOUT);

        _client.DefaultRequestHeaders.Add("X-Api-Key", _auth.Password);
        _client.DefaultRequestHeaders.Add("X-Organization-Id", _auth.Username);
        _client.DefaultRequestHeaders.Add("X-Machine-Id", System.Uri.EscapeDataString(AutoUpdater.DataFolderManager.MachineID));
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
        var uri = new Utility.CompatUri(url);
        var opts = new Dictionary<string, string?>
        {
            [AUTH_API_ID_OPTION] = apiId,
            [AUTH_API_KEY_OPTION] = apiKey,
        };

        var qp = uri.QueryParameters;
        foreach (var kvp in opts)
            if (!string.IsNullOrWhiteSpace(kvp.Value) && string.IsNullOrWhiteSpace(qp.Get(kvp.Key)))
                qp[kvp.Key] = Utility.CompatUri.UrlEncode(kvp.Value);

        var query = Utility.CompatUri.BuildUriQuery(qp);

        if (string.IsNullOrWhiteSpace(uri.HostAndPath) && !string.IsNullOrWhiteSpace(endpoint))
            uri = new Utility.CompatUri(endpoint).SetScheme(uri.Scheme).SetQuery(null);

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

    /// <summary>
    /// Ensures that we have valid credentials for direct S3 access
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task EnsureCredentialsAsync(CancellationToken cancelToken)
    {
        if (_mode == "gateway")
            return;

        // Credentials are still valid with a 5-minute buffer
        if (_mode == "direct" && _s3Backend != null && DateTime.UtcNow < _credentialsExpiry.AddMinutes(-5))
            return;

        await _credentialsLock.WaitAsync(cancelToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_mode == "direct" && _s3Backend != null && DateTime.UtcNow < _credentialsExpiry.AddMinutes(-5))
                return;

            using var request = new HttpRequestMessage(HttpMethod.Get, "/credentials");
            using var response = await Utility.Utility.WithTimeout(_authTimeout, cancelToken, ct =>
                _client.SendAsync(request, ct)
            ).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var creds = await response.Content.ReadFromJsonAsync<CredentialsResponse>(options, cancellationToken: cancelToken).ConfigureAwait(false);
            if (creds != null &&
                creds.Mode == "direct" &&
                !string.IsNullOrEmpty(creds.Endpoint) &&
                !string.IsNullOrEmpty(creds.AccessKeyId) &&
                !string.IsNullOrEmpty(creds.SecretKey) &&
                !string.IsNullOrEmpty(creds.Bucket))
            {
                _mode = "direct";
                _credentialsExpiry = DateTime.UnixEpoch.AddSeconds(creds.RotateAfter ?? 0);
                // In case the server doesn't provide a rotate-after value, we set a buffer
                if (_credentialsExpiry - DateTime.UtcNow < TimeSpan.FromMinutes(15))
                    _credentialsExpiry = DateTime.UtcNow.AddMinutes(15);

                // Build S3 URL: s3://bucket
                var s3Url = $"s3://{creds.Bucket}";
                if (!string.IsNullOrWhiteSpace(creds.Prefix))
                    s3Url += (creds.Prefix.StartsWith('/') ? "" : "/") + creds.Prefix;

                // Configure S3 options
                var s3Options = new Dictionary<string, string?>
                {
                    ["aws-access-key-id"] = creds.AccessKeyId,
                    ["aws-secret-access-key"] = creds.SecretKey,
                    ["s3-server-name"] = creds.Endpoint,
                    ["s3-ext-forcepathstyle"] = "true",
                };

                // Plug in timeout options
                _timeouts.ApplyTo(s3Options, overwrite: true);

                _s3Backend?.Dispose();
                _s3Backend = new S3(s3Url, s3Options);
            }
            else
            {
                _mode = "gateway";
                _s3Backend?.Dispose();
                _s3Backend = null;
            }
        }
        finally
        {
            _credentialsLock.Release();
        }
    }

    /// <summary>
    /// A request to rename a file
    /// </summary>
    /// <param name="Source">The source file</param>
    /// <param name="Destination">The destination file</param>
    private record RenameRequest(
        string Source,
        string Destination
    );

    /// <inheritdoc />
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await _s3Backend.CreateFolderAsync(cancelToken).ConfigureAwait(false);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/createfolder");
            using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => _client.SendAsync(request, ct))
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await _s3Backend.DeleteAsync(remotename, cancelToken).ConfigureAwait(false);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/delete/{remotename}");

            using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => _client.SendAsync(request, ct))
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, Stream destination, CancellationToken cancelToken)
    {
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await _s3Backend.GetAsync(remotename, destination, cancelToken).ConfigureAwait(false);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/get/{remotename}");

            using var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);

            using (var timeoutStream = destination.ObserveWriteTimeout(_timeouts.ReadWriteTimeout, false))
                await Utility.Utility.CopyStreamAsync(responseStream, timeoutStream, cancelToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        => GetAsync(remotename, File.OpenWrite(filename), cancelToken);

    /// <inheritdoc />
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => Task.FromResult(new[] { _client.BaseAddress?.Host ?? string.Empty });

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
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await foreach (var f in _s3Backend.ListAsync(cancelToken).ConfigureAwait(false))
                yield return f;
        }
        else
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
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, Stream source, CancellationToken cancelToken)
    {
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await _s3Backend.PutAsync(remotename, source, cancelToken).ConfigureAwait(false);
        }
        else
        {
            (source, var hashes, var tmp) = await Utility.Utility.CalculateThrottledStreamHash(source, ["MD5", "SHA256"], cancelToken).ConfigureAwait(false);
            using var _ = tmp;
            var md5 = Convert.ToBase64String(Utility.Utility.HexStringAsByteArray(hashes[0]));
            var sha256 = Convert.ToBase64String(Utility.Utility.HexStringAsByteArray(hashes[1]));
            using var timeoutStream = source.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/put/{remotename}");
            request.Content = new StreamContent(timeoutStream);
            request.Content.Headers.ContentLength = source.Length;
            request.Headers.Add("X-Content-MD5", md5 ?? string.Empty);
            request.Headers.Add("X-Content-SHA256", sha256 ?? string.Empty);

            using var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(string targetFilename, string sourceFilePath, CancellationToken cancelToken)
    {
        await using var fileStream = File.OpenRead(sourceFilePath);
        await PutAsync(targetFilename, fileStream, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
    {
        await EnsureCredentialsAsync(cancelToken).ConfigureAwait(false);
        if (_s3Backend != null)
        {
            await _s3Backend.RenameAsync(oldname, newname, cancelToken).ConfigureAwait(false);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/rename");
            var renameRequest = new RenameRequest(oldname, newname);
            request.Content = JsonContent.Create(renameRequest);

            using var response = await _client.SendAsync(request, cancelToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => [
            new CommandLineArgument(AUTH_API_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.DuplicatiBackend.AuthIdOptionsShort, Strings.DuplicatiBackend.AuthIdOptionsLong, null, [AuthOptionsHelper.AuthUsernameOption]),
            new CommandLineArgument(AUTH_API_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.DuplicatiBackend.AuthKeyOptionsShort, Strings.DuplicatiBackend.AuthKeyOptionsLong, null, [AuthOptionsHelper.AuthPasswordOption]),
            new CommandLineArgument(BACKUP_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.DuplicatiBackend.BackupIdOptionsShort, Strings.DuplicatiBackend.BackupIdOptionsLong),
            new CommandLineArgument(ENDPOINT_OPTION, CommandLineArgument.ArgumentType.String, Strings.DuplicatiBackend.EndpointOptionsShort, Strings.DuplicatiBackend.EndpointOptionsLong),
            new CommandLineArgument(AUTH_TIMEOUT_OPTION, CommandLineArgument.ArgumentType.Timespan, Strings.DuplicatiBackend.AuthTimeoutOptionsShort, Strings.DuplicatiBackend.AuthTimeoutOptionsLong, DEFAULT_AUTH_TIMEOUT),
            .. TimeoutOptionsHelper.GetOptions()
        ];

    /// <inheritdoc />
    public Task TestAsync(bool alsoWrite, CancellationToken cancelToken)
        => this.TestBackendAsync(alsoWrite, cancelToken);

    /// <summary>
    /// Response from getting credentials
    /// </summary>
    private sealed record CredentialsResponse
    {
        /// <summary>
        /// The mode for connecting to storage, either "direct" or "gateway"
        /// </summary>
        public string? Mode { get; set; }
        /// <summary>
        /// The S3 endpoint to connect to, in "direct" mode
        /// </summary>
        public string? Endpoint { get; set; }
        /// <summary>
        /// The access key ID to use for S3 in "direct" mode
        /// </summary>
        public string? AccessKeyId { get; set; }
        /// <summary>
        /// The secret key to use for S3 in "direct" mode
        /// </summary>
        public string? SecretKey { get; set; }
        /// <summary>
        /// The bucket to use for S3 in "direct" mode
        /// </summary>
        public string? Bucket { get; set; }
        /// <summary>
        /// The prefix to use for S3 in "direct" mode
        /// </summary>
        public string? Prefix { get; set; }
        /// <summary>
        /// The region to use for S3 in "direct" mode
        /// </summary>
        public string? Region { get; set; }
        /// <summary>
        /// The time the credentials were issued
        /// </summary>
        public long? IssuedAt { get; set; }
        /// <summary>
        /// The time the credentials expire
        /// </summary>
        public long? RotateAfter { get; set; }
    }
}