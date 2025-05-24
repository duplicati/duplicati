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

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;

namespace Duplicati.Library;

public class OAuthHelperHttpClient : JsonWebHelperHttpClient
{
    private string _Token;
    private string _Authid;
    private DateTime _mTokenExpires = DateTime.UtcNow;
    protected string OAuthLoginUrl { get; }

    /// <summary>
    /// Timeout for authentication requests
    /// </summary>
    private static readonly TimeSpan AUTHENTICATION_TIMEOUT = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Maximum number of retries for authorization
    /// </summary>
    private const int MAX_AUTHORIZATION_RETRIES = 5;

    /// <summary>
    /// Returns the URL to use for obtaining an OAuth token for the given module.
    /// </summary>
    /// <param name="modulename">The name of the module to use.</param>
    /// <returns>The URL to use for obtaining an OAuth token.</returns>
    public static string OAUTH_LOGIN_URL(string modulename)
    {
        var u = new Library.Utility.Uri(OAuthContextSettings.ServerURL);
        var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
        return string.Format(addr.ToString(), modulename);
    }

    /// <summary>
    /// Returns the URL to use for obtaining an OAuth token for the given module, defaulting to the new server.
    /// </summary>
    /// <param name="modulename">The name of the module to use.</param>
    public static string OAUTH_LOGIN_URL_NEW(string modulename)
    {
        var baseUrl = OAuthContextSettings.ServerURLRaw;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = OAuthContextSettings.DUPLICATI_OAUTH_SERVICE_NEW;

        var u = new Library.Utility.Uri(baseUrl);
        var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
        return string.Format(addr.ToString(), modulename);
    }

    /// <summary>
    /// Set to true to automatically add the Authorization header to requests
    /// </summary>
    public bool AutoAuthHeader { get; set; }

    /// <summary>
    /// Set to true if the provider does not use refresh tokens, but only access tokens
    /// </summary>
    public bool AccessTokenOnly { get; set; }

    /// <summary>
    /// If true (the default), when a v1 authid is being used it will be swapped
    /// with a v2 authid, when the OAuth service returns one (which it dypically
    /// does after a provider token refresh has been performed). Some providers
    /// are not compatible with v2 authid, tyically because they generate a new
    /// refresh token with every access token refresh and invalidates the old.
    /// If the oauth service still returns a v2 authid for such a provider,
    /// set this property to false to make Duplicati ignore it.
    /// </summary>
    public bool AutoV2 { get; set; } = true;

    public OAuthHelperHttpClient(string authid, string servicename, HttpClient httpClient = null, string useragent = null)
        : base(httpClient ?? HttpClientHelper.CreateClient())
    {
        _Authid = authid;
        OAuthLoginUrl = OAUTH_LOGIN_URL(servicename);

        if (string.IsNullOrEmpty(authid))
            throw new Interface.UserInformationException(
                Strings.OAuthHelper.MissingAuthID(OAuthLoginUrl), "MissingAuthID");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(string url, HttpMethod method, bool noAuthorization, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("User-Agent", UserAgent);
        if (!noAuthorization && AutoAuthHeader && !string.Equals(OAuthContextSettings.ServerURL, url)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
        return request;
    }

    public override Task<HttpRequestMessage> CreateRequestAsync(string url, HttpMethod method, CancellationToken cancellationToken)
        => CreateRequestAsync(url, method, false, cancellationToken);

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (AccessTokenOnly)
            return _Authid;

        if (_Token == null || _mTokenExpires < DateTime.UtcNow)
        {
            var retries = 0;

            while (true)
            {
                HttpResponseMessage response = null;
                try
                {
                    using var request = await CreateRequestAsync(OAuthContextSettings.ServerURL, HttpMethod.Get, false, cancellationToken).ConfigureAwait(false);

                    return await Utility.Utility.WithTimeout(AUTHENTICATION_TIMEOUT, cancellationToken, async ct =>
                    {
                        request.Headers.Add("X-AuthID", _Authid);
                        response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();

                        var res = await ReadJsonResponseAsync<OAuthServiceResponse>(response, ct).ConfigureAwait(false);

                        _mTokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                        if (AutoV2 && !string.IsNullOrWhiteSpace(res.v2_authid))
                            _Authid = res.v2_authid;
                        return _Token = res.access_token;
                    }).ConfigureAwait(false);
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

                    if (response != null && response.Headers.Contains("X-Reason"))
                    {
                        var msg = response.Headers.GetValues("X-Reason").FirstOrDefault();

                        if (string.IsNullOrWhiteSpace(msg))
                            msg = response.StatusCode.ToString();

                        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                        {
                            string errorKey = msg == response.StatusCode.ToString()
                                ? "OAuthOverQuotaError"
                                : "OAuthLoginError";

                            string errorMessage = errorKey == "OAuthOverQuotaError"
                                ? Strings.OAuthHelper.OverQuotaError
                                : Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl);

                            throw new Interface.UserInformationException(errorMessage, errorKey,
                                errorKey == "OAuthLoginError" ? ex : null);
                        }
                    }

                    if (retries >= (clientError ? 1 : MAX_AUTHORIZATION_RETRIES))
                    {
                        await AttemptParseAndThrowExceptionAsync(ex, response, cancellationToken).ConfigureAwait(false);
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

        return _Token;
    }
}