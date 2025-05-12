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

using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;

namespace WebserverCore.Services;

public class SchedulerService : ISchedulerService
{
    private readonly Scheduler scheduler;
    public SchedulerService(Connection connection, EventPollNotify eventPollNotify, INotificationUpdateService notificationUpdateService, IQueueRunnerService queueRunnerService)
    {
        this.scheduler = new Scheduler(connection, queueRunnerService);
        var lastScheduleId = notificationUpdateService.LastDataUpdateId;
        eventPollNotify.NewEvent += (sender, e) =>
        {
            if (lastScheduleId != notificationUpdateService.LastDataUpdateId)
            {
                lastScheduleId = notificationUpdateService.LastDataUpdateId;
                Reschedule();
            }
        };
    }

    public List<KeyValuePair<DateTime, ISchedule>> Schedule => scheduler.Schedule;

    public IList<Tuple<string, DateTime>> GetProposedSchedule()
        => scheduler.GetProposedSchedule();

    public void Reschedule()
        => scheduler.Reschedule();

    public void Terminate(bool wait)
        => scheduler.Terminate(wait);
}
