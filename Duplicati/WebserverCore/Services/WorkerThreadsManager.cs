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
using Duplicati.Library.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

public class WorkerThreadsManager(ILiveControls liveControls, IScheduler scheduler) : IWorkerThreadsManager
{
    public WorkerThread<Runner.IRunnerData>? WorkerThread { get; private set; }

    public void Spawn(Action<Runner.IRunnerData> item)
    {
        WorkerThread = new WorkerThread<Runner.IRunnerData>(item, liveControls.IsPaused);
        scheduler.Init(WorkerThread);
    }

    public Tuple<long, string>? CurrentTask
    {
        get
        {
            var t = WorkerThread?.CurrentTask;
            return t == null ? null : new Tuple<long, string>(t.TaskID, t.Backup.ID);
        }
    }

    public void UpdateThrottleSpeeds(string? uploadSpeed, string? downloadSpeed)
    {
        WorkerThread?.CurrentTask?.UpdateThrottleSpeed(uploadSpeed, downloadSpeed);
    }

    public long AddTask(Runner.IRunnerData data, bool skipQueue = false)
    {
        WorkerThread!.AddTask(data, skipQueue);
        FIXMEGlobal.StatusEventNotifyer.SignalNewEvent();
        return data.TaskID;
    }
}