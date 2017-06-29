﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public class HttpServerConnection : IDisposable
    {
        private const string LOGIN_SCRIPT = "login.cgi";
        private const string STATUS_WINDOW = "index.html";

        private const string XSRF_COOKIE = "xsrf-token";
        private const string XSRF_HEADER = "X-XSRF-Token";
        private const string AUTH_COOKIE = "session-auth";

        private const string TRAYICONPASSWORDSOURCE_HEADER = "X-TrayIcon-PasswordSource";

        private class BackgroundRequest
        {
            public string Method;
            public string Endpoint;
            public Dictionary<string, string> Query;

            public BackgroundRequest() 
            {
            }

            public BackgroundRequest(string method, string endpoint, Dictionary<string, string> query)
            {
                this.Method = method;
                this.Endpoint = endpoint;
                this.Query = query;
            }
        }

        private string m_apiUri;
        private string m_baseUri;
        private string m_password;
        private bool m_saltedpassword;
        private string m_authtoken;
        private string m_xsrftoken;
        private static readonly System.Text.Encoding ENCODING = System.Text.Encoding.GetEncoding("utf-8");

        public delegate void StatusUpdateDelegate(IServerStatus status);
        public event StatusUpdateDelegate OnStatusUpdated;

        public long m_lastNotificationId = -1;
        public DateTime m_firstNotificationTime;
        public delegate void NewNotificationDelegate(INotification notification);
        public event NewNotificationDelegate OnNotification;

        private volatile IServerStatus m_status;

        private volatile bool m_shutdown = false;
        private volatile System.Threading.Thread m_requestThread;
        private volatile System.Threading.Thread m_pollThread;
        private System.Threading.AutoResetEvent m_waitLock;

        private readonly Dictionary<string, string> m_updateRequest;
        private readonly Dictionary<string, string> m_options;
        private readonly bool m_dbPasswordSourceDatabase;
        private string m_TrayIconHeaderValue => m_dbPasswordSourceDatabase ? "database" : "user";

        public IServerStatus Status { get { return m_status; } }

        private object m_lock = new object();
        private Queue<BackgroundRequest> m_workQueue = new Queue<BackgroundRequest>();

        public HttpServerConnection(Uri server, string password, bool saltedpassword, bool dbPasswordSourceDatabase, Dictionary<string, string> options)
        {
            m_baseUri = server.ToString();
            if (!m_baseUri.EndsWith("/"))
                m_baseUri += "/";

            m_apiUri = m_baseUri + "api/v1";

            m_firstNotificationTime = DateTime.Now;

            m_password = password;
            m_saltedpassword = saltedpassword;
            m_options = options;
            m_dbPasswordSourceDatabase = dbPasswordSourceDatabase;

            m_updateRequest = new Dictionary<string, string>();
            m_updateRequest["longpoll"] = "false";
            m_updateRequest["lasteventid"] = "0";

            UpdateStatus();

            //We do the first request without long poll,
            // and all the rest with longpoll
            m_updateRequest["longpoll"] = "true";
            m_updateRequest["duration"] = "5m";
            
            m_waitLock = new System.Threading.AutoResetEvent(false);
            m_requestThread = new System.Threading.Thread(ThreadRunner);
            m_pollThread = new System.Threading.Thread(LongPollRunner);

            m_requestThread.Name = "TrayIcon Request Thread";
            m_pollThread.Name = "TrayIcon Longpoll Thread";

            m_requestThread.Start();
            m_pollThread.Start();
        }

        private void UpdateStatus()
        {
            m_status = PerformRequest<IServerStatus>("GET", "/serverstate", m_updateRequest);
            m_updateRequest["lasteventid"] = m_status.LastEventID.ToString();

            if (OnStatusUpdated != null)
                OnStatusUpdated(m_status);

            if (m_lastNotificationId != m_status.LastNotificationUpdateID)
            {
                m_lastNotificationId = m_status.LastNotificationUpdateID;
                UpdateNotifications();
            }
        }

        private void UpdateNotifications()
        {
            var req = new Dictionary<string, string>();
            var notifications = PerformRequest<INotification[]>("GET", "/notifications", req);
            if (notifications != null)
            {
                foreach(var n in notifications.Where(x => x.Timestamp > m_firstNotificationTime))
                    if (OnNotification != null)
                        OnNotification(n);

                if (notifications.Any())
                    m_firstNotificationTime = notifications.Select(x => x.Timestamp).Max();
            }
        }

        private void LongPollRunner()
        {
            while (!m_shutdown)
            {
                try
                {
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Console.WriteLine("Request error: " + ex.Message);
                }
            }
        }

        private void ThreadRunner()
        {
            while (!m_shutdown)
            {
                try
                {
                    BackgroundRequest req;
                    bool any = false;
                    do
                    {
                        req = null;

                        lock (m_lock)
                            if (m_workQueue.Count > 0)
                                req = m_workQueue.Dequeue();

                        if (m_shutdown)
                            break;

                        if (req != null)
                        {
                            any = true;
                            PerformRequest<string>(req.Method, req.Endpoint, req.Query);
                        }
                    
                    } while (req != null);
                    
                    if (!(any || m_shutdown))
                        m_waitLock.WaitOne(TimeSpan.FromMinutes(1), true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Console.WriteLine("Request error: " + ex.Message);
                }
            }
        }

        public void Close()
        {
            m_shutdown = true;
            m_waitLock.Set();
            m_pollThread.Abort();
            m_pollThread.Join(TimeSpan.FromSeconds(10));
            if (!m_requestThread.Join(TimeSpan.FromSeconds(10)))
            {
                m_requestThread.Abort();
                m_requestThread.Join(TimeSpan.FromSeconds(10));
            }
        }

        private static string EncodeQueryString(Dictionary<string, string> dict)
        {
            return string.Join("&", Array.ConvertAll(dict.Keys.ToArray(), key => string.Format("{0}={1}", Uri.EscapeUriString(key), Uri.EscapeUriString(dict[key]))));
        }

        private class SaltAndNonce
        {
            public string Salt = null;
            public string Nonce = null;
        }

        private SaltAndNonce GetSaltAndNonce()
        {
            var httpOptions = new Duplicati.Library.Modules.Builtin.HttpOptions();
            httpOptions.Configure(m_options);

            using (httpOptions)
            {
                var req = (System.Net.HttpWebRequest) System.Net.WebRequest.Create(m_baseUri + LOGIN_SCRIPT);
                req.Method = "POST";
                req.UserAgent = "Duplicati TrayIcon Monitor, v" +
                                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                req.Headers.Add(TRAYICONPASSWORDSOURCE_HEADER, m_TrayIconHeaderValue);
                req.ContentType = "application/x-www-form-urlencoded";

                Duplicati.Library.Utility.AsyncHttpRequest areq = new Library.Utility.AsyncHttpRequest(req);
                var body = System.Text.Encoding.ASCII.GetBytes("get-nonce=1");
                using (var f = areq.GetRequestStream(body.Length))
                    f.Write(body, 0, body.Length);

                using (var r = (System.Net.HttpWebResponse) areq.GetResponse())
                using (var s = areq.GetResponseStream())
                using (var sr = new System.IO.StreamReader(s, ENCODING, true))
                    return Serializer.Deserialize<SaltAndNonce>(sr);
            }
        }

        private string PerformLogin(string password, string nonce)
        {
            var httpOptions = new Duplicati.Library.Modules.Builtin.HttpOptions();
            httpOptions.Configure(m_options);

            using (httpOptions)
            {
                System.Net.HttpWebRequest req =
                    (System.Net.HttpWebRequest) System.Net.WebRequest.Create(m_baseUri + LOGIN_SCRIPT);
                req.Method = "POST";
                req.UserAgent = "Duplicati TrayIcon Monitor, v" +
                                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                req.Headers.Add(TRAYICONPASSWORDSOURCE_HEADER, m_TrayIconHeaderValue);
                req.ContentType = "application/x-www-form-urlencoded";
                if (req.CookieContainer == null)
                    req.CookieContainer = new System.Net.CookieContainer();
                req.CookieContainer.Add(new System.Net.Cookie("session-nonce", nonce, "/", req.RequestUri.Host));

                //Wrap it all in async stuff
                Duplicati.Library.Utility.AsyncHttpRequest areq = new Library.Utility.AsyncHttpRequest(req);
                var body = System.Text.Encoding.ASCII.GetBytes("password=" +
                                                               Duplicati.Library.Utility.Uri.UrlEncode(password));
                using (var f = areq.GetRequestStream(body.Length))
                    f.Write(body, 0, body.Length);

                using (var r = (System.Net.HttpWebResponse) areq.GetResponse())
                    if (r.StatusCode == System.Net.HttpStatusCode.OK)
                        return (r.Cookies[AUTH_COOKIE] ?? r.Cookies[Library.Utility.Uri.UrlEncode(AUTH_COOKIE)]).Value;

                return null;
            }
        }

        private string GetAuthToken()
        {
            var salt_nonce = GetSaltAndNonce();
            var sha256 = System.Security.Cryptography.SHA256.Create();
            var password = m_password;

            if (string.IsNullOrWhiteSpace(m_password))
                return "";

            if (!m_saltedpassword)
            {
                var str = System.Text.Encoding.UTF8.GetBytes(m_password);
                var buf = Convert.FromBase64String(salt_nonce.Salt);
                sha256.TransformBlock(str, 0, str.Length, str, 0);
                sha256.TransformFinalBlock(buf, 0, buf.Length);
                password = Convert.ToBase64String(sha256.Hash);
                sha256.Initialize();
            }

            var nonce = Convert.FromBase64String(salt_nonce.Nonce);
            sha256.TransformBlock(nonce, 0, nonce.Length, nonce, 0);
            var pwdbuf = Convert.FromBase64String(password);
            sha256.TransformFinalBlock(pwdbuf, 0, pwdbuf.Length);
            var pwd = Convert.ToBase64String(sha256.Hash);

            return PerformLogin(pwd, salt_nonce.Nonce);
        }

        private string GetXSRFToken()
        {
            var httpOptions = new Duplicati.Library.Modules.Builtin.HttpOptions();
            httpOptions.Configure(m_options);

            using (httpOptions)
            {
                System.Net.HttpWebRequest req =
                    (System.Net.HttpWebRequest) System.Net.WebRequest.Create(m_baseUri + STATUS_WINDOW);
                req.Method = "GET";
                req.UserAgent = "Duplicati TrayIcon Monitor, v" +
                                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                req.Headers.Add(TRAYICONPASSWORDSOURCE_HEADER, m_TrayIconHeaderValue);
                if (req.CookieContainer == null)
                    req.CookieContainer = new System.Net.CookieContainer();

                //Wrap it all in async stuff
                Duplicati.Library.Utility.AsyncHttpRequest areq = new Library.Utility.AsyncHttpRequest(req);
                using (var r = (System.Net.HttpWebResponse) areq.GetResponse())
                    if (r.StatusCode == System.Net.HttpStatusCode.OK)
                        return (r.Cookies[XSRF_COOKIE] ?? r.Cookies[Library.Utility.Uri.UrlEncode(XSRF_COOKIE)]).Value;

                return null;
            }
        }

        private T PerformRequest<T>(string method, string urlfragment, Dictionary<string, string> queryparams)
        {
            var hasTriedXSRF = false;
            var hasTriedPassword = false;

            while (true)
            {
                try
                {
                    return PerformRequestInternal<T>(method, urlfragment, queryparams);
                }
                catch (System.Net.WebException wex)
                {
                    var httpex = wex.Response as HttpWebResponse;
                    if (httpex == null)
                        throw;

                    if (
                        !hasTriedXSRF &&
                        wex.Status == System.Net.WebExceptionStatus.ProtocolError &&
                        httpex.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                        httpex.StatusDescription.IndexOf("XSRF", StringComparison.InvariantCultureIgnoreCase) >= 0)
                    {
                        hasTriedXSRF = true;
                        var t = httpex.Cookies[XSRF_COOKIE]?.Value;

                        if (string.IsNullOrWhiteSpace(t))
                            t = GetXSRFToken();

                        m_xsrftoken = Duplicati.Library.Utility.Uri.UrlDecode(t);
                    }
                    else if (
                        !hasTriedPassword &&
                        wex.Status == System.Net.WebExceptionStatus.ProtocolError &&
                        httpex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        if (m_dbPasswordSourceDatabase)
                        {
                            Program.databaseConnection.ApplicationSettings.ReloadSettings();

                            //Can survive if server password is changed via web ui
                            if (Program.databaseConnection.ApplicationSettings.WebserverPasswordTrayIcon != m_password)
                                m_password = Program.databaseConnection.ApplicationSettings.WebserverPasswordTrayIcon;
                            else
                                hasTriedPassword = true;
                        }

                        m_authtoken = GetAuthToken();
                    }
                    else
                        throw;
                }
            }
        }

        private T PerformRequestInternal<T>(string method, string endpoint, Dictionary<string, string> queryparams)
        {
            queryparams["format"] = "json";

            string query = EncodeQueryString(queryparams);

			// TODO: This can interfere with running backups, 
            // as the System.Net.ServicePointManager is shared with
            // all connections doing ftp/http requests
			using (var httpOptions = new Duplicati.Library.Modules.Builtin.HttpOptions())
            {
				httpOptions.Configure(m_options);

				var req =
                    (System.Net.HttpWebRequest) System.Net.WebRequest.Create(
                        new Uri(m_apiUri + endpoint + '?' + query));
                req.Method = method;
                req.Headers.Add("Accept-Charset", ENCODING.BodyName);
                if (m_xsrftoken != null)
                    req.Headers.Add(XSRF_HEADER, m_xsrftoken);
                req.UserAgent = "Duplicati TrayIcon Monitor, v" +
                                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                req.Headers.Add(TRAYICONPASSWORDSOURCE_HEADER, m_TrayIconHeaderValue);
                if (req.CookieContainer == null)
                    req.CookieContainer = new System.Net.CookieContainer();

                if (m_authtoken != null)
                    req.CookieContainer.Add(new System.Net.Cookie(AUTH_COOKIE, m_authtoken, "/", req.RequestUri.Host));
                if (m_xsrftoken != null)
                    req.CookieContainer.Add(new System.Net.Cookie(XSRF_COOKIE, m_xsrftoken, "/", req.RequestUri.Host));

                //Wrap it all in async stuff
                var areq = new Library.Utility.AsyncHttpRequest(req);
                req.AllowWriteStreamBuffering = true;

                //Assign the timeout, and add a little processing time as well
                if (endpoint.Equals("/serverstate", StringComparison.InvariantCultureIgnoreCase) &&
                    queryparams.ContainsKey("duration"))
                    areq.Timeout = (int) (Duplicati.Library.Utility.Timeparser.ParseTimeSpan(queryparams["duration"]) +
                                          TimeSpan.FromSeconds(5)).TotalMilliseconds;

                using (var r = (System.Net.HttpWebResponse) areq.GetResponse())
                using (var s = areq.GetResponseStream())
                    if (typeof(T) == typeof(string))
                    {
                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                        {
                            s.CopyTo(ms);
                            return (T) (object) ENCODING.GetString(ms.ToArray());
                        }
                    }
                    else
                    {
                        using (var sr = new System.IO.StreamReader(s, ENCODING, true))
                            return Serializer.Deserialize<T>(sr);
                    }
            }
        }

        private void ExecuteAndNotify(string method, string urifragment, Dictionary<string, string> req)
        {
            lock (m_lock)
            {
                m_workQueue.Enqueue(new BackgroundRequest(method, urifragment, req));
                m_waitLock.Set();
            }
        }

        public void Pause(string duration = null)
        {
            var req = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(duration))
                req.Add("duration", duration);

            ExecuteAndNotify("POST", "/serverstate/pause", req);
        }

        public void Resume()
        {
            var req = new Dictionary<string, string>();
            ExecuteAndNotify("POST", "/serverstate/resume", req);
        }

        public void StopTask(long id)
        {
            var req = new Dictionary<string, string>();
            ExecuteAndNotify("POST", string.Format("/task/{0}/stop", Library.Utility.Uri.UrlPathEncode(id.ToString())), req);
        }

        public void AbortTask(long id)
        {
            var req = new Dictionary<string, string>();
            ExecuteAndNotify("POST", string.Format("/task/{0}/abort", Library.Utility.Uri.UrlPathEncode(id.ToString())), req);
        }

        public void RunBackup(long id, bool forcefull = false)
        {
            var req = new Dictionary<string, string>();
            if (forcefull)
                req.Add("full", "true");
            ExecuteAndNotify("POST", string.Format("/backup/{0}/start", Library.Utility.Uri.UrlPathEncode(id.ToString())), req);
        }
  
        public void DismissNotification(long id)
        {
            var req = new Dictionary<string, string>();
            ExecuteAndNotify("DELETE", string.Format("/notification/{0}", Library.Utility.Uri.UrlPathEncode(id.ToString())), req);
        }

        public void Dispose()
        {
            Close();
        }
        
        public string StatusWindowURL
        {
            get 
            { 
                if (m_authtoken != null)
                    return m_baseUri + STATUS_WINDOW + "?auth-token=" + GetAuthToken();
                
                return m_baseUri + STATUS_WINDOW; 
            }
        }
    }
}
