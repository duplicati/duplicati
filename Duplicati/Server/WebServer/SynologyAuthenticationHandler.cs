//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HttpServer.HttpModules;

namespace Duplicati.Server.WebServer
{
    /// <summary>
    /// Helper class for enforcing the built-in authentication on Synology DSM
    /// </summary>
    public class SynologyAuthenticationHandler : HttpModule
    {
        /// <summary>
        /// The path to the login.cgi script
        /// </summary>
        private readonly string LOGIN_CGI = GetEnvArg("SYNO_LOGIN_CGI", "/usr/syno/synoman/webman/login.cgi");
        /// <summary>
        /// The path to the authenticate.cgi script
        /// </summary>
		private readonly string AUTH_CGI = GetEnvArg("SYNO_AUTHENTICATE_CGI", "/usr/syno/synoman/webman/modules/authenticate.cgi");
        /// <summary>
        /// A flag indicating if only admins are allowed
        /// </summary>
		private readonly bool ADMIN_ONLY = !(GetEnvArg("SYNO_ALL_USERS", "0") == "1");
        /// <summary>
        /// A flag indicating if the XSRF token should be fetched automatically
        /// </summary>
		private readonly bool AUTO_XSRF = GetEnvArg("SYNO_AUTO_XSRF", "1") == "1";

        /// <summary>
        /// A flag indicating that the auth-module is fully disabled
        /// </summary>
		private readonly bool FULLY_DISABLED;

        /// <summary>
        /// Re-evealuate the logins periodically to ensure it is still valid
        /// </summary>
        private readonly TimeSpan CACHE_TIMEOUT = TimeSpan.FromMinutes(3);

        /// <summary>
        /// A cache of previously authenticated logins
        /// </summary>
        private readonly Dictionary<string, DateTime> m_logincache = new Dictionary<string, DateTime>();

        /// <summary>
        /// The loca guarding the login cache
        /// </summary>
        private object m_lock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Server.WebServer.SynologyAuthenticationHandler"/> class.
        /// </summary>
		public SynologyAuthenticationHandler()
        {
            Console.WriteLine("Enabling Synology integrated authentication handler");
            var disable = false;
            if (!File.Exists(LOGIN_CGI))
            {
                Console.WriteLine("Disabling webserver as the login script is not found: {0}", LOGIN_CGI);
                disable = true;
            }
			if (!File.Exists(AUTH_CGI))
			{
				Console.WriteLine("Disabling webserver as the auth script is not found: {0}", AUTH_CGI);
				disable = true;
			}

            FULLY_DISABLED = disable;
		}

        /// <summary>
        /// Processes the request
        /// </summary>
        /// <returns><c>true</c> if the request is handled <c>false</c> otherwise.</returns>
        /// <param name="request">The request.</param>
        /// <param name="response">The response.</param>
        /// <param name="session">The session.</param>
        public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            if (FULLY_DISABLED)
            {
                response.Status = System.Net.HttpStatusCode.ServiceUnavailable;
                response.Reason = "The system is incorrectly configured";
                return true;
            }

            var limitedAccess =
                request.Uri.AbsolutePath.StartsWith(RESTHandler.API_URI_PATH, StringComparison.InvariantCultureIgnoreCase)
                   ||
                request.Uri.AbsolutePath.StartsWith(AuthenticationHandler.LOGIN_SCRIPT_URI, StringComparison.InvariantCultureIgnoreCase)
                   ||
                request.Uri.AbsolutePath.StartsWith(AuthenticationHandler.LOGOUT_SCRIPT_URI, StringComparison.InvariantCultureIgnoreCase);

            if (!limitedAccess)
                return false;

			var tmpenv = new Dictionary<string, string>();

            tmpenv["REMOTE_ADDR"] = request.RemoteEndPoint.Address.ToString();
            tmpenv["REMOTE_PORT"] = request.RemoteEndPoint.Port.ToString();

            if (!string.IsNullOrWhiteSpace(request.Headers["X-Real-IP"]))
                tmpenv["REMOTE_ADDR"] = request.Headers["X-Real-IP"];
			if (!string.IsNullOrWhiteSpace(request.Headers["X-Real-IP"]))
				tmpenv["REMOTE_PORT"] = request.Headers["X-Real-Port"];

			var loginid = request.Cookies["id"]?.Value;
            if (!string.IsNullOrWhiteSpace(loginid))
                tmpenv["HTTP_COOKIE"] = "id=" + loginid;

            var xsrftoken = request.Headers["X-Syno-Token"];
            if (string.IsNullOrWhiteSpace(xsrftoken))
                xsrftoken = request.QueryString["SynoToken"]?.Value;

            var cachestring = BuildCacheKey(tmpenv, xsrftoken);

            DateTime cacheExpires;
            if (m_logincache.TryGetValue(cachestring, out cacheExpires) && cacheExpires > DateTime.Now)
            {
                // We do not refresh the cache, as we need to ask the synology auth system periodically
                return false;
            }

            if (string.IsNullOrWhiteSpace(xsrftoken) && AUTO_XSRF)
            {
				var authre = new Regex(@"""SynoToken""\s?\:\s?""(?<token>[^""]+)""");
                try
                {
                    var resp = ShellExec(LOGIN_CGI, env: tmpenv).Result;

                    var m = authre.Match(resp);
                    if (m.Success)
                        xsrftoken = m.Groups["token"].Value;
                    else
                        throw new Exception("Unable to get XSRF token");
                }
                catch (Exception ex)
                {
                    response.Status = System.Net.HttpStatusCode.InternalServerError;
					response.Reason = "The system is incorrectly configured";
					return true;

				}
			}

            if (!string.IsNullOrWhiteSpace(xsrftoken))
			    tmpenv["HTTP_X_SYNO_TOKEN"] = xsrftoken;

            cachestring = BuildCacheKey(tmpenv, xsrftoken);

            var username = GetEnvArg("SYNO_USERNAME");
			if (string.IsNullOrWhiteSpace(username))
			{
                try
                {
                    username = ShellExec(AUTH_CGI, shell: false, exitcode: 0, env: tmpenv).Result;
                }
                catch (Exception ex)
                {
					response.Status = System.Net.HttpStatusCode.InternalServerError;
					response.Reason = "The system is incorrectly configured";
					return true;
				}
			}

            if (string.IsNullOrWhiteSpace(username))
            {
				response.Status = System.Net.HttpStatusCode.Forbidden;
				response.Reason = "Permission denied, not logged in";
				return true;
			}

			username = username.Trim();

            if (ADMIN_ONLY)
			{
				var groups = GetEnvArg("SYNO_GROUP_IDS");

				if (string.IsNullOrWhiteSpace(groups))
                    groups = ShellExec("id", "-G '" + username.Trim().Replace("'", "\\'") + "'", exitcode: 0).Result ?? string.Empty;
                if (!groups.Split(new char[] { ' ' }).Contains("101"))
                {
                    response.Status = System.Net.HttpStatusCode.Forbidden;
					response.Reason = "Administrator login required";
					return true;
				}
			}

            // We are now authenticated, add to cache
            m_logincache[cachestring] = DateTime.Now + CACHE_TIMEOUT;
			return false;
		}

        /// <summary>
        /// Builds a cache key from the environment data
        /// </summary>
        /// <returns>The cache key.</returns>
        /// <param name="values">The environment.</param>
        /// <param name="xsrftoken">The XSRF token.</param>
        private static string BuildCacheKey(Dictionary<string, string> values, string xsrftoken)
        {
            if (!values.ContainsKey("REMOTE_ADDR") || !values.ContainsKey("REMOTE_PORT") || !values.ContainsKey("HTTP_COOKIE"))
                return null;
            
            return string.Format("{0}:{1}/{2}?{3}", values["REMOTE_ADDR"], values["REMOTE_PORT"], values["HTTP_COOKIE"], xsrftoken);
        }

		/// <summary>
		/// Runs an external command
		/// </summary>
		/// <returns>The stdout data.</returns>
		/// <param name="command">The executable</param>
		/// <param name="args">The executable and the arguments.</param>
		/// <param name="shell">If set to <c>true</c> use the shell context for execution.</param>
		/// <param name="exitcode">Set the value to check for a particular exitcode.</param>
		private static async Task<string> ShellExec(string command, string args = null, bool shell = false, int exitcode = -1, Dictionary<string, string> env = null)
		{
			var psi = new ProcessStartInfo()
			{
				FileName = command,
				Arguments = shell ? null : args,
				UseShellExecute = false,
				RedirectStandardInput = shell,
				RedirectStandardOutput = true
			};

			if (env != null)
				foreach (var pk in env)
					psi.EnvironmentVariables[pk.Key] = pk.Value;

            using (var p = System.Diagnostics.Process.Start(psi))
			{
				if (shell && args != null)
					await p.StandardInput.WriteLineAsync(args);

				var res = p.StandardOutput.ReadToEndAsync();

                var tries = 10;
                var ms = (int)TimeSpan.FromSeconds(0.5).TotalMilliseconds;
                while (tries > 0 && !p.HasExited)
                {
                    tries--;
                    p.WaitForExit(ms);
                }

				if (!p.HasExited)
                    try { p.Kill(); }
                    catch { }

                if (!p.HasExited || (p.ExitCode != exitcode && exitcode != -1))
					throw new Exception(string.Format("Exit code was: {0}, stdout: {1}", p.ExitCode, res));
				return await res;
			}
		}

		/// <summary>
		/// Gets the environment variable argument.
		/// </summary>
		/// <returns>The environment variable.</returns>
		/// <param name="key">The name of the environment variable.</param>
		/// <param name="default">The default value.</param>
		private static string GetEnvArg(string key, string @default = null)
		{
			var res = Environment.GetEnvironmentVariable(key);
			return string.IsNullOrWhiteSpace(res) ? @default : res.Trim();
		}
	}
}
