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
using Duplicati.Library.Utility;

namespace Duplicati.Library;

public class OAuthHelperHttpClient : JsonWebHelperHttpClient
{
    private string _Token;
    private string _Authid;
    private DateTime _mTokenExpires = DateTime.UtcNow;
    private string OAuthLoginUrl { get; }
        
    /// <summary>
    /// Timeout for authentication requests
    /// </summary>
    private const int AUTHENTICATION_TIMEOUT_SECONDS = 25;

    private const int MAX_AUTHORIZATION_RETRIES = 5;

    private static string OAUTH_LOGIN_URL(string modulename)
    {
        var u = new Utility.Uri(OAuthContextSettings.ServerURL);
        var addr = u.SetPath("")
            .SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
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

    private HttpRequestMessage CreateRequest(string url, string method, bool noAuthorization)
    {
        var request = base.CreateRequest(url, method);
        if (!noAuthorization && AutoAuthHeader && !string.Equals(OAuthContextSettings.ServerURL, url)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return request;
    }

    public override HttpRequestMessage CreateRequest(string url, string method = null)
    {
        return CreateRequest(url, method, false);
    }

    private string AccessToken
    {
        get
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
                        using var request = base.CreateRequest(OAuthContextSettings.ServerURL);

                        request.Headers.TryAddWithoutValidation("X-AuthID", _Authid);

                        using var timeoutToken = new CancellationTokenSource();
                        timeoutToken.CancelAfter(TimeSpan.FromSeconds(AUTHENTICATION_TIMEOUT_SECONDS));

                        response = _httpClient.Send(request, timeoutToken.Token);
                        response.EnsureSuccessStatusCode();

                        var res = ReadJsonResponse<OAuthServiceResponse>(response);

                        _mTokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                        if (AutoV2 && !string.IsNullOrWhiteSpace(res.v2_authid))
                            _Authid = res.v2_authid;
                        return _Token = res.access_token;
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

                            //TODO: Test; this was ported from old code but looks fragile.
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
                            AttemptParseAndThrowException(ex, response);
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
}