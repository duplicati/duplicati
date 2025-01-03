#nullable enable
using System;
using Duplicati.Library.Utility;
using Duplicati.Server;

namespace Duplicati.Library.RestAPI.Abstractions;

public interface IWorkerThreadsManager
{
    void Spawn(Action<Runner.IRunnerData> item);

    Tuple<long, string>? CurrentTask { get; }
    WorkerThread<Runner.IRunnerData>? WorkerThread { get; }
    void UpdateThrottleSpeeds(string uploadSpeed, string downloadSpeed);

    long AddTask(Runner.IRunnerData data, bool skipQueue = false);
}