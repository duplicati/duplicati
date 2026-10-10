using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Jose;
using NetUri = System.Uri;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Implements the API helper class for Microsoft Graph API access
/// </summary>
internal class APIHelper : IDisposable
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<APIHelper>();

    /// <summary>
    /// The options used when parsing Microsoft Graph responses. Graph is not consistent with
    /// property casing across endpoints (the site collection hostname is documented as
    /// <c>hostname</c> for <c>/sites</c> but as <c>hostName</c> for <c>getAllSites</c>), and
    /// silently binding nothing at all is worse than being lenient, so parsing is
    /// case-insensitive.
    /// </summary>
    internal static readonly JsonSerializerOptions GraphJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The options used when serializing Microsoft Graph create/update payloads. Null values
    /// are omitted because the Graph OData deserializer rejects explicit JSON nulls for its
    /// non-nullable properties (dates, enums, complex types, collections) with a
    /// "UnableToDeserializePostBody" 400 error.
    /// </summary>
    internal static readonly JsonSerializerOptions IgnoreNullJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The default page size for API requests that allow big pages
    /// </summary>
    internal const int BIG_PAGE_SIZE = 500;

    /// <summary>
    /// The default page size for API requests
    /// </summary>
    internal const int GENERAL_PAGE_SIZE = 100;

    /// <summary>
    /// The default page size for chat API requests
    /// </summary>
    internal const int CHATS_PAGE_SIZE = 50;

    /// <summary>
    /// The HTTP client used for API requests
    /// </summary>
    private readonly HttpClient _httpClient;
    /// <summary>
    /// The cached Graph access token
    /// </summary>
    private string? _graphAccessToken;
    /// <summary>
    /// The authentication values
    /// </summary>
    private readonly AuthOptionsHelper.AuthOptions _authOptions;
    /// <summary>
    /// The tenant ID for the API requests
    /// </summary>
    private readonly string _tenantId;
    /// <summary>
    /// The base URL for Graph API requests
    /// </summary>
    private readonly string _graphBaseUrl;
    /// <summary>
    /// The base URL for Graph API requests
    /// </summary>
    public string GraphBaseUrl => _graphBaseUrl;

    /// <summary>
    /// The path to the certificate file
    /// </summary>
    private readonly string? _certificatePath;

    /// <summary>
    /// The password for the certificate file
    /// </summary>
    private readonly string? _certificatePassword;

    /// <summary>
    /// The scope for the API requests
    /// </summary>
    private readonly string _scope;

    /// <summary>
    /// The timeout options for the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Initializes a new instance of the <see cref="APIHelper"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests</param>
    /// <param name="authOptions">The authentication options</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="graphBaseUrl">The base URL for Graph API requests</param>
    /// <param name="timeouts">The timeout options</param>
    /// <param name="certificatePath">The path to the certificate file</param>
    /// <param name="certificatePassword">The password for the certificate file</param>
    /// <param name="scope">The scope for the API requests</param>
    private APIHelper(HttpClient httpClient, AuthOptionsHelper.AuthOptions authOptions, string tenantId, string graphBaseUrl, TimeoutOptionsHelper.Timeouts timeouts, string? certificatePath, string? certificatePassword, string scope)
    {
        _httpClient = httpClient;
        _authOptions = authOptions;
        _tenantId = tenantId;
        _graphBaseUrl = graphBaseUrl;
        _timeouts = timeouts;
        _certificatePath = certificatePath;
        _certificatePassword = certificatePassword;
        _scope = scope;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="APIHelper"/> class.
    /// </summary>
    /// <param name="authOptions">The authentication options</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="graphBaseUrl">The base URL for Graph API requests</param>
    /// <param name="timeouts">The timeout options</param>
    /// <param name="certificatePath">The path to the certificate file</param>
    /// <param name="certificatePassword">The password for the certificate file</param>
    /// <param name="scope">The scope for the API requests</param>
    /// <returns>A new instance of the <see cref="APIHelper"/> class</returns>
    public static APIHelper Create(AuthOptionsHelper.AuthOptions authOptions, string tenantId, string graphBaseUrl, TimeoutOptionsHelper.Timeouts timeouts, string? certificatePath = null, string? certificatePassword = null, string scope = "https://graph.microsoft.com/.default")
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false
        };

        var httpClient = HttpClientHelper.CreateClient(handler);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Duplicati/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

        return new APIHelper(httpClient, authOptions, tenantId, graphBaseUrl, timeouts, certificatePath, certificatePassword, scope);
    }

    /// <summary>
    /// Gets the authentication header for the API requests.
    /// </summary>
    /// <param name="refresh">Whether to refresh the access token</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The authentication header</returns>
    public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(bool refresh, CancellationToken cancellationToken)
    {
        var accessToken = await AcquireAccessTokenAsync(refresh, cancellationToken).ConfigureAwait(false);
        return new AuthenticationHeaderValue("Bearer", accessToken);
    }

    /// <summary>
    /// Acquires an access token for the API requests.
    /// </summary>
    /// <param name="refresh">Whether to refresh the access token</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The access token</returns>
    public async Task<string> AcquireAccessTokenAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && !string.IsNullOrWhiteSpace(_graphAccessToken))
            return _graphAccessToken;

        var tokenEndpoint = new NetUri($"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token");
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _authOptions.Username!,
            ["scope"] = _scope
        };

        if (!string.IsNullOrWhiteSpace(_certificatePath))
        {
            if (!File.Exists(_certificatePath))
                throw new UserInformationException($"Certificate file not found: {_certificatePath}", "CertificateFileNotFound");

            try
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(_certificatePath, _certificatePassword);
                var rsa = cert.GetRSAPrivateKey();
                if (rsa == null)
                    throw new UserInformationException("Certificate does not contain a private key", "CertificateNoPrivateKey");

                var payload = new Dictionary<string, object>
                {
                    { "aud", tokenEndpoint.ToString() },
                    { "exp", DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() },
                    { "iss", _authOptions.Username! },
                    { "jti", Guid.NewGuid().ToString() },
                    { "nbf", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() },
                    { "sub", _authOptions.Username! }
                };

                var extraHeaders = new Dictionary<string, object>
                {
                    { "x5t", Convert.ToBase64String(cert.GetCertHash()) }
                };

                var clientAssertion = JWT.Encode(payload, rsa, JwsAlgorithm.RS256, extraHeaders);

                parameters["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
                parameters["client_assertion"] = clientAssertion;
            }
            catch (Exception ex)
            {
                throw new UserInformationException($"Failed to load certificate: {ex.Message}", "CertificateLoadFailed", ex);
            }
        }
        else
        {
            parameters["client_secret"] = _authOptions.Password!;
        }

        using var content = new FormUrlEncodedContent(parameters);

        using var client = HttpClientHelper.CreateClient();
        using var response = await client.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var token = await ParseResponseJson<OfficeTokenResponse>(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new UserInformationException(Strings.MissingAccessToken, "MissingAccessTokenInResponse");

        return token.AccessToken;
    }

    /// <summary>
    /// Parses the response JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <param name="response">The HTTP response message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The deserialized object</returns>
    public static async Task<T> ParseResponseJson<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(content, GraphJsonOptions) ?? throw new JsonException("Deserialized object is null");
        }
        catch (JsonException ex)
        {
            throw new UserInformationException($"Failed to parse response JSON: {ex.Message}, JSON: {content}", "FailedToParseResponseJson", ex);
        }
    }

    /// <summary>
    /// Ensures that the response from the Office API is successful.
    /// </summary>
    /// <param name="response">The HTTP response message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public static async Task EnsureOfficeApiSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? responseBody = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(1));

            responseBody = response.Content == null
                ? null
                : await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Ignore failures when reading the response body; we'll throw with status code regardless.
        }

        var (errorCode, errorMessage) = TryExtractOfficeApiError(responseBody);
        var message = errorMessage
            ?? responseBody
            ?? response.ReasonPhrase
            ?? "Request failed.";

        // Ensure the status code is both in the message (human readable) and on the exception (machine readable).
        var statusCode = response.StatusCode;
        var detailedMessage = $"{message} (HTTP {(int)statusCode} {statusCode}) - Request URI: {response.RequestMessage?.RequestUri}";
        if (response?.Headers?.TryGetValues("Location", out var location) == true)
            detailedMessage += $" - Location: {location.First()}";
        var ex = new HttpRequestException(detailedMessage, inner: null, statusCode);

        // Expose the machine-readable Graph error code so callers can react to specific
        // conditions (e.g. a user without a provisioned mailbox) without matching on the
        // localized human-readable message.
        if (!string.IsNullOrWhiteSpace(errorCode))
            ex.Data[GraphErrorCodeKey] = errorCode;

        throw ex;
    }

    /// <summary>
    /// The <see cref="System.Exception.Data"/> key used to store the Graph API error code
    /// on thrown <see cref="HttpRequestException"/> instances.
    /// </summary>
    internal const string GraphErrorCodeKey = "GraphErrorCode";

    /// <summary>
    /// Gets the Graph API error code stored on an exception, if any.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns>The Graph error code, or null if none is present.</returns>
    internal static string? GetGraphErrorCode(Exception ex)
        => ex.Data.Contains(GraphErrorCodeKey) ? ex.Data[GraphErrorCodeKey] as string : null;

    /// <summary>
    /// Determines whether the exception indicates that the target user does not have an
    /// accessible Exchange Online mailbox (no license, disabled, soft-deleted, or on-premise).
    /// Graph returns HTTP 404 with an error code of <c>MailboxNotEnabledForRESTAPI</c> or
    /// <c>MailboxNotSupportedForRESTAPI</c> in these cases.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception indicates an unavailable mailbox; otherwise <c>false</c>.</returns>
    internal static bool IsMailboxNotEnabled(Exception ex)
    {
        if (ex is not HttpRequestException httpEx || httpEx.StatusCode != HttpStatusCode.NotFound)
            return false;

        var code = GetGraphErrorCode(ex);
        return string.Equals(code, "MailboxNotEnabledForRESTAPI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "MailboxNotSupportedForRESTAPI", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the exception indicates that the OneNote API rejected the
    /// application token. Since March 31st 2025 the OneNote API only accepts delegated
    /// tokens and answers application tokens with HTTP 401 and a message saying so.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception indicates OneNote rejected an app-only token; otherwise <c>false</c>.</returns>
    internal static bool IsOneNoteAppOnlyRejected(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } httpEx
            && httpEx.Message.Contains("app-only", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the exception is a bad request from the To Do API.
    /// The To Do endpoints answer requests made with an application token with a bare
    /// HTTP 400 "Invalid request" for every user, so a bad request on a list call is
    /// treated as "To Do is not available for this authentication".
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception is a bad request from the To Do API; otherwise <c>false</c>.</returns>
    internal static bool IsTodoBadRequest(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest } httpEx
            && httpEx.Message.Contains("/todo/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the exception indicates that SharePoint has locked the site.
    /// Graph answers with HTTP 423 Locked and "Access to this site has been blocked" for
    /// sites in a no-access lock state, typically the OneDrive site of a departed user
    /// that is kept for retention. Nothing in such a site can be read until it is unlocked.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><c>true</c> if the exception indicates a locked site; otherwise <c>false</c>.</returns>
    internal static bool IsSiteLocked(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.Locked };

    /// <summary>
    /// Attempts to extract the error code and message from the Office API response.
    /// </summary>
    /// <param name="responseBody">The response body</param>
    /// <returns>The extracted error code and message; either may be null if not found.</returns>
    private static (string? Code, string? Message) TryExtractOfficeApiError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return (null, null);

        // Fast filter: if it doesn't look like JSON, don't attempt to parse.
        var trimmed = responseBody.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                string? code = null;
                if (error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
                {
                    code = codeElement.GetString();
                    if (string.IsNullOrWhiteSpace(code))
                        code = null;
                }

                string? message = null;
                if (error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    message = messageElement.GetString();
                    if (string.IsNullOrWhiteSpace(message))
                        message = null;
                }

                return (code, message);
            }
        }
        catch (JsonException)
        {
            // Not JSON (or invalid JSON) - fall back to entire response body.
        }

        return (null, null);
    }

    /// <summary>
    /// Gets a single page of Graph API results.
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    /// <param name="url">The URL to fetch the page from</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>>The page of results</returns>
    public async Task<GraphPage<T>?> GetGraphPageAsync<T>(string url, Func<HttpResponseMessage, bool?>? shouldRetry, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, shouldRetry, _timeouts.ListTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<GraphPage<T>>(resp, ct).ConfigureAwait(false)
            ?? new GraphPage<T>();
    }


    /// <summary>
    /// Gets all Graph API results from a paged endpoint as an asynchronous enumerable.
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    /// <param name="initialUrl">The initial URL to fetch results from</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An asynchronous enumerable of all items</returns>
    public IAsyncEnumerable<T> GetAllGraphItemsAsync<T>(string initialUrl, CancellationToken ct)
        => GetAllGraphItemsAsync<T>(initialUrl, null, ct);

    /// <summary>
    /// Gets all Graph API results from a paged endpoint as an asynchronous enumerable.
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    /// <param name="initialUrl">The initial URL to fetch results from</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An asynchronous enumerable of all items</returns>
    public async IAsyncEnumerable<T> GetAllGraphItemsAsync<T>(string initialUrl, Func<HttpResponseMessage, bool?>? shouldRetry, [EnumeratorCancellation] CancellationToken ct)
    {
        var next = initialUrl;
        long? previousSkip = TryGetSkipValue(initialUrl);
        while (!string.IsNullOrWhiteSpace(next))
        {
            ct.ThrowIfCancellationRequested();

            var page = await GetGraphPageAsync<T>(next, shouldRetry, ct).ConfigureAwait(false);

            // If page is null, the shouldRetry callback indicated we should treat this as empty results
            // (e.g., 503 when OneDrive is not provisioned for a user)
            if (page == null)
                yield break;

            foreach (var item in page.Value)
                yield return item;
            next = page.NextLink;

            // Outlook endpoints page with $skip, and the end-of-results link has been observed
            // to carry a $skip that does not advance (e.g. $skip=0), which restarts the listing
            // from the beginning. The $skip value is documented as non-contiguous but it only
            // ever grows, so a value that does not increase cannot lead to unseen items.
            if (!string.IsNullOrWhiteSpace(next))
            {
                var nextSkip = TryGetSkipValue(next);
                if (nextSkip.HasValue && previousSkip.HasValue && nextSkip.Value <= previousSkip.Value)
                {
                    Log.WriteVerboseMessage(LOGTAG, "PagingRestartDetected", "Stopping enumeration because the next page offset {0} does not advance past {1}: {2}", nextSkip.Value, previousSkip.Value, next);
                    yield break;
                }

                if (nextSkip.HasValue)
                    previousSkip = nextSkip;
            }
        }
    }

    /// <summary>
    /// Extracts the <c>$skip</c> query value from a Graph paging URL, if present.
    /// The parameter name may be percent-encoded in links returned by Graph.
    /// </summary>
    /// <param name="url">The URL to inspect</param>
    /// <returns>The skip value, or null if the URL has no parseable <c>$skip</c></returns>
    internal static long? TryGetSkipValue(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var queryStart = url.IndexOf('?');
        if (queryStart < 0 || queryStart == url.Length - 1)
            return null;

        foreach (var part in url.Substring(queryStart + 1).Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = NetUri.UnescapeDataString(part.Substring(0, eq));
            if (!string.Equals(name, "$skip", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = NetUri.UnescapeDataString(part.Substring(eq + 1));
            return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var skip)
                ? skip
                : null;
        }

        return null;
    }

    /// <summary>
    /// Gets a single page of a Graph delta query.
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    /// <param name="url">The URL to fetch the page from</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The page of results</returns>
    private async Task<GraphDeltaPage<T>> GetGraphDeltaPageAsync<T>(string url, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("Prefer", $"odata.maxpagesize={GENERAL_PAGE_SIZE}");
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ListTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<GraphDeltaPage<T>>(resp, ct).ConfigureAwait(false)
            ?? new GraphDeltaPage<T>();
    }

    /// <summary>
    /// Walks a Graph delta endpoint from scratch and returns every item as an asynchronous enumerable.
    /// The walk pages with <c>$skiptoken</c> links and ends when Graph returns an <c>@odata.deltaLink</c>,
    /// which gives a consistent snapshot with a definite end, unlike <c>$skip</c> based paging.
    /// The returned delta link is not kept; this is a full listing, not change tracking.
    /// Items are de-duplicated by id, and a page consisting solely of already seen ids stops the walk.
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    /// <param name="initialUrl">The delta URL to start from</param>
    /// <param name="idSelector">Selects the id used to de-duplicate items</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An asynchronous enumerable of all items</returns>
    public async IAsyncEnumerable<T> GetAllGraphDeltaItemsAsync<T>(string initialUrl, Func<T, string> idSelector, [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var next = initialUrl;
        while (!string.IsNullOrWhiteSpace(next))
        {
            ct.ThrowIfCancellationRequested();

            var page = await GetGraphDeltaPageAsync<T>(next, ct).ConfigureAwait(false);

            var unseen = 0;
            foreach (var item in page.Value)
            {
                var id = idSelector(item);
                if (string.IsNullOrEmpty(id) || seen.Add(id))
                {
                    unseen++;
                    yield return item;
                }
            }

            if (page.Value.Count > 0 && unseen == 0)
            {
                Log.WriteVerboseMessage(LOGTAG, "DeltaPagingRepeatDetected", "Stopping delta enumeration because a page contained only already seen items: {0}", next);
                yield break;
            }

            // A deltaLink marks the end of the walk; it is intentionally not followed or stored
            next = page.NextLink;
        }
    }

    /// <summary>
    /// Re-orders a site feed alphabetically by name.
    /// </summary>
    /// <remarks>
    /// Microsoft Graph cannot sort sites: neither the tenant-wide <c>getAllSites</c> endpoint
    /// nor the subsites collection documents support for <c>$orderby</c>, and the site resource
    /// has no sortable key. The ordering therefore has to be applied on the client, which means
    /// the full feed must be buffered before the first item can be emitted. That is acceptable
    /// here because the site collection is orders of magnitude smaller than the user collection,
    /// and buffering has the added benefit of making the offset-based paging in the listing API
    /// deterministic across requests.
    /// </remarks>
    /// <param name="sites">The unordered site feed</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An asynchronous enumerable of sites, ordered by name</returns>
    internal static async IAsyncEnumerable<GraphSite> OrderSitesByNameAsync(IAsyncEnumerable<GraphSite> sites, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new List<GraphSite>();
        await foreach (var site in sites.ConfigureAwait(false))
            buffer.Add(site);

        // The id is the tie-breaker: display names are neither unique nor immutable,
        // so the name alone is not a deterministic sort key.
        foreach (var site in buffer
            .OrderBy(GetSiteSortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            yield return site;
        }
    }

    /// <summary>
    /// Gets the name to sort a site by. Sites returned from <c>getAllSites</c> frequently
    /// carry only <c>name</c> and no <c>displayName</c>, so both are considered.
    /// </summary>
    /// <param name="site">The site to get the sort name for</param>
    /// <returns>The name to sort by</returns>
    private static string GetSiteSortName(GraphSite site)
    {
        if (!string.IsNullOrWhiteSpace(site.DisplayName))
            return site.DisplayName;
        if (!string.IsNullOrWhiteSpace(site.Name))
            return site.Name;

        return site.WebUrl ?? site.Id;
    }

    /// <summary>
    /// Lists the immediate subsites of a SharePoint site.
    /// </summary>
    /// <param name="siteId">The parent site ID</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An asynchronous enumerable of subsites</returns>
    public IAsyncEnumerable<GraphSite> ListSubsitesAsync(string siteId, CancellationToken ct)
    {
        var baseUrl = GraphBaseUrl.TrimEnd('/');
        var select = GraphSelectBuilder.BuildSelect<GraphSite>();
        var site = System.Uri.EscapeDataString(siteId);

        var url =
            $"{baseUrl}/v1.0/sites/{site}/sites" +
            $"?$select={System.Uri.EscapeDataString(select)}" +
            $"&$top={GENERAL_PAGE_SIZE}";

        return GetAllGraphItemsAsync<GraphSite>(url, ct);
    }

    /// <summary>
    /// Gets a single Graph API item.
    /// </summary>
    /// <typeparam name="T">The graph item to get</typeparam>
    /// <param name="url">The URL to fetch</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>>The graph item</returns>
    public async Task<T> GetGraphItemAsync<T>(string url, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<T>(resp, ct).ConfigureAwait(false)
            ?? throw new UserInformationException("Failed to parse Graph item response.", nameof(SourceProvider));
    }

    /// <summary>
    /// Posts a Graph API item.
    /// </summary>
    /// <typeparam name="T">The type of the item to return</typeparam>
    /// <param name="url">The URL to post to</param>
    /// <param name="stream">The stream to post</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The posted item</returns>
    public async Task<T> PostGraphItemStreamAsync<T>(string url, Stream stream, string contentType, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            stream.Position = 0;
            req.Content = new StreamContent(stream);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<T>(resp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a Graph API item.
    /// </summary>
    /// <typeparam name="T">The type of the item to return</typeparam>
    /// <param name="url">The URL to post to</param>
    /// <param name="content">The content to post</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The posted item</returns>
    public async Task<T> PostGraphItemAsync<T>(string url, HttpContent content, CancellationToken ct)
    {
#if DEBUG
        if (content is StreamContent)
            throw new InvalidOperationException("Use PostGraphItemStreamAsync for stream content.");
#endif

        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = content;
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<T>(resp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a Graph API item and ignores the response body.
    /// </summary>
    /// <param name="url">The URL to post to</param>
    /// <param name="content">The content to post</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task PostGraphItemNoResponseAsync(string url, HttpContent content, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = content;
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Patches a Graph API item.
    /// </summary>
    /// <param name="url">The URL to patch</param>
    /// <param name="content">The content to patch</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task PatchGraphItemAsync(string url, HttpContent content, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = content;
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
    }


    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// This method will buffer the response in memory.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="prefer">The Prefer header value</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    public Task<Stream> GetGraphItemAsStreamAsync(string url, string accept, string prefer, CancellationToken ct)
        => GetGraphItemAsStreamAsync(url, accept, prefer, null, ct);

    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// This method will buffer the response in memory.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    public Task<Stream> GetGraphItemAsStreamAsync(string url, string accept, CancellationToken ct)
        => GetGraphItemAsStreamAsync(url, accept, null, null, ct);

    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// This method will buffer the response in memory.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="prefer">The Prefer header value</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    private async Task<Stream> GetGraphItemAsStreamAsync(
        string url,
        string accept,
        string? prefer,
        Func<HttpResponseMessage, bool?>? shouldRetry,
        CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

            if (!string.IsNullOrWhiteSpace(prefer))
                req.Headers.TryAddWithoutValidation("Prefer", prefer);

            return req;
        }

        using var resp = await SendWithRetryAsync(
            requestFactory,
            HttpCompletionOption.ResponseHeadersRead,
            shouldRetry,
            _timeouts.ShortTimeout,
            ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return Stream.Null;

        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        // Return a stream the caller can own safely after HttpResponseMessage disposal.
        var ms = new MemoryStream();
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);
        await timeoutStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    public async Task<Stream> GetGraphResponseAsRealStreamAsync(string url, string accept, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            return req;
        }

        using var resp = await SendWithRetryAsync(
            requestFactory,
            HttpCompletionOption.ResponseHeadersRead,
            null,
            _timeouts.ShortTimeout,
            ct).ConfigureAwait(false);

        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        if (resp.Content.Headers.ContentLength == 0)
            return Stream.Null;

        // Return a stream the caller can own safely after HttpResponseMessage disposal.
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);

        // The caller expects a FileStream-like stream, so we need to buffer to a temp file.
        // We could wrap this in a stream that exposes the Length property, which would avoid the temp file.
        var tempStream = TempFileStream.Create();
        await timeoutStream.CopyToAsync(tempStream, ct).ConfigureAwait(false);
        tempStream.Position = 0;

        // Return the stream, will delete on dispose.
        return tempStream;
    }

    /// <summary>
    /// Gets the HTTP client, acquiring an access token if needed.
    /// </summary>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The HTTP client</returns>
    private async Task<HttpClient> GetHttpClientAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_graphAccessToken))
            _graphAccessToken = await AcquireAccessTokenAsync(false, ct).ConfigureAwait(false);

        return _httpClient;
    }

    /// <summary>
    /// Sends an HTTP request with retry logic for transient failures, using short timeouts.
    /// </summary>
    /// <param name="requestFactory">The factory to create the HTTP request</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>>The HTTP response message</returns>
    public async Task<HttpResponseMessage> SendWithRetryShortAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        CancellationToken ct
    )
        => await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, _timeouts.ShortTimeout, ct).ConfigureAwait(false);

    /// <summary>
    /// Sends an HTTP request with retry logic for transient failures.
    /// </summary>
    /// <param name="requestFactory">The factory to create the HTTP request</param>
    /// <param name="completionOption">The HTTP completion option</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="timeout">The timeout for the request</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>>The HTTP response message</returns>
    public async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        HttpCompletionOption completionOption,
        Func<HttpResponseMessage, bool?>? shouldRetry,
        TimeSpan? timeout,
        CancellationToken ct
)
    {
        // Budget for timeouts, transport errors and transient server errors
        const int MAX_RETRIES = 4;
        // Separate budget for throttling. Graph tells the client exactly how long to wait,
        // so waiting is the right response; giving up after a few attempts during a
        // throttling window drops a file, or a whole folder listing, from the backup.
        const int MAX_THROTTLE_RETRIES = 12;
        var maxThrottleWait = TimeSpan.FromMinutes(20);

        var attempt = 0;
        var throttleAttempt = 0;
        var throttleWaited = TimeSpan.Zero;
        var isReAuthAttempt = false;
        var client = await GetHttpClientAsync(ct).ConfigureAwait(false);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            HttpResponseMessage? resp = null;
            Exception? transportError = null;
            try
            {
                using var request = await requestFactory(ct).ConfigureAwait(false);
                if (timeout.HasValue)
                {
                    resp = await Utility.WithTimeout(timeout.Value, ct, c => client.SendAsync(request, completionOption, c)).ConfigureAwait(false);
                }
                else
                {
                    resp = await client.SendAsync(request, completionOption, ct).ConfigureAwait(false);
                }
            }
            catch (TimeoutException ex)
            {
                transportError = ex;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == null)
            {
                // No status code means the request never got an HTTP answer: a reset
                // connection, a failed TLS handshake or a name lookup error. These are
                // transient in practice and worth the same retries as a timeout.
                transportError = ex;
            }

            var throttled = false;
            if (resp != null)
            {
                if (resp.IsSuccessStatusCode)
                    return resp;

                if (shouldRetry?.Invoke(resp) == false)
                    return resp;

                // Retryable status codes:
                // 429: TooManyRequests
                // 503: ServiceUnavailable
                // 502: BadGateway
                // 504: GatewayTimeout

                switch (resp.StatusCode)
                {
                    case HttpStatusCode.TooManyRequests:
                        throttled = true;
                        break;
                    case HttpStatusCode.ServiceUnavailable:
                    case HttpStatusCode.BadGateway:
                    case HttpStatusCode.GatewayTimeout:
                        break;
                    case HttpStatusCode.Unauthorized:
                        _graphAccessToken = null; // force re-auth
                        if (isReAuthAttempt)
                            return resp; // already tried re-auth
                        isReAuthAttempt = true;
                        break;
                    default:
                        return resp;
                }
            }

            // Respect Retry-After if present, else exponential backoff.
            TimeSpan delay;
            if (resp?.Headers.RetryAfter?.Delta is TimeSpan ra)
            {
                delay = ra;
            }
            else if (resp?.Headers.RetryAfter?.Date is DateTimeOffset until)
            {
                delay = until - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;
            }
            else
            {
                var baseSeconds = Math.Min(60, Math.Pow(2, Math.Max(attempt, throttleAttempt) + 1)); // cap at 60s
                var jitterMs = Random.Shared.Next(0, 500);
                delay = TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMs);
            }

            if (throttled)
            {
                throttleAttempt++;
                if (throttleAttempt > MAX_THROTTLE_RETRIES || throttleWaited + delay > maxThrottleWait)
                    return resp!; // let EnsureOfficeApiSuccessAsync throw with details

                throttleWaited += delay;
                Log.WriteRetryMessage(LOGTAG, "Office365APIThrottled", null, $"Request was throttled (HTTP 429). Waiting {delay.TotalSeconds:0}s before retrying attempt {throttleAttempt} of {MAX_THROTTLE_RETRIES}.");
            }
            else
            {
                attempt++;
                if (attempt > MAX_RETRIES)
                {
                    if (resp != null)
                        return resp; // let EnsureOfficeApiSuccessAsync throw with details

                    if (transportError is HttpRequestException httpError)
                        throw new HttpRequestException($"Request failed after {MAX_RETRIES} attempts: {httpError.Message}", httpError);

                    throw new TimeoutException($"Request timed out after {MAX_RETRIES} attempts");
                }

                if (resp != null)
                    Log.WriteRetryMessage(LOGTAG, "Office365APIRetry", null, $"Request failed with status code {(int)resp.StatusCode} {resp.StatusCode}. Retrying attempt {attempt} of {MAX_RETRIES}.");
                else if (transportError is HttpRequestException)
                    Log.WriteRetryMessage(LOGTAG, "Office365APITransportError", transportError, $"Request failed before a response was received. Retrying attempt {attempt} of {MAX_RETRIES}.");
                else
                    Log.WriteRetryMessage(LOGTAG, "Office365APITimeout", null, $"Request timed out. Retrying attempt {attempt} of {MAX_RETRIES}.");
            }

            resp?.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Uploads a stream to an upload session.
    /// </summary>
    /// <param name="uploadUrl">The upload session URL</param>
    /// <param name="contentStream">The content stream</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task UploadFileToSessionAsync(string uploadUrl, Stream contentStream, CancellationToken ct)
    {
        // 320 KiB is the required multiple for Graph API
        const int ChunkSize = 320 * 1024 * 10; // 3.2 MB chunks

        var buffer = new byte[ChunkSize];
        long totalLength = contentStream.Length;
        long position = 0;

        while (position < totalLength)
        {
            var bytesRead = await contentStream.ReadAsync(buffer, 0, ChunkSize, ct).ConfigureAwait(false);
            if (bytesRead == 0) break;

            var rangeHeader = $"bytes {position}-{position + bytesRead - 1}/{totalLength}";

            HttpRequestMessage requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Put, new NetUri(uploadUrl));
                var ms = new MemoryStream(buffer, 0, bytesRead);
                var timeoutStream = ms.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);
                req.Content = new StreamContent(timeoutStream);
                req.Content.Headers.ContentLength = bytesRead;
                req.Content.Headers.ContentRange = ContentRangeHeaderValue.Parse(rangeHeader);
                return req;
            }

            using var resp = await SendWithRetryAsync(
                ct => Task.FromResult(requestFactory(ct)),
                HttpCompletionOption.ResponseHeadersRead,
                null,
                null,
                ct).ConfigureAwait(false);

            await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            position += bytesRead;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient.Dispose();
        _graphAccessToken = null;
    }
}
