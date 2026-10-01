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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Server;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using ServerProgram = Duplicati.Server.Program;

namespace Duplicati.UnitTest;

/// <summary>
/// The server pauses when the machine goes to sleep and resumes when it wakes up (#6867).
/// A client that follows the state with long polls, as the tray icon does, must end up with the
/// state the server is in.
/// </summary>
[NonParallelizable]
[Category("Targeted")]
public class SuspendResumeStateTests : BasicSetupHelper
{
    private sealed record State(string ProgramState, string SuggestedStatusIcon, long LastEventID);

    /// <summary>
    /// Follows the server state the way the tray icon does: a long poll from the last seen event
    /// </summary>
    private sealed class Follower
    {
        private readonly HttpClient m_client;
        private long m_lastEventId;
        public volatile State? Last;

        public Follower(HttpClient client) => m_client = client;

        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var json = await m_client.GetStringAsync($"/api/v1/serverstate?longpoll=true&lastEventId={m_lastEventId}&duration=10s", token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var state = new State(
                        root.GetProperty("ProgramState").GetString()!,
                        root.GetProperty("SuggestedStatusIcon").GetString()!,
                        root.GetProperty("LastEventID").GetInt64());
                    m_lastEventId = state.LastEventID;
                    Last = state;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Calls the suspend or resume handler, which the power mode provider calls in the server
    /// </summary>
    private static void Invoke(LiveControls liveControls, string method)
        => typeof(LiveControls).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(liveControls, null);

    /// <summary>
    /// Waits until the follower has seen the latest event of the server, and returns what it saw
    /// </summary>
    private static async Task<State> SettleAsync(Follower follower, HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/serverstate").ConfigureAwait(false));
        var latest = doc.RootElement.GetProperty("LastEventID").GetInt64();

        var end = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < end)
        {
            var last = follower.Last;
            if (last != null && last.LastEventID >= latest)
                return last;
            await Task.Delay(100).ConfigureAwait(false);
        }

        Assert.Fail($"The follower did not reach event {latest}, it has seen {follower.Last}");
        return null!;
    }

    private async Task WithServerAsync(Func<HttpClient, LiveControls, Task> body)
    {
        var dataFolder = Path.Combine(BASEFOLDER, "server-data");
        Directory.CreateDirectory(dataFolder);
        var previousDataFolderEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
        Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, dataFolder);

        var applicationSettings = new ApplicationSettings();
        Task<int>? serverTask = null;
        var started = false;
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var args = new[]
            {
                $"--{WebServerLoader.OPTION_PORT}={port}",
                $"--{WebServerLoader.OPTION_INTERFACE}=127.0.0.1",
                $"--{WebServerLoader.OPTION_WEBSERVICE_PASSWORD}=suspend-resume-test",
                $"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}={dataFolder}",
                "--webservice-api-only=true"
            };

            ServerProgram.ServerStartedEvent.Reset();
            var tcs = new TaskCompletionSource<int>();
            new Thread(() =>
            {
                try { tcs.TrySetResult(ServerProgram.Main(applicationSettings, args)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            })
            { IsBackground = true }.Start();
            serverTask = tcs.Task;

            if (!ServerProgram.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(60)))
                Assert.Fail("Server did not start");

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{ServerProgram.DuplicatiWebserver.Port}") };
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = "suspend-resume-test", rememberMe = true });
            login.EnsureSuccessStatusCode();
            using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginDoc.RootElement.GetProperty("AccessToken").GetString());

            // Debug builds keep the server data next to the test assembly, shared with other tests,
            // so start from the running state and leave it running
            (await client.PostAsync("/api/v1/serverstate/resume", null)).EnsureSuccessStatusCode();
            started = true;
            await body(client, ServerProgram.LiveControl);
        }
        finally
        {
            if (started)
                ServerProgram.LiveControl.Resume();
            applicationSettings.SignalApplicationExit();
            if (serverTask != null)
                try { await serverTask.WaitAsync(TimeSpan.FromSeconds(30)); } catch { }
            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, previousDataFolderEnv);
            ServerProgram.ServerStartedEvent.Reset();
        }
    }

    [Test]
    public Task SuspendThenResume_Async()
        => WithServerAsync(async (client, liveControls) =>
        {
            using var cts = new CancellationTokenSource();
            var follower = new Follower(client);
            var following = follower.RunAsync(cts.Token);

            Assert.That((await SettleAsync(follower, client)).ProgramState, Is.EqualTo("Running"));

            Invoke(liveControls, "OnSuspend");
            Assert.That((await SettleAsync(follower, client)).ProgramState, Is.EqualTo("Paused"), "The follower did not see the pause for the suspend");

            Invoke(liveControls, "OnResume");
            var last = await SettleAsync(follower, client);
            TestContext.Progress.WriteLine($"Server: {liveControls.State}, follower: {last}");
            Assert.That(liveControls.State, Is.EqualTo(LiveControls.LiveControlState.Running), "The server is not running after the resume");
            Assert.That(last.ProgramState, Is.EqualTo("Running"), "The follower still shows the server as paused");

            cts.Cancel();
            await following;
        });

    [Test]
    public Task ResumeWhileSuspendIsStillBeingHandled_Async()
        => WithServerAsync(async (client, liveControls) =>
        {
            using var cts = new CancellationTokenSource();
            var follower = new Follower(client);
            var following = follower.RunAsync(cts.Token);

            Assert.That((await SettleAsync(follower, client)).ProgramState, Is.EqualTo("Running"));

            // The suspend is handled while the machine goes to sleep; if that handling has not
            // finished when the machine is suspended, the resume can arrive before it has
            var original = liveControls.StateChanged;
            using var suspendHandled = new ManualResetEventSlim(false);
            using var suspendHandlingStarted = new ManualResetEventSlim(false);
            liveControls.StateChanged = e =>
            {
                if (e.State == LiveControls.LiveControlState.Paused && !suspendHandled.IsSet)
                {
                    suspendHandlingStarted.Set();
                    suspendHandled.Wait();
                }
                original(e);
            };

            var suspending = Task.Run(() => Invoke(liveControls, "OnSuspend"));
            Assert.That(suspendHandlingStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

            var resuming = Task.Run(() => Invoke(liveControls, "OnResume"));
            // Let the resume run into the suspend that has not been handled yet
            await Task.Delay(500);
            suspendHandled.Set();
            await suspending;
            await resuming;
            liveControls.StateChanged = original;

            var last = await SettleAsync(follower, client);
            TestContext.Progress.WriteLine($"Server: {liveControls.State}, follower: {last}");
            Assert.That(liveControls.State, Is.EqualTo(LiveControls.LiveControlState.Running), "The server is not running after the resume");
            Assert.That(last.ProgramState, Is.EqualTo("Running"), "The follower still shows the server as paused");

            cts.Cancel();
            await following;
        });
}
