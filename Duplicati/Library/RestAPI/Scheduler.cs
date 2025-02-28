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

using Duplicati.Server.Serialization.Interface;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;
using Duplicati.Library.RestAPI;

// TODO: Rewrite this class.
// It should just signal what new backups to run, and not mix with the worker thread.

namespace Duplicati.Server
{
    /// <summary>
    /// This class handles scheduled runs of backups
    /// </summary>
    public class Scheduler
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Scheduler>();

        /// <summary>
        /// The thread that runs the scheduler
        /// </summary>
        private Thread m_thread;

        /// <summary>
        /// A termination flag
        /// </summary>
        private volatile bool m_terminate;

        /// <summary>
        /// The worker thread that is invoked to do work
        /// </summary>
        private WorkerThread<Runner.IRunnerData> m_worker;

        /// <summary>
        /// The wait event
        /// </summary>
        private AutoResetEvent m_event;

        /// <summary>
        /// The data synchronization lock
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// An event that is raised when the schedule changes
        /// </summary>
        public event EventHandler NewSchedule;

        /// <summary>
        /// The currently scheduled items
        /// </summary>
        private KeyValuePair<DateTime, ISchedule>[] m_schedule;

        /// <summary>
        /// List of update tasks, used to set the timestamp on the schedule once completed
        /// </summary>
        private Dictionary<Server.Runner.IRunnerData, Tuple<ISchedule, DateTime, DateTime>> m_updateTasks;

        /// <summary>
        /// Constructs a new scheduler
        /// </summary>
        public Scheduler()
        {
        }

        /// <summary>
        /// Initializes scheduler
        /// </summary>
        /// <param name="worker">The worker thread</param>
        public void Init(WorkerThread<Runner.IRunnerData> worker)
        {
            m_worker = worker;
            m_thread = new Thread(new ThreadStart(Runner));
            m_worker.CompletedWork += OnCompleted;
            m_worker.StartingWork += OnStartingWork;
            m_schedule = new KeyValuePair<DateTime, ISchedule>[0];
            m_terminate = false;
            m_event = new AutoResetEvent(false);
            m_updateTasks = new Dictionary<Server.Runner.IRunnerData, Tuple<ISchedule, DateTime, DateTime>>();
            m_thread.IsBackground = true;
            m_thread.Name = "TaskScheduler";
            m_thread.Start();
        }

        public IList<Tuple<long, string>> GetSchedulerQueueIds()
        {
            return (from n in WorkerQueue
                    where n.Backup != null
                    select new Tuple<long, string>(n.TaskID, n.Backup.ID)).ToList();
        }

        public IList<Tuple<string, DateTime>> GetProposedSchedule()
        {
            return (
                from n in FIXMEGlobal.Scheduler.Schedule
                let backupid = (from t in n.Value.Tags
                                where t != null && t.StartsWith("ID=", StringComparison.Ordinal)
                                select t.Substring("ID=".Length)).FirstOrDefault()
                where !string.IsNullOrWhiteSpace(backupid)
                select new Tuple<string, DateTime>(backupid, n.Key)
            ).ToList();
        }

        /// <summary>
        /// Forces the scheduler to re-evaluate the order. 
        /// Call this method if something changes
        /// </summary>
        public void Reschedule()
        {
            m_event.Set();
        }

        /// <summary>
        /// A snapshot copy of the current schedule list
        /// </summary>
        public List<KeyValuePair<DateTime, ISchedule>> Schedule
        {
            get
            {
                lock (m_lock)
                    return m_schedule.ToList();
            }
        }

        /// <summary>
        /// A snapshot copy of the current worker queue, that is items that are scheduled, but waiting for execution
        /// </summary>
        public List<Runner.IRunnerData> WorkerQueue
        {
            get { return m_worker?.CurrentTasks?.Where(t => t != null)?.ToList() ?? []; }
        }

        /// <summary>
        /// Terminates the thread. Any items still in queue will be removed
        /// </summary>
        /// <param name="wait">True if the call should block until the thread has exited, false otherwise</param>
        public void Terminate(bool wait)
        {
            m_terminate = true;
            m_event.Set();

            if (wait)
            {
                try
                {
                    m_thread.Join();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Returns the next valid date, given the start and the interval
        /// </summary>
        /// <param name="basetime">The base time</param>
        /// <param name="firstdate">The first allowed date</param>
        /// <param name="repetition">The repetition interval</param>
        /// <param name="allowedDays">The days the backup is allowed to run</param>
        /// <returns>The next valid date, or throws an exception if no such date can be found</returns>
        public static DateTime GetNextValidTime(DateTime basetime, DateTime firstdate, string repetition,
            DayOfWeek[] allowedDays, TimeZoneInfo timeZoneInfo)
        {
            var res = basetime;
            var ts = Timeparser.ParseTimeSpan(repetition);

            // If we move in days or more, we keep the time of day across DST changes
            var keepTimeOfDay = ts.Hours == 0 && ts.Minutes == 0 && ts.Seconds == 0;

            var i = 50000;
            while (res < firstdate && i-- > 0)
                res = Timeparser.DSTAwareParseTimeInterval(repetition, res, timeZoneInfo, keepTimeOfDay);

            // If we arrived somewhere after the first allowed date
            if (res >= firstdate)
            {
                if (ts.TotalDays >= 1)
                {
                    // We jump in days, so we pick the first valid day after firstdate
                    for (var n = 0; n < 8; n++)
                        if (IsDateAllowed(res, allowedDays, timeZoneInfo))
                            break;
                        else
                            res = res.AddDays(1);
                }
                else
                {
                    // We jump less than a day, so we keep adding the repetition until
                    // we hit a valid day
                    i = 50000;
                    while (!IsDateAllowed(res, allowedDays, timeZoneInfo) && i-- > 0)
                        res = Timeparser.DSTAwareParseTimeInterval(repetition, res, timeZoneInfo, keepTimeOfDay);
                }
            }

            if (!IsDateAllowed(res, allowedDays, timeZoneInfo) || res < firstdate)
            {
                StringBuilder sb = new StringBuilder();
                if (allowedDays != null)
                    foreach (DayOfWeek w in allowedDays)
                    {
                        if (sb.Length != 0)
                            sb.Append(", ");
                        sb.Append(w.ToString());
                    }

                throw new Exception(Strings.Scheduler.InvalidTimeSetupError(basetime, repetition, sb.ToString()));
            }

            return res;
        }

        private void OnCompleted(WorkerThread<Runner.IRunnerData> worker, Runner.IRunnerData task)
        {
            Tuple<ISchedule, DateTime, DateTime> t = null;
            lock (m_lock)
            {
                if (task != null && m_updateTasks.TryGetValue(task, out t))
                    m_updateTasks.Remove(task);
            }

            if (t != null)
            {
                t.Item1.Time = t.Item2;
                t.Item1.LastRun = t.Item3;
                FIXMEGlobal.DataConnection.AddOrUpdateSchedule(t.Item1);
            }
        }

        private void OnStartingWork(WorkerThread<Runner.IRunnerData> worker, Runner.IRunnerData task)
        {
            if (task is null)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_updateTasks.TryGetValue(task, out Tuple<ISchedule, DateTime, DateTime> scheduleInfo))
                {
                    // Item2 is the scheduled start time (Time in the Schedule table).
                    // Item3 is the actual start time (LastRun in the Schedule table).
                    m_updateTasks[task] = Tuple.Create(scheduleInfo.Item1, scheduleInfo.Item2, DateTime.UtcNow);
                }
            }
        }

        /// <summary>
        /// The actual scheduling procedure
        /// </summary>
        private void Runner()
        {
            var scheduled = new Dictionary<long, KeyValuePair<long, DateTime>>();
            while (!m_terminate)
            {
                //TODO: As this is executed repeatedly we should cache it
                // to avoid frequent db lookups

                //Determine schedule list
                var timeZoneInfo = FIXMEGlobal.DataConnection.ApplicationSettings.Timezone;
                var lst = FIXMEGlobal.DataConnection.Schedules;
                foreach (var sc in lst)
                {
                    if (!string.IsNullOrEmpty(sc.Repeat))
                    {
                        KeyValuePair<long, DateTime> startkey;

                        DateTime last = new DateTime(0, DateTimeKind.Utc);
                        DateTime start;
                        var scticks = sc.Time.Ticks;

                        if (!scheduled.TryGetValue(sc.ID, out startkey) || startkey.Key != scticks)
                        {
                            start = new DateTime(scticks, DateTimeKind.Utc);
                            last = sc.LastRun;
                        }
                        else
                        {
                            start = startkey.Value;
                        }

                        try
                        {
                            // Recover from timedrift issues by overriding the dates if the last run date is in the future.
                            if (last > DateTime.UtcNow)
                            {
                                start = DateTime.UtcNow;
                                last = DateTime.UtcNow;
                            }

                            start = GetNextValidTime(start, last, sc.Repeat, sc.AllowedDays, timeZoneInfo);
                        }
                        catch (Exception ex)
                        {
                            FIXMEGlobal.DataConnection.LogError(sc.ID.ToString(), "Scheduler failed to find next date",
                                ex);
                        }

                        //If time is exceeded, run it now
                        if (start <= DateTime.UtcNow)
                        {
                            var jobsToRun = new List<Server.Runner.IRunnerData>();
                            //TODO: Cache this to avoid frequent lookups
                            foreach (var id in FIXMEGlobal.DataConnection.GetBackupIDsForTags(sc.Tags).Distinct()
                                         .Select(x => x.ToString()))
                            {
                                //See if it is already queued
                                var tmplst = from n in m_worker.CurrentTasks
                                             where n.Operation == Duplicati.Server.Serialization.DuplicatiOperation.Backup
                                             select n.Backup;
                                var tastTemp = m_worker.CurrentTask;
                                if (tastTemp != null && tastTemp.Operation ==
                                    Duplicati.Server.Serialization.DuplicatiOperation.Backup)
                                    tmplst = tmplst.Union(new[] { tastTemp.Backup });

                                //If it is not already in queue, put it there
                                if (!tmplst.Any(x => x.ID == id))
                                {
                                    var entry = FIXMEGlobal.DataConnection.GetBackup(id);
                                    if (entry != null)
                                    {
                                        Dictionary<string, string> options = Duplicati.Server.Runner.GetCommonOptions();
                                        Duplicati.Server.Runner.ApplyOptions(entry, options);
                                        if ((new Duplicati.Library.Main.Options(options)).DisableOnBattery &&
                                            (Duplicati.Library.Utility.Power.PowerSupply.GetSource() ==
                                             Duplicati.Library.Utility.Power.PowerSupply.Source.Battery))
                                        {
                                            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG,
                                                "BackupDisabledOnBattery",
                                                "Scheduled backup disabled while on battery power.");
                                        }
                                        else
                                        {
                                            Dictionary<string, string> taskOptions = null;
                                            try
                                            {
                                                var nextRun = GetNextValidTime(start,
                                                    new DateTime(
                                                        Math.Max(DateTime.UtcNow.AddSeconds(1).Ticks, start.AddSeconds(1).Ticks),
                                                        DateTimeKind.Utc), sc.Repeat, sc.AllowedDays, timeZoneInfo);

                                                taskOptions = new Dictionary<string, string>()
                                                { { "next-scheduled-run", Utility.SerializeDateTime(nextRun.ToUniversalTime()) } };
                                            }
                                            catch
                                            {
                                            }

                                            jobsToRun.Add(Server.Runner.CreateTask(
                                                Serialization.DuplicatiOperation.Backup, entry, taskOptions));
                                        }
                                    }
                                }
                            }

                            // Calculate next time, by finding the first entry later than now
                            try
                            {
                                start = GetNextValidTime(start,
                                    new DateTime(
                                        Math.Max(DateTime.UtcNow.AddSeconds(1).Ticks, start.AddSeconds(1).Ticks),
                                        DateTimeKind.Utc), sc.Repeat, sc.AllowedDays, timeZoneInfo);
                            }
                            catch (Exception ex)
                            {
                                FIXMEGlobal.DataConnection.LogError(sc.ID.ToString(),
                                    "Scheduler failed to find next date", ex);
                                continue;
                            }

                            Server.Runner.IRunnerData lastJob = jobsToRun.LastOrDefault();
                            if (lastJob != null)
                            {
                                lock (m_lock)
                                {
                                    // The actual last run time will be updated when the StartingWork event is raised.
                                    m_updateTasks[lastJob] =
                                        new Tuple<ISchedule, DateTime, DateTime>(sc, start, DateTime.UtcNow);
                                }
                            }

                            foreach (var job in jobsToRun)
                                m_worker.AddTask(job);

                            if (start < DateTime.UtcNow)
                            {
                                //TODO: Report this somehow
                                continue;
                            }
                        }

                        scheduled[sc.ID] = new KeyValuePair<long, DateTime>(scticks, start);
                    }
                }

                var existing = lst.ToDictionary(x => x.ID);
                //Sort them, lock as we assign the m_schedule variable
                lock (m_lock)
                    m_schedule = (from n in scheduled
                                  where existing.ContainsKey(n.Key)
                                  orderby n.Value.Value
                                  select new KeyValuePair<DateTime, ISchedule>(n.Value.Value, existing[n.Key])).ToArray();

                // Remove unused entries                        
                foreach (var c in (from n in scheduled where !existing.ContainsKey(n.Key) select n.Key).ToArray())
                    scheduled.Remove(c);

                //Raise event if needed
                // TODO: This triggers a new data event and a reconnect with long-poll
                if (NewSchedule != null)
                    NewSchedule(this, null);

                int waittime = 0;

                //Figure out a sensible amount of time to sleep the thread
                if (scheduled.Count > 0)
                {
                    //When is the next run scheduled?
                    TimeSpan nextrun = scheduled.Values.Min((x) => x.Value) - DateTime.UtcNow;
                    if (nextrun.TotalMilliseconds < 0)
                        continue;

                    //Don't sleep for more than 5 minutes
                    waittime = (int)Math.Min(nextrun.TotalMilliseconds, 60 * 1000 * 5);
                }
                else
                {
                    // TODO: This should be handled with events, instead of one wakeup per minute
                    //No tasks, check back later
                    waittime = 60 * 1000;
                }

                //Waiting on the event, enables a wakeup call from termination
                // never use waittime = 0
                m_event.WaitOne(Math.Max(100, waittime), false);
            }
        }

        /// <summary>
        /// Returns true if the time is at an allowed weekday, false otherwise
        /// </summary>
        /// <param name="time">The time to evaluate</param>
        /// <param name="allowedDays">The allowed days</param>
        /// <param name="timeZoneInfo">The time zone info</param>
        /// <returns>True if the backup is allowed to run, false otherwise</returns>
        private static bool IsDateAllowed(DateTime time, DayOfWeek[] allowedDays, TimeZoneInfo timeZoneInfo)
        {
            if (allowedDays == null || allowedDays.Length == 0)
                return true;

            var localTime = TimeZoneInfo.ConvertTimeFromUtc(time, timeZoneInfo);
            return Array.IndexOf(allowedDays, localTime.DayOfWeek) >= 0;
        }
    }
}