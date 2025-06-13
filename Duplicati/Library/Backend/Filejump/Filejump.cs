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

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// FileJump backend implementation for Duplicati.
    /// </summary>
    public class Filejump : IBackend, IStreamingBackend
    {
        /// <summary>
        /// Name of the API URL option.
        /// </summary>
        private const string API_URL_OPTION = "api-url";
        /// <summary>
        /// Name of the page size option.
        /// </summary>
        private const string PAGE_SIZE_OPTION = "page-size";
        /// <summary>
        /// Name of the soft delete option.
        /// </summary>
        private const string SOFT_DELETE_OPTION = "soft-delete";
        /// <summary>
        /// Name of the API token option.
        /// </summary>
        private const string API_TOKEN_OPTION = "api-token";
        /// <summary>
        /// Default value for the soft delete option.
        /// </summary>
        private const string SOFT_DELETE_DEFAULT = "false";
        /// <summary>
        /// Default API URL for filejump.
        /// </summary>
        private const string DEFAULT_API_URL = "https://drive.filejump.com/api/v1/";
        /// <summary>
        /// The expiration time for the token.
        /// </summary>
        private static readonly TimeSpan TOKEN_EXPIRATION = TimeSpan.FromHours(1);
        /// <summary>
        /// Default page size for filejump API requests.
        /// </summary>
        private const int DEFAULT_PAGE_SIZE = 1000;
        /// <summary>
        /// Type of folder in filejump API.
        /// </summary>
        private const string FOLDER_TYPE = "folder";
        /// <summary>
        /// The path to the filejump folder.
        /// </summary>
        private readonly string m_path;
        /// <summary>
        /// The expiration time of the token.
        /// </summary>
        private DateTime m_tokenExpiration;
        /// <summary>
        /// The HTTP client used to communicate with the filejump API.
        /// </summary>
        private HttpClient? m_client;
        /// <summary>
        /// The ID of the target folder.
        /// </summary>
        private long? m_targetPathId;
        /// <summary>
        /// The API URL for filejump.
        /// </summary>
        private readonly string m_apiUrl;
        /// <summary>
        /// The page size for filejump API requests.
        /// </summary>
        private readonly int m_pageSize;
        /// <summary>
        /// The soft delete option for filejump API requests.
        /// </summary>
        private readonly bool m_softDelete = false;
        /// <summary>
        /// The JSON serializer options used for deserializing API responses.
        /// </summary>
        private readonly JsonSerializerOptions m_jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        /// <summary>
        /// The timeout options for filejump API requests.
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts m_timeoutOptions;
        /// <summary>
        /// The authentication options for filejump API requests.
        /// </summary>
        private readonly AuthOptionsHelper.AuthOptions m_authOptions;
        /// <summary>
        /// The API token for filejump authentication.
        /// </summary>
        private readonly string? m_api_token;
        /// <summary>
        /// The cache for file entry IDs.
        /// </summary>
        private Dictionary<string, CachedFileEntry>? m_fileEntryIdCache;

        public Filejump()
        {
            m_path = null!;
            m_apiUrl = null!;
            m_timeoutOptions = null!;
            m_authOptions = null!;
        }

        public Filejump(string url, Dictionary<string, string?> options)
        {
            var u = new Utility.Uri(url);
            m_path = u.HostAndPath.Trim('/');
            if (string.IsNullOrWhiteSpace(m_path))
                m_path = "/";
            else
                m_path = $"/{m_path}/";

            m_api_token = options.GetValueOrDefault(API_TOKEN_OPTION);
            m_authOptions = AuthOptionsHelper.Parse(options, u);
            if (string.IsNullOrWhiteSpace(m_api_token) && (!m_authOptions.HasUsername || !m_authOptions.HasPassword))
                throw new ArgumentException("Either an API token, or username/password are required for filejump authentication.");

            m_timeoutOptions = TimeoutOptionsHelper.Parse(options);
            m_apiUrl = options.GetValueOrDefault(API_URL_OPTION) ?? "";
            if (string.IsNullOrWhiteSpace(m_apiUrl))
                m_apiUrl = DEFAULT_API_URL;
            m_pageSize = Utility.Utility.ParseIntOption(options, PAGE_SIZE_OPTION, DEFAULT_PAGE_SIZE);
            m_softDelete = Utility.Utility.ParseBoolOption(options, SOFT_DELETE_OPTION);

            m_client = HttpClientHelper.CreateClient();
            m_client.Timeout = Timeout.InfiniteTimeSpan;
            m_client.BaseAddress = new System.Uri(DEFAULT_API_URL);
        }

        ///<inheritdoc/>
        public string ProtocolKey => "filejump";
        ///<inheritdoc/>
        public string DisplayName => Strings.Filejump.DisplayName;
        ///<inheritdoc/>
        public string Description => Strings.Filejump.Description;

        ///<inheritdoc/>
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(API_TOKEN_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Filejump.DescriptionApitokenShort, Strings.Filejump.DescriptionApitokenLong, null),
            .. AuthOptionsHelper.GetOptions(),
            new CommandLineArgument(API_URL_OPTION, CommandLineArgument.ArgumentType.String, Strings.Filejump.DescriptionApiurlShort, Strings.Filejump.DescriptionApiurlLong, DEFAULT_API_URL),
            new CommandLineArgument(PAGE_SIZE_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.Filejump.DescriptionPagesizeShort, Strings.Filejump.DescriptionPagesizeLong, DEFAULT_PAGE_SIZE.ToString()),
            new CommandLineArgument(SOFT_DELETE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Filejump.DescriptionSoftDeleteShort, Strings.Filejump.DescriptionSoftDeleteLong, SOFT_DELETE_DEFAULT),
            .. TimeoutOptionsHelper.GetOptions()
        ];

        /// <summary>
        /// Creates the http client or returns a cached one if it is still valid.
        /// </summary>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>>The http client</returns>
        private async Task<HttpClient> GetClient(CancellationToken cancelToken)
        {
            if (m_client != null && m_tokenExpiration > DateTime.UtcNow.AddMinutes(-5))
                return m_client;

            m_client?.Dispose();
            m_client = null;

            var client = HttpClientHelper.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.BaseAddress = new System.Uri(m_apiUrl);

            if (!string.IsNullOrWhiteSpace(m_api_token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", m_api_token);
                m_tokenExpiration = DateTime.UtcNow.Add(TOKEN_EXPIRATION);
                return m_client = client;
            }

            var data = new { email = m_authOptions.Username, password = m_authOptions.Password, token_name = $"Duplicati v{AutoUpdater.UpdaterManager.SelfVersion.Version}" };
            var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login") { Content = content };
            request.Headers.Add("Accept", "application/json");
            var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ShortTimeout, cancelToken, ct => client.SendAsync(request, ct));

            var body = await response.Content.ReadAsStringAsync(cancelToken);
            var result = JsonSerializer.Deserialize<AuthResponseOuter>(body, m_jsonOptions);
            if (result == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                throw new UserInformationException($"Failed to authenticate: {result?.Message}", "LoginFailed");
            if (!string.IsNullOrWhiteSpace(result?.User?.Banned_At))
                throw new UserInformationException($"User is banned: {result.User.Banned_At}", "UserBanned");
            response.EnsureSuccessStatusCode();
            var token = result?.User?.Access_Token ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Failed to get access token");
            m_tokenExpiration = DateTime.UtcNow.Add(TOKEN_EXPIRATION);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return m_client = client;
        }

        ///<inheritdoc/>
        public async Task PutAsync(string remotename, string filename, CancellationToken token)
        {
            using var fs = File.OpenRead(filename);
            await PutAsync(remotename, fs, token);
        }

        ///<inheritdoc/>
        public async Task PutAsync(string remotename, Stream stream, CancellationToken token)
        {
            var client = await GetClient(token);
            var folderId = await GetTargetPathIdAsync(m_path, token);
            if (folderId == null && !string.IsNullOrWhiteSpace(m_path))
                throw new FolderMissingException($"Folder not found: {m_path}");

            // Filejump does not support overwrite, so we need to delete the previous version if it exists
            var cache = await GetCacheTable(token);
            cache.TryGetValue(remotename, out var _);
            await DeleteAsync(remotename, token);

            var content = new MultipartFormDataContent();
            using var timeoutStream = stream.ObserveReadTimeout(m_timeoutOptions.ReadWriteTimeout);
            var fileContent = new StreamContent(timeoutStream);
            content.Add(fileContent, "file", remotename);
            if (folderId.HasValue)
                content.Add(new StringContent(folderId.Value.ToString()), "parentId");

            using var request = new HttpRequestMessage(HttpMethod.Post, "uploads") { Content = content };
            request.Headers.Add("Accept", "application/json");

            var response = await client.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);
            var result = JsonSerializer.Deserialize<FileEntryOuter>(body, m_jsonOptions);
            if (result == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Failed to upload file: {result?.Message}");

            if (string.IsNullOrWhiteSpace(result?.FileEntry?.Id.ToString()))
                throw new InvalidOperationException($"Failed to upload file: {result?.Message}");
            await EnsureSuccessStatusCode(response);

            cache[result.FileEntry.Name] = new(result.FileEntry.Id, result.FileEntry.Url);
        }

        ///<inheritdoc/>
        public async Task GetAsync(string remotename, string filename, CancellationToken token)
        {
            using var fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
            await GetAsync(remotename, fs, token);
        }

        ///<inheritdoc/>
        public async Task GetAsync(string remotename, Stream stream, CancellationToken token)
        {
            var client = await GetClient(token);

            var entry = await FindFileEntryIdAsync(remotename, token);
            if (entry == null)
                throw new FileMissingException($"File not found: {remotename}");

            var url = entry.Url;
            if (!url.StartsWith("/") && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "/" + url;

            var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ShortTimeout, token,
                ct => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct));
            response.EnsureSuccessStatusCode();
            using var timeoutStream = stream.ObserveWriteTimeout(m_timeoutOptions.ReadWriteTimeout);
            await response.Content.CopyToAsync(timeoutStream, token);
        }

        /// <summary>
        /// Parses the date time from the string returned by the API (ISO-8601 format).
        /// </summary>
        /// <param name="timestamp">The timestamp to parse</param>
        /// <returns>>The parsed date time</returns>
        private static DateTime ParseDateTime(string timestamp)
        {
            if (DateTime.TryParseExact(
                timestamp,
                "yyyy-MM-ddTHH:mm:ss.ffffffZ",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var result
            )) return result;

            return new DateTime(0);
        }

        /// <summary>
        /// Gets the folder ID for the given path. The path is split into parts and each part is searched for in the parent folder.
        /// </summary>
        /// <param name="folderPath">The path to the folder</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>>The ID of the folder, or null if not found</returns>
        private async Task<long?> GetTargetPathIdAsync(string folderPath, CancellationToken token)
        {
            if (m_targetPathId.HasValue)
                return m_targetPathId.Value;

            if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "/")
                return null;

            var pathParts = folderPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            long? currentParentId = null;

            foreach (var part in pathParts)
            {
                currentParentId = await GetFolderIdAsync(currentParentId, part, token);
                if (currentParentId == null)
                    throw new FolderMissingException($"Folder not found: {part}");
            }

            return m_targetPathId = currentParentId;
        }


        /// <summary>
        /// Gets the folder ID for the given folder.
        /// </summary>
        /// <param name="parentFolderId">The ID of the parent folder</param>
        /// <param name="folderName">The name of the folder</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>>The ID of the folder, or null if not found</returns>
        private async Task<long?> GetFolderIdAsync(long? parentFolderId, string folderName, CancellationToken token)
        {
            var client = await GetClient(token);

            int page = 1;
            var currentParentId = parentFolderId;

            while (true)
            {
                // Note: In principle, we can add /&query={folderName}" to the URL, but it is super slow
                var url = $"drive/file-entries?page={page}&perPage={m_pageSize}&parentIds={currentParentId}&type={FOLDER_TYPE}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/json");
                var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ListTimeout, token, ct => client.SendAsync(request, ct));
                await EnsureSuccessStatusCode(response);

                var body = await response.Content.ReadAsStringAsync(token);
                var entries = JsonSerializer.Deserialize<PagedListResponse>(body, m_jsonOptions);
                if (entries?.Data == null || entries.Data.Count == 0)
                    break;

                foreach (var entry in entries.Data)
                    if (string.Equals(entry.Type, FOLDER_TYPE, StringComparison.OrdinalIgnoreCase)
                        && entry.Name == folderName
                        && string.IsNullOrWhiteSpace(entry.Deleted_At)
                        && ((entry.Parent_Id.HasValue && entry.Parent_Id.Value == currentParentId) || (entry.Parent_Id == null && currentParentId == null)))
                        return entry.Id;

                if (entries.Data.Count < entries.Per_Page || entries.Next_Page == null)
                    break;

                page++;
            }

            return null;
        }

        /// <summary>
        /// Gets the cache table for file entries. It first checks if the cache is already populated, and if not, it lists all entries to populate the cache.
        /// </summary>
        /// <param name="token">The cancellation token</param>
        /// <returns>>The cache table</returns>
        private async Task<Dictionary<string, CachedFileEntry>> GetCacheTable(CancellationToken token)
        {
            if (m_fileEntryIdCache != null)
                return m_fileEntryIdCache;

            await foreach (var _ in ListAsync(token))
            {
                // The list method populates the cache
            }

            return m_fileEntryIdCache!;
        }

        ///<inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken token)
        {
            var client = await GetClient(token);
            var folderId = await GetTargetPathIdAsync(m_path, token);

            int page = 1;
            m_fileEntryIdCache = null;
            var cache = new Dictionary<string, CachedFileEntry>();

            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"drive/file-entries?page={page}&perPage={m_pageSize}&parentIds={folderId}");
                request.Headers.Add("Accept", "application/json");
                var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ListTimeout, token, ct => client.SendAsync(request, ct));
                await EnsureSuccessStatusCode(response);

                var body = await response.Content.ReadAsStringAsync(token);
                var entries = JsonSerializer.Deserialize<PagedListResponse>(body, m_jsonOptions);

                if (entries?.Data == null || entries.Data.Count == 0)
                    break;

                foreach (var entry in entries.Data)
                {
                    // Ignore soft deleted files
                    if (!string.IsNullOrWhiteSpace(entry.Deleted_At))
                        continue;

                    if (entry.Type == "file")
                    {
                        cache[entry.Name] = new(entry.Id, entry.Url);
                        var e = new Common.IO.FileEntry(
                            entry.Name,
                            entry.File_Size
                        );

                        e.LastModification = ParseDateTime(entry.Updated_At);
                        e.Created = ParseDateTime(entry.Created_At);
                        e.IsFolder = string.Equals(entry.Type, FOLDER_TYPE, StringComparison.OrdinalIgnoreCase);

                        yield return e;
                    }
                }

                if (entries.Data.Count < entries.Per_Page || entries.Next_Page == null)
                    break;

                page++;
            }

            m_fileEntryIdCache = cache;
        }

        ///<inheritdoc/>
        public async Task DeleteAsync(string remotename, CancellationToken token)
        {
            var client = await GetClient(token);

            var entry = await FindFileEntryIdAsync(remotename, token);
            if (entry == null)
                return;

            var data = new { entryIds = new[] { entry.Id }, deleteForever = !m_softDelete };
            var json = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "file-entries/delete") { Content = json };
            request.Headers.Add("Accept", "application/json");

            var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ShortTimeout, token, ct => client.SendAsync(request, ct));
            await EnsureSuccessStatusCode(response);
        }

        ///<inheritdoc/>
        public async Task CreateFolderAsync(CancellationToken token)
        {
            var client = await GetClient(token);

            if (m_client == null || m_timeoutOptions == null)
                throw new InvalidOperationException("Backend not initialized correctly");

            long? currentParentId = null;
            var pathParts = m_path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var newFolder = false;

            foreach (var part in pathParts)
            {
                var foundId = newFolder ? null : await GetFolderIdAsync(currentParentId, part, token);
                if (foundId != null)
                {
                    currentParentId = foundId;
                    continue;
                }

                var data = new
                {
                    name = part,
                    parentId = currentParentId
                };

                var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, "folders") { Content = content };
                request.Headers.Add("Accept", "application/json");
                var response = await Utility.Utility.WithTimeout(m_timeoutOptions.ShortTimeout, token, ct => m_client.Send(request, ct));
                await EnsureSuccessStatusCode(response);

                var body = await response.Content.ReadAsStringAsync(token);
                var result = JsonSerializer.Deserialize<FolderEntryOuter>(body, m_jsonOptions);
                currentParentId = result?.Folder?.Id;
                if (!string.Equals(result?.Status, "success", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Failed to create folder: {part}, {result?.Message}");
                if (currentParentId == null || currentParentId.Value == 0)
                    throw new InvalidOperationException($"Failed to create folder: {part}");
                newFolder = true;
            }

            m_targetPathId = currentParentId;
        }

        ///<inheritdoc/>
        public Task TestAsync(CancellationToken token)
            => this.TestReadWritePermissionsAsync(token);

        ///<inheritdoc/>
        public Task<string[]> GetDNSNamesAsync(CancellationToken token)
            => Task.FromResult<string[]>([new System.Uri(m_apiUrl).Host]);

        ///<inheritdoc/>
        public void Dispose()
        {
            m_client?.Dispose();
        }

        /// <summary>
        /// Locates the file entry ID for the given name. It first checks the cache, and if not found, it lists all entries to populate the cache.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<CachedFileEntry?> FindFileEntryIdAsync(string name, CancellationToken token)
        {
            if (m_fileEntryIdCache != null && m_fileEntryIdCache.TryGetValue(name, out var entry))
                return entry;

            m_fileEntryIdCache = null;
            var cache = await GetCacheTable(token);

            return cache.TryGetValue(name, out entry) ? entry : null;
        }

        /// <summary>
        /// Ensures that the HTTP response was successful.
        /// Attempts to deserialize the response body to get the error message.
        /// </summary>
        /// <param name="response">The HTTP response to check</param>
        /// <returns>An awaitable task</returns>
        private static async Task EnsureSuccessStatusCode(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ResponseEnvelope>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null || !string.IsNullOrWhiteSpace(result.Message))
                throw new InvalidOperationException($"Request failed: {result?.Message}");

            await EnsureSuccessStatusCode(response);
        }

        /// <summary>
        /// Entry for the file entry cache.
        /// </summary>
        /// <param name="Id">The ID of the file entry</param>
        /// <param name="Url">The (relative) URL of the file entry</param>
        private sealed record CachedFileEntry(long Id, string Url);

        /// <summary>
        /// Response envelope for paginated list responses.
        /// </summary>
        private class PagedListResponse
        {
            /// <summary>
            /// The page this response is
            /// </summary>
            public int Current_Page { get; set; }
            /// <summary>
            /// The entries in this page
            /// </summary>
            public List<FileEntry> Data { get; set; } = new List<FileEntry>();
            /// <summary>
            /// The requested number of entries per page
            /// </summary>
            public int Per_Page { get; set; }
            /// <summary>
            /// The next page, or null if there are no more pages
            /// </summary>
            public int? Next_Page { get; set; }
        }

        /// <summary>
        /// A file entry in the filejump API.
        /// </summary>
        private class FileEntry
        {
            /// <summary>
            /// The ID of the file entry.
            /// </summary>
            public long Id { get; set; }
            /// <summary>
            /// The name of the file entry.
            /// </summary>
            public string Name { get; set; } = string.Empty;
            /// <summary>
            /// The type of the file entry (file or folder).
            /// </summary>
            public string Type { get; set; } = string.Empty;
            /// <summary>
            /// The URL of the file entry.
            /// </summary>
            public string Url { get; set; } = string.Empty;
            /// <summary>
            /// The size of the file entry in bytes.
            /// </summary>
            public long File_Size { get; set; }
            /// <summary>
            /// The time the entry was deleted
            /// </summary>
            public string Deleted_At { get; set; } = string.Empty;
            /// <summary>
            /// The time the entry was created
            /// </summary>
            public string Created_At { get; set; } = string.Empty;
            /// <summary>
            /// The time the entry was last updated
            /// </summary>
            public string Updated_At { get; set; } = string.Empty;
            /// <summary>
            /// The ID of the parent folder, or null if it is in the root folder.
            /// </summary>
            public long? Parent_Id { get; set; }
        }

        /// <summary>
        /// Response envelope for API responses.
        /// </summary>
        private class ResponseEnvelope
        {
            /// <summary>
            /// The error message, if any.
            /// </summary>
            public string? Message { get; set; }
            /// <summary>
            /// The status of the response.
            /// </summary>
            public string? Status { get; set; }
        }

        /// <summary>
        /// Response envelope for file entry responses.
        /// </summary>
        private class FileEntryOuter : ResponseEnvelope
        {
            /// <summary>
            /// The file entry returned by the API.
            /// </summary>
            public FileEntry? FileEntry { get; set; }
        }

        /// <summary>
        /// Response envelope for folder entry responses.
        /// </summary>
        private class FolderEntryOuter : ResponseEnvelope
        {
            /// <summary>
            /// The folder entry returned by the API.
            /// </summary>
            public FolderEntry? Folder { get; set; }
        }

        /// <summary>
        /// A folder entry in the filejump API.
        /// </summary>
        private class FolderEntry
        {
            /// <summary>
            /// The ID of the folder entry.
            /// </summary>
            public long Id { get; set; }
            /// <summary>
            /// The name of the folder entry.
            /// </summary>
            public string Name { get; set; } = string.Empty;
            /// <summary>
            /// The ID of the parent folder, or null if it is in the root folder.
            /// </summary>
            public string Type { get; set; } = string.Empty;
        }

        /// <summary>
        /// Response envelope for authentication responses.
        /// </summary>
        private class AuthResponseOuter : ResponseEnvelope
        {
            public AuthResponseUser? User { get; set; }
        }

        /// <summary>
        /// User information returned by the authentication response.
        /// </summary>
        private class AuthResponseUser
        {
            /// <summary>
            /// The email of the user.
            /// </summary>
            public string Email { get; set; } = string.Empty;
            /// <summary>
            /// The name of the user
            /// </summary>
            public string Name { get; set; } = string.Empty;
            /// <summary>
            /// The returned access token.
            /// </summary>
            public string Access_Token { get; set; } = string.Empty;
            /// <summary>
            /// The time the user was banned, if applicable.
            /// </summary>
            public string Banned_At { get; set; } = string.Empty;
        }
    }
}
