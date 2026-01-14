using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using NetUri = System.Uri;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Implements the API helper class for Microsoft Graph API access
/// </summary>
internal class APIHelper : IDisposable
{
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
    /// Initializes a new instance of the <see cref="APIHelper"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests</param>
    /// <param name="authOptions">The authentication options</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="graphBaseUrl">The base URL for Graph API requests</param>
    private APIHelper(HttpClient httpClient, AuthOptionsHelper.AuthOptions authOptions, string tenantId, string graphBaseUrl)
    {
        _httpClient = httpClient;
        _authOptions = authOptions;
        _tenantId = tenantId;
        _graphBaseUrl = graphBaseUrl;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="APIHelper"/> class.
    /// </summary>
    /// <param name="authOptions">The authentication options</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="graphBaseUrl">The base URL for Graph API requests</param>
    /// <returns>A new instance of the <see cref="APIHelper"/> class</returns>
    public static APIHelper Create(AuthOptionsHelper.AuthOptions authOptions, string tenantId, string graphBaseUrl)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false
        };

        var httpClient = HttpClientHelper.CreateClient(handler);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Duplicati/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

        return new APIHelper(httpClient, authOptions, tenantId, graphBaseUrl);
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
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _authOptions.Username!,
            ["client_secret"] = _authOptions.Password!,
            ["scope"] = $"{_graphBaseUrl.TrimEnd('/')}/.default"
        });

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
            return JsonSerializer.Deserialize<T>(content) ?? throw new JsonException("Deserialized object is null");
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

        var message = TryExtractOfficeApiErrorMessage(responseBody)
            ?? responseBody
            ?? response.ReasonPhrase
            ?? "Request failed.";

        // Ensure the status code is both in the message (human readable) and on the exception (machine readable).
        var statusCode = response.StatusCode;
        var detailedMessage = $"{message} (HTTP {(int)statusCode} {statusCode}) - Request URI: {response.RequestMessage?.RequestUri}";
        if (response?.Headers?.TryGetValues("Location", out var location) == true)
            detailedMessage += $" - Location: {location.First()}";
        throw new HttpRequestException(detailedMessage, inner: null, statusCode);
    }

    /// <summary>
    /// Attempts to extract the error message from the Office API response.
    /// </summary>
    /// <param name="responseBody">The response body</param>
    /// <returns>The extracted error message, or null if none could be found</returns>
    private static string? TryExtractOfficeApiErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        // Fast filter: if it doesn't look like JSON, don't attempt to parse.
        var trimmed = responseBody.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    var message = messageElement.GetString();
                    return string.IsNullOrWhiteSpace(message) ? null : message;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (or invalid JSON) - fall back to entire response body.
        }

        return null;
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

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, shouldRetry, ct).ConfigureAwait(false);
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
        }
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
        var select = GraphSelectBuilder.BuildSelect<T>();

        async Task<HttpRequestMessage> requestFactory(CancellationToken cancellationToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, cancellationToken);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, null, ct).ConfigureAwait(false);
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        return await ParseResponseJson<T>(resp, ct).ConfigureAwait(false)
            ?? throw new UserInformationException("Failed to parse Graph item response.", nameof(SourceProvider));
    }

    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    public Task<Stream> GetGraphAsStreamAsync(string url, string accept, CancellationToken ct)
        => GetGraphAsStreamAsync(url, accept, null, ct);

    /// <summary>
    /// Returns the response from the Graph API as a stream.
    /// </summary>
    /// <param name="url">The URL to fetch</param>
    /// <param name="accept">The Accept header value</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The response stream</returns>
    private async Task<Stream> GetGraphAsStreamAsync(string url, string accept, Func<HttpResponseMessage, bool?>? shouldRetry, CancellationToken ct)
    {
        async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, new NetUri(url));
            req.Headers.Authorization = await GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            return req;
        }

        using var resp = await SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, shouldRetry, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return Stream.Null;
        await EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

        // Return a stream the caller can own safely after HttpResponseMessage disposal.
        var ms = new MemoryStream();
        await resp.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Position = 0;
        return ms;
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
    /// Sends an HTTP request with retry logic for transient failures.
    /// </summary>
    /// <param name="requestFactory">The factory to create the HTTP request</param>
    /// <param name="completionOption">The HTTP completion option</param>
    /// <param name="shouldRetry">A callback to determine if a request should be retried</param>
    /// <param name="ct">The cancellation token</param>
    /// <param name="maxRetries">The maximum number of retries</param>
    /// <returns>>The HTTP response message</returns>
    public async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        HttpCompletionOption completionOption,
        Func<HttpResponseMessage, bool?>? shouldRetry,
        CancellationToken ct,
        int maxRetries = 4)
    {
        var attempt = 0;
        var isReAuthAttempt = false;
        var client = await GetHttpClientAsync(ct).ConfigureAwait(false);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using var request = await requestFactory(ct).ConfigureAwait(false);
            var resp = await client.SendAsync(request, completionOption, ct).ConfigureAwait(false);

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

            attempt++;
            if (attempt > maxRetries)
                return resp; // let EnsureOfficeApiSuccessAsync throw with details

            // Respect Retry-After if present, else exponential backoff.
            TimeSpan delay;
            if (resp.Headers.RetryAfter?.Delta is TimeSpan ra)
            {
                delay = ra;
            }
            else
            {
                var baseSeconds = Math.Min(60, Math.Pow(2, attempt)); // cap at 60s
                var jitterMs = Random.Shared.Next(0, 500);
                delay = TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMs);
            }

            resp.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient.Dispose();
        _graphAccessToken = null;
    }
}
