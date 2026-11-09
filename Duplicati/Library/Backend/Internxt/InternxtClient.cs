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
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Duplicati.Library.Backend.Internxt.Models;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.Internxt;

/// <summary>
/// An error response from the Internxt API
/// </summary>
public sealed class InternxtApiException : Exception
{
    /// <summary>
    /// The HTTP status code
    /// </summary>
    public HttpStatusCode StatusCode { get; }
    /// <summary>
    /// The response body, if any
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Creates a new API exception
    /// </summary>
    /// <param name="statusCode">The HTTP status code</param>
    /// <param name="body">The response body</param>
    public InternxtApiException(HttpStatusCode statusCode, string body)
        : base(Strings.InternxtBackend.ApiError((int)statusCode, body))
    {
        StatusCode = statusCode;
        Body = body;
    }
}

/// <summary>
/// Client for the Internxt Drive (metadata) and network (storage) APIs
/// </summary>
public sealed class InternxtClient : IDisposable
{
    /// <summary>
    /// The default gateway URL
    /// </summary>
    public const string DefaultApiUrl = "https://gateway.internxt.com";
    /// <summary>
    /// The default value of the internxt-client header
    /// </summary>
    public const string DefaultClientName = "duplicati";
    /// <summary>
    /// The page size for listings; the server caps at 1000
    /// </summary>
    private const int PageSize = 1000;
    /// <summary>
    /// The status value of a live entry
    /// </summary>
    private const string StatusExists = "EXISTS";
    /// <summary>
    /// The longest a 429 is waited out before giving up
    /// </summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    /// <summary>
    /// The lifetime assumed for a token whose expiry cannot be read
    /// </summary>
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(1);
    /// <summary>
    /// How long before expiry a token is treated as expired
    /// </summary>
    private static readonly TimeSpan TokenExpiryMargin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The log tag
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<InternxtClient>();

    /// <summary>
    /// The JSON options matching the API
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly string _driveUrl;
    private readonly string _networkUrl;
    private readonly string _clientName;
    private readonly string _clientVersion;
    private readonly string _basicAuth;
    private string _token;

    /// <summary>
    /// The user's network bucket
    /// </summary>
    public string Bucket { get; }
    /// <summary>
    /// The UUID of the user's root folder
    /// </summary>
    public string RootFolderId { get; }
    /// <summary>
    /// The BIP39 seed derived from the account mnemonic
    /// </summary>
    public byte[] Seed { get; }
    /// <summary>
    /// The time the current token stops being usable
    /// </summary>
    public DateTime ValidUntil { get; private set; }
    /// <summary>
    /// The gateway host, for DNS reporting
    /// </summary>
    public string GatewayHost => new Uri(_driveUrl).Host;

    private InternxtClient(HttpClient http, string apiUrl, string clientName, string token, string bucket, string rootFolderId, string bridgeUser, string userId, string mnemonic)
    {
        _http = http;
        var baseUrl = apiUrl.TrimEnd('/');
        _driveUrl = baseUrl + "/drive";
        _networkUrl = baseUrl + "/network";
        _clientName = clientName;
        _clientVersion = AutoUpdater.UpdaterManager.SelfVersion.Version ?? "0.0.0.0";
        _token = token;
        ValidUntil = ReadTokenExpiry(token);
        Bucket = bucket;
        RootFolderId = rootFolderId;
        _basicAuth = InternxtCrypto.NetworkBasicAuth(bridgeUser, userId);
        Seed = InternxtCrypto.Bip39Seed(mnemonic);
    }

    /// <summary>
    /// Logs in and creates a client
    /// </summary>
    /// <param name="http">The HTTP client to use</param>
    /// <param name="apiUrl">The gateway URL</param>
    /// <param name="email">The account email</param>
    /// <param name="password">The account password</param>
    /// <param name="twoFactorCode">The two-factor code, if any</param>
    /// <param name="twoFactorSecret">The TOTP secret to compute a code from when the account needs one and no code is given</param>
    /// <param name="clientName">The value for the internxt-client header</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The client</returns>
    public static async Task<InternxtClient> CreateAsync(HttpClient http, string apiUrl, string email, string password, string? twoFactorCode, string? twoFactorSecret, string clientName, CancellationToken cancellationToken)
    {
        var driveUrl = apiUrl.TrimEnd('/') + "/drive";
        var version = AutoUpdater.UpdaterManager.SelfVersion.Version ?? "0.0.0.0";

        // Step 1: fetch the encrypted salt and the 2FA flag
        using var securityRequest = new HttpRequestMessage(HttpMethod.Post, $"{driveUrl}/auth/login")
        {
            Content = JsonContent(new { email })
        };
        AddClientHeaders(securityRequest, clientName, version);
        using var securityResponse = await http.SendAsync(securityRequest, cancellationToken).ConfigureAwait(false);
        if (securityResponse.StatusCode == HttpStatusCode.NotFound)
            throw new UserInformationException(Strings.InternxtBackend.UnknownAccount(email), "InternxtUnknownAccount");
        var security = await ReadResponseAsync<LoginSecurityResponse>(securityResponse, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(security.SKey))
            throw new UserInformationException(Strings.InternxtBackend.UnknownAccount(email), "InternxtUnknownAccount");
        if (security.Tfa == true && string.IsNullOrWhiteSpace(twoFactorCode))
        {
            if (string.IsNullOrWhiteSpace(twoFactorSecret))
                throw new UserInformationException(Strings.InternxtBackend.TwoFactorRequired, "InternxtTwoFactorRequired");

            try
            {
                twoFactorCode = InternxtTotp.ComputeNow(twoFactorSecret);
            }
            catch (FormatException ex)
            {
                throw new UserInformationException(Strings.InternxtBackend.TwoFactorSecretInvalid, "InternxtTwoFactorSecretInvalid", ex);
            }
        }

        // Step 2: log in with the wrapped password hash
        using var accessRequest = new HttpRequestMessage(HttpMethod.Post, $"{driveUrl}/auth/cli/login/access")
        {
            Content = JsonContent(new LoginAccessRequest
            {
                Email = email,
                Password = InternxtCrypto.EncryptPasswordHash(password, security.SKey),
                Tfa = string.IsNullOrWhiteSpace(twoFactorCode) ? null : twoFactorCode
            })
        };
        AddClientHeaders(accessRequest, clientName, version);
        using var accessResponse = await http.SendAsync(accessRequest, cancellationToken).ConfigureAwait(false);
        if (accessResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            throw new UserInformationException(Strings.InternxtBackend.LoginRejected, "InternxtLoginRejected");
        if (accessResponse.StatusCode == HttpStatusCode.PaymentRequired)
            throw new UserInformationException(Strings.InternxtBackend.PlanWithoutApiAccess(await accessResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)), "InternxtPlanWithoutApiAccess");
        var access = await ReadResponseAsync<LoginAccessResponse>(accessResponse, cancellationToken).ConfigureAwait(false);

        var user = access.User ?? throw new UserInformationException(Strings.InternxtBackend.LoginRejected, "InternxtLoginRejected");
        var token = access.NewToken ?? access.Token ?? throw new UserInformationException(Strings.InternxtBackend.LoginRejected, "InternxtLoginRejected");
        if (string.IsNullOrWhiteSpace(user.Mnemonic) || string.IsNullOrWhiteSpace(user.Bucket) || string.IsNullOrWhiteSpace(user.RootFolderId) || string.IsNullOrWhiteSpace(user.BridgeUser) || string.IsNullOrWhiteSpace(user.UserId))
            throw new UserInformationException(Strings.InternxtBackend.LoginRejected, "InternxtLoginIncomplete");

        // Step 3: unlock the account key with the password
        string mnemonic;
        try
        {
            mnemonic = InternxtCrypto.DecryptText(user.Mnemonic, password);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new UserInformationException(Strings.InternxtBackend.MnemonicDecryptFailed, "InternxtMnemonicDecryptFailed", ex);
        }

        if (!InternxtCrypto.LooksLikeMnemonic(mnemonic))
            throw new UserInformationException(Strings.InternxtBackend.MnemonicDecryptFailed, "InternxtMnemonicDecryptFailed");

        return new InternxtClient(http, apiUrl, clientName, token, user.Bucket, user.RootFolderId, user.BridgeUser, user.UserId, mnemonic);
    }

    /// <summary>
    /// Creates a client without logging in, for tests
    /// </summary>
    internal static InternxtClient CreateForTesting(HttpClient http, string apiUrl, string token, string bucket, string rootFolderId, string bridgeUser, string userId, string mnemonic)
        => new InternxtClient(http, apiUrl, DefaultClientName, token, bucket, rootFolderId, bridgeUser, userId, mnemonic);

    /// <summary>
    /// Reads the expiry claim of a JWT without verifying it
    /// </summary>
    /// <param name="token">The token</param>
    /// <returns>The expiry time, less a safety margin</returns>
    private static DateTime ReadTokenExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length == 3)
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
                if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                    return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToLocalTime() - TokenExpiryMargin;
            }
        }
        catch (Exception ex)
        {
            Logging.Log.WriteVerboseMessage(LOGTAG, "TokenExpiryUnreadable", ex, "Could not read the token expiry, assuming {0}", DefaultTokenLifetime);
        }

        return DateTime.Now + DefaultTokenLifetime - TokenExpiryMargin;
    }

    /// <summary>
    /// Rotates the token
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        var response = await SendDriveAsync<LoginAccessResponse>(HttpMethod.Get, "/users/cli/refresh", null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.NewToken))
            throw new UserInformationException(Strings.InternxtBackend.LoginRejected, "InternxtRefreshFailed");
        _token = response.NewToken;
        ValidUntil = ReadTokenExpiry(_token);
    }

    #region Names

    /// <summary>
    /// Splits a file name into the plain name and type the Drive API stores
    /// </summary>
    /// <param name="name">The file name</param>
    /// <returns>The plain name and the type (extension without the dot, or empty)</returns>
    public static (string PlainName, string Type) SplitName(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
            return (name, string.Empty);
        return (name[..dot], name[(dot + 1)..]);
    }

    /// <summary>
    /// Joins a plain name and type back into a file name
    /// </summary>
    /// <param name="plainName">The plain name</param>
    /// <param name="type">The type, or null</param>
    /// <returns>The file name</returns>
    public static string JoinName(string plainName, string? type)
        => string.IsNullOrEmpty(type) ? plainName : $"{plainName}.{type}";

    #endregion

    #region HTTP plumbing

    /// <summary>
    /// Creates a JSON body
    /// </summary>
    private static HttpContent JsonContent(object body)
        => new StringContent(JsonSerializer.Serialize(body, body.GetType(), JsonOptions), new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" });

    /// <summary>
    /// Adds the client identification headers
    /// </summary>
    private static void AddClientHeaders(HttpRequestMessage request, string clientName, string clientVersion)
    {
        AddUserAgent(request, clientVersion);
        request.Headers.TryAddWithoutValidation("internxt-client", clientName);
        request.Headers.TryAddWithoutValidation("internxt-version", clientVersion);
    }

    /// <summary>
    /// Adds the User-Agent header. The gateway sits behind Cloudflare, which
    /// blocks POST requests that carry no User-Agent, and .NET sends none by default.
    /// </summary>
    private static void AddUserAgent(HttpRequestMessage request, string clientVersion)
        => request.Headers.TryAddWithoutValidation("User-Agent", $"Duplicati/{clientVersion}");

    /// <summary>
    /// Reads a JSON response, throwing on error status
    /// </summary>
    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InternxtApiException(response.StatusCode, "Empty response");
    }

    /// <summary>
    /// Throws an <see cref="InternxtApiException"/> for a non-success status
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = string.Empty;
        try { body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); }
        catch { }

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
            throw new UserInformationException(Strings.InternxtBackend.PlanWithoutApiAccess(body), "InternxtPlanWithoutApiAccess");

        throw new InternxtApiException(response.StatusCode, body);
    }

    /// <summary>
    /// Sends a request, waiting out a single 429 and retrying once on 401 after a token refresh
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        var refreshed = false;
        var waited = false;
        while (true)
        {
            var request = requestFactory();
            var response = await _http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && !waited)
            {
                waited = true;
                var delay = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.Now : (TimeSpan?)null)
                    ?? TimeSpan.FromSeconds(1);
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.FromSeconds(1);
                if (delay <= MaxRetryAfter)
                {
                    response.Dispose();
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RateLimited", "Rate limited by Internxt, waiting {0}", delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed && request.Headers.Authorization?.Scheme == "Bearer")
            {
                refreshed = true;
                response.Dispose();
                await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    /// <summary>
    /// Builds a Drive API request
    /// </summary>
    private HttpRequestMessage DriveRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, _driveUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        AddClientHeaders(request, _clientName, _clientVersion);
        if (body != null)
            request.Content = JsonContent(body);
        return request;
    }

    /// <summary>
    /// Builds a network API request
    /// </summary>
    private HttpRequestMessage NetworkRequest(HttpMethod method, string path, object? body, bool apiVersion2)
    {
        var request = new HttpRequestMessage(method, _networkUrl + path);
        request.Headers.TryAddWithoutValidation("Authorization", _basicAuth);
        AddClientHeaders(request, _clientName, _clientVersion);
        if (apiVersion2)
            request.Headers.TryAddWithoutValidation("x-api-version", "2");
        if (body != null)
            request.Content = JsonContent(body);
        return request;
    }

    /// <summary>
    /// Sends a Drive API request and reads the JSON response
    /// </summary>
    private async Task<T> SendDriveAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => DriveRequest(method, path, body), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a Drive API request and discards the response body
    /// </summary>
    private async Task SendDriveAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => DriveRequest(method, path, body), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a network API request and reads the JSON response
    /// </summary>
    private async Task<T> SendNetworkAsync<T>(HttpMethod method, string path, object? body, bool apiVersion2, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => NetworkRequest(method, path, body, apiVersion2), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Drive API

    /// <summary>
    /// Lists the files in a folder
    /// </summary>
    /// <param name="folderUuid">The folder UUID</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The live files</returns>
    public async IAsyncEnumerable<FileDto> ListFilesAsync(string folderUuid, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            var path = $"/folders/v2/content/{folderUuid}/files?limit={PageSize}" + (cursor == null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await SendDriveAsync<FilesPageResponse>(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
            foreach (var file in page.Files)
                if (IsLive(file.Status))
                    yield return file;

            cursor = page.Files.Count == 0 ? null : page.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Lists the sub-folders of a folder
    /// </summary>
    /// <param name="folderUuid">The folder UUID</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The live folders</returns>
    public async IAsyncEnumerable<FolderDto> ListFoldersAsync(string folderUuid, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            var path = $"/folders/v2/content/{folderUuid}/folders?limit={PageSize}" + (cursor == null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await SendDriveAsync<FoldersPageResponse>(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
            foreach (var folder in page.Folders)
                if (IsLive(folder.Status))
                    yield return folder;

            cursor = page.Folders.Count == 0 ? null : page.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Whether a status marks a live entry; a missing status counts as live
    /// </summary>
    private static bool IsLive(string? status)
        => string.IsNullOrEmpty(status) || string.Equals(status, StatusExists, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a folder by its path from the root
    /// </summary>
    /// <param name="path">The path, with forward slashes</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The folder, or null if not found</returns>
    public async Task<FolderDto?> GetFolderByPathAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await SendDriveAsync<FolderDto>(HttpMethod.Get, $"/folders/meta?path={Uri.EscapeDataString("/" + path.Trim('/'))}", null, cancellationToken).ConfigureAwait(false);
        }
        catch (InternxtApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a folder
    /// </summary>
    /// <param name="parentUuid">The parent folder UUID</param>
    /// <param name="name">The folder name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The folder, or null if a folder with that name already exists</returns>
    public async Task<FolderDto?> CreateFolderAsync(string parentUuid, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await SendDriveAsync<FolderDto>(HttpMethod.Post, "/folders", new CreateFolderRequest { PlainName = name, ParentFolderUuid = parentUuid }, cancellationToken).ConfigureAwait(false);
        }
        catch (InternxtApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    /// <summary>
    /// Looks up a single file by name without listing the folder
    /// </summary>
    /// <param name="folderUuid">The folder UUID</param>
    /// <param name="name">The file name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The file, or null if it does not exist</returns>
    public async Task<FileDto?> FindFileAsync(string folderUuid, string name, CancellationToken cancellationToken)
    {
        var (plainName, type) = SplitName(name);
        var response = await SendDriveAsync<FileExistenceResponse>(HttpMethod.Post, $"/folders/content/{folderUuid}/files/existence",
            new FileExistenceRequest { Files = [new FileNameAndType { PlainName = plainName, Type = type }] }, cancellationToken).ConfigureAwait(false);

        return response.ExistentFiles.FirstOrDefault(f =>
            f.Exists != false
            && IsLive(f.Status)
            && !string.IsNullOrEmpty(f.Uuid)
            && string.Equals(f.FullName, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Creates a file entry
    /// </summary>
    public Task<FileDto> CreateFileEntryAsync(CreateFileRequest request, CancellationToken cancellationToken)
        => SendDriveAsync<FileDto>(HttpMethod.Post, "/files", request, cancellationToken);

    /// <summary>
    /// Replaces the content of an existing file entry
    /// </summary>
    public Task<FileDto> ReplaceFileAsync(string uuid, ReplaceFileRequest request, CancellationToken cancellationToken)
        => SendDriveAsync<FileDto>(HttpMethod.Put, $"/files/{uuid}", request, cancellationToken);

    /// <summary>
    /// Renames a file entry
    /// </summary>
    public Task RenameFileAsync(string uuid, string newName, CancellationToken cancellationToken)
    {
        var (plainName, type) = SplitName(newName);
        return SendDriveAsync(HttpMethod.Put, $"/files/{uuid}/meta", new UpdateFileMetaRequest { PlainName = plainName, Type = type }, cancellationToken);
    }

    /// <summary>
    /// Deletes a file entry permanently
    /// </summary>
    public Task DeleteFileAsync(string uuid, CancellationToken cancellationToken)
        => SendDriveAsync(HttpMethod.Delete, $"/files/{uuid}", null, cancellationToken);

    /// <summary>
    /// Moves a file entry to the trash
    /// </summary>
    public Task TrashFileAsync(string uuid, CancellationToken cancellationToken)
        => SendDriveAsync(HttpMethod.Post, "/storage/trash/add", new TrashRequest { Items = [new TrashItem { Uuid = uuid }] }, cancellationToken);

    /// <summary>
    /// Gets the space used
    /// </summary>
    public Task<UsageResponse> GetUsageAsync(CancellationToken cancellationToken)
        => SendDriveAsync<UsageResponse>(HttpMethod.Get, "/users/usage", null, cancellationToken);

    /// <summary>
    /// Gets the space limit
    /// </summary>
    public Task<LimitResponse> GetLimitAsync(CancellationToken cancellationToken)
        => SendDriveAsync<LimitResponse>(HttpMethod.Get, "/users/limit", null, cancellationToken);

    /// <summary>
    /// Gets the per-file upload limits
    /// </summary>
    public Task<FileLimitsResponse> GetFileLimitsAsync(CancellationToken cancellationToken)
        => SendDriveAsync<FileLimitsResponse>(HttpMethod.Get, "/files/limits", null, cancellationToken);

    #endregion

    #region Network API

    /// <summary>
    /// Starts an upload
    /// </summary>
    /// <param name="size">The plain-text size</param>
    /// <param name="parts">The number of parts, 1 for a single upload</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The upload target</returns>
    public async Task<UploadTarget> StartUploadAsync(long size, int parts, CancellationToken cancellationToken)
    {
        var response = await SendNetworkAsync<StartUploadResponse>(HttpMethod.Post, $"/v2/buckets/{Bucket}/files/start?multiparts={parts}",
            new StartUploadRequest { Uploads = [new StartUploadPart { Index = 0, Size = size }] }, false, cancellationToken).ConfigureAwait(false);

        var target = response.Uploads.FirstOrDefault()
            ?? throw new InternxtApiException(HttpStatusCode.OK, "Start upload returned no upload target");

        if (parts == 1 && string.IsNullOrEmpty(target.Url))
            throw new InternxtApiException(HttpStatusCode.OK, "Start upload returned no URL");
        if (parts > 1 && (target.Urls == null || target.Urls.Count < parts || string.IsNullOrEmpty(target.UploadId)))
            throw new InternxtApiException(HttpStatusCode.OK, "Start upload returned too few multipart URLs");

        return target;
    }

    /// <summary>
    /// Uploads a body to a pre-signed URL
    /// </summary>
    /// <param name="url">The pre-signed URL</param>
    /// <param name="content">The bytes to upload</param>
    /// <param name="length">The content length</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The ETag header, if any</returns>
    public async Task<string?> UploadToUrlAsync(string url, Stream content, long length, CancellationToken cancellationToken)
    {
        // The content disposes its stream with the request; the caller still needs the stream for the hash
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StreamContent(new LeaveOpenStream(content))
        };
        AddUserAgent(request, _clientVersion);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = length;

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return response.Headers.ETag?.Tag ?? (response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null);
    }

    /// <summary>
    /// Finishes an upload
    /// </summary>
    /// <param name="indexHex">The hex encoded file index</param>
    /// <param name="shard">The shard description</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The network file id</returns>
    public async Task<string> FinishUploadAsync(string indexHex, FinishShard shard, CancellationToken cancellationToken)
    {
        var response = await SendNetworkAsync<FinishUploadResponse>(HttpMethod.Post, $"/v2/buckets/{Bucket}/files/finish",
            new FinishUploadRequest { Index = indexHex, Shards = [shard] }, false, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(response.Id))
            throw new InternxtApiException(HttpStatusCode.OK, "Finish upload returned no file id");
        return response.Id;
    }

    /// <summary>
    /// Gets the download information for a network file
    /// </summary>
    /// <param name="fileId">The network file id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The file info</returns>
    public Task<FileInfoResponse> GetFileInfoAsync(string fileId, CancellationToken cancellationToken)
        => SendNetworkAsync<FileInfoResponse>(HttpMethod.Get, $"/buckets/{Bucket}/files/{fileId}/info", null, true, cancellationToken);

    /// <summary>
    /// Deletes a network file; used to clean up when the metadata call fails after an upload
    /// </summary>
    public async Task DeleteNetworkFileAsync(string fileId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => NetworkRequest(HttpMethod.Delete, $"/v2/buckets/{Bucket}/files/{fileId}", null, false), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a pre-signed download URL
    /// </summary>
    /// <param name="url">The URL</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The response stream; disposing it disposes the response</returns>
    public async Task<Stream> OpenDownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddUserAgent(request, _clientVersion);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A read-only view of a stream that does not dispose it
    /// </summary>
    private sealed class LeaveOpenStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A stream that disposes its response with itself
    /// </summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    #endregion

    /// <inheritdoc/>
    public void Dispose()
        => _http.Dispose();
}
