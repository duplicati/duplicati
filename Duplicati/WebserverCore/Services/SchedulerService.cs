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
