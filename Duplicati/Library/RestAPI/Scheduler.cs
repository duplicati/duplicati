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

using Duplicati.Server.Serialization.Interface;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.Server.Database;

// TODO: Rewrite this class.
// It should just signal what new backups to run, and not mix with the worker thread.

namespace Duplicati.Server
{
    /// <summary>
    /// This class handles scheduled runs of backups
    /// </summary>
    public class Scheduler
    {
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<Scheduler>();

        /// <summary>
        /// The thread that runs the scheduler
        /// </summary>
        private Thread m_thread;

        /// <summary>
        /// A termination flag
        /// </summary>
        private volatile bool m_terminate;

        /// <summary>
        /// The wait event
        /// </summary>
        private AutoResetEvent m_event;

        /// <summary>
        /// The data synchronization lock
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// The queue runner service
        /// </summary>
        private readonly IQueueRunnerService m_queueRunnerService;
        /// <summary>
        /// The data connection
        /// </summary>
        private readonly Connection m_dataConnection;

        /// <summary>
        /// The currently scheduled items
        /// </summary>
        private KeyValuePair<DateTime, ISchedule>[] m_schedule;

        /// <summary>
        /// List of update tasks, used to set the timestamp on the schedule once completed
        /// </summary>
        private Dictionary<Runner.IRunnerData, Tuple<ISchedule, DateTime, DateTime>> m_updateTasks;

        /// <summary>
        /// Returns the current power source
        /// </summary>
        private readonly Func<Library.Utility.Power.PowerSupply.Source> m_getPowerSource;

        /// <summary>
        /// The IDs of the backups whose scheduled run was skipped on battery power,
        /// and that should run once the system is no longer on battery power.
        /// Only used by the scheduler thread.
        /// </summary>
        private readonly HashSet<string> m_skippedOnBattery = new HashSet<string>();

        /// <summary>
        /// Constructs a new scheduler
        /// </summary>
        /// <param name="connection">The data connection</param>
        /// <param name="queueRunnerService">The queue runner service</param>
        /// <param name="getPowerSource">Returns the current power source; defaults to <see cref="Library.Utility.Power.PowerSupply.GetSource"/></param>
        public Scheduler(Connection connection, IQueueRunnerService queueRunnerService, Func<Library.Utility.Power.PowerSupply.Source>? getPowerSource = null)
        {
            m_dataConnection = connection;
            m_queueRunnerService = queueRunnerService;
            m_getPowerSource = getPowerSource ?? Library.Utility.Power.PowerSupply.GetSource;
            m_thread = new Thread(new ThreadStart(Runner));
            m_schedule = [];
            m_terminate = false;
            m_event = new AutoResetEvent(false);
            m_updateTasks = new Dictionary<Runner.IRunnerData, Tuple<ISchedule, DateTime, DateTime>>();
            m_thread.IsBackground = true;
            m_thread.Name = "TaskScheduler";
        }

        public IList<Tuple<string, DateTime>> GetProposedSchedule()
        {
            // Disabled backups are skipped when their schedule fires, so they have no upcoming run
            var disabled = m_dataConnection.GetDisabledBackupIDs();
            return (
                from n in this.Schedule
                let backupid = (from t in n.Value.Tags
                                where t != null && t.StartsWith("ID=", StringComparison.Ordinal)
                                select t.Substring("ID=".Length)).FirstOrDefault()
                where !string.IsNullOrWhiteSpace(backupid) && !disabled.Contains(backupid)
                select new Tuple<string, DateTime>(backupid, n.Key)
            ).ToList();
        }

        /// <summary>
        /// Forces the scheduler to re-evaluate the order. 
        /// Call this method if something changes
        /// </summary>
        public void Reschedule()
        {
            if (!m_thread.IsAlive && !m_terminate)
                m_thread.Start();
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
                    return (m_schedule ?? []).ToList();
            }
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
            DayOfWeek[]? allowedDays, TimeZoneInfo timeZoneInfo)
        {
            var res = basetime;
            var ts = Timeparser.ParseTimeSpan(repetition);

            var i = 50000;
            while (res < firstdate && i-- > 0)
                res = Timeparser.DSTAwareParseTimeInterval(repetition, res, timeZoneInfo);

            // If we arrived somewhere after the first allowed date
            if (res >= firstdate)
            {
                if (ts.TotalDays >= 1)
                {
                    // We jump in days, so we pick the first valid day after firstdate.
                    // The skipped days must keep the local time of day, as daylight saving
                    // time changes on a Sunday, which a weekday-only schedule skips over
                    for (var n = 0; n < 8; n++)
                        if (IsDateAllowed(res, allowedDays, timeZoneInfo))
                            break;
                        else
                            res = Timeparser.DSTAwareParseTimeInterval("1D", res, timeZoneInfo);
                }
                else
                {
                    // We jump less than a day, so we keep adding the repetition until
                    // we hit a valid day
                    i = 50000;
                    while (!IsDateAllowed(res, allowedDays, timeZoneInfo) && i-- > 0)
                        res = Timeparser.DSTAwareParseTimeInterval(repetition, res, timeZoneInfo);
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

        /// <summary>
        /// Checks if a backup is still covered by a repeating schedule
        /// </summary>
        /// <param name="backupId">The ID of the backup</param>
        /// <returns><c>true</c> if a schedule still runs the backup</returns>
        private bool IsScheduled(string backupId)
            => m_dataConnection.Schedules
                .Where(sc => !string.IsNullOrEmpty(sc.Repeat))
                .Any(sc => m_dataConnection.GetBackupIDsForTags(sc.Tags).Any(id => id.ToString() == backupId));

        /// <summary>
        /// Checks if a scheduled run comes too soon after the last completed run of the backup,
        /// as limited by the min-time-between-scheduled-runs option
        /// </summary>
        /// <param name="backup">The backup to check</param>
        /// <param name="options">The options that apply to the backup</param>
        /// <param name="now">The current time, in UTC</param>
        /// <param name="lastFinished">The time the last completed run finished, if any</param>
        /// <returns><c>true</c> if the scheduled run should be skipped</returns>
        private static bool IsTooSoonAfterLastRun(IBackup backup, Library.Main.Options options, DateTime now, out DateTime lastFinished)
        {
            lastFinished = default;
            TimeSpan? minTime;
            try
            {
                minTime = options.MinTimeBetweenScheduledRuns;
            }
            catch
            {
                // An invalid value must not stop the scheduler; the run goes ahead and its option validation reports it.
                // The value is not logged, as option values can hold secrets.
                Library.Logging.Log.WriteWarningMessage(LOGTAG,
                    "InvalidMinTimeBetweenScheduledRuns", null,
                    "Ignoring the min-time-between-scheduled-runs option for backup {0} because its value is not a valid timespan.",
                    backup.Name);
                return false;
            }

            if (minTime == null || minTime.Value <= TimeSpan.Zero)
                return false;

            if (backup.Metadata == null
                || !backup.Metadata.TryGetValue(Serialization.LastRunMetadata.FinishedKey(backup.OperationType), out var value)
                || !Utility.TryDeserializeDateTime(value, out lastFinished))
                return false;

            return now - lastFinished.ToUniversalTime() < minTime.Value;
        }

        /// <summary>
        /// Thrown when a queued scheduled run is about to start, but its backup is no longer scheduled
        /// </summary>
        /// <param name="backupId">The ID of the backup</param>
        public sealed class RunNoLongerScheduledException(string backupId)
            : Exception($"The scheduled run of backup {backupId} was skipped, as the backup is no longer scheduled");

        private Task OnCompletedAsync(Runner.IRunnerData task)
        {
            Tuple<ISchedule, DateTime, DateTime>? t = null;
            lock (m_lock)
            {
                if (task != null && m_updateTasks.TryGetValue(task, out t))
                    m_updateTasks.Remove(task);
            }

            if (t != null)
            {
                t.Item1.Time = t.Item2;
                t.Item1.LastRun = t.Item3;
                m_dataConnection.AddOrUpdateSchedule(t.Item1);
            }

            return Task.CompletedTask;
        }

        private Task OnStartingWorkAsync(Runner.IRunnerData task)
        {
            if (task is null)
                return Task.CompletedTask;

            // A run can wait in the queue, as it does while the server is paused after a start;
            // if the schedule of the backup was removed meanwhile, the run is no longer wanted
            if (task.BackupID != null && !IsScheduled(task.BackupID))
            {
                lock (m_lock)
                    m_updateTasks.Remove(task);
                throw new RunNoLongerScheduledException(task.BackupID);
            }

            lock (m_lock)
            {
                if (m_updateTasks.TryGetValue(task, out var scheduleInfo))
                {
                    // Item2 is the scheduled start time (Time in the Schedule table).
                    // Item3 is the actual start time (LastRun in the Schedule table).
                    m_updateTasks[task] = Tuple.Create(scheduleInfo.Item1, scheduleInfo.Item2, DateTime.UtcNow);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates the task that runs a backup, unless the backup must not be queued now:
        /// it is already queued or running, it is disabled, it does not run on battery power, or,
        /// for a scheduled run, its last run finished too recently
        /// </summary>
        /// <param name="entry">The backup to run</param>
        /// <param name="taskOptions">The extra options for the run, or <c>null</c> if there are none</param>
        /// <param name="scheduledRun">If the run comes from the schedule of the backup, so --min-time-between-scheduled-runs applies
        /// and a run skipped on battery power is recorded for --run-skipped-on-ac-power</param>
        /// <returns>The task to queue, or <c>null</c> if the backup must not be queued</returns>
        private Runner.IRunnerData? TryCreateBackupTask(IBackup entry, Dictionary<string, string?>? taskOptions, bool scheduledRun = false)
        {
            //See if it is already queued
            if (GetQueuedBackupIDs().Any(x => x == entry.ID))
                return null;

            if (!entry.Enabled)
            {
                // The schedule still advances, so the backup resumes at its next slot once enabled
                Library.Logging.Log.WriteInformationMessage(LOGTAG,
                    "BackupDisabled",
                    "Skipping scheduled run of backup {0} because it is disabled.", entry.Name);
                return null;
            }

            var options = Server.Runner.GetCommonOptions(m_dataConnection);
            Server.Runner.ApplyOptions(m_dataConnection, entry, options, out var _);
            var parsedOptions = new Library.Main.Options(options);
            if (parsedOptions.DisableOnBattery &&
                (m_getPowerSource() ==
                 Library.Utility.Power.PowerSupply.Source.Battery))
            {
                Library.Logging.Log.WriteInformationMessage(LOGTAG,
                    "BackupDisabledOnBattery",
                    "Scheduled backup disabled while on battery power.");
                // The set is only used by the scheduler thread, so only scheduled runs are recorded
                if (scheduledRun && parsedOptions.RunSkippedOnAcPower)
                    m_skippedOnBattery.Add(entry.ID);
                return null;
            }

            if (scheduledRun && IsTooSoonAfterLastRun(entry, parsedOptions, DateTime.UtcNow, out var lastFinished))
            {
                // The schedule still advances, so the backup runs at the next slot that is far enough away
                Library.Logging.Log.WriteInformationMessage(LOGTAG,
                    "BackupSkippedTooSoonAfterLastRun",
                    "Skipping scheduled run of backup {0} because its last run finished at {1}, less than {2} ago.",
                    entry.Name, lastFinished, parsedOptions.MinTimeBetweenScheduledRuns);
                return null;
            }

            // This run replaces a run that was skipped on battery power
            if (scheduledRun)
                m_skippedOnBattery.Remove(entry.ID);

            return Server.Runner.CreateTask(Serialization.DuplicatiOperation.BackupOrSync, entry, taskOptions);
        }

        /// <summary>
        /// Queues the backups that are set to run after a backup, when a run of that backup has finished.
        /// This is called from the task queue between two tasks, so it never throws.
        /// </summary>
        /// <param name="result">The result of the task that finished</param>
        public void OnTaskCompleted(CachedTaskResult result)
        {
            try
            {
                // Only a backup run that took place can start other backups
                if (result.Operation != Serialization.DuplicatiOperation.BackupOrSync || result.BackupId == null || result.Skipped)
                    return;

                var succeeded = result.Exception == null
                    && result.Summary != null
                    && !result.Summary.Interrupted
                    && result.Summary.ParsedResult is Library.Interface.ParsedResultType.Success or Library.Interface.ParsedResultType.Warning;

                foreach (var id in m_dataConnection.GetBackupIDsRunningAfter(result.BackupId))
                {
                    var entry = m_dataConnection.GetBackup(id);
                    if (entry == null)
                        continue;

                    if (!succeeded && entry.RunAfterCondition != Serialization.RunAfterCondition.Always)
                    {
                        Library.Logging.Log.WriteInformationMessage(LOGTAG,
                            "RunAfterSkipped",
                            "Not running backup {0} after backup {1}, because that run did not complete successfully.", entry.Name, result.BackupId);
                        continue;
                    }

                    var job = TryCreateBackupTask(entry, null);
                    if (job == null)
                        continue;

                    Library.Logging.Log.WriteInformationMessage(LOGTAG,
                        "RunAfterQueued",
                        "Queueing backup {0} to run after backup {1}.", entry.Name, result.BackupId);
                    m_queueRunnerService.AddTask(job);
                }
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteErrorMessage(LOGTAG, "RunAfterFailed", ex,
                    "Failed to queue the backups that run after backup {0}", result.BackupId);
            }
        }

        /// <summary>
        /// Returns a fingerprint of the schedule fields that determine the next run time.
        /// The cached next-run computed by <see cref="Runner"/> is invalidated when this
        /// changes, so that editing the time, the repetition interval or the allowed
        /// weekdays all cause the next run to be recomputed.
        /// </summary>
        /// <param name="schedule">The schedule to compute the fingerprint for</param>
        /// <returns>The fingerprint of the schedule</returns>
        public static string GetScheduleFingerprint(ISchedule schedule)
        {
            // Fold the days into a bitmask so ordering and duplicates do not matter
            var daysMask = 0;
            if (schedule.AllowedDays != null)
                foreach (var d in schedule.AllowedDays)
                    daysMask |= 1 << (int)d;

            return FormattableString.Invariant($"{schedule.Time.Ticks}:{daysMask}:{schedule.Repeat}");
        }

        /// <summary>
        /// Returns the IDs of the backups that are queued or running
        /// </summary>
        /// <returns>The IDs of the queued or running backups</returns>
        private IEnumerable<string?> GetQueuedBackupIDs()
        {
            var tmplst = from n in m_queueRunnerService.GetCurrentTasks()
                         where n.Operation == Serialization.DuplicatiOperation.BackupOrSync
                         select n.BackupID;
            var tastTemp = m_queueRunnerService.GetCurrentTask();
            if (tastTemp != null && tastTemp.Operation ==
                Serialization.DuplicatiOperation.BackupOrSync)
                tmplst = tmplst.Union(new[] { tastTemp.BackupID });
            return tmplst;
        }

        /// <summary>
        /// Queues the backups whose scheduled run was skipped on battery power,
        /// once the system is no longer on battery power
        /// </summary>
        private void QueueRunsSkippedOnBattery()
        {
            if (m_skippedOnBattery.Count == 0 ||
                m_getPowerSource() == Library.Utility.Power.PowerSupply.Source.Battery)
                return;

            var queued = GetQueuedBackupIDs().ToHashSet();
            foreach (var id in m_skippedOnBattery.ToArray())
            {
                m_skippedOnBattery.Remove(id);

                // Only one instance of a backup is kept in the queue
                if (queued.Contains(id))
                    continue;

                // The backup is read again, so the run uses the current settings,
                // and is dropped if it was disabled, deleted or unscheduled meanwhile
                var entry = m_dataConnection.GetBackup(id);
                if (entry == null || !entry.Enabled || !IsScheduled(id))
                    continue;

                var options = Server.Runner.GetCommonOptions(m_dataConnection);
                Server.Runner.ApplyOptions(m_dataConnection, entry, options, out var _);
                var parsed = new Library.Main.Options(options);
                if (!parsed.DisableOnBattery || !parsed.RunSkippedOnAcPower)
                    continue;

                Library.Logging.Log.WriteInformationMessage(LOGTAG,
                    "RunningBackupSkippedOnBattery",
                    "Running scheduled backup {0} that was skipped on battery power.", entry.Name);

                var job = Server.Runner.CreateTask(Serialization.DuplicatiOperation.BackupOrSync, entry, null);
                job.OnStarting = () => OnStartingWorkAsync(job);
                m_queueRunnerService.AddTask(job);
            }
        }

        /// <summary>
        /// The actual scheduling procedure
        /// </summary>
        private void Runner()
        {
            var scheduled = new Dictionary<long, KeyValuePair<string, DateTime>>();
            while (!m_terminate)
            {
                //TODO: As this is executed repeatedly we should cache it
                // to avoid frequent db lookups

                //Determine schedule list
                var timeZoneInfo = m_dataConnection.ApplicationSettings.Timezone;
                var lst = m_dataConnection.Schedules;
                foreach (var sc in lst)
                {
                    if (!string.IsNullOrEmpty(sc.Repeat))
                    {
                        KeyValuePair<string, DateTime> startkey;

                        // The last run is applied on both paths so the timedrift recovery
                        // below also works on a cached entry; a last run in the future
                        // would otherwise keep the stale cached time until a restart
                        DateTime last = sc.LastRun;
                        DateTime start;
                        var scfingerprint = GetScheduleFingerprint(sc);

                        if (!scheduled.TryGetValue(sc.ID, out startkey) || startkey.Key != scfingerprint)
                        {
                            start = new DateTime(sc.Time.Ticks, DateTimeKind.Utc);
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
                            m_dataConnection.LogError(sc.ID.ToString(), "Scheduler failed to find next date",
                                ex);
                        }

                        //If time is exceeded, run it now
                        if (start <= DateTime.UtcNow)
                        {
                            var collectedJobs = new List<(DateTime When, Server.Runner.IRunnerData Job)>();
                            //TODO: Cache this to avoid frequent lookups
                            foreach (var id in m_dataConnection.GetBackupIDsForTags(sc.Tags).Distinct()
                                         .Select(x => x.ToString()))
                            {
                                var entry = m_dataConnection.GetBackup(id);
                                if (entry == null)
                                    continue;

                                Dictionary<string, string?>? taskOptions = null;
                                try
                                {
                                    var nextRun = GetNextValidTime(start,
                                        new DateTime(
                                            Math.Max(DateTime.UtcNow.AddSeconds(1).Ticks, start.AddSeconds(1).Ticks),
                                            DateTimeKind.Utc), sc.Repeat, sc.AllowedDays, timeZoneInfo);

                                    taskOptions = new Dictionary<string, string?>()
                                    { { "next-scheduled-run", Utility.SerializeDateTime(nextRun.ToUniversalTime()) } };
                                }
                                catch
                                {
                                }

                                var job = TryCreateBackupTask(entry, taskOptions, scheduledRun: true);
                                if (job != null)
                                {
                                    job.OnStarting = () => OnStartingWorkAsync(job);
                                    job.OnFinished = (_) => OnCompletedAsync(job);
                                    collectedJobs.Add((start, job));
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
                                m_dataConnection.LogError(sc.ID.ToString(),
                                    "Scheduler failed to find next date", ex);
                                continue;
                            }

                            var jobsToRun = collectedJobs.OrderBy(x => x.When)
                                                 .Select(x => x.Job)
                                                 .ToList();

                            var lastJob = jobsToRun.LastOrDefault();
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
                                m_queueRunnerService.AddTask(job);

                            if (start < DateTime.UtcNow)
                            {
                                //TODO: Report this somehow
                                continue;
                            }
                        }

                        scheduled[sc.ID] = new KeyValuePair<string, DateTime>(scfingerprint, start);
                    }
                }

                QueueRunsSkippedOnBattery();

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

                // Check the power source at least once a minute while a skipped run is waiting for it
                if (m_skippedOnBattery.Count > 0)
                    waittime = Math.Min(waittime, 60 * 1000);

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
        private static bool IsDateAllowed(DateTime time, DayOfWeek[]? allowedDays, TimeZoneInfo timeZoneInfo)
        {
            if (allowedDays == null || allowedDays.Length == 0)
                return true;

            var localTime = TimeZoneInfo.ConvertTimeFromUtc(time, timeZoneInfo);
            return Array.IndexOf(allowedDays, localTime.DayOfWeek) >= 0;
        }
    }
}