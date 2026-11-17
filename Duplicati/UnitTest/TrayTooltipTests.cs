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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Duplicati.GUI.TrayIcon;
using Duplicati.Server.Serialization;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The tray icon tooltip showed only the application name, so seeing which backup was
/// running, or that the last run failed, needed the web UI (issue #1382).
/// </summary>
public class TrayTooltipTests
{
    private sealed record Status(
        LiveControlState ProgramState,
        SuggestedStatusIcon SuggestedStatusIcon,
        bool HasActiveTask = false,
        string? ActiveBackupName = null,
        long LastEventID = 0,
        long LastDataUpdateID = 0,
        long LastNotificationUpdateID = 0) : IServerStatus;

    private sealed class MenuItem : IMenuItem
    {
        public void SetDefault(bool isDefault) { }
        public void SetIcon(MenuIcons icon) { }
        public void SetText(string text) { }
        public void SetEnabled(bool enabled) { }
        public void SetHidden(bool hidden) { }
    }

    /// <summary>
    /// A tray icon that records the tooltips it is given
    /// </summary>
    private sealed class RecordingTrayIcon : TrayIconBase
    {
        public readonly List<string> Tooltips = new();

        public RecordingTrayIcon() => BuildMenu();

        public Task UpdateAsync(IServerStatus status) => OnStatusUpdatedAsync(status);

        protected override void SetTooltip(string text) => Tooltips.Add(text);
        protected override void Run(string[] args) { }
        protected override IMenuItem CreateMenuItem(string text, MenuIcons icon, Action callback, IList<IMenuItem> subitems) => new MenuItem();
        protected override void Exit() { }
        protected override void SetIcon(TrayIcons icon) { }
        protected override void SetMenu(IEnumerable<IMenuItem> items) { }
        public override void NotifyUser(string title, string message, GUI.TrayIcon.NotificationType type) { }
        public override void Dispose() { }
    }

    [Test]
    [Category("Server")]
    public async Task AStatusUpdateSetsTheTooltipToTheRunningBackup()
    {
        using var tray = new RecordingTrayIcon();

        await tray.UpdateAsync(new Status(LiveControlState.Running, SuggestedStatusIcon.Active, true, "Documents"));
        await tray.UpdateAsync(new Status(LiveControlState.Running, SuggestedStatusIcon.ReadyError));

        var app = Library.AutoUpdater.AutoUpdateSettings.AppName;
        Assert.That(tray.Tooltips, Is.EqualTo(new[] {
            $"{app}: running \"Documents\"",
            $"{app}: the last run had errors"
        }));
    }

    [Test]
    [Category("Server")]
    public void TheTooltipDescribesTheState()
    {
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Running, SuggestedStatusIcon.Ready)),
            Is.EqualTo("Duplicati"));
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Paused, SuggestedStatusIcon.Paused)),
            Is.EqualTo("Duplicati: paused"));
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Paused, SuggestedStatusIcon.Disconnected)),
            Is.EqualTo("Duplicati: not connected"));
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Running, SuggestedStatusIcon.ReadyWarning)),
            Is.EqualTo("Duplicati: the last run had warnings"));
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Running, SuggestedStatusIcon.Active, true)),
            Is.EqualTo("Duplicati: running a task"));
        Assert.That(TrayTooltip.Build("Duplicati", new Status(LiveControlState.Paused, SuggestedStatusIcon.ActivePaused, true, "Photos")),
            Is.EqualTo("Duplicati: paused while running \"Photos\""));
    }

    [Test]
    [Category("Server")]
    public void ALongBackupNameIsShortenedToFitTheTooltip()
    {
        var text = TrayTooltip.Build("Duplicati", new Status(LiveControlState.Running, SuggestedStatusIcon.Active, true, new string('x', 300)));

        Assert.That(text.Length, Is.LessThanOrEqualTo(TrayTooltip.MaxLength));
        Assert.That(text, Does.StartWith("Duplicati: running \"xxx"));
        Assert.That(text, Does.EndWith("...\""));
    }

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
    public async Task TheConnectionReadsTheNameOfTheRunningBackup()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var requested = new ConcurrentQueue<string>();
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
                requested.Enqueue(path);
                _ = Task.Run(async () =>
                {
                    if (path.EndsWith("/auth/login"))
                        await ReplyAsync(context, "{\"AccessToken\":\"token\"}");
                    else if (path.EndsWith("/serverstate") && query.Contains("longpoll=true"))
                    {
                        // The long poll waits, so only the plain request sets the state
                        lock (waiting)
                            waiting.Add(context);
                    }
                    else if (path.EndsWith("/serverstate"))
                        await ReplyAsync(context, "{\"ActiveTask\":{\"Item1\":7,\"Item2\":\"3\"},\"ProgramState\":\"Running\",\"SuggestedStatusIcon\":\"Active\",\"LastEventID\":1,\"LastDataUpdateID\":0,\"LastNotificationUpdateID\":0}");
                    else if (path.EndsWith("/backup/3"))
                        await ReplyAsync(context, "{\"Schedule\":null,\"Backup\":{\"ID\":\"3\",\"Name\":\"Documents\",\"TargetURL\":\"file:///tmp\"}}");
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
            await connection.UpdateStatusAsync().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(requested, Has.Member("/api/v1/backup/3"), "the tray should ask for the running backup");
            Assert.That(connection.Status.HasActiveTask, Is.True);
            Assert.That(connection.Status.ActiveBackupName, Is.EqualTo("Documents"));
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
