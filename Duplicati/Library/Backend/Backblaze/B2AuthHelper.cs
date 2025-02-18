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
using Newtonsoft.Json;
using Exception = System.Exception;
using Uri = System.Uri;

namespace Duplicati.Library.Backend.Backblaze;

/// <summary>
/// Helper class for handling Backblaze B2 authorization
/// </summary>
/// <param name="userid">Username</param>
/// <param name="password">Password</param>
/// <param name="httpClient">HttpClient instance to use and pass along to JsonWebHelperHttpClient</param>
public class B2AuthHelper(string userid, string password, HttpClient httpClient) : JsonWebHelperHttpClient(httpClient)
{
    /// <summary>
    /// Cached authorization response
    /// </summary>
    private AuthResponse _CachedAuthResponse;

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
    /// Timeout for authentication requests
    /// </summary>
    private const int AUTHENTICATION_TIMEOUT_SECONDS = 10;

    /// <summary>
    /// Maximum number of retries for authorization
    /// </summary>
    private const int MAX_AUTHORIZATION_RETRIES = 5;

    /// <summary>
    /// Authorization token (fetches from Config, which will refresh if needed)
    /// </summary>
    private string AuthorizationToken => Config.AuthorizationToken;

    /// <summary>
    /// API URL (fetches from Config, which will refresh if needed)
    /// </summary>
    public string ApiUrl => Config.APIUrl;

    /// <summary>
    /// Download URL (fetches from Config, which will refresh if needed)
    /// </summary>
    public string DownloadUrl => Config.DownloadUrl;

    /// <summary>
    /// Account ID (fetches from Config, which will refresh if needed)
    /// </summary>
    public string AccountId => Config.AccountID;

    /// <summary>
    /// Creates an HTTP request message with authorization header
    /// </summary>
    /// <param name="url">The URL for the request</param>
    /// <param name="method">HTTP method (defaults to GET if null)</param>
    /// <returns>Configured HttpRequestMessage</returns>
    public override HttpRequestMessage CreateRequest(string url, string method = null)
    {
        HttpRequestMessage request = base.CreateRequest(url, method);
        request.Headers.TryAddWithoutValidation("Authorization", AuthorizationToken);
        request.Headers.Add("User-Agent", UserAgent);
        return request;
    }

    /// <summary>
    /// Cleans the url to remove trailing slashes
    /// </summary>
    /// <param name="url">URL</param>
    private string DropTrailingSlashes(string url)
    {
        while (url.EndsWith("/", StringComparison.Ordinal))
            url = url.Substring(0, url.Length - 1);
        return url;
    }

    /// <summary>
    /// API DNS Name
    /// </summary>
    public string ApiDnsName =>
        _CachedAuthResponse == null || string.IsNullOrWhiteSpace(_CachedAuthResponse.APIUrl)
            ? null
            : new Uri(_CachedAuthResponse.APIUrl).Host;

    /// <summary>
    /// Download DNS Name
    /// </summary>
    public string DownloadDnsName =>
        _CachedAuthResponse == null || string.IsNullOrWhiteSpace(_CachedAuthResponse.DownloadUrl)
            ? null
            : new Uri(_CachedAuthResponse.DownloadUrl).Host;

    private AuthResponse Config
    {
        get
        {
            if (_CachedAuthResponse != null && _configExpires >= DateTime.UtcNow) return _CachedAuthResponse;
            var retries = 0;

            while (true)
            {
                HttpResponseMessage response = null;
                try
                {

                    using var request = base.CreateRequest(AUTH_URL);

                    request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(userid + ":" + password))}");
                    request.Headers.Add("ContentType", "application/json; charset=utf-8");

                    using var timeoutToken = new CancellationTokenSource();
                    timeoutToken.CancelAfter(TimeSpan.FromSeconds(AUTHENTICATION_TIMEOUT_SECONDS));

                    response = _httpClient.Send(request, timeoutToken.Token);
                    response.EnsureSuccessStatusCode();

                    _CachedAuthResponse = ReadJsonResponse<AuthResponse>(response);
                    _CachedAuthResponse.APIUrl = DropTrailingSlashes(_CachedAuthResponse.APIUrl);
                    _CachedAuthResponse.DownloadUrl = DropTrailingSlashes(_CachedAuthResponse.DownloadUrl);
                    _configExpires = DateTime.UtcNow + TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES);

                    response.Dispose();
                    return _CachedAuthResponse;
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
                        AttemptParseAndThrowException(ex, response);
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                    retries++;
                }
            }
        }
    }

    /// <summary>
    /// Parses the exception and throws a new exception with the error message
    /// </summary>
    /// <param name="ex">Exception to be parsed</param>
    /// <param name="responseContext">Response context</param>
    /// <exception cref="Exception">New detailed exception</exception>
    public override void AttemptParseAndThrowException(Exception ex, HttpResponseMessage responseContext = null)
    {
        if (ex is not HttpRequestException || responseContext == null)
            return;

        if (ex is HttpRequestException && responseContext != null && responseContext.StatusCode == HttpStatusCode.TooManyRequests)
            throw new TooManyRequestException(responseContext.Headers.RetryAfter);

        using var stream = responseContext.Content.ReadAsStream();
        using var reader = new StreamReader(stream);
        var rawData = reader.ReadToEnd();
        var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(rawData);
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