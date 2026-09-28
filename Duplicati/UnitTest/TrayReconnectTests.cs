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
using System.Text;
using System.Threading.Tasks;
using Duplicati.GUI.TrayIcon;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The tray icon sends pause and resume through a queue that its connection reads. The queue
/// was a named channel, and a named channel is the same object for everyone asking for that
/// name in the process. Closing a connection retires its queue, so a connection made after it
/// in the same process got a queue that was already retired: its pause and resume went
/// nowhere, with no error.
/// </summary>
[NonParallelizable]
public class TrayReconnectTests
{
    /// <summary>
    /// A stand-in for the server, which records the pause and resume requests it gets
    /// </summary>
    private sealed class StandInServer : IDisposable
    {
        private readonly HttpListener m_listener = new();
        private readonly Task m_loop;
        private readonly List<HttpListenerContext> m_held = new();

        /// <summary>The address to give the tray</summary>
        public string Prefix { get; }

        /// <summary>Completes when a pause request comes in</summary>
        public TaskCompletionSource PauseReceived { get; private set; } = new();

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

        /// <summary>Starts waiting for the next pause request</summary>
        public void ExpectPause() => PauseReceived = new TaskCompletionSource();

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

            if (path.EndsWith("/auth/login"))
                await ReplyAsync(context, "{\"AccessToken\":\"token\"}");
            else if (path.EndsWith("/serverstate/pause"))
            {
                PauseReceived.TrySetResult();
                await ReplyAsync(context, "{}");
            }
            else if (path.EndsWith("/serverstate") && query.Contains("longpoll=true"))
            {
                // The long poll is kept waiting, so it takes no part
                lock (m_held)
                    m_held.Add(context);
            }
            else if (path.EndsWith("/serverstate"))
                await ReplyAsync(context, "{\"ProgramState\":\"Running\",\"SuggestedStatusIcon\":\"Ready\",\"LastEventID\":1,\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0}");
            else
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
        var connection = new HttpServerConnection(null, Program.PasswordSource.SuppliedPassword, false, "", false, new Dictionary<string, string>(), passwords);
        await connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return connection;
    }

    /// <summary>
    /// The case found: a pause from a connection made after an earlier one was closed
    /// </summary>
    [Test]
    [Category("Server")]
    public async Task APauseFromAConnectionMadeAfterAnotherWasClosedReachesTheServer()
    {
        using var server = new StandInServer();

        var first = await ConnectAsync(server);
        server.ExpectPause();
        first.Pause();
        Assert.That(await Task.WhenAny(server.PauseReceived.Task, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(server.PauseReceived.Task), "the first connection should send the pause");
        first.Close();

        var second = await ConnectAsync(server);
        var closedWhileOpen = false;
        second.ConnectionClosed = () => closedWhileOpen = true;
        try
        {
            server.ExpectPause();
            second.Pause();
            var arrived = await Task.WhenAny(server.PauseReceived.Task, Task.Delay(TimeSpan.FromSeconds(10))) == server.PauseReceived.Task;

            Assert.That(arrived, Is.True, "the pause from the second connection should reach the server");
            Assert.That(closedWhileOpen, Is.False, "the second connection should not report itself closed while open");
        }
        finally
        {
            second.ConnectionClosed = null;
            second.Close();
        }
    }
}
