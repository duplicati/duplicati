//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
        private const string OAUTH_LOGIN_URL_TEMPLATE = "https://duplicati-oauth-handler.appspot.com/?type={0}";

        public static string OAUTH_LOGIN_URL(string modulename) 
        {
            var u = new Library.Utility.Uri(OAuthContextSettings.ServerURL);
            var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
            return string.Format(addr.ToString(), modulename); 
        }

        /// <summary>
        /// Set to true to automatically add the Authorization header to requets
        /// </summary>
        public bool AutoAuthHeader { get; set; }
        /// <summary>
        /// Set to true if the provider does not use refresh tokens, but only access tokens
        /// </summary>
        public bool AccessTokenOnly { get; set; }

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
            var r = base.CreateRequest(url, method);
            if (AutoAuthHeader && !string.Equals(OAuthContextSettings.ServerURL, url))
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
                            if (!string.IsNullOrWhiteSpace(res.v2_authid))
                                m_authid = res.v2_authid;
                            return m_token = res.access_token;
                        }
                        catch (Exception ex)
                        {
                            var msg = ex.Message;
                            var clienterror = false;
                            if (ex is WebException)
                            {
                                var resp = ((WebException)ex).Response as HttpWebResponse;
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
                                            throw new Duplicati.Library.Interface.UserInformationException(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), "OAuthLoginError", ex);
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

