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
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Backend.DrimeCloud.Model;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Duplicati.StreamUtil;
using FileEntry = Duplicati.Library.Common.IO.FileEntry;
using UtilityUri = Duplicati.Library.Utility.Uri;

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
        var uri = new UtilityUri(url);
        _path = uri.HostAndPath?.Trim('/') ?? "";

        if (string.IsNullOrWhiteSpace(_path))
            _path = "/";
        else
            _path = $"/{_path}/";

        // Get API token or parse auth options
        _apiToken = options.GetValueOrDefault(API_TOKEN_OPTION);
        _authOptions = AuthOptionsHelper.Parse(options, uri);

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
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var folderId = await GetTargetFolderIdAsync(cancelToken).ConfigureAwait(false);

        int page = 1;
        _fileCache = null;
        var cache = new ConcurrentDictionary<string, CachedFileEntry>();

        while (true)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["page"] = page.ToString(),
                ["perPage"] = _pageSize.ToString(),
                ["workspaceId"] = _workspaceId.ToString()
            };

            if (folderId.HasValue)
                queryParams["parentIds"] = folderId.Value.ToString();

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={System.Uri.EscapeDataString(kvp.Value)}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"drive/file-entries?{queryString}");
            request.Headers.Add("Accept", "application/json");

            var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
                ct => client.SendAsync(request, ct)).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

            var entries = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
                response.Content.ReadFromJsonAsync<PaginatedResponse<Model.FileEntry>>(_jsonOptions, ct)).ConfigureAwait(false);

            if (entries?.Data == null || entries.Data.Count == 0)
                break;

            foreach (var entry in entries.Data)
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

            if (entries.Last_Page == null || page >= entries.Last_Page.Value)
                break;

            page++;
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

        var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
            ct => client.SendAsync(request, ct)).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

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
            createdNewFolder = true;
        }

        _targetFolderId = currentParentId;
    }

    /// <inheritdoc/>
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestReadWritePermissionsAsync(cancelToken);

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
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var page = 1;

        while (true)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["page"] = page.ToString(),
                ["perPage"] = _pageSize.ToString(),
                ["workspaceId"] = _workspaceId.ToString(),
                ["type"] = FOLDER_TYPE
            };

            if (parentId.HasValue)
                queryParams["parentIds"] = parentId.Value.ToString();

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={System.Uri.EscapeDataString(kvp.Value)}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"drive/file-entries?{queryString}");
            request.Headers.Add("Accept", "application/json");

            var response = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
                ct => client.SendAsync(request, ct)).ConfigureAwait(false);

            await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

            var entries = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
                ct => response.Content.ReadFromJsonAsync<PaginatedResponse<Model.FileEntry>>(ct)).ConfigureAwait(false);

            if (entries?.Data == null || entries.Data.Count == 0)
                break;

            foreach (var entry in entries.Data)
            {
                if (string.Equals(entry.Name, folderName, StringComparison.OrdinalIgnoreCase)
                    && entry.Type == FOLDER_TYPE
                    && string.IsNullOrWhiteSpace(entry.Deleted_At)
                    && entry.Parent_Id == parentId)
                {
                    return entry.Id;
                }
            }

            if (entries.Last_Page == null || page >= entries.Last_Page.Value)
                break;

            page++;
        }

        return null;
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
        try
        {
            var result = JsonSerializer.Deserialize<ErrorResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            errorMessage = result?.Message;
        }
        catch (JsonException)
        {
            // Not a JSON response, use raw body or status code
            if (!string.IsNullOrWhiteSpace(body))
                errorMessage = body.Trim();
        }

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
}
