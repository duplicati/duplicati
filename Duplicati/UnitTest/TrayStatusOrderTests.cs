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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.GUI.TrayIcon;
using Duplicati.Server.Serialization;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// At start and on reconnect, the tray icon asks for the server state with a plain request,
/// next to the long poll it keeps running. Each answer replaced the state the tray holds, in
/// the order the answers arrived. A plain answer that was older than a long poll answer that
/// arrived first put the old state back, and the long poll, already waiting for the event
/// after the newer one, left it there until the next change or its timeout of five minutes.
/// </summary>
public class TrayStatusOrderTests
{
    /// <summary>
    /// A server state, as the server sends it
    /// </summary>
    private static string State(string programState, string icon, long eventId)
        => $"{{\"ProgramState\":\"{programState}\",\"SuggestedStatusIcon\":\"{icon}\",\"LastEventID\":{eventId},\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0}}";

    /// <summary>
    /// Sends a JSON answer
    /// </summary>
    private static async Task ReplyAsync(HttpListenerContext context, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    [Test]
    [Category("Server")]
    public async Task AnOlderPlainAnswerDoesNotReplaceANewerLongPollAnswer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var nextLongPollWaiting = new TaskCompletionSource();
        var plainAnswered = new TaskCompletionSource();
        var waiting = new List<HttpListenerContext>();

        var server = Task.Run(async () =>
        {
            while (true)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch { return; }

                var path = context.Request.Url!.AbsolutePath;
                var query = context.Request.Url.Query;
                _ = Task.Run(async () =>
                {
                    if (path.EndsWith("/auth/login"))
                        await ReplyAsync(context, "{\"AccessToken\":\"token\"}");
                    else if (path.EndsWith("/serverstate") && query.Contains("longpoll=true") && query.Contains("lastEventId=0"))
                    {
                        // The long poll sees the state after a change: paused, event 2
                        await ReplyAsync(context, State("Paused", "Paused", 2));
                    }
                    else if (path.EndsWith("/serverstate") && query.Contains("longpoll=true"))
                    {
                        // Nothing changes after that, so the next long poll waits
                        lock (waiting)
                            waiting.Add(context);
                        nextLongPollWaiting.TrySetResult();
                    }
                    else if (path.EndsWith("/serverstate"))
                    {
                        // The plain request got the state from before the change, and its
                        // answer arrives after the long poll's. The next long poll only comes
                        // once the tray has taken the long poll's answer.
                        await nextLongPollWaiting.Task;
                        await ReplyAsync(context, State("Running", "Ready", 1));
                        plainAnswered.TrySetResult();
                    }
                    else if (path.EndsWith("/notifications"))
                        await ReplyAsync(context, "[]");
                    else
                        await ReplyAsync(context, "{}");
                });
            }
        });

        var passwords = await PasswordStorageHelper.CreateAsync(prefix, true, "password", Program.PasswordSource.SuppliedPassword, new Dictionary<string, string?>());
        var connection = new HttpServerConnection(null, Program.PasswordSource.SuppliedPassword, false, "", false, new Dictionary<string, string>(), passwords);
        try
        {
            // As at start: a plain request next to the long poll the connection started
            await connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await Task.WhenAll(plainAnswered.Task, nextLongPollWaiting.Task).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(connection.Status.ProgramState, Is.EqualTo(LiveControlState.Paused), "the tray should keep the newer state");
            Assert.That(connection.Status.LastEventID, Is.EqualTo(2));
        }
        finally
        {
            connection.Close();
            lock (waiting)
                foreach (var context in waiting)
                    try { context.Response.Abort(); } catch { }
            listener.Stop();
            await server;
        }
    }
}
