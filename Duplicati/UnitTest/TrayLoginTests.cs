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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.GUI.TrayIcon;
using Duplicati.Server.Serialization;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The tray icon logs in with its password to get an access token, when it has none, and
/// again when the server refuses the one it has. Requests running at the same time each
/// logged in: at start, the long poll and the plain status request both did.
/// </summary>
[NonParallelizable]
public class TrayLoginTests
{
    /// <summary>
    /// A stand-in for the server, which hands out numbered tokens and can refuse the ones it
    /// handed out before
    /// </summary>
    private sealed class StandInServer : IDisposable
    {
        private readonly HttpListener m_listener = new();
        private readonly Task m_loop;
        private readonly List<HttpListenerContext> m_held = new();
        private int m_logins;

        /// <summary>The address to give the tray</summary>
        public string Prefix { get; }

        /// <summary>The number of logins so far</summary>
        public int Logins => Volatile.Read(ref m_logins);

        /// <summary>Tokens below this number are refused</summary>
        public volatile int OldestAccepted = 1;

        /// <summary>If set, the login is refused, as with a wrong password</summary>
        public volatile bool RefuseLogin;

        /// <summary>If set, refused status requests are answered only once this many are waiting</summary>
        public volatile int RefuseTogether = 1;

        /// <summary>The tokens the status requests came with</summary>
        public readonly List<string?> TokensSeen = new();

        private readonly List<HttpListenerContext> m_refused = new();

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

        private async Task HandleAsync(HttpListenerContext context)
        {
            var path = context.Request.Url!.AbsolutePath;
            var query = context.Request.Url.Query;
            var auth = context.Request.Headers["Authorization"];

            if (path.EndsWith("/auth/login"))
            {
                if (RefuseLogin)
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }
                var number = Interlocked.Increment(ref m_logins);
                await ReplyAsync(context, $"{{\"AccessToken\":\"token{number}\"}}");
                return;
            }

            if (path.EndsWith("/serverstate") && query.Contains("longpoll=true"))
            {
                // The long poll is kept waiting, so it takes no part
                lock (m_held)
                    m_held.Add(context);
                return;
            }

            if (path.EndsWith("/serverstate"))
            {
                lock (TokensSeen)
                    TokensSeen.Add(auth);

                var token = auth?.StartsWith("Bearer token") == true ? int.Parse(auth.Substring("Bearer token".Length)) : 0;
                if (token < OldestAccepted)
                {
                    // Refused requests are answered together, so they are all refused before
                    // any of them gets a new token
                    List<HttpListenerContext>? all = null;
                    lock (m_refused)
                    {
                        m_refused.Add(context);
                        if (m_refused.Count >= RefuseTogether)
                        {
                            all = m_refused.ToList();
                            m_refused.Clear();
                        }
                    }
                    if (all != null)
                        foreach (var c in all)
                        {
                            c.Response.StatusCode = 401;
                            c.Response.Close();
                        }
                    return;
                }

                await ReplyAsync(context, "{\"ProgramState\":\"Running\",\"SuggestedStatusIcon\":\"Ready\",\"LastEventID\":1,\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0}");
                return;
            }

            await ReplyAsync(context, path.EndsWith("/notifications") ? "[]" : "{}");
        }

        public void Dispose()
        {
            lock (m_held)
                foreach (var c in m_held)
                    try { c.Response.Abort(); } catch { }
            m_listener.Stop();
            m_loop.Wait(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// Connects the tray to the stand-in, with a password
    /// </summary>
    private static async Task<HttpServerConnection> ConnectAsync(StandInServer server)
    {
        var passwords = await PasswordStorageHelper.CreateAsync(server.Prefix, true, "password", Program.PasswordSource.SuppliedPassword, new Dictionary<string, string?>());
        return new HttpServerConnection(null, Program.PasswordSource.SuppliedPassword, false, "", false, new Dictionary<string, string>(), passwords);
    }

    /// <summary>
    /// The case found: at start, the long poll and the plain status request each logged in
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task RequestsAtStartShareOneLogin()
    {
        using var server = new StandInServer();
        var connection = await ConnectAsync(server);
        try
        {
            // The connection has started its long poll; the plain requests go next to it
            await Task.WhenAll(connection.UpdateStatusAsync(), connection.UpdateStatusAsync()).WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);

            Assert.That(server.Logins, Is.EqualTo(1), "the requests should share one login");
            lock (server.TokensSeen)
                Assert.That(server.TokensSeen, Is.All.EqualTo("Bearer token1"));
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// The same when the server refuses the token the requests came with: one of them logs in
    /// again, and the others use its token
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task RequestsRefusedTogetherShareOneNewLogin()
    {
        using var server = new StandInServer();
        var connection = await ConnectAsync(server);
        try
        {
            await connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var loginsBefore = server.Logins;

            // The token runs out, and two requests are refused with it at the same time
            server.OldestAccepted = loginsBefore + 1;
            server.RefuseTogether = 2;
            await Task.WhenAll(connection.UpdateStatusAsync(), connection.UpdateStatusAsync()).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(server.Logins - loginsBefore, Is.EqualTo(1), "the refused requests should share one new login");
            lock (server.TokensSeen)
                Assert.That(server.TokensSeen.Last(), Is.EqualTo($"Bearer token{loginsBefore + 1}"));
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Green before and after: a refused token is renewed once, and a request refused again
    /// with the new one fails, rather than trying for ever
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task ARequestRefusedAgainAfterANewLoginFails()
    {
        using var server = new StandInServer();
        var connection = await ConnectAsync(server);
        try
        {
            await connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var loginsBefore = server.Logins;

            server.OldestAccepted = int.MaxValue;
            var ex = Assert.ThrowsAsync<HttpRequestException>(() => connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30)));

            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(server.Logins - loginsBefore, Is.EqualTo(1), "the token should be renewed once");
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Green before and after: with a wrong password the login is refused, and the request
    /// fails
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task AWrongPasswordFails()
    {
        using var server = new StandInServer();
        server.RefuseLogin = true;
        var connection = await ConnectAsync(server);
        try
        {
            var ex = Assert.ThrowsAsync<HttpRequestException>(() => connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30)));

            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(server.Logins, Is.EqualTo(0));
        }
        finally
        {
            connection.Close();
        }
    }
}
