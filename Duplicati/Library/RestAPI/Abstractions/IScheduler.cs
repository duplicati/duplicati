using System;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Abstractions;

public interface IScheduler
{
    /// <summary>
    /// Initializes scheduler
    /// </summary>
    /// <param name="worker">The worker thread</param>
    void Init(WorkerThread<Runner.IRunnerData> worker);

    /// <summary>
    /// Gets the current ids in the scheduler queue
    /// </summary>
    IList<Tuple<long, string>> GetSchedulerQueueIds();

    /// <summary>
    /// Gets the current proposed schedule
    /// </summary>
    IList<Tuple<string, DateTime>> GetProposedSchedule();

    /// <summary>
    /// Terminates the thread. Any items still in queue will be removed
    /// </summary>
    /// <param name="wait">True if the call should block until the thread has exited, false otherwise</param>
    void Terminate(bool wait);

    /// <summary>
    /// Subscribes to the event that is triggered when the schedule changes
    /// </summary>
    void SubScribeToNewSchedule(Action handler);

    /// <summary>
    /// A snapshot copy of the current schedule list
    /// </summary>
    List<KeyValuePair<DateTime, ISchedule>> Schedule { get; }

    /// <summary>
    /// A snapshot copy of the current worker queue, that is items that are scheduled, but waiting for execution
    /// </summary>
    List<Runner.IRunnerData> WorkerQueue { get; }

    /// <summary>
    /// Forces the scheduler to re-evaluate the order. 
    /// Call this method if something changes
    /// </summary>
    void Reschedule();
}