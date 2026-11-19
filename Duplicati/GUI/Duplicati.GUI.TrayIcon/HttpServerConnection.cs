// Copyright (C) 2026, The Duplicati Team
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
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

        /// <summary>
        /// Sets up the websocket on the same handler as the requests, so it checks the server
        /// certificate the same way
        /// </summary>
        private readonly HttpMessageInvoker m_websocketInvoker;

        /// <summary>
        /// The longest time setting up the websocket may take, the same as for a request
        /// </summary>
        private static readonly TimeSpan WEBSOCKET_SETUP_TIMEOUT = TimeSpan.FromSeconds(100);

        /// <summary>
        /// The close status the server uses when it does not accept the access token
        /// </summary>
        private const WebSocketCloseStatus WEBSOCKET_NOT_AUTHENTICATED = (WebSocketCloseStatus)4401;

        /// <summary>
        /// Set when the server does not send the status over a websocket, such as an older
        /// server, or a proxy that does not pass websockets on; the long poll is used then
        /// </summary>
        private volatile bool m_websocketUnavailable;

        private readonly string m_userAgent;

        private record ServerStatusImpl(
            LiveControlState ProgramState,
            SuggestedStatusIcon SuggestedStatusIcon,
            long LastEventID,
            long LastDataUpdateID,
            long LastNotificationUpdateID
        ) : IServerStatus
        {
            /// <summary>
            /// The running task, as the server sends it: the task ID and the backup ID
            /// </summary>
            public ActiveTaskImpl? ActiveTask { get; init; }

            /// <inheritdoc />
            [JsonIgnore]
            public bool HasActiveTask => ActiveTask != null;

            /// <inheritdoc />
            [JsonIgnore]
            public string? ActiveBackupName { get; init; }
        }

        private sealed record ActiveTaskImpl(long Item1, string? Item2);

        private sealed record BackupNameImpl(string? Name);

        private sealed record BackupResponseImpl(BackupNameImpl? Backup);

        /// <summary>
        /// The backup names looked up, by backup ID, with the data update they were read at
        /// </summary>
        private readonly Dictionary<string, (long DataUpdateId, string? Name)> m_backupNames = new();

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

        private string? m_accesstoken;
        /// <summary>
        /// Lets one request at a time get an access token
        /// </summary>
        private readonly SemaphoreSlim m_accessTokenLock = new SemaphoreSlim(1, 1);
        private bool m_isTryingWithPassword;
        private string ApiUri => BaseUri + "api/v1";
        private string BaseUri => Util.AppendDirSeparator(_passwordStorageHelper.HostUrl ?? "", "/");

        /// <summary>
        /// The address of the websocket the server sends its status on
        /// </summary>
        private Uri WebsocketUri
        {
            get
            {
                var builder = new UriBuilder(BaseUri + "notifications");
                builder.Scheme = string.Equals(builder.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? Uri.UriSchemeWss
                    : Uri.UriSchemeWs;
                return builder.Uri;
            }
        }

        public Func<IServerStatus, Task>? OnStatusUpdated;
        public Action? ConnectionClosed;
        private int _connectionClosedInvoked;
        public long m_lastNotificationId = -1;
        public DateTime m_firstNotificationTime;

        public delegate void NewNotificationDelegate(INotification notification);

        public event NewNotificationDelegate? OnNotification;

        private long m_lastEventId = 0;
        /// <summary>
        /// The number of times a status was taken, to tell whether one came in during a request
        /// </summary>
        private long m_statusUpdates;
        /// <summary>
        /// Guards taking a status
        /// </summary>
        private readonly object m_statusLock = new object();
        private long m_lastDataUpdateId = -1;
        private bool m_disableTrayIconLogin;

        private volatile IServerStatus m_status =
            new ServerStatusImpl(LiveControlState.Paused, SuggestedStatusIcon.Disconnected, 0, 0, 0);

        private Task m_requestHandlerTask;
        private Task m_pollThread;
        private readonly CancellationTokenSource m_stopToken = new CancellationTokenSource();

        private readonly Program.PasswordSource m_passwordSource;

        public IServerStatus Status
        {
            get { return m_status; }
        }

        private void UpdateServerUri()
        {
            // Reset state to ensure clean reconnection to the new server
            m_lastEventId = 0;
            m_lastDataUpdateId = -1;
            m_lastNotificationId = -1;
        }

        // Not a named channel: a named channel is shared by the whole process, so closing
        // one connection would retire the queue of any connection made after it
        private readonly IChannel<BackgroundRequest> m_workQueue =
            Channel.Create<BackgroundRequest>();

        private readonly CancellationToken _applicationExitEvent;
        private readonly PasswordStorageHelper _passwordStorageHelper;

        public Program.PasswordSource PasswordSource => m_passwordSource;

        public HttpServerConnection(IApplicationSettings? applicationSettings,
            Program.PasswordSource passwordSource, bool disableTrayIconLogin, string acceptedHostCertificate,
            bool ignoreRevocationFailure,
            Dictionary<string, string> options,
            PasswordStorageHelper passwordStorageHelper)
        {
            _applicationExitEvent = applicationSettings?.ApplicationExit
                ?? CancellationToken.None;

            _passwordStorageHelper = passwordStorageHelper;
            _passwordStorageHelper.OnPasswordChanged += (sender, e) => UpdateServerUri();
            m_disableTrayIconLogin = disableTrayIconLogin;

            m_firstNotificationTime = DateTime.Now;

            m_passwordSource = passwordSource;

            var acceptedCertificates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(acceptedHostCertificate))
                acceptedCertificates.UnionWith(acceptedHostCertificate.Split(new char[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Configure the certificate validator using the shared helper, which handles
            // accept-all, specific hashes and the ignore-revocation-failure option in a
            // single place instead of replicating the logic here.
            var httpHandler = new HttpClientHandler();
            var acceptAll = acceptedCertificates.Contains("*");
            if (acceptAll || acceptedCertificates.Count > 0 || ignoreRevocationFailure)
                Duplicati.Library.Utility.HttpClientHelper.ConfigureHandlerCertificateValidator(
                    httpHandler,
                    acceptAll,
                    acceptAll ? null : [.. acceptedCertificates],
                    ignoreRevocationFailure);

            var userAgent = new ProductInfoHeaderValue("Duplicati-TrayIcon-Monitor",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");
            m_userAgent = userAgent.ToString();

            HTTPCLIENT = new HttpClient(httpHandler)
            {
                // Max time a request can be pending, actual requests can set a lower limit
                Timeout = Library.Utility.Timeparser.ParseTimeSpan(LONGPOLL_TIMEOUT) + TimeSpan.FromSeconds(10),
                DefaultRequestHeaders =
                {
                    UserAgent = { userAgent }
                }
            };
            m_websocketInvoker = new HttpMessageInvoker(httpHandler, disposeHandler: false);

            m_requestHandlerTask = ThreadRunnerAsync();
            m_pollThread = LongPollRunnerAsync();

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

        public async Task UpdateStatusAsync()
        {
            await PasswordAvailableIfNeededAsync().ConfigureAwait(false);
            await UpdateStatusAsync(false).ConfigureAwait(false);
        }

        private async Task UpdateStatusAsync(bool longpoll)
        {
            var query = longpoll ? $"?longpoll=true&lastEventId={m_lastEventId}&duration={LONGPOLL_TIMEOUT}" : "";
            var updatesBefore = Interlocked.Read(ref m_statusUpdates);

            var status = await PerformRequestAsync<ServerStatusImpl>("GET", $"/serverstate{query}", null,
                longpoll ? Library.Utility.Timeparser.ParseTimeSpan(LONGPOLL_TIMEOUT) : null);

            await TakeStatusAsync(status, !longpoll, updatesBefore).ConfigureAwait(false);
        }

        /// <summary>
        /// Takes a status from the server, and fetches what it says has changed
        /// </summary>
        /// <param name="status">The status</param>
        /// <param name="plainRequest">If the status is the answer to a plain request, not one the server sent on a change</param>
        /// <param name="updatesBefore">For a plain request, the number of statuses taken when it was sent</param>
        private async Task TakeStatusAsync(ServerStatusImpl status, bool plainRequest, long updatesBefore)
        {
            // The name of the running backup is shown in the tooltip, for a status from a request and from the websocket alike
            var activeBackupId = status.ActiveTask?.Item2;
            if (!string.IsNullOrWhiteSpace(activeBackupId))
                status = status with { ActiveBackupName = await GetBackupNameAsync(activeBackupId, status.LastDataUpdateID).ConfigureAwait(false) };

            lock (m_statusLock)
            {
                // A plain request runs next to the long poll or the websocket. If one of those
                // has taken a state since this request was sent, that state may be newer than
                // this answer, and taking this one would put an old state back until the next
                // event, which can be minutes away. If this answer was the newer one, the next
                // long poll brings the change at once, as it waits from the event it has seen,
                // and the websocket gets every change as it happens.
                if (plainRequest && m_statusUpdates != updatesBefore)
                {
                    Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconStaleStatus", null,
                        "Ignoring the status from event {0}, as a newer status came in meanwhile", status.LastEventID);
                    return;
                }

                m_statusUpdates++;
                m_status = status;
                m_lastEventId = status.LastEventID;
            }

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

        /// <summary>
        /// Gets the name of a backup, to show which backup is running. A name is read again
        /// once the server data has changed, as the backup may have been renamed.
        /// </summary>
        /// <param name="backupId">The backup ID</param>
        /// <param name="dataUpdateId">The data update the server state was taken at</param>
        /// <returns>The name, or <c>null</c> if it could not be read</returns>
        private async Task<string?> GetBackupNameAsync(string backupId, long dataUpdateId)
        {
            lock (m_backupNames)
                if (m_backupNames.TryGetValue(backupId, out var cached) && cached.DataUpdateId == dataUpdateId)
                    return cached.Name;

            string? name;
            try
            {
                var response = await PerformRequestAsync<BackupResponseImpl>("GET", $"/backup/{Uri.EscapeDataString(backupId)}", null, null)
                    .ConfigureAwait(false);
                name = response?.Backup?.Name;
            }
            catch (Exception ex) when (!m_stopToken.IsCancellationRequested && !_applicationExitEvent.IsCancellationRequested)
            {
                // The name is only shown in the tooltip, so the status update goes on without it
                Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconBackupNameError", ex,
                    "Failed to get the name of backup {0}", backupId);
                return null;
            }

            lock (m_backupNames)
                m_backupNames[backupId] = (dataUpdateId, name);

            return name;
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
            if (settings != null && settings.TryGetValue(Server.Database.ServerSettings.CONST.DISABLE_TRAY_ICON_LOGIN, out var str))
                m_disableTrayIconLogin = Library.Utility.Utility.ParseBool(str, false);
        }

        private async Task PasswordAvailableIfNeededAsync()
        {
            if (m_passwordSource == Program.PasswordSource.SuppliedPassword && string.IsNullOrWhiteSpace(_passwordStorageHelper.Password))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_stopToken.Token, _applicationExitEvent);
                await _passwordStorageHelper.WaitForPasswordUpdateAsync(cts.Token).ConfigureAwait(false);
            }
        }

        private async Task LongPollRunnerAsync()
        {
            var started = DateTime.Now;
            var errorCount = 0;
            var hasConnected = false;

            while (!m_stopToken.IsCancellationRequested && !_applicationExitEvent.IsCancellationRequested)
            {
                try
                {
                    var waitTime = TimeSpan.FromSeconds(Math.Min(10, errorCount * 2)) - (DateTime.Now - started);
                    if (waitTime.TotalSeconds > 0)
                    {
                        // Wait for the specified time or until stop or exit is requested
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_stopToken.Token, _applicationExitEvent);
                        await Task.Delay(waitTime, cts.Token).ConfigureAwait(false);
                    }

                    await PasswordAvailableIfNeededAsync().ConfigureAwait(false);

                    started = DateTime.Now;
                    if (!m_websocketUnavailable)
                    {
                        // Runs until the connection closes, or returns at once if the server
                        // does not send the status over a websocket
                        await FollowStatusOverWebsocketAsync(() =>
                        {
                            errorCount = 0;
                            hasConnected = true;
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        await UpdateStatusAsync(true).ConfigureAwait(false);
                        errorCount = 0;
                        hasConnected = true;
                    }
                }
                catch (Exception ex) when (ex.IsRetiredException() || m_stopToken.IsCancellationRequested || _applicationExitEvent.IsCancellationRequested)
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

        /// <summary>
        /// Gets the status from the server over a websocket, which the server sends on every
        /// change, until the connection closes. A long poll instead holds a request open,
        /// and starts a new one after every change and every five minutes.
        /// </summary>
        /// <param name="onStatus">Called when a status comes in</param>
        /// <returns>A task that completes without error if the server does not send the status this way, and the long poll is to be used</returns>
        private async Task FollowStatusOverWebsocketAsync(Action onStatus)
        {
            if (string.IsNullOrWhiteSpace(m_accesstoken))
                await ObtainAccessTokenOnceAsync(null).ConfigureAwait(false);
            var usedToken = m_accesstoken;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_stopToken.Token, _applicationExitEvent);
            using var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("User-Agent", m_userAgent);

            cts.CancelAfter(WEBSOCKET_SETUP_TIMEOUT);
            try
            {
                await socket.ConnectAsync(WebsocketUri, m_websocketInvoker, cts.Token).ConfigureAwait(false);
            }
            catch (WebSocketException ex) when (socket.HttpStatusCode != 0)
            {
                // The server answered, but did not take the request as a websocket
                UseLongPoll(ex, $"the server answered with status code {(int)socket.HttpStatusCode}");
                return;
            }

            // The token goes in a message, as the server takes a websocket with a token it
            // does not accept as one that is yet to authenticate, and waits for the message
            await SendWebsocketMessageAsync(socket, new { Version = 1, Token = usedToken ?? "" }, cts.Token).ConfigureAwait(false);
            using (var reply = await ReceiveWebsocketMessageAsync(socket, cts.Token).ConfigureAwait(false))
            {
                if (reply == null)
                    await ThrowWebsocketClosedAsync(socket, usedToken).ConfigureAwait(false);
                else if (GetWebsocketMessageType(reply) != "auth" || !IsWebsocketReplySuccess(reply))
                {
                    UseLongPoll(null, "the server did not accept the websocket authentication");
                    return;
                }
            }

            await SendWebsocketMessageAsync(socket, new { Version = 1, Id = "trayicon-status", Action = "sub", Service = "legacystatus" }, cts.Token).ConfigureAwait(false);

            // The connection is set up, and stays open until it closes or the tray stops
            cts.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
            Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconWebsocketConnected", null,
                "Getting the server status over a websocket");

            while (true)
            {
                using var message = await ReceiveWebsocketMessageAsync(socket, cts.Token).ConfigureAwait(false);
                if (message == null)
                {
                    await ThrowWebsocketClosedAsync(socket, usedToken).ConfigureAwait(false);
                    return;
                }

                switch (GetWebsocketMessageType(message))
                {
                    case "legacystatus":
                        var status = message.Deserialize<ServerStatusImpl>(serializerOptions)
                            ?? throw new JsonException("Failed to deserialize the status.");
                        onStatus();
                        await TakeStatusAsync(status, false, 0).ConfigureAwait(false);
                        break;

                    case "reply" when !IsWebsocketReplySuccess(message):
                        // The server does not offer the status as a subscription
                        UseLongPoll(null, "the server did not accept the status subscription");
                        await CloseWebsocketAsync(socket).ConfigureAwait(false);
                        return;
                }
            }
        }

        /// <summary>
        /// Switches to the long poll, for as long as this connection lasts
        /// </summary>
        private void UseLongPoll(Exception? ex, string reason)
        {
            m_websocketUnavailable = true;
            Library.Logging.Log.WriteInformationMessage(LOGTAG, "TrayIconWebsocketUnavailable",
                "Getting the server status with a long poll, as {0}", reason);
            if (ex != null)
                Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconWebsocketUnavailable", ex,
                    "The websocket could not be set up");
        }

        /// <summary>
        /// Throws for a websocket the server closed, after getting a new access token if the
        /// server closed it because it did not accept the token
        /// </summary>
        private async Task ThrowWebsocketClosedAsync(ClientWebSocket socket, string? usedToken)
        {
            var closeStatus = socket.CloseStatus;
            await CloseWebsocketAsync(socket).ConfigureAwait(false);

            if (closeStatus == WEBSOCKET_NOT_AUTHENTICATED)
            {
                await ObtainAccessTokenOnceAsync(usedToken).ConfigureAwait(false);
                throw new WebSocketException("The server did not accept the access token for the websocket");
            }

            throw new WebSocketException($"The server closed the websocket: {closeStatus}");
        }

        /// <summary>
        /// Answers the close the server sent, so it does not wait for it
        /// </summary>
        private static async Task CloseWebsocketAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteVerboseMessage(LOGTAG, "TrayIconWebsocketCloseFailed", ex,
                    "Failed to close the websocket");
            }
        }

        private static Task SendWebsocketMessageAsync<T>(ClientWebSocket socket, T message, CancellationToken token)
            => socket.SendAsync(new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(message)), WebSocketMessageType.Text, true, token).AsTask();

        /// <summary>
        /// Reads a message from the websocket
        /// </summary>
        /// <returns>The message, or <c>null</c> if the server closed the websocket</returns>
        private static async Task<JsonDocument?> ReceiveWebsocketMessageAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using var data = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                data.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    break;
            }

            data.Position = 0;
            return JsonDocument.Parse(data);
        }

        private static string? GetWebsocketMessageType(JsonDocument message)
            => message.RootElement.ValueKind == JsonValueKind.Object
                && message.RootElement.TryGetProperty("Type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;

        private static bool IsWebsocketReplySuccess(JsonDocument message)
            => message.RootElement.ValueKind == JsonValueKind.Object
                && message.RootElement.TryGetProperty("Success", out var success)
                && success.ValueKind == JsonValueKind.True;

        private async Task ThreadRunnerAsync()
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
                await ObtainAccessTokenOnceAsync(null).ConfigureAwait(false);

            var hasTriedPassword = m_isTryingWithPassword;

            while (true)
            {
                var usedToken = m_accesstoken;
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

                    // Only try once, and get a new token for the next try
                    hasTriedPassword = true;
                    try
                    {
                        m_isTryingWithPassword = true;
                        await ObtainAccessTokenOnceAsync(usedToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_isTryingWithPassword = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets an access token, one request at a time. Requests running at the same time, such
        /// as the long poll and a status request at start, each logged in otherwise.
        /// </summary>
        /// <param name="refusedToken">The token a request was refused with, or <c>null</c> if it had none</param>
        private async Task ObtainAccessTokenOnceAsync(string? refusedToken)
        {
            await m_accessTokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another request got a token while this one waited, so that is the one to use
                if (!string.IsNullOrWhiteSpace(m_accesstoken) && m_accesstoken != refusedToken)
                    return;

                m_accesstoken = null;
                await ObtainAccessTokenAsync().ConfigureAwait(false);
            }
            finally
            {
                m_accessTokenLock.Release();
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
            if (!string.IsNullOrWhiteSpace(_passwordStorageHelper.Password))
            {
                m_accesstoken = (await PerformRequestInternalAsync<SigninResponse>("POST", "/auth/login",
                    JsonSerializer.Serialize(new { Password = _passwordStorageHelper.Password }), null).ConfigureAwait(false)).AccessToken;
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
            var request = new HttpRequestMessage(new HttpMethod(method), new Uri(ApiUri + endpoint));
            if (!string.IsNullOrWhiteSpace(m_accesstoken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_accesstoken);

            if (!string.IsNullOrWhiteSpace(body))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Set up response timeout, use 100s which is the .NET default
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_stopToken.Token, _applicationExitEvent);
            cts.CancelAfter(timeout.HasValue
                ? timeout.Value + TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(100));

            var response = await HTTPCLIENT.SendAsync(request, cts.Token).ConfigureAwait(false);
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
                {
                    var jwtConfig = JsonSerializer.Deserialize<JWTConfig>(cfg) ?? throw new JsonException("Failed to deserialize JWT config");
                    signinjwt = new JWTTokenProvider(() => jwtConfig).CreateSigninToken("trayicon");
                }
            }

            // If we know the password, issue a token from the API
            if (string.IsNullOrWhiteSpace(signinjwt) && !string.IsNullOrWhiteSpace(_passwordStorageHelper.Password))
                signinjwt = await IssueSigninTokenAsync(_passwordStorageHelper.Password);

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
                ? BaseUri + STATUS_WINDOW
                : BaseUri + SIGNIN_WINDOW + $"?token={signinjwt}";
        }
    }
}
