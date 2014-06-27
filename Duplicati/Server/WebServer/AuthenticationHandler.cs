//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
        private const string AUTH_COOKIE_NAME = "session_auth";
        private const string NONCE_COOKIE_NAME = "session_nonce";

        private Dictionary<string, DateTime> m_activeTokens = new Dictionary<string, DateTime>();
        private Dictionary<string, Tuple<DateTime, string>> m_activeNonces = new Dictionary<string, Tuple<DateTime, string>>();
        System.Security.Cryptography.RandomNumberGenerator m_prng = System.Security.Cryptography.RNGCryptoServiceProvider.Create();

        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var auth_token = request.Cookies[AUTH_COOKIE_NAME] == null || string.IsNullOrWhiteSpace(request.Cookies[AUTH_COOKIE_NAME].Value) ? null : request.Cookies[AUTH_COOKIE_NAME].Value;
            if (input["auth-token"] != null && !string.IsNullOrWhiteSpace(input["auth-token"].Value))
                auth_token = input["auth-token"].Value;

            if (request.Uri.AbsolutePath == "/logout.cgi")
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
            else if (request.Uri.AbsolutePath == "/login.cgi")
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
                    var expires = DateTime.UtcNow.AddMinutes(10);
                    m_prng.GetBytes(buf);
                    var nonce = Convert.ToBase64String(buf);

                    var sha256 = System.Security.Cryptography.SHA256.Create();
                    sha256.TransformBlock(buf, 0, buf.Length, buf, 0);
                    buf = Convert.FromBase64String(Program.DataConnection.ApplicationSettings.WebserverPassword);
                    sha256.TransformFinalBlock(buf, 0, buf.Length);
                    var pwd = Convert.ToBase64String(sha256.Hash);

                    m_activeNonces.Add(nonce, new Tuple<DateTime, string>(expires, pwd));

                    response.Cookies.Add(new HttpServer.ResponseCookie(NONCE_COOKIE_NAME, nonce, expires));
                    using(var bw = new BodyWriter(response))
                    {
                        bw.SetOK();
                        bw.WriteJsonObject(new {
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
                        var nonce = request.Cookies[NONCE_COOKIE_NAME];
                        if (nonce == null || string.IsNullOrWhiteSpace(nonce.Value) || !m_activeNonces.ContainsKey(nonce.Value))
                        {
                            response.Status = System.Net.HttpStatusCode.Unauthorized;
                            response.Reason = "Unauthorized";
                            response.ContentType = "application/json";
                            return true;
                        }

                        var pwd = m_activeNonces[nonce.Value].Item2;
                        m_activeNonces.Remove(nonce.Value);

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

                        Console.WriteLine("Added token: {0}", token);
                        m_activeTokens.Add(token, expires);
                        response.Cookies.Add(new  HttpServer.ResponseCookie(AUTH_COOKIE_NAME, token, expires));

                        response.Status = System.Net.HttpStatusCode.OK;
                        response.Reason = "OK";
                        return true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(Program.DataConnection.ApplicationSettings.WebserverPassword))
                return false;

            foreach(var k in (from n in m_activeTokens where DateTime.UtcNow > n.Value select n.Key).ToList())
            {
                Console.WriteLine("Removed token: {0}", k);
                m_activeTokens.Remove(k);
            }


            // If we have a valid token, proceeed
            if (!string.IsNullOrWhiteSpace(auth_token))
            {
                DateTime expires;
                if (m_activeTokens.TryGetValue(auth_token, out expires) && DateTime.UtcNow < expires)
                {
                    Console.WriteLine("Approved token: {0}", auth_token);
                    expires = DateTime.UtcNow.AddHours(1);
                    m_activeTokens[auth_token] = expires;
                    response.Cookies.Add(new ResponseCookie(AUTH_COOKIE_NAME, auth_token, expires));
                    return false;
                }
            }

            if (request.Uri.AbsolutePath == "/" || request.Uri.AbsolutePath == "/index.html")
            {
                response.Redirect("/login.html");
                return true;
            }
                
            if (request.Uri.AbsolutePath == "/control.cgi")
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

