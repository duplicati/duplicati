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

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Backend.DrimeCloud.Model;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using FileEntry = Duplicati.Library.Common.IO.FileEntry;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Backend.DrimeCloud;

/// <summary>
/// Drime Cloud backend implementation for Duplicati
/// </summary>
public class DrimeBackend : IBackend, IStreamingBackend //, IRenameEnabledBackend - Disabled rename as the API is currently not working
{
    /// <summary>
    /// Option name for API URL
    /// </summary>
    private const string API_URL_OPTION = "api-url";

    /// <summary>
    /// Option name for API token
    /// </summary>
    private const string API_TOKEN_OPTION = "api-token";

    /// <summary>
    /// Option name for page size
    /// </summary>
    private const string PAGE_SIZE_OPTION = "page-size";

    /// <summary>
    /// Option name for workspace ID
    /// </summary>
    private const string WORKSPACE_ID_OPTION = "workspace-id";

    /// <summary>
    /// Option name for soft delete
    /// </summary>
    private const string SOFT_DELETE_OPTION = "soft-delete";

    /// <summary>
    /// Default API URL
    /// </summary>
    private const string DEFAULT_API_URL = "https://app.drime.cloud/api/v1";

    /// <summary>
    /// Default page size
    /// </summary>
    private const int DEFAULT_PAGE_SIZE = 50;

    /// <summary>
    /// Drime currently caps listing pages server-side. Keeping the requested
    /// size within the observed limit avoids undefined pagination behaviour
    /// when a larger value is configured in Duplicati.
    /// </summary>
    private const int MAX_PAGE_SIZE = 200;

    /// <summary>
    /// Number of attempts made when Drime returns a different page than the
    /// one requested.
    /// </summary>
    private const int PAGE_VALIDATION_ATTEMPTS = 3;

    private const int WINDOW_RESTART_PAGE = 20;
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<DrimeBackend>();
    private readonly Dictionary<long, string> _folderHashes = new();

    /// <summary>
    /// Default soft delete setting
    /// </summary>
    private const string SOFT_DELETE_DEFAULT = "false";

    /// <summary>
    /// Default workspace ID (0 = personal workspace)
    /// </summary>
    private const long DEFAULT_WORKSPACE_ID = 0;

    /// <summary>
    /// Folder type identifier
    /// </summary>
    private const string FOLDER_TYPE = "folder";

    /// <summary>
    /// File type identifier
    /// </summary>
    private const string FILE_TYPE = "file";

    /// <summary>
    /// Threshold for using multipart upload (5MB)
    /// </summary>
    private const long MULTIPART_THRESHOLD = 5 * 1024 * 1024; // 5,242,880 bytes

    /// <summary>
    /// Path to the target folder
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// API URL
    /// </summary>
    private readonly string _apiUrl;

    /// <summary>
    /// Workspace ID
    /// </summary>
    private readonly long _workspaceId;

    /// <summary>
    /// Page size for API requests
    /// </summary>
    private readonly int _pageSize;

    /// <summary>
    /// Whether to use soft delete
    /// </summary>
    private readonly bool _softDelete;

    /// <summary>
    /// API token for authentication
    /// </summary>
    private readonly string? _apiToken;

    /// <summary>
    /// Authentication options (username/password)
    /// </summary>
    private readonly AuthOptionsHelper.AuthOptions _authOptions;

    /// <summary>
    /// Timeout options
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// JSON serializer options
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// HTTP client for Drime Cloud API
    /// </summary>
    private HttpClient? _httpClient;

    /// <summary>
    /// HTTP client for S3 uploads (created on first multipart upload)
    /// </summary>
    private HttpClient? _s3HttpClient;

    /// <summary>
    /// Target folder ID (cached)
    /// </summary>
    private long? _targetFolderId;

    /// <summary>
    /// File entry cache
    /// </summary>
    private ConcurrentDictionary<string, CachedFileEntry>? _fileCache;

    /// <summary>
    /// Empty constructor required for backend factory
    /// </summary>
    public DrimeBackend()
    {
        _path = null!;
        _apiUrl = null!;
        _authOptions = null!;
        _timeouts = null!;
    }

    /// <summary>
    /// Main constructor
    /// </summary>
    /// <param name="url">Backend URL</param>
    /// <param name="options">Options dictionary</param>
    public DrimeBackend(string url, Dictionary<string, string?> options)
    {
        var uri = new RelaxedUri(url);
        _path = uri.HostAndPath?.Trim('/') ?? "";

        if (string.IsNullOrWhiteSpace(_path))
            _path = "/";
        else
            _path = $"/{_path}/";

        // Get API token or parse auth options
        _apiToken = options.GetValueOrDefault(API_TOKEN_OPTION);
        _authOptions = AuthOptionsHelper.Parse(options, uri.Username, uri.Password);

        if (string.IsNullOrWhiteSpace(_apiToken) && (!_authOptions.HasUsername || !_authOptions.HasPassword))
            throw new UserInformationException(Strings.DrimeCloud.MissingCredentialsError, "DrimeCloudMissingCredentials");

        // Parse timeout options
        _timeouts = TimeoutOptionsHelper.Parse(options);

        // Parse API URL
        _apiUrl = options.GetValueOrDefault(API_URL_OPTION) ?? "";
        if (string.IsNullOrWhiteSpace(_apiUrl))
            _apiUrl = DEFAULT_API_URL;

        // Parse page size
        _pageSize = Utility.Utility.ParseIntOption(options, PAGE_SIZE_OPTION, DEFAULT_PAGE_SIZE);
        if (_pageSize <= 0)
            throw new UserInformationException(Strings.DrimeCloud.InvalidPageSizeError(PAGE_SIZE_OPTION, _pageSize), "DrimeCloudInvalidPageSize");

        // Parse workspace ID
        _workspaceId = Library.Utility.Utility.ParseLongOption(options, WORKSPACE_ID_OPTION, DEFAULT_WORKSPACE_ID);

        // Parse soft delete option
        _softDelete = Utility.Utility.ParseBoolOption(options, SOFT_DELETE_OPTION);
    }

    /// <summary>
    /// Constructor that takes a preconfigured message handler, used for testing
    /// </summary>
    /// <param name="url">Backend URL</param>
    /// <param name="options">Options dictionary</param>
    /// <param name="handler">The message handler to send requests through</param>
    internal DrimeBackend(string url, Dictionary<string, string?> options, HttpMessageHandler handler)
        : this(url, options)
    {
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new System.Uri(_apiUrl.TrimEnd('/') + "/")
        };

        // GetClientAsync hands back a client that already carries an authorization
        // header, so setting one here keeps the login round-trip out of the tests
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    }

    /// <inheritdoc/>
    public string ProtocolKey => "drimecloud";

    /// <inheritdoc/>
    public string DisplayName => Strings.DrimeCloud.DisplayName;

    /// <inheritdoc/>
    public string Description => Strings.DrimeCloud.Description;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(API_TOKEN_OPTION, CommandLineArgument.ArgumentType.Password,
            Strings.DrimeCloud.DescriptionApiTokenShort, Strings.DrimeCloud.DescriptionApiTokenLong, null),
        new CommandLineArgument(API_URL_OPTION, CommandLineArgument.ArgumentType.String,
            Strings.DrimeCloud.DescriptionApiUrlShort, Strings.DrimeCloud.DescriptionApiUrlLong, DEFAULT_API_URL),
        new CommandLineArgument(PAGE_SIZE_OPTION, CommandLineArgument.ArgumentType.Integer,
            Strings.DrimeCloud.DescriptionPageSizeShort, Strings.DrimeCloud.DescriptionPageSizeLong, DEFAULT_PAGE_SIZE.ToString()),
        new CommandLineArgument(WORKSPACE_ID_OPTION, CommandLineArgument.ArgumentType.Integer,
            Strings.DrimeCloud.DescriptionWorkspaceIdShort, Strings.DrimeCloud.DescriptionWorkspaceIdLong, DEFAULT_WORKSPACE_ID.ToString()),
        new CommandLineArgument(SOFT_DELETE_OPTION, CommandLineArgument.ArgumentType.Boolean,
            Strings.DrimeCloud.DescriptionSoftDeleteShort, Strings.DrimeCloud.DescriptionSoftDeleteLong, SOFT_DELETE_DEFAULT),
        .. AuthOptionsHelper.GetOptions(),
        .. TimeoutOptionsHelper.GetOptions()
    ];

    /// <inheritdoc/>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {
        var folderId = await GetTargetFolderIdAsync(cancelToken).ConfigureAwait(false);
        _fileCache = null;
        var cache = new ConcurrentDictionary<string, CachedFileEntry>();

        // Buffer and validate the complete remote listing before yielding it.
        // This allows an alternate Drime folder filter to be tried without
        // leaking a partial or duplicate listing to Duplicati.
        var entries = await ListEntriesAsync(folderId, foldersOnly: false, cancelToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            // Skip soft-deleted entries
            if (!string.IsNullOrWhiteSpace(entry.Deleted_At))
                continue;

            if (entry.Type == FILE_TYPE)
            {
                cache[entry.Name] = new CachedFileEntry(entry.Id, entry.Hash, entry.File_Size);

                var fileEntry = new FileEntry(entry.Name, entry.File_Size)
                {
                    LastModification = ParseDateTime(entry.Updated_At),
                    Created = ParseDateTime(entry.Created_At),
                    IsFolder = false
                };

                yield return fileEntry;
            }
        }

        _fileCache = cache;
    }

    /// <inheritdoc/>
    public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using var fs = File.OpenRead(filename);
        await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var folderId = await GetTargetFolderIdAsync(cancelToken).ConfigureAwait(false);

        // Drime Cloud doesn't support overwrite, so delete first if exists
        var cache = await GetFileCacheAsync(cancelToken).ConfigureAwait(false);
        if (cache.ContainsKey(remotename))
            await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);

        // We need the length, but the stream may not support reporting it
        var streamLength = -1L;
        try
        {
            streamLength = stream.Length;
        }
        catch
        {
        }

        // Use multipart upload for files >= 5MB where we can read the length
        if (streamLength >= MULTIPART_THRESHOLD)
        {
            await PutMultipartAsync(remotename, stream, folderId, cancelToken).ConfigureAwait(false);
            return;
        }

        // Fallback to form data for small files and streams where we can't read the length
        var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout));
        content.Add(fileContent, "file", remotename);
        content.Add(new StringContent(_workspaceId.ToString()), "workspaceId");

        if (folderId.HasValue)
            content.Add(new StringContent(folderId.Value.ToString()), "parentId");

        using var request = new HttpRequestMessage(HttpMethod.Post, "uploads")
        {
            Content = content
        };
        request.Headers.Add("Accept", "application/json");

        // No timeout here as the stream content has a timeout
        var response = await client.SendAsync(request, cancelToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        var result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
            response.Content.ReadFromJsonAsync<FileEntryResponse>(_jsonOptions, ct)).ConfigureAwait(false);

        if (result == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.DrimeCloud.UploadFailedError(result?.Message ?? "Unknown error"));

        if (result.FileEntry == null || result.FileEntry.Id == 0)
            throw new InvalidOperationException("Failed to upload file: Invalid response");

        // Update cache
        cache[result.FileEntry.Name] = new CachedFileEntry(result.FileEntry.Id, result.FileEntry.Hash, result.FileEntry.File_Size);
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using var fs = File.Create(filename);
        await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var entry = await FindFileEntryAsync(remotename, cancelToken).ConfigureAwait(false);

        if (entry == null)
            throw new FileMissingException($"File not found: {remotename}");

        // Download by hash
        var url = $"file-entries/download/{entry.Hash}";

        var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url),
                HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        await using var responseStream = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
            response.Content.ReadAsStreamAsync(ct)).ConfigureAwait(false);

        using var timeoutStream = stream.ObserveWriteTimeout(_timeouts.ReadWriteTimeout);
        await responseStream.CopyToAsync(timeoutStream, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var entry = await FindFileEntryAsync(remotename, cancelToken).ConfigureAwait(false);

        if (entry == null)
            return; // File doesn't exist, nothing to delete

        var data = new
        {
            entryIds = new[] { entry.Id },
            deleteForever = !_softDelete
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "file-entries/delete")
        {
            Content = JsonContent.Create(data)
        };
        request.Headers.Add("Accept", "application/json");

        try
        {
            var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => client.SendAsync(request, ct)).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);
        }
        catch
        {
            // The request may have reached Drime even though the answer did not reach
            // us, which leaves the cache holding an id that no longer resolves. Drop
            // the whole cache rather than the one name, because an upload asks the
            // cache whether the name is taken and would otherwise skip the delete it
            // has to make before it can reuse the name.
            _fileCache = null;
            throw;
        }

        // Remove from cache
        _fileCache?.TryRemove(remotename, out var _);
    }

    /// <inheritdoc/>
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_path) || _path == "/")
            return; // Root folder always exists

        var pathParts = _path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        long? currentParentId = null;
        var createdNewFolder = false;

        foreach (var part in pathParts)
        {
            // Check if folder already exists
            var existingId = createdNewFolder ? null : await GetFolderIdAsync(currentParentId, part, cancelToken).ConfigureAwait(false);

            if (existingId != null)
            {
                currentParentId = existingId;
                continue;
            }

            // Create new folder
            var data = new
            {
                name = part,
                parentId = currentParentId
            };

            var query = $"?workspaceId={_workspaceId}";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"folders{query}")
            {
                Content = JsonContent.Create(data)
            };
            request.Headers.Add("Accept", "application/json");

            var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => client.SendAsync(request, ct)).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

            var result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => response.Content.ReadFromJsonAsync<FolderEntryResponse>(ct)).ConfigureAwait(false);

            if (result?.Folder == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Failed to create folder: {part}, {result?.Message}");

            currentParentId = result.Folder.Id;
            _folderHashes[result.Folder.Id] = result.Folder.Hash;
            createdNewFolder = true;
        }

        _targetFolderId = currentParentId;
    }

    /// <inheritdoc/>
    public Task TestAsync(bool alsoWrite, CancellationToken cancelToken)
        => this.TestBackendAsync(alsoWrite, cancelToken);

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => Task.FromResult<string[]>([new System.Uri(_apiUrl).Host]);

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _s3HttpClient?.Dispose();
        _s3HttpClient = null;
    }

    /// <summary>
    /// Gets or creates an authenticated HTTP client
    /// </summary>
    private async Task<HttpClient> GetClientAsync(CancellationToken cancelToken)
    {
        if (_httpClient?.DefaultRequestHeaders.Authorization != null)
            return _httpClient;

        _httpClient?.Dispose();
        _httpClient = null;

        var client = HttpClientHelper.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.BaseAddress = new System.Uri(_apiUrl.TrimEnd('/') + "/");

        // If API token provided, use it directly
        if (!string.IsNullOrWhiteSpace(_apiToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            return _httpClient = client;
        }

        // Otherwise use username/password to obtain token
        var data = new
        {
            email = _authOptions.Username,
            password = _authOptions.Password,
            device_name = $"Duplicati v{AutoUpdater.UpdaterManager.SelfVersion.Version}"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login") { Content = JsonContent.Create(data) };
        request.Headers.Add("Accept", "application/json");

        var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => client.SendAsync(request, ct)).ConfigureAwait(false);

        AuthResponse? result = null;
        try
        {
            result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => response.Content.ReadFromJsonAsync<AuthResponse>(ct)).ConfigureAwait(false);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(result?.User?.Banned_At))
            throw new UserInformationException(Strings.DrimeCloud.UserBannedError(result.User.Banned_At), "DrimeCloudUserBanned");

        if (result == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            throw new UserInformationException(Strings.DrimeCloud.AuthenticationFailedError((int)response.StatusCode, result?.Message ?? "Unknown error"), "DrimeCloudAuthFailed");

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        var token = result.User?.Access_Token;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Failed to obtain access token");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _httpClient = client;
    }

    /// <summary>
    /// Gets the target folder ID, resolving the path if necessary
    /// </summary>
    private async Task<long?> GetTargetFolderIdAsync(CancellationToken cancelToken)
    {
        if (_targetFolderId.HasValue)
            return _targetFolderId.Value;

        if (string.IsNullOrWhiteSpace(_path) || _path == "/")
            return null; // Root folder has no ID

        var pathParts = _path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        long? currentParentId = null;

        foreach (var part in pathParts)
        {
            currentParentId = await GetFolderIdAsync(currentParentId, part, cancelToken).ConfigureAwait(false);
            if (currentParentId == null)
                throw new FolderMissingException(Strings.DrimeCloud.FolderNotFoundError(part));
        }

        return _targetFolderId = currentParentId;
    }

    /// <summary>
    /// Finds a folder by name within a parent folder
    /// </summary>
    private async Task<long?> GetFolderIdAsync(long? parentId, string folderName, CancellationToken cancelToken)
    {
        var entries = await ListEntriesAsync(parentId, foldersOnly: true, cancelToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            if (string.Equals(entry.Name, folderName, StringComparison.OrdinalIgnoreCase)
                && entry.Type == FOLDER_TYPE
                && string.IsNullOrWhiteSpace(entry.Deleted_At)
                && entry.Parent_Id == parentId)
            {
                _folderHashes[entry.Id] = entry.Hash;
                return entry.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Concrete folders use timestamp windows and independent folder counts.
    /// Root path discovery retains the existing folder-only pagination.
    /// </summary>
    private async Task<List<Model.FileEntry>> ListEntriesAsync(long? parentId, bool foldersOnly, CancellationToken cancelToken)
    {
        if (!parentId.HasValue)
            return await ListEntriesUsingFilterAsync(parentId, foldersOnly, useFolderId: true, cancelToken).ConfigureAwait(false);

        if (!_folderHashes.TryGetValue(parentId.Value, out var hash) || string.IsNullOrWhiteSpace(hash))
            throw new DrimePaginationException("The resolved folder did not supply a hash for folder-scoped listing.");

        // Count all children, including folders; filtering before this check
        // would compare unlike counts and could hide missing entries.
        return await ListFolderWindowsAsync(parentId.Value, hash, cancelToken).ConfigureAwait(false);
    }

    private async Task<long> GetFolderCountAsync(long folderId, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"folders/{folderId}/count?workspaceId={_workspaceId}");
        request.Headers.Add("Accept", "application/json");
        using var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            ct => client.SendAsync(request, ct)).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);
        using var document = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)).ConfigureAwait(false);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("count", out var value)
            || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count) || count < 0)
            throw new DrimePaginationException("The separate folder count was missing or invalid.");
        return count;
    }

    private static DateTimeOffset ParseListingDate(string value)
    {
        // Require an explicit timezone; a machine's local timezone must not
        // silently change the inclusive continuation boundary.
        if (string.IsNullOrWhiteSpace(value) || value.Length < 20
            || !(value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && (value[^6] == '+' || value[^6] == '-')))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new DrimePaginationException("A listing entry contained an invalid created_at timestamp.");
        return date.ToUniversalTime();
    }

    private async Task<List<Model.FileEntry>> ListFolderWindowsAsync(long folderId, string folderHash, CancellationToken cancelToken)
    {
        var expected = await GetFolderCountAsync(folderId, cancelToken).ConfigureAwait(false);
        var seen = new Dictionary<long, Model.FileEntry>();
        var ordered = new List<Model.FileEntry>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? lower = null;
        DateTimeOffset? lastDate = null;
        var page = 1;
        var window = 1;
        var requests = 0L;
        // Allows small configured pages and large timestamp ties, while
        // preventing unlimited work on a non-progressing remote listing.
        var requestLimit = Math.Max(1000L, expected > long.MaxValue / 4 ? long.MaxValue : expected * 4);
        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "DrimeListingStart",
            "Drime folder {0}: expecting {1} entries", folderId, expected);

        while (seen.Count < expected)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (++requests > requestLimit)
                throw new DrimePaginationException($"Listing request limit reached with {seen.Count}/{expected} IDs.");
            var result = await GetWindowPageAsync(folderHash, page, lower, cancelToken).ConfigureAwait(false);
            if (result.Data == null || result.Data.Count == 0)
                throw new DrimePaginationException($"Empty page before completeness: {seen.Count}/{expected} IDs.");

            var fingerprint = string.Join(",", result.Data.Select(e => e.Id).OrderBy(id => id));
            if (!fingerprints.Add(fingerprint))
                throw new DrimePaginationException($"Repeated page in window {window}; {seen.Count}/{expected} IDs.");

            foreach (var entry in result.Data)
            {
                if (entry.Id <= 0 || entry.Parent_Id != folderId)
                    throw new DrimePaginationException("A listing entry had an invalid ID or belonged to another folder.");
                var date = ParseListingDate(entry.Created_At);
                if ((lower.HasValue && date < lower.Value) || (lastDate.HasValue && date < lastDate.Value))
                    throw new DrimePaginationException("Drime ignored the date boundary or returned timestamps out of order.");
                lastDate = date;
                if (seen.TryGetValue(entry.Id, out var prior))
                {
                    if (prior.Name != entry.Name || prior.File_Size != entry.File_Size || prior.Type != entry.Type
                        || prior.Hash != entry.Hash || prior.Deleted_At != entry.Deleted_At
                        || prior.Updated_At != entry.Updated_At || ParseListingDate(prior.Created_At) != date)
                        throw new DrimePaginationException("Metadata changed for a repeated entry ID while listing.");
                }
                else
                {
                    seen.Add(entry.Id, entry);
                    ordered.Add(entry);
                }
            }
            if (seen.Count > expected)
                throw new DrimePaginationException("The listing contains more unique IDs than the separate folder count.");

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "DrimeListingProgress",
                "Drime folder {0}: {1}/{2} unique IDs, window {3}, page {4}", folderId, seen.Count, expected, window, page);

            if (page >= WINDOW_RESTART_PAGE && (!lower.HasValue || lastDate > lower))
            {
                // Keep the boundary inclusive. Never advance past equal
                // timestamps: that could silently omit other entries.
                lower = lastDate;
                page = 1;
                window++;
                fingerprints.Clear();
            }
            else
                page = checked(page + 1);
        }

        var after = await GetFolderCountAsync(folderId, cancelToken).ConfigureAwait(false);
        if (after != expected)
            throw new DrimePaginationException($"Folder count changed during listing: {expected} -> {after}.");
        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "DrimeListingComplete",
            "Drime folder {0}: validated {1} unique IDs against both folder counts", folderId, seen.Count);
        // No partial listing is exposed to Duplicati. Different IDs with the
        // same name deliberately survive so real duplicates remain visible.
        return ordered;
    }

    private async Task<PaginatedResponse<Model.FileEntry>> GetWindowPageAsync(
        string folderHash, int page, DateTimeOffset? lower, CancellationToken cancelToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["pageId"] = folderHash,
            ["folderId"] = folderHash,
            ["workspaceId"] = _workspaceId.ToString(CultureInfo.InvariantCulture),
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["perPage"] = Math.Min(_pageSize, 100).ToString(CultureInfo.InvariantCulture),
            ["orderBy"] = "created_at",
            ["orderDir"] = "asc"
        };
        if (lower.HasValue)
        {
            var filter = new[] { new Dictionary<string, string>
            {
                ["key"] = "created_at", ["operator"] = ">=",
                ["value"] = lower.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture)
            }};
            // Match encodeDriveFilters, followed by URL query encoding.
            parameters["filters"] = System.Uri.EscapeDataString(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(filter)));
        }
        var query = string.Join("&", parameters.Select(kv => $"{kv.Key}={System.Uri.EscapeDataString(kv.Value)}"));
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        int? returnedPage = null;
        for (var attempt = 1; attempt <= PAGE_VALIDATION_ATTEMPTS; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"drive/file-entries?{query}");
            request.Headers.Add("Accept", "application/json");
            using var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
                ct => client.SendAsync(request, ct)).ConfigureAwait(false);
            await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);
            var result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => response.Content.ReadFromJsonAsync<PaginatedResponse<Model.FileEntry>>(_jsonOptions, ct)).ConfigureAwait(false);
            returnedPage = result?.Current_Page;
            if (returnedPage == page)
                return result!;
            if (attempt < PAGE_VALIDATION_ATTEMPTS)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancelToken).ConfigureAwait(false);
        }
        throw new DrimePaginationException($"Requested page {page}, but Drime returned current_page={returnedPage} in all {PAGE_VALIDATION_ATTEMPTS} attempts. Timestamp-window listing is incomplete.");
    }

    /// <summary>
    /// Retrieves a complete listing with strict progress and completeness
    /// checks. Exact duplicate entry IDs caused by overlapping API pages are
    /// suppressed only when the final unique count matches Drime's total.
    /// Different IDs with the same name are intentionally preserved so that
    /// Duplicati can report real duplicate remote files.
    /// </summary>
    private async Task<List<Model.FileEntry>> ListEntriesUsingFilterAsync(
        long? parentId,
        bool foldersOnly,
        bool useFolderId,
        CancellationToken cancelToken)
    {
        var requestedPage = 1;
        var entriesById = new Dictionary<long, Model.FileEntry>();
        var orderedEntries = new List<Model.FileEntry>();
        var pageFingerprints = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var entries = await GetValidatedListingPageAsync(
                parentId,
                foldersOnly,
                useFolderId,
                requestedPage,
                cancelToken).ConfigureAwait(false);

            if (entries.Total == null || entries.Total.Value < 0)
                throw new DrimePaginationException($"Page {requestedPage} did not contain a valid total entry count.");

            if (entries.Last_Page == null || entries.Last_Page.Value < entries.Current_Page)
                throw new DrimePaginationException(
                    $"Page {requestedPage} returned an invalid last_page value ({entries.Last_Page?.ToString() ?? "null"}).");

            if (entries.Data.Count == 0)
            {
                if (entries.Current_Page == entries.Last_Page.Value && entriesById.Count == entries.Total.Value)
                    return orderedEntries;

                throw new DrimePaginationException(
                    $"Page {requestedPage} was empty after receiving {entriesById.Count} unique entries; " +
                    $"Drime reported total={entries.Total.Value} and last_page={entries.Last_Page.Value}.");
            }

            var fingerprint = string.Join(",", entries.Data.Select(entry => entry.Id));
            if (!pageFingerprints.Add(fingerprint))
                throw new DrimePaginationException(
                    $"Page {requestedPage} repeated a page that was already returned by Drime.");

            foreach (var entry in entries.Data)
            {
                if (entry.Id <= 0)
                    throw new DrimePaginationException($"Page {requestedPage} contained an entry without a valid ID.");

                if (parentId.HasValue && entry.Parent_Id != parentId)
                    throw new DrimePaginationException(
                        $"Page {requestedPage} returned entry {entry.Id} from parent {entry.Parent_Id?.ToString() ?? "null"} " +
                        $"while listing parent {parentId.Value}.");

                if (entriesById.TryAdd(entry.Id, entry))
                    orderedEntries.Add(entry);
            }

            if (entries.Current_Page == entries.Last_Page.Value)
            {
                // Drime grows both last_page and total while pagination is in
                // progress. Only the values on the terminal page describe the
                // complete listing and are therefore used for the final check.
                if (entriesById.Count != entries.Total.Value)
                    throw new DrimePaginationException(
                        $"Drime marked page {requestedPage} as the last page after returning {entriesById.Count} unique entries, " +
                        $"but the terminal page reported total={entries.Total.Value}.");

                return orderedEntries;
            }

            requestedPage = entries.Current_Page + 1;
        }
    }

    /// <summary>
    /// Requests a page and verifies that Drime returned the requested page.
    /// A bounded retry handles transient stale responses without ever skipping
    /// a page.
    /// </summary>
    private async Task<PaginatedResponse<Model.FileEntry>> GetValidatedListingPageAsync(
        long? parentId,
        bool foldersOnly,
        bool useFolderId,
        int requestedPage,
        CancellationToken cancelToken)
    {
        PaginatedResponse<Model.FileEntry>? lastResponse = null;

        for (var attempt = 1; attempt <= PAGE_VALIDATION_ATTEMPTS; attempt++)
        {
            lastResponse = await GetListingPageAsync(
                parentId,
                foldersOnly,
                useFolderId,
                requestedPage,
                cancelToken).ConfigureAwait(false);

            if (lastResponse.Current_Page == requestedPage)
                return lastResponse;

            if (attempt < PAGE_VALIDATION_ATTEMPTS)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancelToken).ConfigureAwait(false);
        }

        throw new DrimePaginationException(
            $"Requested page {requestedPage}, but Drime returned current_page={lastResponse?.Current_Page.ToString() ?? "null"} " +
            $"in all {PAGE_VALIDATION_ATTEMPTS} attempts.");
    }

    /// <summary>
    /// Requests one Drime listing page with deterministic ordering.
    /// </summary>
    private async Task<PaginatedResponse<Model.FileEntry>> GetListingPageAsync(
        long? parentId,
        bool foldersOnly,
        bool useFolderId,
        int page,
        CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var queryParams = new Dictionary<string, string>
        {
            ["page"] = page.ToString(),
            ["perPage"] = Math.Min(_pageSize, MAX_PAGE_SIZE).ToString(),
            ["workspaceId"] = _workspaceId.ToString(),
            ["orderBy"] = "name",
            ["orderDir"] = "asc"
        };

        if (foldersOnly)
            queryParams["type"] = FOLDER_TYPE;

        if (parentId.HasValue)
            queryParams[useFolderId ? "folderId" : "parentIds"] = parentId.Value.ToString();

        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={System.Uri.EscapeDataString(kvp.Value)}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"drive/file-entries?{queryString}");
        request.Headers.Add("Accept", "application/json");

        using var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            ct => client.SendAsync(request, ct)).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        var entries = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
            response.Content.ReadFromJsonAsync<PaginatedResponse<Model.FileEntry>>(_jsonOptions, ct)).ConfigureAwait(false);

        return entries ?? throw new DrimePaginationException($"Page {page} returned an empty JSON response.");
    }

    /// <summary>
    /// Finds a file entry by name
    /// </summary>
    private async Task<CachedFileEntry?> FindFileEntryAsync(string name, CancellationToken cancelToken)
    {
        if (_fileCache != null && _fileCache.TryGetValue(name, out var cached))
            return cached;

        // Rebuild cache
        _fileCache = null;
        var cache = await GetFileCacheAsync(cancelToken).ConfigureAwait(false);

        return cache.TryGetValue(name, out var entry) ? entry : null;
    }

    /// <summary>
    /// Gets the file cache, populating it if necessary
    /// </summary>
    private async Task<ConcurrentDictionary<string, CachedFileEntry>> GetFileCacheAsync(CancellationToken cancelToken)
    {
        if (_fileCache != null)
            return _fileCache;

        await foreach (var _ in ListAsync(cancelToken).ConfigureAwait(false))
        {
            // ListAsync populates the cache
        }

        if (_fileCache == null)
            throw new InvalidOperationException("File cache was not populated during listing");

        return _fileCache;
    }

    /// <summary>
    /// Parses a timestamp string to DateTime
    /// </summary>
    private static DateTime ParseDateTime(string timestamp)
    {
        if (DateTime.TryParseExact(
            timestamp,
            "yyyy-MM-ddTHH:mm:ss.ffffffZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var result))
        {
            return result;
        }

        return new DateTime(0);
    }

    /// <summary>
    /// Ensures the HTTP response was successful, throwing appropriate exceptions
    /// </summary>
    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new FileMissingException();

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UserInformationException("Authentication failed. Please check your API token or credentials.", "DrimeCloudAuthError");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UserInformationException("Access denied. Check your permissions.", "DrimeCloudPermissionDenied");

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Try to parse JSON error response, but handle non-JSON responses gracefully
        string? errorMessage = null;
        ErrorResponse? result = null;
        try
        {
            result = JsonSerializer.Deserialize<ErrorResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            errorMessage = result?.Message;
        }
        catch (JsonException)
        {
            // Not a JSON response, use raw body or status code
            if (!string.IsNullOrWhiteSpace(body))
                errorMessage = body.Trim();
        }

        // Deleting an entry that is no longer there is reported as a validation
        // failure on the ids rather than as a 404, and entryIds is only ever sent
        // by DeleteAsync. BackendManager confirms this against a listing before it
        // accepts the delete, so report it as the file being missing rather than
        // deciding here that the delete succeeded.
        if (result?.Errors?.ContainsKey("entryIds") == true)
            throw new FileMissingException();

        if (!string.IsNullOrWhiteSpace(errorMessage))
            throw new InvalidOperationException($"Request failed: {errorMessage}");

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Uploads a file using multipart S3 upload for files >= 5MB
    /// </summary>
    private async Task PutMultipartAsync(string remotename, Stream stream, long? folderId, CancellationToken cancelToken)
    {
        const int PART_SIZE = 5 * 1024 * 1024; // 5,242,880 bytes
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var fileSize = stream.Length;
        var extension = Path.GetExtension(remotename).TrimStart('.');

        // Step 1: Create multipart upload
        var createData = new
        {
            filename = remotename,
            mime = "application/octet-stream",
            size = fileSize,
            extension = string.IsNullOrEmpty(extension) ? "bin" : extension,
            workspaceId = _workspaceId,
            parentId = folderId
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "s3/multipart/create") { Content = JsonContent.Create(createData) };
        createRequest.Headers.Add("Accept", "application/json");

        var createResponse = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => client.SendAsync(createRequest, ct)).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(createResponse).ConfigureAwait(false);

        var createResult = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => createResponse.Content.ReadFromJsonAsync<CreateMultipartResponse>(ct)).ConfigureAwait(false);

        if (createResult == null || !string.Equals(createResult.Status, "success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Failed to create multipart upload");

        var key = createResult.Key;
        var uploadId = createResult.UploadId;

        try
        {
            // Step 2: Calculate parts and sign URLs
            var totalParts = (int)Math.Ceiling((double)fileSize / PART_SIZE);
            var partNumbers = Enumerable.Range(1, totalParts).ToList();

            var signData = new
            {
                key,
                uploadId,
                partNumbers
            };

            using var signRequest = new HttpRequestMessage(HttpMethod.Post, "s3/multipart/batch-sign-part-urls") { Content = JsonContent.Create(signData) };
            signRequest.Headers.Add("Accept", "application/json");

            var signResponse = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => client.SendAsync(signRequest, ct)).ConfigureAwait(false);
            await EnsureSuccessStatusCodeAsync(signResponse).ConfigureAwait(false);

            var signResult = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => signResponse.Content.ReadFromJsonAsync<SignPartUrlsResponse>(ct)).ConfigureAwait(false);

            if (signResult?.Urls == null || signResult.Urls.Count == 0)
                throw new InvalidOperationException("Failed to get signed URLs for multipart upload");

            // Step 3: Upload each part
            var uploadedParts = new List<MultipartPart>();

            foreach (var partUrl in signResult.Urls.OrderBy(u => u.PartNumber))
            {
                var bytesToRead = Math.Min(PART_SIZE, fileSize - stream.Position);

                using var partContent = new StreamContent(new ReadLimitLengthStream(stream, stream.Position, bytesToRead)
                    .ObserveReadTimeout(_timeouts.ReadWriteTimeout, false));

                // The presigned URL already contains all required signature parameters.
                // We must NOT add any extra headers (including Content-Type) as they would
                // conflict with the presigned URL signature.
                partContent.Headers.ContentType = null;
                partContent.Headers.ContentLength = bytesToRead;

                // Use a dedicated HttpClient for S3 uploads to avoid the Authorization header
                // from the Drime Cloud API interfering with S3 signature verification
                if (_s3HttpClient == null)
                {
                    _s3HttpClient = HttpClientHelper.CreateClient();
                    _s3HttpClient.Timeout = Timeout.InfiniteTimeSpan;
                }

                // The part is handled by a timeout observing stream, so we don't need to use the WithTimeout method
                var partResponse = await _s3HttpClient.PutAsync(partUrl.Url, partContent, cancelToken).ConfigureAwait(false);

                if (!partResponse.IsSuccessStatusCode)
                {
                    var partBody = await partResponse.Content.ReadAsStringAsync(cancelToken).ConfigureAwait(false);
                    throw new InvalidOperationException($"Failed to upload part {partUrl.PartNumber}: {partResponse.StatusCode} - {partBody}");
                }

                var etag = partResponse.Headers.ETag?.ToString();
                if (string.IsNullOrWhiteSpace(etag))
                {
                    partResponse.Headers.TryGetValues("ETag", out var values);
                    etag = values?.FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(etag))
                    throw new InvalidOperationException($"Missing ETag for part {partUrl.PartNumber}");

                uploadedParts.Add(new MultipartPart { PartNumber = partUrl.PartNumber, ETag = etag });
            }

            // Step 4: Complete multipart upload
            var completeData = new
            {
                key,
                uploadId,
                parts = uploadedParts.OrderBy(p => p.PartNumber).ToList()
            };

            using var completeRequest = new HttpRequestMessage(HttpMethod.Post, "s3/multipart/complete")
            {
                // NOTE: Has odd casing requirements for the "parts" elements
                Content = JsonContent.Create(completeData, options: JsonSerializerOptions.Default)
            };
            completeRequest.Headers.Add("Accept", "application/json");

            var completeResponse = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => client.SendAsync(completeRequest, ct)).ConfigureAwait(false);
            await EnsureSuccessStatusCodeAsync(completeResponse).ConfigureAwait(false);

            // Step 5: Register the file entry
            var uuid = key.Split('/').Last();
            var registerData = new
            {
                filename = uuid,
                size = fileSize,
                clientName = remotename,
                clientMime = "application/octet-stream",
                clientExtension = string.IsNullOrEmpty(extension) ? "bin" : extension,
                workspaceId = _workspaceId,
                parentId = folderId
            };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, "s3/entries") { Content = JsonContent.Create(registerData) };
            registerRequest.Headers.Add("Accept", "application/json");

            var registerResponse = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => client.SendAsync(registerRequest, ct)).ConfigureAwait(false);
            await EnsureSuccessStatusCodeAsync(registerResponse).ConfigureAwait(false);

            var registerResult = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                ct => registerResponse.Content.ReadFromJsonAsync<CreateS3EntryResponse>(ct)).ConfigureAwait(false);

            if (registerResult?.FileEntry == null || !string.Equals(registerResult.Status, "success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Failed to register uploaded file");

            // Update cache
            if (_fileCache != null)
                _fileCache[remotename] = new CachedFileEntry(registerResult.FileEntry.Id, registerResult.FileEntry.Hash, registerResult.FileEntry.File_Size);
        }
        catch
        {
            // If the upload fails, clear the attempt if we can
            try
            {
                using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, "s3/multipart/abort")
                {
                    Content = JsonContent.Create(new
                    {
                        key,
                        uploadId
                    })
                };
                cancelRequest.Headers.Add("Accept", "application/json");

                using var cancelResponse = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                    ct => client.SendAsync(cancelRequest, ct)).ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Cached file entry information
    /// </summary>
    private sealed record CachedFileEntry(long Id, string Hash, long Size);

    /// <summary>
    /// Raised when Drime's pagination metadata or returned entries cannot be
    /// proven to form a complete remote listing.
    /// </summary>
    private sealed class DrimePaginationException : Exception
    {
        public DrimePaginationException(string message)
            : base(message)
        {
        }

        public DrimePaginationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
