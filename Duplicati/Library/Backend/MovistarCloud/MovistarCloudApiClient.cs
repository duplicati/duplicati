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

using Duplicati.Library.Utility;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Duplicati.Library.Backend.MovistarCloud;

/// <summary>
/// API client for Movistar Cloud (MiCloud/Zefiro)
/// </summary>
internal sealed class MovistarCloudApiClient : IDisposable
{
    /// <summary>
    /// The base URL for the Movistar Cloud API
    /// </summary>
    private const string BaseUrl = "https://micloud.movistar.es";

    /// <summary>
    /// The base URL for upload operations
    /// </summary>
    private const string UploadUrl = "https://upload.micloud.movistar.es";

    /// <summary>
    /// The cookie domain for session cookies
    /// </summary>
    private const string CookieDomain = "micloud.movistar.es";

    /// <summary>
    /// The API path for login
    /// </summary>
    private const string LoginPath = "/sapi/login?action=login";

    /// <summary>
    /// The API path for listing media files
    /// </summary>
    private const string MediaListPath = "/sapi/media?action=get";

    /// <summary>
    /// The API path for listing folders
    /// </summary>
    private const string FolderListPath = "/sapi/media/folder?action=list";

    /// <summary>
    /// The API path for uploading files
    /// </summary>
    private const string UploadPath = "/sapi/upload?action=save&acceptasynchronous=true";

    /// <summary>
    /// The API path for getting validation status
    /// </summary>
    private const string ValidationStatusPath = "/sapi/media?action=get-validation-status";

    /// <summary>
    /// The API path for getting download URLs
    /// </summary>
    private const string DownloadUrlPath = "/sapi/media?action=get&origin=omh,dropbox";

    /// <summary>
    /// The API path for deleting files
    /// </summary>
    private const string DeletePath = "/sapi/media/file?action=delete&softdelete=true";

    /// <summary>
    /// The API path for getting storage space information
    /// </summary>
    private const string StorageSpacePath = "/sapi/media?action=get-storage-space&softdeleted=true";

    /// <summary>
    /// The API path for listing trash items
    /// </summary>
    private const string TrashPath = "/sapi/media/trash?action=get";

    /// <summary>
    /// The API path for creating folders
    /// </summary>
    private const string FolderCreatePath = "/sapi/media/folder?action=save";

    /// <summary>
    /// The API path for getting the root folder
    /// </summary>
    private const string RootFolderPath = "/sapi/media/folder/root?action=get";

    /// <summary>
    /// The User-Agent string used for HTTP requests
    /// </summary>
    private const string UserAgent = "Duplicati-MovistarCloud-Unofficial/0.1";

    /// <summary>
    /// The email address for authentication
    /// </summary>
    private readonly string _email;

    /// <summary>
    /// The password for authentication
    /// </summary>
    private readonly string _password;

    /// <summary>
    /// The device ID for authentication
    /// </summary>
    private readonly string _deviceId;

    /// <summary>
    /// The HTTP client instance
    /// </summary>
    private HttpClient? _http;

    /// <summary>
    /// The cookie container for session management
    /// </summary>
    private CookieContainer? _cookies;

    /// <summary>
    /// The validation key for API requests
    /// </summary>
    private string? _validationKey;

    /// <summary>
    /// Whether a relogin has been attempted
    /// </summary>
    private bool _reloginAttempted;

    /// <summary>
    /// Gets the HTTP client, throwing if not initialized
    /// </summary>
    private HttpClient Http
        => _http ?? throw new InvalidOperationException("HTTP client not initialized. Call EnsureLoggedInAsync first.");

    /// <summary>
    /// Gets the validation key, throwing if not initialized
    /// </summary>
    private string ValidationKey
        => _validationKey ?? throw new InvalidOperationException("ValidationKey not initialized. Call EnsureLoggedInAsync first.");

    /// <summary>
    /// Private constructor to prevent direct instantiation. Use CreateAsync factory method instead.
    /// </summary>
    /// <param name="email">The email address for authentication</param>
    /// <param name="password">The password for authentication</param>
    /// <param name="deviceID">The device ID for authentication</param>
    private MovistarCloudApiClient(string email, string password, string deviceID)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _password = password ?? throw new ArgumentNullException(nameof(password));
        _deviceId = deviceID;
    }

    /// <summary>
    /// Factory method to create and initialize a new instance of the Movistar Cloud API client.
    /// </summary>
    /// <param name="email">The email address for authentication</param>
    /// <param name="password">The password for authentication</param>
    /// <param name="deviceID">The device ID for authentication</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The initialized API client</returns>
    public static async Task<MovistarCloudApiClient> CreateAsync(string email, string password, string deviceID, CancellationToken cancellationToken = default)
    {
        var client = new MovistarCloudApiClient(email, password, deviceID);
        await client.EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose() => _http?.Dispose();

    /// <summary>
    /// Executes an operation with automatic relogin on session expiration.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="op">The operation to execute</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The result of the operation</returns>
    public async Task<T> WithAutoRelogin<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        try
        {
            _reloginAttempted = false;
            return await op(ct).ConfigureAwait(false);
        }
        catch (MovistarCloudSessionExpiredException) when (!_reloginAttempted)
        {
            _reloginAttempted = true;
            await EnsureLoggedInAsync(ct).ConfigureAwait(false);
            return await op(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes an operation with automatic relogin on session expiration.
    /// </summary>
    /// <param name="op">The operation to execute</param>
    /// <param name="ct">The cancellation token</param>
    public async Task WithAutoRelogin(Func<CancellationToken, Task> op, CancellationToken ct)
    {
        await WithAutoRelogin(async x => { await op(x).ConfigureAwait(false); return true; }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the client is logged in, performing login if necessary.
    /// </summary>
    /// <param name="ct">The cancellation token</param>
    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        var (js, vk) = await LoginAsync(_email, _password, ct).ConfigureAwait(false);
        _validationKey = vk;
        BuildHttpClientWithSession(js, vk);
    }

    /// <summary>
    /// Builds the HTTP client with session cookies.
    /// </summary>
    /// <param name="jsessionid">The JSESSIONID cookie value</param>
    /// <param name="validationkey">The validation key cookie value</param>
    private void BuildHttpClientWithSession(string jsessionid, string validationkey)
    {
        _http?.Dispose();

        _cookies = new CookieContainer();
        _cookies.Add(new System.Uri(BaseUrl), new Cookie("JSESSIONID", jsessionid, "/", CookieDomain));
        _cookies.Add(new System.Uri(BaseUrl), new Cookie("validationkey", validationkey, "/", CookieDomain));

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _http = HttpClientHelper.CreateClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Checks if the response looks like an HTML error page (session expired).
    /// </summary>
    /// <param name="resp">The HTTP response</param>
    /// <param name="body">The response body</param>
    /// <returns>True if the response looks like HTML</returns>
    private static bool LooksLikeHtml(HttpResponseMessage resp, string body)
    {
        var ct = resp.Content.Headers.ContentType?.ToString() ?? "";
        if ((int)resp.StatusCode == 401) return true;
        if (ct.Contains("text/html", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(body) && body.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(body) && body.Contains("<title>Movistar Cloud</title>", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Reads the response body as JSON or throws if it looks like HTML.
    /// </summary>
    /// <param name="resp">The HTTP response</param>
    /// <param name="timeout">The read timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response body as a string</returns>
    /// <exception cref="MovistarCloudSessionExpiredException">Thrown when the session appears expired</exception>
    private async Task<string> ReadJsonOrThrowAsync(HttpResponseMessage resp, TimeSpan timeout, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var timeoutStream = stream.ObserveReadTimeout(timeout, false);
        using var reader = new StreamReader(timeoutStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        if (LooksLikeHtml(resp, body))
            throw new MovistarCloudSessionExpiredException("Session expired (HTML/401).");

        resp.EnsureSuccessStatusCode();
        return body;
    }

    /// <summary>
    /// Adds the validation key to a URL if not already present.
    /// </summary>
    /// <param name="url">The URL to modify</param>
    /// <returns>The URL with validation key</returns>
    private string AddValidationKey(string url)
        => url.Contains("validationkey=") ? url : $"{url}{(url.Contains("?") ? "&" : "?")}validationkey={System.Uri.EscapeDataString(ValidationKey)}";

    /// <summary>
    /// Performs login with email and password.
    /// </summary>
    /// <param name="email">The email address</param>
    /// <param name="password">The password</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The JSESSIONID and validation key</returns>
    private async Task<(string JSessionId, string ValidationKey)> LoginAsync(string email, string password, CancellationToken ct)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var http = HttpClientHelper.CreateClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        // Any public/light endpoint works; we use the home page
        using var respInitial = await http.GetAsync($"{BaseUrl}/", ct).ConfigureAwait(false);
        respInitial.EnsureSuccessStatusCode();

        var url = $"{BaseUrl}{LoginPath}";
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("login", email),
            new KeyValuePair<string,string>("password", password)
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = content;
        req.Headers.Add("X-deviceid", _deviceId);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");

        var js = data.GetProperty("jsessionid").GetString();
        var vk = data.GetProperty("validationkey").GetString();

        if (string.IsNullOrWhiteSpace(js) || string.IsNullOrWhiteSpace(vk))
            throw new Exception("Login succeeded but did not return jsessionid/validationkey.");

        return (js!, vk!);
    }

    /// <summary>
    /// Lists files and folders in the specified folder.
    /// </summary>
    /// <param name="folderId">The folder ID (0 for root)</param>
    /// <param name="limit">The maximum number of items to return</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The list of remote files</returns>
    public async Task<List<RemoteFile>> ListFilesAsync(long folderId, int limit, TimeSpan timeout, CancellationToken ct)
    {
        if (folderId == 0)
        {
            folderId = await GetRootFolderIdAsync(timeout, ct).ConfigureAwait(false);
        }

        var result = new List<RemoteFile>();

        // List Files
        var url = AddValidationKey($"{BaseUrl}{MediaListPath}&folderid={folderId}&limit={limit}");
        var payload = new { data = new { fields = new[] { "name", "modificationdate", "size", "etag" } } };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var media = doc.RootElement.GetProperty("data").GetProperty("media");

        foreach (var item in media.EnumerateArray())
        {
            var id = long.Parse(item.GetProperty("id").GetString() ?? "0");
            var name = item.GetProperty("name").GetString() ?? "";
            var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var md = item.TryGetProperty("modificationdate", out var m) ? m.GetInt64() : 0;
            var et = item.TryGetProperty("etag", out var ep) ? ep.GetString() ?? "" : "";

            var last = md > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(md).UtcDateTime : DateTime.UtcNow;
            result.Add(new RemoteFile(id, name, size, last, false, et));
        }

        // List Directories
        url = AddValidationKey($"{BaseUrl}{FolderListPath}&parentid={folderId}&limit={limit}");
        payload = new { data = new { fields = new[] { "name", "date" } } };

        using var contentDir = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var respDir = await Http.PostAsync(url, contentDir, ct).ConfigureAwait(false);
        body = await ReadJsonOrThrowAsync(respDir, timeout, ct).ConfigureAwait(false);

        using var docDir = JsonDocument.Parse(body);
        media = docDir.RootElement.GetProperty("data").GetProperty("folders");

        foreach (var item in media.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt64();
            var name = item.GetProperty("name").GetString() ?? "";
            var size = 0L;
            var md = item.TryGetProperty("date", out var m) ? m.GetInt64() : 0;
            var et = "";

            var last = md > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(md).UtcDateTime : DateTime.UtcNow;
            result.Add(new RemoteFile(id, name, size, last, true, et));
        }

        return result;
    }

    /// <summary>
    /// Uploads a file to the specified folder.
    /// </summary>
    /// <param name="folderId">The destination folder ID</param>
    /// <param name="remoteName">The remote file name</param>
    /// <param name="localFilePath">The local file path</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The upload result</returns>
    public async Task<UploadResult> UploadFileAsync(long folderId, string remoteName, string localFilePath, TimeSpan timeout, CancellationToken ct)
    {
        var fi = new FileInfo(localFilePath);
        if (!fi.Exists) throw new FileNotFoundException("Local file not found", localFilePath);

        var url = AddValidationKey($"{UploadUrl}{UploadPath}");

        var meta = new
        {
            data = new
            {
                name = remoteName,
                size = fi.Length,
                modificationdate = fi.LastWriteTimeUtc.ToString("yyyyMMdd'T'HHmmss'Z'"),
                contenttype = "application/octet-stream",
                folderid = folderId
            }
        };

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8, "application/json"), "data");

        await using var fs = File.OpenRead(localFilePath);
        await using var timeoutStream = fs.ObserveReadTimeout(timeout, false);
        var filePart = new StreamContent(timeoutStream);
        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(filePart, "file", remoteName);

        using var resp = await Http.PostAsync(url, form, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var idElem = doc.RootElement.GetProperty("id");
        var id = idElem.ValueKind == JsonValueKind.String ? long.Parse(idElem.GetString()!) : idElem.GetInt64();
        var st = doc.RootElement.TryGetProperty("status", out var stp) ? stp.GetString() ?? "" : "";
        var et = doc.RootElement.TryGetProperty("etag", out var etp) ? etp.GetString() ?? "" : "";

        return new UploadResult(id, st, et);
    }

    /// <summary>
    /// Gets the validation status of an uploaded file.
    /// </summary>
    /// <param name="id">The file ID</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The validation status string</returns>
    public async Task<string> GetValidationStatusAsync(long id, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{ValidationStatusPath}");
        var payload = new { data = new { ids = new[] { new { id } } } };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").GetProperty("ids")[0].GetProperty("status").GetString() ?? "";
    }

    /// <summary>
    /// Gets a signed download URL for a file.
    /// </summary>
    /// <param name="fileId">The file ID</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The signed download URL</returns>
    public async Task<string> GetDownloadUrlAsync(long fileId, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{DownloadUrlPath}");
        var payload = new { data = new { ids = new[] { fileId }, fields = new[] { "url", "name", "size", "etag" } } };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data").GetProperty("media")[0].GetProperty("url").GetString()!;
    }

    /// <summary>
    /// Downloads a file from a signed URL to a local file.
    /// </summary>
    /// <param name="signedUrl">The signed URL to download from</param>
    /// <param name="localFilePath">The local file path to save to</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    public async Task DownloadToFileAsync(string signedUrl, string localFilePath, TimeSpan timeout, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localFilePath) ?? ".");
        using var resp = await Http.GetAsync(signedUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var rs = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var timeoutStream = rs.ObserveWriteTimeout(timeout, false);
        await using var fs = File.Create(localFilePath);
        await timeoutStream.CopyToAsync(fs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Soft deletes a file (moves to trash).
    /// </summary>
    /// <param name="fileId">The file ID to delete</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    public async Task SoftDeleteFileAsync(long fileId, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{DeletePath}");
        var payload = new { data = new { files = new[] { fileId } } };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        _ = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the storage space information.
    /// </summary>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The storage space information</returns>
    public async Task<StorageSpace> GetStorageSpaceAsync(TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{StorageSpacePath}");
        using var resp = await Http.PostAsync(url, new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"), ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var d = doc.RootElement.GetProperty("data");
        return new StorageSpace(
            Used: d.GetProperty("used").GetInt64(),
            Free: d.GetProperty("free").GetInt64(),
            SoftDeleted: d.GetProperty("softdeleted").GetInt64(),
            NoLimit: d.TryGetProperty("nolimit", out var nl) && nl.GetBoolean()
        );
    }

    /// <summary>
    /// Lists items in the trash.
    /// </summary>
    /// <param name="pageSize">The number of items to return</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The list of trash entries</returns>
    public async Task<List<TrashEntry>> ListTrashAsync(int pageSize, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{TrashPath}");

        var payload = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["max-page-size"] = pageSize,
                ["origin"] = new[] { "omh", "dropbox" }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("data").GetProperty("entries");

        var res = new List<TrashEntry>(entries.GetArrayLength());
        foreach (var e in entries.EnumerateArray())
        {
            res.Add(new TrashEntry(
                Id: e.GetProperty("id").GetInt64(),
                Name: e.GetProperty("name").GetString() ?? "",
                Size: e.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                Origin: e.TryGetProperty("origin", out var o) ? o.GetString() ?? "" : ""
            ));
        }
        return res;
    }

    /// <summary>
    /// Tries to get a folder ID by name under a parent folder.
    /// </summary>
    /// <param name="parentId">The parent folder ID</param>
    /// <param name="name">The folder name to search for</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The folder ID if found, null otherwise</returns>
    public async Task<long?> TryGetFolderIdByNameAsync(long parentId, string name, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{FolderListPath}&parentid={parentId}&limit=2000");

        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var folders = doc.RootElement.GetProperty("data").GetProperty("folders");

        foreach (var f in folders.EnumerateArray())
        {
            var fname = f.GetProperty("name").GetString() ?? "";
            if (string.Equals(fname, name, StringComparison.Ordinal))
                return f.GetProperty("id").GetInt64();
        }

        return null;
    }

    /// <summary>
    /// Creates a folder under the specified parent folder.
    /// If the folder already exists, returns the existing folder ID.
    /// </summary>
    /// <param name="parentId">The parent folder ID</param>
    /// <param name="name">The folder name to create</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The created or existing folder ID</returns>
    public async Task<long> CreateFolderAsync(long parentId, string name, TimeSpan timeout, CancellationToken ct)
    {
        if (parentId == 0)
        {
            parentId = await GetRootFolderIdAsync(timeout, ct).ConfigureAwait(false);
        }

        // Idempotency: if it already exists, return its id
        var existing = await TryGetFolderIdByNameAsync(parentId, name, timeout, ct).ConfigureAwait(false);
        if (existing.HasValue)
            return existing.Value;

        // Create folder
        var url = AddValidationKey($"{BaseUrl}{FolderCreatePath}");

        var payload = new
        {
            data = new
            {
                magic = false,
                offline = false,
                name = name,
                parentid = parentId
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var createdFolder = doc.RootElement.GetProperty("data").GetProperty("folder");
        var id = createdFolder.GetProperty("id").GetInt64();

        return id;
    }

    /// <summary>
    /// Gets the root folder ID for the current user.
    /// </summary>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The root folder ID</returns>
    public async Task<long> GetRootFolderIdAsync(TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{RootFolderPath}");
        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var folders = doc.RootElement.GetProperty("data").GetProperty("folders");
        return folders[0].GetProperty("id").GetInt64();
    }

    /// <summary>
    /// Asserts that a folder exists by checking if it can be listed.
    /// </summary>
    /// <param name="folderId">The folder ID to check</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    public async Task AssertFolderExistsByIdAsync(long folderId, TimeSpan timeout, CancellationToken ct)
    {
        var url = AddValidationKey($"{BaseUrl}{MediaListPath}&folderid={folderId}&limit=1");
        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        _ = await ReadJsonOrThrowAsync(resp, timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures a folder path exists, creating parent folders as needed.
    /// </summary>
    /// <param name="path">The folder path to ensure</param>
    /// <param name="timeout">The operation timeout</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The folder ID of the final path segment</returns>
    public async Task<long> EnsureFolderPathAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var p = path.Trim();
        if (string.IsNullOrWhiteSpace(p))
            throw new ArgumentException("root-folder-path empty");

        var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var currentId = await GetRootFolderIdAsync(timeout, ct).ConfigureAwait(false);

        foreach (var segment in segments)
        {
            var existing = await TryGetFolderIdByNameAsync(currentId, segment, timeout, ct).ConfigureAwait(false);
            if (existing.HasValue)
            {
                currentId = existing.Value;
                continue;
            }

            currentId = await CreateFolderAsync(currentId, segment, timeout, ct).ConfigureAwait(false);
        }

        return currentId;
    }
}

/// <summary>
/// Represents a remote file or folder.
/// </summary>
/// <param name="Id">The file/folder ID</param>
/// <param name="Name">The file/folder name</param>
/// <param name="Size">The file size (0 for folders)</param>
/// <param name="LastWriteUtc">The last write time in UTC</param>
/// <param name="IsFolder">Whether this is a folder</param>
/// <param name="ETag">The entity tag</param>
internal sealed record RemoteFile(long Id, string Name, long Size, DateTime LastWriteUtc, bool IsFolder, string ETag);

/// <summary>
/// Represents the result of a file upload operation.
/// </summary>
/// <param name="Id">The uploaded file ID</param>
/// <param name="Status">The upload status</param>
/// <param name="ETag">The entity tag</param>
internal sealed record UploadResult(long Id, string Status, string ETag);

/// <summary>
/// Represents storage space information.
/// </summary>
/// <param name="Used">The used space in bytes</param>
/// <param name="Free">The free space in bytes</param>
/// <param name="SoftDeleted">The soft-deleted space in bytes</param>
/// <param name="NoLimit">Whether there is no storage limit</param>
internal sealed record StorageSpace(long Used, long Free, long SoftDeleted, bool NoLimit);

/// <summary>
/// Represents a trash entry.
/// </summary>
/// <param name="Id">The entry ID</param>
/// <param name="Name">The entry name</param>
/// <param name="Size">The entry size</param>
/// <param name="Origin">The entry origin</param>
internal sealed record TrashEntry(long Id, string Name, long Size, string Origin);

/// <summary>
/// Exception thrown when the Movistar Cloud session has expired.
/// </summary>
internal sealed class MovistarCloudSessionExpiredException : Exception
{
    /// <summary>
    /// Creates a new session expired exception.
    /// </summary>
    /// <param name="message">The exception message</param>
    public MovistarCloudSessionExpiredException(string message) : base(message) { }
}
