
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;

namespace WebserverCore.Services;

public class SchedulerService : IScheduler
{
    private readonly Duplicati.Server.Scheduler scheduler;
    public SchedulerService(Duplicati.Server.Scheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    public List<KeyValuePair<DateTime, ISchedule>> Schedule => scheduler.Schedule;

    public List<Runner.IRunnerData> WorkerQueue => scheduler.WorkerQueue;

    public void SubScribeToNewSchedule(Action handler)
        => scheduler.NewSchedule += (_, _) => handler();

    public IList<Tuple<long, string>> GetSchedulerQueueIds()
        => scheduler.GetSchedulerQueueIds();

    public IList<Tuple<string, DateTime>> GetProposedSchedule()
        => scheduler.GetProposedSchedule();

    public void Reschedule()
        => scheduler.Reschedule();

    public void Terminate(bool wait)
        => scheduler.Terminate(wait);

    public void Init(WorkerThread<Runner.IRunnerData> worker)
        => scheduler.Init(worker);
}
