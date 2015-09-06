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
using System.Linq;
using HttpServer;
using HttpServer.HttpModules;
using System.Collections.Generic;

namespace Duplicati.Server.WebServer
{
    internal class AuthenticationHandler : HttpModule
    {
        private const string AUTH_COOKIE_NAME = "session-auth";
        private const string NONCE_COOKIE_NAME = "session-nonce";

        private const string XSRF_COOKIE_NAME = "xsrf-token";
        private const string XSRF_HEADER_NAME = "X-XSRF-Token";

        public const string LOGIN_SCRIPT_URI = "/login.cgi";
        public const string LOGOUT_SCRIPT_URI = "/logout.cgi";

        private const int XSRF_TIMEOUT_MINUTES = 10;
        private const int AUTH_TIMEOUT_MINUTES = 10;

        private Dictionary<string, DateTime> m_activeTokens = new Dictionary<string, DateTime>();
        private Dictionary<string, Tuple<DateTime, string>> m_activeNonces = new Dictionary<string, Tuple<DateTime, string>>();

        private Dictionary<string, DateTime> m_activexsrf = new Dictionary<string, DateTime>();

        System.Security.Cryptography.RandomNumberGenerator m_prng = System.Security.Cryptography.RNGCryptoServiceProvider.Create();

        private string FindXSRFToken(HttpServer.IHttpRequest request)
        {
            string xsrftoken = request.Headers[XSRF_HEADER_NAME] ?? "";

            if (string.IsNullOrWhiteSpace(xsrftoken))
                xsrftoken = Duplicati.Library.Utility.Uri.UrlDecode(xsrftoken);

            if (string.IsNullOrWhiteSpace(xsrftoken))
            {
                var xsrfq = request.Form[XSRF_HEADER_NAME] ?? request.Form[Duplicati.Library.Utility.Uri.UrlEncode(XSRF_HEADER_NAME)];
                xsrftoken = (xsrfq == null || string.IsNullOrWhiteSpace(xsrfq.Value)) ? "" : xsrfq.Value;
            }

            if (string.IsNullOrWhiteSpace(xsrftoken))
            {
                var xsrfq = request.QueryString[XSRF_HEADER_NAME] ?? request.QueryString[Duplicati.Library.Utility.Uri.UrlEncode(XSRF_HEADER_NAME)];
                xsrftoken = (xsrfq == null || string.IsNullOrWhiteSpace(xsrfq.Value)) ? "" : xsrfq.Value;
            }

            return xsrftoken;
        }

        private bool AddXSRFTokenToRespone(HttpServer.IHttpResponse response)
        {
            if (m_activexsrf.Count > 500)
                return false;

            var buf = new byte[32];
            var expires = DateTime.UtcNow.AddMinutes(XSRF_TIMEOUT_MINUTES);
            m_prng.GetBytes(buf);
            var token = Convert.ToBase64String(buf);

            m_activexsrf.Add(token, expires);
            response.Cookies.Add(new HttpServer.ResponseCookie(XSRF_COOKIE_NAME, token, expires));
            return true;
        }

        private string FindAuthCookie(HttpServer.IHttpRequest request)
        {
            var authcookie = request.Cookies[AUTH_COOKIE_NAME] ?? request.Cookies[Library.Utility.Uri.UrlEncode(AUTH_COOKIE_NAME)];
            var authform = request.Form["auth-token"] ?? request.Form[Library.Utility.Uri.UrlEncode("auth-token")];
            var authquery = request.QueryString["auth-token"] ?? request.QueryString[Library.Utility.Uri.UrlEncode("auth-token")];

            var auth_token = authcookie == null || string.IsNullOrWhiteSpace(authcookie.Value) ? null : authcookie.Value;
            if (authquery != null && !string.IsNullOrWhiteSpace(authquery.Value))
                auth_token = authquery["auth-token"].Value;
            if (authform != null && !string.IsNullOrWhiteSpace(authform.Value))
                auth_token = authform["auth-token"].Value;

            return auth_token;
        }

        private bool HasXSRFCookie(HttpServer.IHttpRequest request)
        {
            foreach(var k in (from n in m_activexsrf where DateTime.UtcNow > n.Value select n.Key).ToList())
                m_activexsrf.Remove(k);

            var xsrfcookie = request.Cookies[XSRF_COOKIE_NAME] ?? request.Cookies[Library.Utility.Uri.UrlEncode(XSRF_COOKIE_NAME)];
            var value = xsrfcookie == null ? null : xsrfcookie.Value;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (m_activexsrf.ContainsKey(value))
            {
                m_activexsrf[value] = DateTime.UtcNow.AddMinutes(XSRF_TIMEOUT_MINUTES);
                return true;
            }
            else if (m_activexsrf.ContainsKey(Library.Utility.Uri.UrlDecode(value)))
            {
                m_activexsrf[Library.Utility.Uri.UrlDecode(value)] = DateTime.UtcNow.AddMinutes(XSRF_TIMEOUT_MINUTES);
                return true;
            }

            return false;
        }

        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            var auth_token = FindAuthCookie(request);
            var xsrf_token = FindXSRFToken(request);

            if (!HasXSRFCookie(request))
            {
                var cookieAdded = AddXSRFTokenToRespone(response);

                if (!cookieAdded)
                {
                    response.Status = System.Net.HttpStatusCode.ServiceUnavailable;
                    response.Reason = "Too Many Concurrent Request, try again later";
                    return true;
                }
            }

            if (LOGOUT_SCRIPT_URI.Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(auth_token))
                {
                    if (m_activeTokens.ContainsKey(auth_token))
                        m_activeTokens.Remove(auth_token);
                }

                response.Status = System.Net.HttpStatusCode.NoContent;
                response.Reason = "OK";

                return true;
            }
            else if (LOGIN_SCRIPT_URI.Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase))
            {
                foreach(var k in (from n in m_activeNonces where DateTime.UtcNow > n.Value.Item1 select n.Key).ToList())
                    m_activeNonces.Remove(k);

                if (input["get-nonce"] != null && !string.IsNullOrWhiteSpace(input["get-nonce"].Value))
                {
                    if (m_activeNonces.Count > 50)
                    {
                        response.Status = System.Net.HttpStatusCode.ServiceUnavailable;
                        response.Reason = "Too many active login attempts";
                        return true;
                    }

                    var buf = new byte[32];
                    var expires = DateTime.UtcNow.AddMinutes(AUTH_TIMEOUT_MINUTES);
                    m_prng.GetBytes(buf);
                    var nonce = Convert.ToBase64String(buf);

                    var sha256 = System.Security.Cryptography.SHA256.Create();
                    sha256.TransformBlock(buf, 0, buf.Length, buf, 0);
                    buf = Convert.FromBase64String(Program.DataConnection.ApplicationSettings.WebserverPassword);
                    sha256.TransformFinalBlock(buf, 0, buf.Length);
                    var pwd = Convert.ToBase64String(sha256.Hash);

                    m_activeNonces.Add(nonce, new Tuple<DateTime, string>(expires, pwd));

                    response.Cookies.Add(new HttpServer.ResponseCookie(NONCE_COOKIE_NAME, nonce, expires));
                    using(var bw = new BodyWriter(response, request))
                    {
                        bw.OutputOK(new {
                            Status = "OK",
                            Nonce = nonce,
                            Salt = Program.DataConnection.ApplicationSettings.WebserverPasswordSalt
                        });
                    }
                    return true;
                }
                else
                {
                    if (input["password"] != null && !string.IsNullOrWhiteSpace(input["password"].Value))
                    {
                        var nonce_el = request.Cookies[NONCE_COOKIE_NAME] ?? request.Cookies[Library.Utility.Uri.UrlEncode(NONCE_COOKIE_NAME)];
                        var nonce = nonce_el == null || string.IsNullOrWhiteSpace(nonce_el.Value) ? "" : nonce_el.Value;
                        var urldecoded = nonce == null ? "" : Duplicati.Library.Utility.Uri.UrlDecode(nonce);
                        if (m_activeNonces.ContainsKey(urldecoded))
                            nonce = urldecoded;

                        if (!m_activeNonces.ContainsKey(nonce))
                        {
                            response.Status = System.Net.HttpStatusCode.Unauthorized;
                            response.Reason = "Unauthorized";
                            response.ContentType = "application/json";
                            return true;
                        }

                        var pwd = m_activeNonces[nonce].Item2;
                        m_activeNonces.Remove(nonce);

                        if (pwd != input["password"].Value)
                        {
                            response.Status = System.Net.HttpStatusCode.Unauthorized;
                            response.Reason = "Unauthorized";
                            response.ContentType = "application/json";
                            return true;
                        }

                        var buf = new byte[32];
                        var expires = DateTime.UtcNow.AddHours(1);
                        m_prng.GetBytes(buf);
                        var token = Duplicati.Library.Utility.Utility.Base64UrlEncode(buf);
                        while (token.Length > 0 && token.EndsWith("="))
                            token = token.Substring(0, token.Length - 1);

                        m_activeTokens.Add(token, expires);
                        response.Cookies.Add(new  HttpServer.ResponseCookie(AUTH_COOKIE_NAME, token, expires));

                        using(var bw = new BodyWriter(response, request))
                            bw.OutputOK();

                        return true;
                    }
                }
            }

            var limitedAccess =
                ControlHandler.CONTROL_HANDLER_URI.Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase)

                // While the REST API is under development, it is nice to be able to access it directly
#if !DEBUG
                ||
                request.Uri.AbsolutePath.StartsWith(RESTHandler.API_URI_PATH, StringComparison.InvariantCultureIgnoreCase)
#endif
            ;

            if (limitedAccess)
            {
                if (xsrf_token != null && m_activexsrf.ContainsKey(xsrf_token))
                {
                    var expires = DateTime.UtcNow.AddMinutes(XSRF_TIMEOUT_MINUTES);
                    m_activexsrf[xsrf_token] = expires;
                    response.Cookies.Add(new ResponseCookie(XSRF_COOKIE_NAME, xsrf_token, expires));
                }
                else
                {
                    response.Status = System.Net.HttpStatusCode.BadRequest;
                    response.Reason = "Missing XSRF Token";

                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(Program.DataConnection.ApplicationSettings.WebserverPassword))
                return false;

            foreach(var k in (from n in m_activeTokens where DateTime.UtcNow > n.Value select n.Key).ToList())
                m_activeTokens.Remove(k);

                
            // If we have a valid token, proceeed
            if (!string.IsNullOrWhiteSpace(auth_token))
            {
                DateTime expires;
                var found = m_activeTokens.TryGetValue(auth_token, out expires);
                if (!found)
                {
                    auth_token = Duplicati.Library.Utility.Uri.UrlDecode(auth_token);
                    found = m_activeTokens.TryGetValue(auth_token, out expires);
                }

                if (found && DateTime.UtcNow < expires)
                {
                    expires = DateTime.UtcNow.AddHours(1);
                    m_activeTokens[auth_token] = expires;
                    response.Cookies.Add(new ResponseCookie(AUTH_COOKIE_NAME, auth_token, expires));
                    return false;
                }
            }

            if ("/".Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase) || "/index.html".Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase))
            {
                response.Redirect("/login.html");
                return true;
            }
                
            if (ControlHandler.CONTROL_HANDLER_URI.Equals(request.Uri.AbsolutePath, StringComparison.InvariantCultureIgnoreCase))
            {
                response.Status = System.Net.HttpStatusCode.Unauthorized;
                response.Reason = "Not logged in";
                response.AddHeader("Location", "login.html");

                return true;
            }

            return false;
        }
    }
}

