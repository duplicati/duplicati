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

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Newtonsoft.Json;

namespace Duplicati.Library.Backend;

public class JottacloudAuthHelper : OAuthHelperHttpClient, IDisposable
{
    private const string USERINFO_URL = "https://id.jottacloud.com/auth/realms/jottacloud/protocol/openid-connect/userinfo";

    private JottacloudAuthHelper(HttpClient httpClient, string accessToken, string oauthUrl)
        : base(accessToken, "jottacloud", oauthUrl, httpClient) // This will purposefully create a new HttpClient instance per AuthHelper instance
    {
        AutoAuthHeader = true;
        AutoV2 = false; // Jottacloud is not v2 compatible because it generates a new refresh token with every access token refresh and invalidates the old.
        Username = null!; // Setting via initializer
    }

    public static Task<JottacloudAuthHelper> CreateAsync(string accessToken, string oauthUrl, CancellationToken cancellationToken = default)
        => CreateAsync(accessToken, oauthUrl, null, cancellationToken);

    /// <summary>
    /// Creates the helper with the supplied <see cref="HttpClient"/>, or with a new one if none is given
    /// </summary>
    internal static async Task<JottacloudAuthHelper> CreateAsync(string accessToken, string oauthUrl, HttpClient? httpClient, CancellationToken cancellationToken)
    {
        if (httpClient == null)
        {
            httpClient = HttpClientHelper.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }
        try
        {
            var inst = new JottacloudAuthHelper(httpClient, accessToken, oauthUrl);
            var userinfo = await inst.GetJsonDataAsync<UserInfo>(USERINFO_URL, cancellationToken);
            if (userinfo == null || string.IsNullOrEmpty(userinfo.Username))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError, "JottaNoUsername");

            inst.Username = userinfo.Username;
            return inst;
        }
        catch (Exception ex)
        {
            httpClient.Dispose();
            throw new UserInformationException($"Failed to retrieve user information: {ex.Message}", "JottaUserInfoError", ex);
        }
    }

    public string Username { get; private set; }

    /// <summary>
    /// The longest error body that is read to explain a failed request
    /// </summary>
    private const int MAX_ERROR_BODY_LENGTH = 16 * 1024;

    /// <summary>
    /// How long to wait for the error body of a failed request
    /// </summary>
    private static readonly TimeSpan ERROR_BODY_TIMEOUT = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Adds the explanation from the Jottacloud error body to the message of a failed request.
    /// A response that tells the client to slow down gets a <see cref="TooManyRequestException"/>
    /// as the inner exception, so the wait the server asks for can be respected. Jottacloud answers
    /// with 429 when the number of requests is limited; a 503 counts when it says how long to wait.
    /// </summary>
    /// <inheritdoc/>
    public override async Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? responseContext, CancellationToken cancellationToken)
    {
        if (ex is not HttpRequestException hex || responseContext == null)
            return;

        var rateLimited = IsRateLimited(responseContext);
        var error = await JottacloudError.TryReadAsync(responseContext, MAX_ERROR_BODY_LENGTH, ERROR_BODY_TIMEOUT, cancellationToken).ConfigureAwait(false);
        var description = JottacloudError.Describe(error, responseContext.StatusCode, responseContext.RequestMessage?.RequestUri);
        if (description == null && !rateLimited)
            return;

        var message = description == null ? hex.Message : $"{hex.Message} {Strings.Jottacloud.ErrorResponse(description)}";
        var inner = rateLimited ? new TooManyRequestException(responseContext.Headers.RetryAfter!) : hex.InnerException;
        throw new HttpRequestException(hex.HttpRequestError, message, inner, responseContext.StatusCode);
    }

    /// <summary>
    /// Determines whether a response refuses the request because of a rate limit
    /// </summary>
    /// <param name="response">The response</param>
    /// <returns><c>true</c> if the client should wait before sending more requests</returns>
    private static bool IsRateLimited(HttpResponseMessage response)
        => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter != null);

    private class UserInfo
    {
        [JsonProperty("sub")]
        public string? Subject { get; set; }
        [JsonProperty("email_verified")]
        public bool EmailVerified { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("realm")]
        public string? Realm { get; set; }
        [JsonProperty("preferred_username")]
        public string? PreferredUsername { get; set; } // The numeric internal username, same as Username
        [JsonProperty("given_name")]
        public string? GivenName { get; set; }
        [JsonProperty("family_name")]
        public string? FamilyName { get; set; }
        [JsonProperty("email")]
        public string? Email { get; set; }
        [JsonProperty("username")]
        public string? Username { get; set; } // The numeric internal username, same as PreferredUsername
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}