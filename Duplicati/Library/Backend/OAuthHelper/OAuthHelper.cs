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
using System.Net;
using Duplicati.Library.Utility;
using System.Collections.Generic;
using System.Web;
namespace Duplicati.Library
{
	/// <summary>
	/// Class for providing call-context access to http settings
	/// </summary>
	public static class OAuthContextSettings
	{
		/// <summary>
		/// The struct wrapping the OAuth settings
		/// </summary>
		private struct OAuthSettings
		{
			/// <summary>
			/// The server url
			/// </summary>
			public string ServerURL;
		}

		/// <summary>
		/// Starts the session.
		/// </summary>
		/// <returns>The session.</returns>
		/// <param name="serverurl">The url to use for the server.</param>
		public static IDisposable StartSession(string serverurl)
		{
            return CallContextSettings<OAuthSettings>.StartContext(new OAuthSettings { ServerURL = serverurl });
		}

		/// <summary>
		/// Gets the server URL to use for OAuth.
		/// </summary>
        public static string ServerURL
        {
            get
            {
                var r = CallContextSettings<OAuthSettings>.Settings.ServerURL;
                return string.IsNullOrWhiteSpace(r) ? OAuthHelper.DUPLICATI_OAUTH_SERVICE : r;
            }
        }
	}

    public class OAuthHelper : JSONWebHelper
    {
        private string m_token;
        private string m_authid;
        private DateTime m_tokenExpires = DateTime.UtcNow;

        public const string DUPLICATI_OAUTH_SERVICE = "https://duplicati-oauth-handler.appspot.com/refresh";

        public static string OAUTH_LOGIN_URL(string modulename) 
        {
            var u = new Library.Utility.Uri(OAuthContextSettings.ServerURL);
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

        public OAuthHelper(string authid, string servicename, string useragent = null)
            : base(useragent)
        {
            m_authid = authid;
            OAuthLoginUrl = OAUTH_LOGIN_URL(servicename);

            if (string.IsNullOrEmpty(authid))
                throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.MissingAuthID(OAuthLoginUrl), "MissingAuthID");
        }

        public T GetTokenResponse<T>()
        {
            var req = CreateRequest(OAuthContextSettings.ServerURL);
            req.Headers["X-AuthID"] = m_authid;
            req.Timeout = (int)TimeSpan.FromSeconds(25).TotalMilliseconds;

            return ReadJSONResponse<T>(req);
        }

        public override HttpWebRequest CreateRequest(string url, string method = null)
        {
            return this.CreateRequest(url, method, false);
        }

        public HttpWebRequest CreateRequest(string url, string method, bool noAuthorization)
        {
            var r = base.CreateRequest(url, method);
            if (!noAuthorization && AutoAuthHeader && !string.Equals(OAuthContextSettings.ServerURL, url))
                r.Headers["Authorization"] = string.Format("Bearer {0}", AccessToken);
            return r;
        } 

        public string AccessToken
        {
            get
            {
                if (AccessTokenOnly)
                    return m_authid;

                if (m_token == null || m_tokenExpires < DateTime.UtcNow)
                {
                    var retries = 0;

                    while(true)
                    {
                        try
                        {
                            var res = GetTokenResponse<OAuth_Service_Response>();

                            m_tokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                            if (AutoV2 && !string.IsNullOrWhiteSpace(res.v2_authid))
                                m_authid = res.v2_authid;
                            return m_token = res.access_token;
                        }
                        catch (Exception ex)
                        {
                            var msg = ex.Message;
                            var clienterror = false;
                            if (ex is WebException exception)
                            {
                                var resp = exception.Response as HttpWebResponse;
                                if (resp != null)
                                {
                                    msg = resp.Headers["X-Reason"];
                                    if (string.IsNullOrWhiteSpace(msg))
                                        msg = resp.StatusDescription;

                                    if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                                    {
                                        if (msg == resp.StatusDescription)
                                            throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.OverQuotaError, "OAuthOverQuotaError");
                                        else
                                            throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), "OAuthLoginError", exception);
                                    }

                                    //Fail faster on client errors
                                    clienterror = (int)resp.StatusCode >= 400 && (int)resp.StatusCode <= 499;
                                }
                            }

                            if (retries >= (clienterror ? 1 : 5))
                                throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), "OAuthLoginError", ex);

                            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                            retries++;
                        }
                    }
                }

                return m_token;
            }
        }

        public void ThrowOverQuotaError()
        {
            throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.OverQuotaError, "OAuthOverQuotaError");
        }

        public void ThrowAuthException(string msg, Exception ex)
        {
            if (ex == null)
                throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), "OAuthLoginError");
            else
                throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), "OAuthLoginError", ex);
        }



        private class OAuth_Service_Response
        {
            public string access_token { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public int expires { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public string v2_authid { get; set; }

        }

    }
}

