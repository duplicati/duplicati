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

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The server pauses and resumes the backup queue from the state change events of
/// <see cref="LiveControls"/>, using the state each event carries. Some events were sent after
/// the lock was released, so a pause and a resume from two threads could arrive in the opposite
/// order to the one they happened in: the state was running, the queue was paused, and it
/// stayed paused, as nothing changed the state again.
/// </summary>
public class LiveControlsEventOrderTests
{
    private string _folder = null!;
    private Connection _connection = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"duplicati-live-controls-{Guid.NewGuid()}");
        Directory.CreateDirectory(_folder);
        var databasePath = Path.Combine(_folder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var db = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(db, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(db, true, null, _folder, () => { });
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        try { Directory.Delete(_folder, true); } catch { }
    }

    /// <summary>
    /// Pauses on one thread and resumes on another once the pause has sent its event. The
    /// pausing thread is held up before its event is handled, for as long as it takes the
    /// resume event to be handled, or at most a few seconds.
    /// </summary>
    /// <param name="liveControls">The controls to work on</param>
    /// <param name="pause">The pause to make</param>
    /// <returns>The state, and the state the queue was told last.</returns>
    private static (LiveControls.LiveControlState State, LiveControls.LiveControlState? Told) PauseAndResumeCrossing(LiveControls liveControls, Action pause)
    {
        LiveControls.LiveControlState? told = null;
        var gate = new object();
        var resumeHandled = new ManualResetEventSlim();
        var pauseEventArrived = new ManualResetEventSlim();
        var pausingThread = -1;

        liveControls.StateChanged = e =>
        {
            if (e.State == LiveControls.LiveControlState.Paused && Environment.CurrentManagedThreadId == pausingThread)
            {
                pauseEventArrived.Set();
                resumeHandled.Wait(TimeSpan.FromSeconds(3));
            }
            lock (gate)
                told = e.State;
            if (e.State == LiveControls.LiveControlState.Running)
                resumeHandled.Set();
        };

        var pauser = new Thread(() =>
        {
            pausingThread = Environment.CurrentManagedThreadId;
            pause();
        });
        pauser.Start();

        Assert.That(pauseEventArrived.Wait(TimeSpan.FromSeconds(10)), Is.True, "the pause did not send its event");

        var resumer = new Thread(liveControls.Resume);
        resumer.Start();

        Assert.That(pauser.Join(TimeSpan.FromSeconds(20)), Is.True, "the pause did not finish");
        Assert.That(resumer.Join(TimeSpan.FromSeconds(20)), Is.True, "the resume did not finish");

        lock (gate)
            return (liveControls.State, told);
    }

    /// <summary>
    /// The case found: a suspend pauses as a resume comes in, from the user or from a timed
    /// pause that runs out. The queue stayed paused with the state running, and waking up did
    /// not help, as there was nothing left to resume.
    /// </summary>
    [Test]
    [Category("Server")]
    public void TheQueueIsToldTheLastStateWhenASuspendAndAResumeCross()
    {
        var liveControls = new LiveControls(_connection);
        var onSuspend = typeof(LiveControls).GetMethod("OnSuspend", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var onResume = typeof(LiveControls).GetMethod("OnResume", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var (state, told) = PauseAndResumeCrossing(liveControls, () => onSuspend.Invoke(liveControls, null));

        Assert.That(told, Is.EqualTo(state), "the queue should be told the state the controls are in");

        onResume.Invoke(liveControls, null);
        Assert.That(liveControls.State, Is.EqualTo(LiveControls.LiveControlState.Running));
    }

    /// <summary>
    /// The same with a pause while paused, which only renews the pause and sends its event
    /// again
    /// </summary>
    [Test]
    [Category("Server")]
    public void TheQueueIsToldTheLastStateWhenARenewedPauseAndAResumeCross()
    {
        var liveControls = new LiveControls(_connection);
        liveControls.Pause(false);

        var (state, told) = PauseAndResumeCrossing(liveControls, () => liveControls.Pause(TimeSpan.FromHours(1), false));

        Assert.That(told, Is.EqualTo(state), "the queue should be told the state the controls are in");
    }

    /// <summary>
    /// Green before and after: a pause from the user already sends its event under the lock,
    /// so the resume waits for it
    /// </summary>
    [Test]
    [Category("Server")]
    public void TheQueueIsToldTheLastStateWhenAPauseAndAResumeCross()
    {
        var liveControls = new LiveControls(_connection);

        var (state, told) = PauseAndResumeCrossing(liveControls, () => liveControls.Pause(false));

        Assert.That(told, Is.EqualTo(state), "the queue should be told the state the controls are in");
    }
}
