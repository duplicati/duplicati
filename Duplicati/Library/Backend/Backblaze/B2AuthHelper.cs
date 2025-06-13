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

using System.Net;
using System.Text;
using Duplicati.Library.Backend.Backblaze.Model;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Exception = System.Exception;

namespace Duplicati.Library.Backend.Backblaze;

/// <summary>
/// Helper class for handling Backblaze B2 authorization
/// </summary>
/// <param name="userid">Username</param>
/// <param name="password">Password</param>
/// <param name="httpClient">HttpClient instance to use and pass along to JsonWebHelperHttpClient</param>
public class B2AuthHelper(string userid, string password, HttpClient httpClient, TimeoutOptionsHelper.Timeouts timeouts) : JsonWebHelperHttpClient(httpClient)
{
    /// <summary>
    /// The configuration details after authorization
    /// </summary>
    /// <param name="AccountID">The account ID</param>
    /// <param name="APIUrl">The API URL</param>
    /// <param name="AuthorizationToken">The authorization token</param>
    /// <param name="DownloadUrl">The download URL</param>
    public record AuthResponse(string AccountID, string APIUrl, string AuthorizationToken, string DownloadUrl);

    /// <summary>
    /// Cached authorization response
    /// </summary>
    private AuthResponse? _CachedAuthResponse;

    /// <summary>
    /// Cached authorization response expiration time
    /// </summary>
    private DateTime _configExpires;

    /// <summary>
    /// Defines lifetime of cached authorization response
    /// </summary>
    private const int CACHE_EXPIRATION_MINUTES = 60;

    /// <summary>
    /// Authorization URL
    /// </summary>
    internal const string AUTH_URL = "https://api.backblazeb2.com/b2api/v1/b2_authorize_account";

    /// <summary>
    /// Maximum number of retries for authorization
    /// </summary>
    private const int MAX_AUTHORIZATION_RETRIES = 5;

    /// <summary>
    /// Creates an HTTP request message with authorization header
    /// </summary>
    /// <param name="url">The URL for the request</param>
    /// <param name="method">HTTP method (defaults to GET if null)</param>
    /// <returns>Configured HttpRequestMessage</returns>
    public override async Task<HttpRequestMessage> CreateRequestAsync(string url, HttpMethod method, CancellationToken cancellationToken)
    {
        var request = await base.CreateRequestAsync(url, method, cancellationToken).ConfigureAwait(false);
        var config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.TryAddWithoutValidation("Authorization", config.AuthorizationToken);
        request.Headers.Add("User-Agent", UserAgent);
        return request;
    }

    /// <summary>
    /// Cleans the url to remove trailing slashes
    /// </summary>
    /// <param name="url">URL</param>
    private static string DropTrailingSlashes(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        while (url.EndsWith("/", StringComparison.Ordinal))
            url = url.Substring(0, url.Length - 1);
        return url;
    }

    public async Task<AuthResponse> GetConfigAsync(CancellationToken cancelToken)
    {
        if (_CachedAuthResponse != null && _configExpires >= DateTime.UtcNow)
            return _CachedAuthResponse;

        var retries = 0;

        while (true)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = await base.CreateRequestAsync(AUTH_URL, HttpMethod.Get, cancelToken).ConfigureAwait(false);

                request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(userid + ":" + password))}");
                request.Headers.Add("ContentType", "application/json; charset=utf-8");


                var authResponse = await Utility.Utility.WithTimeout(timeouts.ShortTimeout, cancelToken, async ct =>
                {
                    response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    return await ReadJsonResponseAsync<ApiAuthResponse>(response, ct).ConfigureAwait(false)
                        ?? throw new Exception("Failed to parse authorization response");
                }).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(authResponse.AccountID))
                    throw new Exception("Account ID is missing from authorization response");

                if (string.IsNullOrWhiteSpace(authResponse.APIUrl))
                    throw new Exception("API URL is missing from authorization response");

                if (string.IsNullOrWhiteSpace(authResponse.AuthorizationToken))
                    throw new Exception("Authorization token is missing from authorization response");

                if (string.IsNullOrWhiteSpace(authResponse.DownloadUrl))
                    throw new Exception("Download URL is missing from authorization response");

                authResponse.APIUrl = DropTrailingSlashes(authResponse.APIUrl);
                authResponse.DownloadUrl = DropTrailingSlashes(authResponse.DownloadUrl);
                _configExpires = DateTime.UtcNow + TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES);

                return _CachedAuthResponse = new AuthResponse(authResponse.AccountID, authResponse.APIUrl, authResponse.AuthorizationToken, authResponse.DownloadUrl);
            }
            catch (Exception ex)
            {
                var clientError = false;

                try
                {
                    // Only retry once on client errors
                    if (ex is HttpRequestException { StatusCode: not null } exception)
                    {
                        var sc = (int)exception.StatusCode;
                        clientError = sc is >= 400 and <= 499;
                    }
                }
                catch
                {
                    // ignored
                }

                if (retries >= (clientError ? 1 : MAX_AUTHORIZATION_RETRIES))
                {
                    await AttemptParseAndThrowExceptionAsync(ex, response, cancelToken);
                    throw;
                }

                Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                retries++;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    /// <summary>
    /// Parses the exception and throws a new exception with the error message
    /// </summary>
    /// <param name="ex">Exception to be parsed</param>
    /// <param name="responseContext">Response context</param>
    /// <exception cref="Exception">New detailed exception</exception>
    public override async Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? responseContext, CancellationToken cancelToken)
    {
        if (ex is not HttpRequestException || responseContext == null)
            return;

        if (ex is HttpRequestException && responseContext.StatusCode == HttpStatusCode.TooManyRequests)
            throw new TooManyRequestException(responseContext.Headers.RetryAfter);

        var rawData = await Utility.Utility.WithTimeout(timeouts.ShortTimeout, cancelToken, ct =>
        {
            using var stream = responseContext.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }).ConfigureAwait(false);

        var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(rawData)
            ?? new ErrorResponse { Status = (int)responseContext.StatusCode, Message = responseContext.ReasonPhrase };
        throw new UserInformationException($"Backblaze ErrorResponse: {errorResponse.Status} - {errorResponse.Code}: {errorResponse.Message}", "BackblazeErrorResponse");
    }

    /// <summary>
    /// Extract Http status code from exception
    /// </summary>
    /// <param name="ex">Exception to be parsed</param>
    public static HttpStatusCode GetExceptionStatusCode(Exception ex)
    {
        return ex is HttpRequestException { StatusCode: not null } httpEx ? httpEx.StatusCode.Value : default;
    }
}