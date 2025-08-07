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

#nullable enable

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
using CoCoL;
using Duplicati.Library.Common.IO;
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
            Converters =
            {
                new JsonStringEnumConverter(),
                new DayOfWeekStringEnumConverter()
            }
        };

        private const string STATUS_WINDOW = "index.html";
        private const string SIGNIN_WINDOW = "signin.html";

        private record BackgroundRequest(string Method, string Endpoint, string? Body, TimeSpan? Timeout = null);

        private readonly string m_apiUri;
        private readonly string m_baseUri;
        private string m_password;
        private string? m_accesstoken;
        private bool m_isTryingWithPassword;

        public Func<IServerStatus, Task>? OnStatusUpdated;
        public Action? ConnectionClosed;
        private int _connectionClosedInvoked;
        public long m_lastNotificationId = -1;
        public DateTime m_firstNotificationTime;

        public delegate void NewNotificationDelegate(INotification notification);

        public event NewNotificationDelegate? OnNotification;

        private long m_lastEventId = 0;
        private long m_lastDataUpdateId = -1;
        private bool m_disableTrayIconLogin;

        private volatile IServerStatus m_status =
            new ServerStatusImpl(LiveControlState.Paused, SuggestedStatusIcon.Disconnected, 0, 0, 0);

        private Task m_requestHandlerTask;
        private Task m_pollThread;
        private readonly CancellationTokenSource m_stopToken = new CancellationTokenSource();

        private readonly Dictionary<string, string> m_options;
        private readonly Program.PasswordSource m_passwordSource;

        public IServerStatus Status
        {
            get { return m_status; }
        }

        private readonly IChannel<BackgroundRequest> m_workQueue =
            Channel.Create<BackgroundRequest>(name: "TrayIconRequestQueue");

        private readonly EventWaitHandle _applicationExitEvent;

        public HttpServerConnection(IApplicationSettings? applicationSettings, System.Uri server, string password,
            Program.PasswordSource passwordSource, bool disableTrayIconLogin, string acceptedHostCertificate,
            Dictionary<string, string> options)
        {
            _applicationExitEvent = applicationSettings?.ApplicationExitEvent
                ?? new ManualResetEvent(false);

            m_baseUri = Util.AppendDirSeparator(server.ToString(), "/");

            m_apiUri = m_baseUri + "api/v1";

            m_disableTrayIconLogin = disableTrayIconLogin;

            m_firstNotificationTime = DateTime.Now;

            m_password = password;
            m_options = options;
            m_passwordSource = passwordSource;

            var acceptedCertificates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(acceptedHostCertificate))
                acceptedCertificates.UnionWith(acceptedHostCertificate.Split(new char[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            HTTPCLIENT = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = acceptedCertificates switch
                {
                    { Count: 0 } or null => null,

                    { } when acceptedCertificates.Contains("*") => HttpClientHandler
                        .DangerousAcceptAnyServerCertificateValidator,

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
                DefaultRequestHeaders =
                {
                    UserAgent =
                    {
                        new ProductInfoHeaderValue("Duplicati-TrayIcon-Monitor",
                            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0")
                    }
                }
            };

            m_requestHandlerTask = ThreadRunner();
            m_pollThread = LongPollRunner();

            m_requestHandlerTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconRequestError", t.Exception,
                        "Crashed request handler");
                if (Interlocked.CompareExchange(ref _connectionClosedInvoked, 1, 0) == 0)
                    ConnectionClosed?.Invoke();
            });

            m_pollThread.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconRequestError", t.Exception,
                        "Crashed poll thread");
                if (Interlocked.CompareExchange(ref _connectionClosedInvoked, 1, 0) == 0)
                    ConnectionClosed?.Invoke();
            });
        }

        public Task UpdateStatus()
            => UpdateStatusAsync(false);

        private async Task UpdateStatusAsync(bool longpoll)
        {
            var query = longpoll ? $"?longpoll=true&lastEventId={m_lastEventId}&duration={LONGPOLL_TIMEOUT}" : "";

            m_status = await PerformRequestAsync<ServerStatusImpl>("GET", $"/serverstate{query}", null,
                longpoll ? Library.Utility.Timeparser.ParseTimeSpan(LONGPOLL_TIMEOUT) : null);
            m_lastEventId = m_status.LastEventID;

            if (OnStatusUpdated != null)
                await OnStatusUpdated(m_status).ConfigureAwait(false);

            if (m_lastNotificationId != m_status.LastNotificationUpdateID)
            {
                m_lastNotificationId = m_status.LastNotificationUpdateID;
                await UpdateNotificationsAsync().ConfigureAwait(false);
            }

            if (m_lastDataUpdateId != m_status.LastDataUpdateID)
            {
                m_lastDataUpdateId = m_status.LastDataUpdateID;
                await UpdateApplicationSettingsAsync().ConfigureAwait(false);
            }
        }

        private async Task UpdateNotificationsAsync()
        {
            var notifications = await PerformRequestAsync<NotificationImpl[]>("GET", "/notifications", null, null)
                .ConfigureAwait(false);
            if (notifications != null)
            {
                foreach (var n in notifications.Where(x => x.Timestamp > m_firstNotificationTime))
                    if (OnNotification != null)
                        OnNotification(n);

                if (notifications.Any())
                    m_firstNotificationTime = notifications.Select(x => x.Timestamp).Max();
            }
        }

        private async Task UpdateApplicationSettingsAsync()
        {
            var settings = await PerformRequestAsync<Dictionary<string, string>>("GET", "/serversettings", null, null)
                .ConfigureAwait(false);
            if (settings != null && settings.TryGetValue("disable-tray-icon-login", out var str))
                m_disableTrayIconLogin = Library.Utility.Utility.ParseBool(str, false);
        }

        private async Task LongPollRunner()
        {
            var started = DateTime.Now;
            var errorCount = 0;
            var hasConnected = false;

            while (!m_stopToken.IsCancellationRequested && !_applicationExitEvent.WaitOne(0))
            {
                try
                {
                    var waitTime = TimeSpan.FromSeconds(Math.Min(10, errorCount * 2)) - (DateTime.Now - started);
                    if (waitTime.TotalSeconds > 0)
                    {
                        // Wait for either the delay, cancellation, or ManualResetEvent
                        var delayTask = Task.Delay(waitTime, m_stopToken.Token);
                        var waitHandleTask = Task.Run(() =>
                        {
                            // Wait for either cancellation or ManualResetEvent
                            WaitHandle.WaitAny(
                                [m_stopToken.Token.WaitHandle, _applicationExitEvent]);
                        });

                        // Await the first task to complete (delay or wait handle)
                        await Task.WhenAny(delayTask, waitHandleTask).ConfigureAwait(false);

                        // Check if cancellation was requested
                        m_stopToken.Token.ThrowIfCancellationRequested();

                        if (_applicationExitEvent.WaitOne(0))
                        {
                            return; // Exit the loop if ManualResetEvent is signaled
                        }
                    }

                    started = DateTime.Now;
                    await UpdateStatusAsync(true).ConfigureAwait(false);
                    errorCount = 0;
                    hasConnected = true;
                }
                catch (Exception ex) when (ex.IsRetiredException() || m_stopToken.IsCancellationRequested)
                {
                    Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconPollRequestError", ex,
                        "Failed to get response");
                    return;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    // More than 1 error, and we are disconnected
                    if (errorCount >= 2 && OnStatusUpdated != null || !hasConnected)
                    {
                        if (OnStatusUpdated != null)
                            await OnStatusUpdated.Invoke(new ServerStatusImpl(
                                LiveControlState.Paused,
                                SuggestedStatusIcon.Disconnected,
                                m_lastEventId,
                                m_lastDataUpdateId,
                                m_lastNotificationId)).ConfigureAwait(false);
                    }

                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconPollRequestError", ex,
                        "Failed to get response");
                }
            }
        }

        private async Task ThreadRunner()
        {
            while (true)
            {
                try
                {
                    var req = await m_workQueue.ReadAsync().ConfigureAwait(false);
                    await PerformRequestAsync<string>(req.Method, req.Endpoint, req.Body, req.Timeout)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex.IsRetiredException() || m_stopToken.IsCancellationRequested)
                {
                    Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconRequestError", ex,
                        "Failed to get response");
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "TrayIconRequestError", ex,
                        "Failed to get response");
                }
            }
        }

        public void Close()
        {
            m_stopToken.Cancel();
            m_workQueue.Retire();
            if (Interlocked.CompareExchange(ref _connectionClosedInvoked, 1, 0) == 0)
                ConnectionClosed?.Invoke();
        }

        private sealed record SigninResponse(string AccessToken);

        private async Task<T> PerformRequestAsync<T>(string method, string urlfragment, string? body, TimeSpan? timeout)
        {
            if (string.IsNullOrWhiteSpace(m_accesstoken) && !urlfragment.StartsWith("/auth/"))
                await ObtainAccessTokenAsync().ConfigureAwait(false);

            var hasTriedPassword = m_isTryingWithPassword;

            while (true)
            {
                try
                {
                    return await PerformRequestInternalAsync<T>(method, urlfragment, body, timeout)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                    when (ex is HttpRequestException hex && (hex.StatusCode == HttpStatusCode.Unauthorized ||
                                                             hex.StatusCode == HttpStatusCode.Forbidden)
                          || ex is AggregateException aex && aex.InnerExceptions.Any(x =>
                              x is HttpRequestException hex && (hex.StatusCode == HttpStatusCode.Unauthorized ||
                                                                hex.StatusCode == HttpStatusCode.Forbidden))
                         )
                {
                    if (hasTriedPassword)
                        throw;

                    // TODO: This error handling is error prone and can end up in infinite recursion
                    // Should rewrite the entire class and use websockets instead

                    // Only try once, and clear the token for the next try
                    hasTriedPassword = true;
                    m_accesstoken = null;
                    try
                    {
                        m_isTryingWithPassword = true;
                        await ObtainAccessTokenAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_isTryingWithPassword = false;
                    }
                }
            }
        }

        private async Task ObtainAccessTokenAsync()
        {
            // If we host the server, issue the access token from the service
            var sp = Server.Program.DuplicatiWebserver?.Provider;
            if (sp != null && m_passwordSource == Program.PasswordSource.HostedServer)
            {
                var provider = sp.GetRequiredService<IJWTTokenProvider>();
                m_accesstoken = provider.CreateAccessToken("trayicon", provider.TemporaryFamilyId);
                return;
            }

            // If we know the password, issue a token from the API
            if (!string.IsNullOrWhiteSpace(m_password))
            {
                m_accesstoken = (await PerformRequestInternalAsync<SigninResponse>("POST", "/auth/login",
                    JsonSerializer.Serialize(new { Password = m_password }), null).ConfigureAwait(false)).AccessToken;
                return;
            }

            // Otherwise, try to get a signin token
            var token = await ObtainSignInTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                return;

            // Use the signin token to get an access token
            m_accesstoken =
                (await PerformRequestInternalAsync<SigninResponse>("POST", "/auth/signin",
                    JsonSerializer.Serialize(new { SigninToken = token }), null).ConfigureAwait(false)).AccessToken;
        }

        private async Task<T> PerformRequestInternalAsync<T>(string method, string endpoint, string? body,
            TimeSpan? timeout)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), new Uri(m_apiUri + endpoint));
            if (!string.IsNullOrWhiteSpace(m_accesstoken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_accesstoken);

            if (!string.IsNullOrWhiteSpace(body))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Set up response timeout, use 100s which is the .NET default
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(timeout.HasValue
                ? timeout.Value + TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(100));

            // Combine CancellationToken and ManualResetEvent
            var sendTask = HTTPCLIENT.SendAsync(request, cts.Token);
            var waitHandleTask = Task.Run(() =>
            {
                // Wait for either cancellation or ManualResetEvent
                WaitHandle.WaitAny([cts.Token.WaitHandle, _applicationExitEvent]);
            });

            // Await the first task to complete (HTTP request or wait handle)
            await Task.WhenAny(sendTask, waitHandleTask).ConfigureAwait(false);

            // Check if cancellation was requested or ManualResetEvent was signaled
            if (cts.Token.IsCancellationRequested || _applicationExitEvent.WaitOne(0))
            {
                cts.Token.ThrowIfCancellationRequested(); // Throws OperationCanceledException if canceled
                throw new OperationCanceledException(
                    "Operation canceled due to ServerSettings.ExitResetEvent being signaled.");
            }

            // Get the response from the completed sendTask
            var response = await sendTask.ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (typeof(T) == typeof(string))
                return (T)(object)await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, serializerOptions, cts.Token).ConfigureAwait(false)
                ?? throw new JsonException("Failed to deserialize response.");
        }

        private void ExecuteAndNotify(string method, string urifragment, string? body)
        {
            m_workQueue.WriteNoWait(new BackgroundRequest(method, urifragment, body, null));
        }

        public void Pause(string? duration = null)
        {
            ExecuteAndNotify("POST",
                $"/serverstate/pause{(string.IsNullOrWhiteSpace(duration) ? "" : $"?duration={duration}")}", null);
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

        private async Task<string> IssueSigninTokenAsync(string password)
            => (await PerformRequestInternalAsync<SigninTokenResponse>("POST", "/auth/issuesignintoken",
                JsonSerializer.Serialize(new { Password = password }), null).ConfigureAwait(false)).Token;

        private async Task<string?> ObtainSignInTokenAsync()
        {
            string? signinjwt = null;

            // If we host the server, issue the token from the service
            var sp = Server.Program.DuplicatiWebserver?.Provider;
            if (sp != null && m_passwordSource == Program.PasswordSource.HostedServer)
            {
                if (sp.GetRequiredService<Server.Database.Connection>().ApplicationSettings.DisableSigninTokens)
                    return null;

                signinjwt = sp.GetRequiredService<IJWTTokenProvider>().CreateSigninToken("trayicon");
            }

            // If we have database access, grab the issuer key from the db and issue a token
            if (string.IsNullOrWhiteSpace(signinjwt) && m_passwordSource == Program.PasswordSource.Database)
            {
                Program.databaseConnection.ApplicationSettings.ReloadSettings();
                if (Program.databaseConnection.ApplicationSettings.DisableSigninTokens)
                    return null;

                var cfg = Program.databaseConnection.ApplicationSettings.JWTConfig;
                if (!string.IsNullOrWhiteSpace(cfg))
                    signinjwt =
                        new JWTTokenProvider(JsonSerializer.Deserialize<JWTConfig>(cfg) ?? throw new JsonException("Failed to deserialize JWT config")).CreateSigninToken("trayicon");
            }

            // If we know the password, issue a token from the API
            if (string.IsNullOrWhiteSpace(signinjwt) && !string.IsNullOrWhiteSpace(m_password))
                signinjwt = await IssueSigninTokenAsync(m_password);

            return signinjwt;
        }

        public async Task<string> GetStatusWindowURLAsync()
        {
            string? signinjwt = null;
            if (!m_disableTrayIconLogin)
            {
                try
                {
                    signinjwt = await ObtainSignInTokenAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            return string.IsNullOrWhiteSpace(signinjwt)
                ? m_baseUri + STATUS_WINDOW
                : m_baseUri + SIGNIN_WINDOW + $"?token={signinjwt}";
        }
    }
}
