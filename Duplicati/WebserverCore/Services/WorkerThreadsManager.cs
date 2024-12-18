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

    public void UpdateThrottleSpeeds(string uploadSpeed, string downloadSpeed)
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