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
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.GUI.TrayIcon;
using Duplicati.Server.Serialization;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The tray icon got the server state with a long poll: a request the server holds until the
/// state changes, or for five minutes, after which the tray sends the next one. The server
/// also sends the state over a websocket, on every change, and the tray now follows it that
/// way, with the long poll left for servers that do not offer it (issue #5273).
/// </summary>
[NonParallelizable]
public class TrayWebsocketTests
{
    /// <summary>
    /// A stand-in for the server, which can send the state over a websocket
    /// </summary>
    private sealed class StandInServer : IDisposable
    {
        private readonly HttpListener m_listener = new();
        private readonly Task m_loop;
        private readonly List<HttpListenerContext> m_held = new();
        private readonly List<WebSocket> m_sockets = new();
        private int m_logins;
        private int m_longPolls;
        private int m_websockets;

        /// <summary>The address to give the tray</summary>
        public string Prefix { get; }

        /// <summary>If set, the server answers the websocket as a plain request, as an older server would</summary>
        public volatile bool NoWebsocket;

        /// <summary>The websocket connections that are refused with the not authenticated close status</summary>
        public volatile int RefuseWebsockets;

        /// <summary>The number of logins so far</summary>
        public int Logins => Volatile.Read(ref m_logins);

        /// <summary>The number of long poll requests so far</summary>
        public int LongPolls => Volatile.Read(ref m_longPolls);

        /// <summary>The messages the tray sent on the websockets, in order</summary>
        public readonly List<string> MessagesReceived = new();

        /// <summary>Completes with the websocket once the tray has subscribed on it</summary>
        public TaskCompletionSource<WebSocket> Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StandInServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Prefix = $"http://localhost:{port}/";
            m_listener.Prefixes.Add(Prefix);
            m_listener.Start();
            m_loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (true)
            {
                HttpListenerContext context;
                try { context = await m_listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private static async Task ReplyAsync(HttpListenerContext context, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private static async Task<string?> ReceiveAsync(WebSocket socket)
        {
            var buffer = new byte[16 * 1024];
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            return result.MessageType == WebSocketMessageType.Close
                ? null
                : Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        public static Task SendAsync(WebSocket socket, string json)
            => socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        private async Task HandleAsync(HttpListenerContext context)
        {
            var path = context.Request.Url!.AbsolutePath;
            var query = context.Request.Url.Query;

            if (path == "/notifications" && context.Request.IsWebSocketRequest && !NoWebsocket)
            {
                await HandleWebsocketAsync(context);
                return;
            }

            if (path == "/notifications")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            else if (path.EndsWith("/auth/login"))
            {
                var number = Interlocked.Increment(ref m_logins);
                await ReplyAsync(context, $"{{\"AccessToken\":\"token{number}\"}}");
            }
            else if (path.EndsWith("/serverstate") && query.Contains("longpoll=true"))
            {
                // The first long poll gets the state, and the next ones wait
                if (Interlocked.Increment(ref m_longPolls) == 1)
                    await ReplyAsync(context, State("Paused", "Paused", 2, false));
                else
                    lock (m_held)
                        m_held.Add(context);
            }
            else if (path.EndsWith("/serverstate"))
                await ReplyAsync(context, State("Running", "Ready", 1, false));
            else if (path.EndsWith("/backup/3"))
                await ReplyAsync(context, "{\"Schedule\":null,\"Backup\":{\"ID\":\"3\",\"Name\":\"Documents\",\"TargetURL\":\"file:///tmp\"}}");
            else
                await ReplyAsync(context, path.EndsWith("/notifications") ? "[]" : "{}");
        }

        private async Task HandleWebsocketAsync(HttpListenerContext context)
        {
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            lock (m_sockets)
                m_sockets.Add(socket);

            try
            {
                // The tray authenticates with a message, as the browser does
                var auth = await ReceiveAsync(socket);
                if (auth == null)
                    return;
                lock (MessagesReceived)
                    MessagesReceived.Add(auth);

                if (Interlocked.Increment(ref m_websockets) <= RefuseWebsockets)
                {
                    await socket.CloseAsync((WebSocketCloseStatus)4401, "Invalid token", CancellationToken.None);
                    return;
                }
                await SendAsync(socket, "{\"Version\":1,\"Message\":\"Authenticated successfully\",\"Success\":true,\"Type\":\"auth\"}");

                var subscription = await ReceiveAsync(socket);
                if (subscription == null)
                    return;
                lock (MessagesReceived)
                    MessagesReceived.Add(subscription);

                var id = JsonDocument.Parse(subscription).RootElement.GetProperty("Id").GetString();
                await SendAsync(socket, $"{{\"Version\":1,\"Id\":\"{id}\",\"Service\":\"legacystatus\",\"Message\":\"Subscribed successfully\",\"Success\":true,\"Data\":null,\"Type\":\"reply\"}}");
                Subscribed.TrySetResult(socket);

                // Wait for the tray to close
                while (await ReceiveAsync(socket) != null)
                {
                }
            }
            catch
            {
                // The tray went away
            }
        }

        public void Dispose()
        {
            lock (m_held)
                foreach (var c in m_held)
                    try { c.Response.Abort(); } catch { }
            lock (m_sockets)
                foreach (var s in m_sockets)
                    try { s.Abort(); } catch { }
            m_listener.Stop();
            m_loop.Wait(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// A server state, as the server sends it
    /// </summary>
    private static string State(string programState, string icon, long eventId, bool overWebsocket)
        => $"{{\"ProgramState\":\"{programState}\",\"SuggestedStatusIcon\":\"{icon}\",\"LastEventID\":{eventId},\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0{(overWebsocket ? ",\"Type\":\"legacystatus\"" : "")}}}";

    /// <summary>
    /// Connects the tray to the stand-in, with a password, and reports each state it takes
    /// </summary>
    private static async Task<(HttpServerConnection Connection, Func<long, Task> WaitForEvent)> ConnectAsync(StandInServer server)
    {
        var passwords = await PasswordStorageHelper.CreateAsync(server.Prefix, true, "password", Program.PasswordSource.SuppliedPassword, new Dictionary<string, string?>());
        var connection = new HttpServerConnection(null, Program.PasswordSource.SuppliedPassword, false, "", false, new Dictionary<string, string>(), passwords);

        var seen = new List<long>();
        var changed = new SemaphoreSlim(0);
        connection.OnStatusUpdated = status =>
        {
            lock (seen)
                seen.Add(status.LastEventID);
            changed.Release();
            return Task.CompletedTask;
        };

        async Task WaitForEvent(long eventId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true)
            {
                lock (seen)
                    if (seen.Contains(eventId))
                        return;
                try { await changed.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) { Assert.Fail($"The tray did not get the state of event {eventId}"); }
            }
        }

        return (connection, WaitForEvent);
    }

    /// <summary>
    /// The case asked for: the tray gets each state the server sends on the websocket, and
    /// sends no long poll
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task TheTrayFollowsTheStateOverTheWebsocket()
    {
        using var server = new StandInServer();
        var (connection, waitForEvent) = await ConnectAsync(server);
        try
        {
            var socket = await server.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(20));

            await StandInServer.SendAsync(socket, State("Paused", "Paused", 5, true));
            await waitForEvent(5);
            Assert.That(connection.Status.ProgramState, Is.EqualTo(LiveControlState.Paused));

            // A later change comes in on the same connection
            await StandInServer.SendAsync(socket, State("Running", "Active", 6, true));
            await waitForEvent(6);
            Assert.That(connection.Status.ProgramState, Is.EqualTo(LiveControlState.Running));
            Assert.That(connection.Status.SuggestedStatusIcon, Is.EqualTo(SuggestedStatusIcon.Active));

            Assert.That(server.LongPolls, Is.EqualTo(0), "the tray should not long poll while it has the websocket");
            lock (server.MessagesReceived)
            {
                Assert.That(server.MessagesReceived, Has.Count.EqualTo(2));
                using var auth = JsonDocument.Parse(server.MessagesReceived[0]);
                Assert.That(auth.RootElement.GetProperty("Version").GetInt32(), Is.EqualTo(1));
                Assert.That(auth.RootElement.GetProperty("Token").GetString(), Is.EqualTo("token1"), "the tray should authenticate with the token it logged in for");
                using var subscription = JsonDocument.Parse(server.MessagesReceived[1]);
                Assert.That(subscription.RootElement.GetProperty("Action").GetString(), Is.EqualTo("sub"));
                Assert.That(subscription.RootElement.GetProperty("Service").GetString(), Is.EqualTo("legacystatus"));
            }
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// A state that comes in on the websocket while a backup runs gives the tooltip the name of
    /// the backup, as a state from a request does
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task TheTrayReadsTheNameOfTheBackupThatRunsOverTheWebsocket()
    {
        using var server = new StandInServer();
        var (connection, waitForEvent) = await ConnectAsync(server);
        try
        {
            var socket = await server.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(20));

            await StandInServer.SendAsync(socket, "{\"ActiveTask\":{\"Item1\":7,\"Item2\":\"3\"},\"ProgramState\":\"Running\",\"SuggestedStatusIcon\":\"Active\",\"LastEventID\":5,\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0,\"Type\":\"legacystatus\"}");
            await waitForEvent(5);

            Assert.That(connection.Status.HasActiveTask, Is.True);
            Assert.That(connection.Status.ActiveBackupName, Is.EqualTo("Documents"), "the tray should read the name of the running backup for a state from the websocket");
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// When the server does not accept the token on the websocket, the tray logs in again and
    /// connects with the new token
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task ARefusedTokenIsRenewedForTheWebsocket()
    {
        using var server = new StandInServer();
        server.RefuseWebsockets = 1;
        var (connection, waitForEvent) = await ConnectAsync(server);
        try
        {
            var socket = await server.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await StandInServer.SendAsync(socket, State("Paused", "Paused", 5, true));
            await waitForEvent(5);

            Assert.That(server.Logins, Is.EqualTo(2), "the tray should log in again once");
            lock (server.MessagesReceived)
            {
                using var auth = JsonDocument.Parse(server.MessagesReceived[1]);
                Assert.That(auth.RootElement.GetProperty("Token").GetString(), Is.EqualTo("token2"));
            }
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Green before and after: a server without the websocket still gets long polled
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task WithoutTheWebsocketTheTrayLongPolls()
    {
        using var server = new StandInServer();
        server.NoWebsocket = true;
        var (connection, waitForEvent) = await ConnectAsync(server);
        try
        {
            await waitForEvent(2);
            Assert.That(connection.Status.ProgramState, Is.EqualTo(LiveControlState.Paused));
            Assert.That(server.LongPolls, Is.GreaterThanOrEqualTo(1));
        }
        finally
        {
            connection.Close();
        }
    }
}
