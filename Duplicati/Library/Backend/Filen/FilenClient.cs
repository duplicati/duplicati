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
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// The Filen client implementation
/// </summary>
public class FilenClient : IDisposable
{
    /// <summary>
    /// The URLs of the Filen gateways
    /// </summary>
    public static IReadOnlyList<string> GatewayUrls = [
        "https://gateway.filen.io",
        "https://gateway.filen.net",
        "https://gateway.filen-1.net",
        "https://gateway.filen-2.net",
        "https://gateway.filen-3.net",
        "https://gateway.filen-4.net",
        "https://gateway.filen-5.net",
        "https://gateway.filen-6.net"
    ];

    /// <summary>
    /// The URLs of the Filen egest servers
    /// </summary>
    public static IReadOnlyList<string> EgestUrls = [
        "https://egest.filen.io",
        "https://egest.filen.net",
        "https://egest.filen-1.net",
        "https://egest.filen-2.net",
        "https://egest.filen-3.net",
        "https://egest.filen-4.net",
        "https://egest.filen-5.net",
        "https://egest.filen-6.net"
    ];

    /// <summary>
    /// The URLs of the Filen ingest servers
    /// </summary>
    public static IReadOnlyList<string> IngestURLs = [
        "https://ingest.filen.io",
        "https://ingest.filen.net",
        "https://ingest.filen-1.net",
        "https://ingest.filen-2.net",
        "https://ingest.filen-3.net",
        "https://ingest.filen-4.net",
        "https://ingest.filen-5.net",
        "https://ingest.filen-6.net"
    ];

    /// <summary>
    /// The log tag for the Filen client
    /// </summary>
    private static string LOGTAG = Logging.Log.LogTagFromType<FilenClient>();

    /// <summary>
    /// The authentication result from the initial login
    /// </summary>
    private sealed record FilenAuthResult
    {
        /// <summary>
        /// The API key to use for auntehticated requests
        /// </summary>
        public required string ApiKey { get; init; }
        /// <summary>
        /// The account master key
        /// </summary>
        public required DerivedKey AccountMasterKey { get; init; }
        /// <summary>
        /// The master keys for the account
        /// </summary>
        public required IReadOnlyList<DerivedKey> MasterKeys { get; init; }

        /// <summary>
        /// Encrypts metadata with the latest master key
        /// </summary>
        /// <param name="data">The metadata to encrypt</param>
        /// <returns>The encrypted metadata</returns>
        public string EncryptMetadata(string data)
            => MasterKeys.Last().EncryptMetadata(data);

        /// <summary>
        /// Decrypts metadata with the correct master key
        /// </summary>
        /// <param name="data">The metadata to decrypt</param>
        /// <returns>The decrypted metadata</returns>
        public string DecryptMetadata(string data)
        {
            Exception? firstExecption = null;
            var keyIx = MasterKeys.Count;
            foreach (var key in MasterKeys.Reverse())
            {
                try
                {
                    return key.DecryptMetadata(data);
                }
                catch (Exception ex)
                {
                    keyIx--;
                    firstExecption ??= ex;
                    Logging.Log.WriteVerboseMessage(LOGTAG, "DecryptMetadataAttemptFailed", ex, "Failed to decrypt metadata with key {0}", keyIx);
                }
            }
            throw new Exception("Failed to decrypt metadata", firstExecption);
        }
    }

    /// <summary>
    /// The base url for all requests
    /// </summary>
    private readonly string _baseUrl;
    /// <summary>
    /// The fixed chunk size for uploads
    /// </summary>
    private const int ChunkSize = 1024 * 1024; // 1 MB
    /// <summary>
    /// The HTTP client to use for requests
    /// </summary>
    private readonly HttpClient _httpClient;
    /// <summary>
    /// The UUID of the root folder; null until loaded
    /// </summary>
    private string? _rootFolderUuid;
    /// <summary>
    /// The authentication result from the initial login
    /// </summary>
    private FilenAuthResult _authResult;

    /// <summary>
    /// The time the auth token is valid until
    /// </summary>
    private readonly DateTime _validUntil;

    /// <summary>
    /// The cached files to avoid re-fetching
    /// </summary>
    private readonly Dictionary<string, FilenFileEntry> _cachedFiles = new();

    /// <summary>
    /// Creates a new Filen client
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests</param>
    /// <param name="authResult">The authentication result from the initial login</param>
    /// <param name="baseUrl">The base url for all requests</param>
    private FilenClient(HttpClient httpClient, FilenAuthResult authResult, string baseUrl)
    {
        _httpClient = httpClient;
        _authResult = authResult;
        _baseUrl = baseUrl;
        _validUntil = DateTime.Now + TimeSpan.FromMinutes(50);
    }

    /// <summary>
    /// The time the client is valid
    /// </summary>
    public DateTime ValidUntil => _validUntil;

    /// <summary>
    /// Creates a new Filen client and authenticates
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests</param>
    /// <param name="email">The email address to use for login</param>
    /// <param name="password">The password to use for login</param>
    /// <param name="twoFactorCode">The two-factor code to use for login</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The authenticated Filen client</returns>
    public static async Task<FilenClient> CreateClientAsync(HttpClient httpClient, string email, string password, string? twoFactorCode, CancellationToken cancellationToken)
    {
        var baseUrl = GatewayUrls[Random.Shared.Next(0, GatewayUrls.Count)];
        var authResult = await FilenLogin.AuthenticateAsync(httpClient, baseUrl, email, password, twoFactorCode, cancellationToken).ConfigureAwait(false);
        return new FilenClient(httpClient, authResult, baseUrl);
    }

    /// <summary>
    /// Methods used for the initial login
    /// </summary>
    private static class FilenLogin
    {
        /// <summary>
        /// Returns the authentication information for the user
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for requests</param>
        /// <param name="baseUrl">The base url for all requests</param>
        /// <param name="email">The email address to use for login</param>
        /// <param name="cancellationToken">The cancellation token to use for the operation</param>
        /// <returns>The authentication information for the user</returns>
        private static async Task<AuthInfo> GetAuthInfoAsync(HttpClient httpClient, string baseUrl, string email, CancellationToken cancellationToken)
        {
            var loginUrl = $"{baseUrl}/v3/auth/info";
            using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
            request.Content = new StringContent(JsonSerializer.Serialize(new { email }), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await ExtractDataFromResponse<AuthInfo>(response, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Authenticates the user with the Filen API
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for requests</param>
        /// <param name="baseUrl">The base url for all requests</param>
        /// <param name="email">The email address to use for login</param>
        /// <param name="password">The password to use for login</param>
        /// <param name="twoFactorCode">The two-factor code to use for login</param>
        /// <param name="cancellationToken">The cancellation token to use for the operation</param>
        /// <returns>The authentication result from the initial login</returns>
        public static async Task<FilenAuthResult> AuthenticateAsync(HttpClient httpClient, string baseUrl, string email, string password, string? twoFactorCode, CancellationToken cancellationToken)
        {
            var authInfo = await GetAuthInfoAsync(httpClient, baseUrl, email, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(twoFactorCode))
                twoFactorCode = "XXXXXX";

            var rootKeys = FilenCrypto.GeneratePasswordAndMasterKeyBasedOnAuthVersion(password, authInfo.AuthVersion, authInfo.Salt);
            var loginUrl = $"{baseUrl}/v3/login";
            using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
            request.Content = new StringContent(JsonSerializer.Serialize(new { email, password = rootKeys.Password, twoFactorCode, authVersion = authInfo.AuthVersion }), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var result = await ExtractDataFromResponse<AuthResponse>(response, cancellationToken).ConfigureAwait(false);

            // var mk = await GetAllMasterKeys(masterKey1, result.ApiKey, cancellationToken).ConfigureAwait(false);

            var masterKeys = rootKeys.MasterKey.DecryptMetadata(result.MasterKeys);

            return new FilenAuthResult
            {
                ApiKey = result.ApiKey,
                AccountMasterKey = rootKeys.MasterKey,
                MasterKeys = masterKeys.Split('|').Select(DerivedKey.Create).ToList()
            };
        }
    }

    /// <summary>
    /// Extracts the data from a response or throws an exception
    /// </summary>
    /// <typeparam name="T">The data type to extract</typeparam>
    /// <param name="response">The response to extract from</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The extracted data</returns>
    private static async Task<T> ExtractDataFromResponse<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();

        // var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // var result = JsonSerializer.Deserialize<FilenResponseEnvelope<T>>(json);
        var result = await response.Content.ReadFromJsonAsync<FilenResponseEnvelope<T>>(cancellationToken).ConfigureAwait(false);
        if (result is null)
            throw new Exception("Failed to read response");
        if (!result.Status || !string.IsNullOrWhiteSpace(result.Error))
            throw new UserInformationException($"{result.Error ?? result.Message ?? "Unknown"}", "FilenAPIError");

        return result.Data;
    }

    /// <summary>
    /// Gets the file entry for a file in a folder
    /// </summary>
    /// <param name="folderUuid">The UUID of the folder</param>
    /// <param name="filename">The name of the file</param>
    /// <param name="timeout">The timeout for the operation</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The file entry for the file, or null if not found</returns>
    public async Task<FilenFileEntry?> GetFileEntryAsync(string folderUuid, string filename, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var f = _cachedFiles.GetValueOrDefault($"{folderUuid}:{filename}");
        if (f is not null)
            return f;

        return await ListFolderDecryptedAsync(folderUuid, timeout, cancellationToken)
            .FirstOrDefaultAsync(e => e.Name == filename, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the contents of a folder
    /// </summary>
    /// <param name="folderUuid">The folder UUID to list</param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<FilenFileEntry> ListFolderDecryptedAsync(string folderUuid, TimeSpan timeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = await Utility.Utility.WithTimeout(timeout, cancellationToken, async ct =>
        {
            var url = $"{_baseUrl}/v3/dir/content";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new { uuid = folderUuid }), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await ExtractDataFromResponse<DirListData>(response, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var entries = new List<FilenFileEntry>();

        foreach (var folder in result.Folders)
        {
            NameEntry? decryptedName = null;
            try
            {
                decryptedName = JsonSerializer.Deserialize<NameEntry>(_authResult.DecryptMetadata(folder.EncryptedName))
                    ?? throw new Exception("Failed to decrypt name");
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "SkipFolderDueToDecryptionError", ex, "Failed to decrypt folder name");
            }

            if (!string.IsNullOrWhiteSpace(decryptedName?.Name))
                yield return new FilenFileEntry
                {
                    Uuid = folder.UUID,
                    Name = decryptedName.Name,
                    IsFolder = true,
                    LastModified = DateTimeOffset.FromUnixTimeMilliseconds(folder.LastModified).UtcDateTime,
                    Size = 0,
                    Region = string.Empty,
                    Bucket = string.Empty,
                    Chunks = 0,
                    FileKey = string.Empty,
                    Version = 0
                };

        }

        foreach (var file in result.Files)
        {
            FileInfoMetadata? fileInfo = null;

            try
            {
                fileInfo = JsonSerializer.Deserialize<FileInfoMetadata>(_authResult.DecryptMetadata(file.MetadataEncrypted))
                    ?? throw new Exception("Failed to decrypt metadata");
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "SkipFileDueToDecryptionError", ex, "Failed to decrypt file name");
            }

            if (!string.IsNullOrWhiteSpace(fileInfo?.Name))
                yield return _cachedFiles[$"{folderUuid}:{fileInfo.Name}"] = new FilenFileEntry
                {
                    Uuid = file.Uuid,
                    Name = fileInfo.Name,
                    IsFolder = false,
                    Size = file.Size,
                    LastModified = DateTimeOffset.FromUnixTimeMilliseconds(fileInfo.LastModified).UtcDateTime,
                    Region = file.Region,
                    Bucket = file.Bucket,
                    Chunks = file.Chunks,
                    FileKey = fileInfo.Key,
                    Version = file.Version
                };
        }
    }

    /// <summary>
    /// Deletes a file from the Filen API
    /// </summary>
    /// <param name="fileUuid">The UUID of the file to delete</param>
    /// <param name="permanent">True to permanently delete the file, false to move it to the trash</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>A task that completes when the file is deleted</returns>
    public async Task DeleteFileAsync(string fileUuid, bool permanent, CancellationToken cancellationToken)
    {
        var url = permanent
            ? $"{_baseUrl}/v3/file/delete/permanent"
            : $"{_baseUrl}/v3/file/trash";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { uuid = fileUuid }), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ExtractDataFromResponse<string?>(response, cancellationToken).ConfigureAwait(false);
        var key = _cachedFiles.FirstOrDefault(kv => kv.Value.Uuid == fileUuid).Key;
        if (string.IsNullOrWhiteSpace(key))
            _cachedFiles.Clear();
        else
            _cachedFiles.Remove(key);
    }

    /// <summary>
    /// Uploads a file to the Filen API
    /// </summary>
    /// <param name="stream">The stream to upload</param>
    /// <param name="remoteName">The name of the file</param>
    /// <param name="parentFolderUuid">The UUID of the parent folder</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>A task that completes when the file is uploaded</returns>
    public async Task UploadStreamedEncryptedFileAsync(Stream stream, string remoteName, string parentFolderUuid, CancellationToken cancellationToken)
    {
        var size = stream.Length;
        if (stream.Length == 0)
            throw new InvalidOperationException("Cannot upload empty file");

        var uploadKey = FilenCrypto.GenerateRandomString(32);
        var fileKey = DerivedKey.Create(FilenCrypto.GenerateRandomString(32));
        var fileUuid = Guid.NewGuid().ToString();

        var chunks = await UploadEncryptedChunksAsync(stream, fileUuid, parentFolderUuid, fileKey, uploadKey, cancellationToken).ConfigureAwait(false);
        if (chunks == 0)
            throw new Exception("Failed to upload any chunks");
        await CompleteChunkedUploadAsync(fileUuid, remoteName, size, chunks, fileKey, uploadKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads encrypted chunks of a file to the Filen API
    /// </summary>
    /// <param name="inputStream">The stream to read from</param>
    /// <param name="uuid">The UUID of the file</param>
    /// <param name="parentUuid">The UUID of the parent folder</param>
    /// <param name="fileKey">The encryption key for the file</param>
    /// <param name="uploadKey">The upload key for the file</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The number of chunks uploaded</returns>
    private async Task<int> UploadEncryptedChunksAsync(Stream inputStream, string uuid, string parentUuid, DerivedKey fileKey, string uploadKey, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkSize];
        var chunk = 0;
        while (true)
        {
            int bytesRead = 0;
            while (bytesRead < ChunkSize)
            {
                int read = await inputStream.ReadAsync(buffer, bytesRead, ChunkSize - bytesRead, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                bytesRead += read;
            }

            Logging.Log.WriteVerboseMessage(LOGTAG, "UploadingChunk", "Uploading chunk {0}, size {1}", chunk, bytesRead);

            // Stream is exhausted
            if (bytesRead == 0) break;

            var cipherText = fileKey.EncryptData(buffer.AsSpan().Slice(0, bytesRead));
            var hash = FilenCrypto.HashChunk(cipherText);

            var url = $"{IngestURLs[Random.Shared.Next(0, IngestURLs.Count)]}/v3/upload";
            var qp = new Dictionary<string, string>
            {
                { "uuid", uuid },
                { "index", chunk.ToString() },
                { "parent", parentUuid },
                { "uploadKey", uploadKey },
                { "hash", hash }
            };

            url += "?" + string.Join("&", qp.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
            request.Content = new ByteArrayContent(cipherText);
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var res = await ExtractDataFromResponse<ChunkUploadResponse>(response, cancellationToken).ConfigureAwait(false);
            chunk++;
        }

        Logging.Log.WriteVerboseMessage(LOGTAG, "UploadingChunk", "Finished uploading {0} chunks", chunk);

        return chunk;
    }

    /// <summary>
    /// Completes a chunked upload of a file to the Filen API
    /// </summary>
    /// <param name="fileUuid">The UUID of the file</param>
    /// <param name="remoteName">The name of the file</param>
    /// <param name="size">The size of the file</param>
    /// <param name="chunks">The number of chunks</param>
    /// <param name="fileKey">The encryption key for the file</param>
    /// <param name="uploadKey">The upload key for the file</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>A task that completes when the file is uploaded</returns>
    private async Task CompleteChunkedUploadAsync(string fileUuid, string remoteName, long size, int chunks, DerivedKey fileKey, string uploadKey, CancellationToken cancellationToken)
    {
        var encryptedName = _authResult.EncryptMetadata(JsonSerializer.Serialize(new NameEntry() { Name = remoteName }));
        var nameHashed = FilenCrypto.HashFn(remoteName);
        var now = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        var encryptedMime = fileKey.EncryptMetadata("application/octet-stream");
        var encryptedFilesize = fileKey.EncryptMetadata(JsonSerializer.Serialize(size));
        var encryptedMetadata = _authResult.EncryptMetadata(JsonSerializer.Serialize(new FileInfoMetadata
        {
            Name = remoteName,
            Size = size,
            Mime = "application/octet-stream",
            Key = fileKey.Key,
            LastModified = now,
            Create = now
        }));

        var completeBody = new
        {
            uuid = fileUuid,
            name = encryptedName,
            nameHashed = nameHashed,
            size = encryptedFilesize,
            chunks = chunks,
            mime = encryptedMime,
            rm = FilenCrypto.GenerateRandomString(32),
            metadata = encryptedMetadata,
            version = 2,
            uploadKey = uploadKey,
        };

        var url = $"{_baseUrl}/v3/upload/done";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(completeBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var res = await ExtractDataFromResponse<FileUploadResponse>(response, cancellationToken).ConfigureAwait(false);
        if (res.Size != size)
            throw new Exception($"Failed to upload file, size mismatch. Expected {size}, got {res.Size}");
    }

    /// <summary>
    /// Downloads and decrypts a file to a stream
    /// </summary>
    /// <param name="file">The file to download</param>
    /// <param name="outputStream">The stream to write to</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>A task that completes when the file is downloaded</returns>
    public async Task DownloadAndDecryptToStreamAsync(FilenFileEntry file, Stream outputStream, CancellationToken cancellationToken)
    {
        var chunk = 0;
        var downloaded = 0L;
        var fileKey = DerivedKey.Create(file.FileKey);

        var totalSize = file.Size;
        while (downloaded < totalSize)
        {
            if (chunk >= file.Chunks)
                throw new Exception($"Attempted to download more chunks than available, expected {file.Chunks}, got {chunk}. File: {file.Name}, total size: {totalSize}, downloaded: {downloaded}");

            Logging.Log.WriteVerboseMessage(LOGTAG, "DownloadingChunk", "Downloading chunk {0}, size {1}", chunk, totalSize - downloaded);
            var url = $"{EgestUrls[Random.Shared.Next(0, EgestUrls.Count)]}/{file.Region}/{file.Bucket}/{file.Uuid}/{chunk}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var encrypted = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            var decrypted = fileKey.DecryptData(file.Version, encrypted);
            await outputStream.WriteAsync(decrypted, cancellationToken);
            downloaded += decrypted.Length;
            chunk++;
        }
    }

    /// <summary>
    /// Creates a folder in the Filen API
    /// </summary>
    /// <param name="parentUuid">The UUID of the parent folder</param>
    /// <param name="folderName">The name of the folder</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The UUID of the created folder</returns>
    public async Task<string> CreateFolderAsync(string parentUuid, string folderName, CancellationToken cancellationToken)
    {
        var self = Guid.NewGuid().ToString();
        var encryptedName = _authResult.EncryptMetadata(JsonSerializer.Serialize(new NameEntry() { Name = folderName }));
        var hashedName = FilenCrypto.HashFn(folderName);

        var body = new
        {
            uuid = self,
            name = encryptedName,
            nameHashed = hashedName,
            parent = parentUuid
        };

        var url = $"{_baseUrl}/v3/dir/create";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await ExtractDataFromResponse<CreateFolderResponse>(response, cancellationToken).ConfigureAwait(false);
        return result.Uuid;
    }

    /// <summary>
    /// Gets the UUID of the user's base folder
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The UUID of the user's base folder</returns>
    public async Task<string> GetUserBaseFolder(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_rootFolderUuid))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v3/user/baseFolder");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authResult.ApiKey);
            var response = await _httpClient.SendAsync(request);
            var result = await ExtractDataFromResponse<CreateFolderResponse>(response, cancellationToken);
            return _rootFolderUuid = result.Uuid;
        }
        return _rootFolderUuid;
    }

    /// <summary>
    /// Resolves a folder path to a UUID
    /// </summary>
    /// <param name="path">The path to resolve</param>
    /// <param name="timeout">The timeout for the operation</param>
    /// <param name="cancellationToken">The cancellation token to use for the operation</param>
    /// <returns>The UUID of the resolved folder</returns>
    public async Task<string> ResolveFolderPathAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var rootFolderUuid = await GetUserBaseFolder(cancellationToken);

        if (string.IsNullOrWhiteSpace(path)) return rootFolderUuid;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentUuid = rootFolderUuid;
        var prev = "/";

        foreach (var segment in segments)
        {
            var match = await ListFolderDecryptedAsync(currentUuid, timeout, cancellationToken)
                .FirstOrDefaultAsync(f => f.Name == segment)
                .ConfigureAwait(false);

            if (match == null)
                throw new FolderMissingException($"Path segment '{segment}' not found under {prev} {currentUuid}");

            currentUuid = match.Uuid;
            prev += segment + "/";
        }

        return currentUuid;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

