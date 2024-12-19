﻿// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.Extensions.DependencyInjection;

namespace Duplicati.GUI.TrayIcon
{
    public class HttpServerConnection : IDisposable
    {
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<HttpServerConnection>();
        private const string LONGPOLL_TIMEOUT = "5m";
        private readonly HttpClient HTTPCLIENT;
        private record ServerStatusImpl(
            LiveControlState ProgramState,
            SuggestedStatusIcon SuggestedStatusIcon,
            long LastEventID,
            long LastDataUpdateID,
            long LastNotificationUpdateID
        ) : IServerStatus;

        private record NotificationImpl(
            long ID,
            Server.Serialization.NotificationType Type,
            string Title,
            string Message,
            string Exception,
            string BackupID,
            string Action,
            DateTime Timestamp,
            string LogEntryID,
            string MessageID,
            string MessageLogTag
        ) : INotification;

        private readonly JsonSerializerOptions serializerOptions = new()
        {
            PropertyNamingPolicy = null,
            Converters = {
                new JsonStringEnumConverter(),
                new DayOfWeekStringEnumConverter()
            }
        };

        private const string STATUS_WINDOW = "index.html";
        private const string SIGNIN_WINDOW = "signin.html";

        private record BackgroundRequest(string Method, string Endpoint, string Body, TimeSpan? Timeout = null);

        private readonly string m_apiUri;
        private readonly string m_baseUri;
        private string m_password;
        private string m_accesstoken;

        public delegate void StatusUpdateDelegate(IServerStatus status);
        public event StatusUpdateDelegate OnStatusUpdated;

        public long m_lastNotificationId = -1;
        public DateTime m_firstNotificationTime;
        public delegate void NewNotificationDelegate(INotification notification);
        public event NewNotificationDelegate OnNotification;

        private long m_lastEventId = 0;
        private long m_lastDataUpdateId = -1;
        private bool m_disableTrayIconLogin;

        private volatile IServerStatus m_status;

        private volatile bool m_shutdown = false;
        private volatile Thread m_requestThread;
        private volatile Thread m_pollThread;
        private readonly AutoResetEvent m_waitLock;

        private readonly Dictionary<string, string> m_options;
        private readonly Program.PasswordSource m_passwordSource;

        public IServerStatus Status { get { return m_status; } }

        private readonly object m_lock = new object();
        private readonly Queue<BackgroundRequest> m_workQueue = new Queue<BackgroundRequest>();

        public HttpServerConnection(System.Uri server, string password, Program.PasswordSource passwordSource, bool disableTrayIconLogin, string acceptedHostCertificate, Dictionary<string, string> options)
        {
            m_baseUri = Util.AppendDirSeparator(server.ToString(), "/");

            m_apiUri = m_baseUri + "api/v1";

            m_disableTrayIconLogin = disableTrayIconLogin;

            m_firstNotificationTime = DateTime.Now;

            m_password = password;
            m_options = options;
            m_passwordSource = passwordSource;

            var acceptedCertificates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(acceptedHostCertificate))
                acceptedCertificates.UnionWith(acceptedHostCertificate.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            HTTPCLIENT = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = acceptedCertificates switch
                {
                    { Count: 0 } => null,
                    { } when acceptedCertificates.Contains("*") => HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    _ => (sender, cert, chain, sslPolicyErrors) =>
                    {
                        if (sslPolicyErrors == SslPolicyErrors.None)
                            return true;

                        if (cert == null)
                            return false;

                        var certHash = cert.GetCertHashString();
                        return acceptedCertificates.Contains(certHash);
                    }
                }
            })
            {
                // Max time a request can be pending, actual requests can set a lower limit
                Timeout = Library.Utility.Timeparser.ParseTimeSpan(LONGPOLL_TIMEOUT) + TimeSpan.FromSeconds(10),
                DefaultRequestHeaders = {
                    UserAgent = { new ProductInfoHeaderValue("Duplicati-TrayIcon-Monitor", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()) }
                }
            };

            // TODO: Not nice to do in constructor
            // Get a connection
            UpdateStatus(false);

            m_waitLock = new AutoResetEvent(false);
            m_requestThread = new Thread(ThreadRunner);
            m_pollThread = new Thread(LongPollRunner);

            m_requestThread.Name = "TrayIcon Request Thread";
            m_pollThread.Name = "TrayIcon Longpoll Thread";

            m_requestThread.Start();
            m_pollThread.Start();
        }

        private void UpdateStatus(bool longpoll)
        {
            var query = longpoll ? $"?longpoll=true&lastEventId={m_lastEventId}&duration={LONGPOLL_TIMEOUT}" : "";

            m_status = PerformRequest<ServerStatusImpl>("GET", $"/serverstate{query}", null, longpoll ? Library.Utility.Timeparser.ParseTimeSpan(LONGPOLL_TIMEOUT) : null);
            m_lastEventId = m_status.LastEventID;

            if (OnStatusUpdated != null)
                OnStatusUpdated(m_status);

            if (m_lastNotificationId != m_status.LastNotificationUpdateID)
            {
                m_lastNotificationId = m_status.LastNotificationUpdateID;
                UpdateNotifications();
            }

            if (m_lastDataUpdateId != m_status.LastDataUpdateID)
            {
                m_lastDataUpdateId = m_status.LastDataUpdateID;
                UpdateApplicationSettings();
            }
        }

        private void UpdateNotifications()
        {
            var notifications = PerformRequest<NotificationImpl[]>("GET", "/notifications", null, null);
            if (notifications != null)
            {
                foreach (var n in notifications.Where(x => x.Timestamp > m_firstNotificationTime))
                    if (OnNotification != null)
                        OnNotification(n);

                if (notifications.Any())
                    m_firstNotificationTime = notifications.Select(x => x.Timestamp).Max();
            }
        }

        private void UpdateApplicationSettings()
        {
            var settings = PerformRequest<Dictionary<string, string>>("GET", "/serversettings", null, null);
            if (settings != null && settings.TryGetValue("disable-tray-icon-login", out var str))
                m_disableTrayIconLogin = Library.Utility.Utility.ParseBool(str, false);
        }

        private void LongPollRunner()
        {
            var started = DateTime.Now;
            var errorCount = 0;

            // TODO: Add an upper limit to the number of errors,
            // or implement a "disconnected" state
            while (!m_shutdown)
            {
                try
                {
                    var waitTime = TimeSpan.FromSeconds(Math.Min(10, errorCount * 2)) - (DateTime.Now - started);
                    if (waitTime.TotalSeconds > 0)
                        Thread.Sleep(waitTime);
                    started = DateTime.Now;
                    UpdateStatus(true);
                    errorCount = 0;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconRequestError", ex, "Failed to get response");
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
                            PerformRequest<string>(req.Method, req.Endpoint, req.Body, req.Timeout);
                        }

                    } while (req != null);

                    if (!(any || m_shutdown))
                        m_waitLock.WaitOne(TimeSpan.FromMinutes(1), true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconRequestError", ex, "Failed to get response");
                }
            }
        }

        public void Close()
        {
            m_shutdown = true;
            m_waitLock.Set();
            m_pollThread.Interrupt();
            m_pollThread.Join(TimeSpan.FromSeconds(10));
            if (!m_requestThread.Join(TimeSpan.FromSeconds(10)))
            {
                m_requestThread.Interrupt();
                m_requestThread.Join(TimeSpan.FromSeconds(10));
            }
        }

        private sealed record SigninResponse(string AccessToken);

        private T PerformRequest<T>(string method, string urlfragment, string body, TimeSpan? timeout)
        {
            if (string.IsNullOrWhiteSpace(m_accesstoken) && !urlfragment.StartsWith("/auth/"))
                ObtainAccessToken();

            var hasTriedPassword = false;

            while (true)
            {
                try
                {
                    return PerformRequestInternal<T>(method, urlfragment, body, timeout);
                }
                catch (AggregateException aex)
                {
                    if (hasTriedPassword || !aex.InnerExceptions.Any(x => x is HttpRequestException hex && hex.StatusCode == HttpStatusCode.Unauthorized))
                        throw;

                    // Only try once, and clear the token for the next try
                    hasTriedPassword = true;
                    m_accesstoken = null;
                    ObtainAccessToken();
                }
            }
        }

        private void ObtainAccessToken()
        {
            var token = ObtainSignInToken();
            if (string.IsNullOrWhiteSpace(token))
                return;
            m_accesstoken = PerformRequestInternal<SigninResponse>("POST", "/auth/signin", JsonSerializer.Serialize(new { SigninToken = token }), null).AccessToken;
        }

        private async Task<T> PerformRequestInternalAsync<T>(string method, string endpoint, string body, TimeSpan? timeout)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), new System.Uri(m_apiUri + endpoint));
            if (!string.IsNullOrWhiteSpace(m_accesstoken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_accesstoken);

            if (!string.IsNullOrWhiteSpace(body))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Set up response timeout, use 100s which is the .NET default
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(timeout.HasValue
                ? timeout.Value + TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(100));

            var response = await HTTPCLIENT.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (typeof(T) == typeof(string))
                return (T)(object)await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using (var stream = await response.Content.ReadAsStreamAsync())
                return await JsonSerializer.DeserializeAsync<T>(stream, serializerOptions).ConfigureAwait(false);
        }

        private T PerformRequestInternal<T>(string method, string endpoint, string body, TimeSpan? timeout)
            => PerformRequestInternalAsync<T>(method, endpoint, body, timeout).Await();

        private void ExecuteAndNotify(string method, string urifragment, string body)
        {
            lock (m_lock)
            {
                m_workQueue.Enqueue(new BackgroundRequest(method, urifragment, body, null));
                m_waitLock.Set();
            }
        }

        public void Pause(string duration = null)
        {
            ExecuteAndNotify("POST", $"/serverstate/pause{(string.IsNullOrWhiteSpace(duration) ? "" : $"?duration={duration}")}", null);
        }

        public void Resume()
        {
            ExecuteAndNotify("POST", "/serverstate/resume", null);
        }


        public void Dispose()
        {
            Close();
        }

        private sealed record SigninTokenResponse(string Token);
        private string IssueSigninToken(string password)
            => PerformRequest<SigninTokenResponse>("POST", "/auth/issuesignintoken", JsonSerializer.Serialize(new { Password = password }), null).Token;

        private string ObtainSignInToken()
        {
            string signinjwt = null;

            // If we host the server, issue the token from the service
            if (FIXMEGlobal.IsServerStarted && m_passwordSource == Program.PasswordSource.HostedServer)
                signinjwt = FIXMEGlobal.Provider.GetRequiredService<IJWTTokenProvider>().CreateSigninToken("trayicon");

            // If we have database access, grab the issuer key from the db and issue a token
            if (string.IsNullOrWhiteSpace(signinjwt) && m_passwordSource == Program.PasswordSource.Database)
            {
                Program.databaseConnection.ApplicationSettings.ReloadSettings();
                var cfg = Program.databaseConnection.ApplicationSettings.JWTConfig;
                if (!string.IsNullOrWhiteSpace(cfg))
                    signinjwt = new JWTTokenProvider(JsonSerializer.Deserialize<JWTConfig>(cfg)).CreateSigninToken("trayicon");
            }

            // If we know the password, issue a token from the API
            if (string.IsNullOrWhiteSpace(signinjwt) && !string.IsNullOrWhiteSpace(m_password))
                signinjwt = IssueSigninToken(m_password);

            return signinjwt;
        }

        public string StatusWindowURL
        {
            get
            {
                var signinjwt = m_disableTrayIconLogin ? null : ObtainSignInToken();
                return string.IsNullOrWhiteSpace(signinjwt)
                    ? m_baseUri + STATUS_WINDOW
                    : m_baseUri + SIGNIN_WINDOW + $"?token={signinjwt}";
            }
        }
    }
}
