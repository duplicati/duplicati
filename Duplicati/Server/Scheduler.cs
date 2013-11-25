#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
using Duplicati.Server.Serialization.Interface;


#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;

namespace Duplicati.Server
{
    /// <summary>
    /// This class handles scheduled runs of backups
    /// </summary>
    public class Scheduler
    {
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
        private WorkerThread<Tuple<long, Server.Serialization.DuplicatiOperation>> m_worker;
        /// <summary>
        /// The wait event
        /// </summary>
        private AutoResetEvent m_event;
        /// <summary>
        /// The data syncronization lock
        /// </summary>
        private object m_lock = new object();

        /// <summary>
        /// An event that is raised when the schedule changes
        /// </summary>
        public event EventHandler NewSchedule;

        /// <summary>
        /// The currently scheduled items
        /// </summary>
        private ISchedule[] m_schedule;

        /// <summary>
        /// Constructs a new scheduler
        /// </summary>
        /// <param name="connection">The database connection</param>
        /// <param name="worker">The worker thread</param>
        /// <param name="datalock">The database lock object</param>
        public Scheduler(WorkerThread<Tuple<long, Server.Serialization.DuplicatiOperation>> worker)
        {
            m_thread = new Thread(new ThreadStart(Runner));
            m_worker = worker;
            m_schedule = new ISchedule[0];
            m_terminate = false;
            m_event = new AutoResetEvent(false);
            m_thread.IsBackground = true;
            m_thread.Start();
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
        public List<ISchedule> Schedule 
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
        public List<Tuple<long, Duplicati.Server.Serialization.DuplicatiOperation>> WorkerQueue
        {
            get
            {
                return (from t in m_worker.CurrentTasks where t != null select t).ToList();
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
                m_thread.Join();
        }

        /// <summary>
        /// Returns the next valid date, given the start and the interval
        /// </summary>
        /// <param name="start">The starting time</param>
        /// <param name="repetition">The repetition interval</param>
        /// <param name="allowedDays">The days the backup is allowed to run</param>
        /// <returns>The next valid date, or throws an exception if no such date can be found</returns>
        public static DateTime GetNextValidTime(DateTime start, string repetition, DayOfWeek[] allowedDays)
        {
            DateTime res = start;

            int i = 50000;

            while (!IsDateAllowed(res, allowedDays) && i-- > 0)
                res = Timeparser.ParseTimeInterval(repetition, res);

            if (!IsDateAllowed(res, allowedDays))
            {
                StringBuilder sb = new StringBuilder();
                if (allowedDays != null)
                    foreach (DayOfWeek w in allowedDays)
                    {
                        if (sb.Length != 0)
                            sb.Append(", ");
                        sb.Append(w.ToString());
                    }

                throw new Exception(string.Format(Strings.Scheduler.InvalidTimeSetupError, start, repetition, sb.ToString()));
            }

            return res;
        }
        
        /// <summary>
        /// The actual scheduling procedure
        /// </summary>
        private void Runner()
        {
            var scheduled = new Dictionary<long, DateTime>();
            while (!m_terminate)
            {
            
                //TODO: As this is executed repeatedly we should cache it
                // to avoid frequent db lookups
                
                //Determine schedule list
                var lst = Program.DataConnection.Schedules;
                foreach(var sc in lst)
                {
                    if (!string.IsNullOrEmpty(sc.Repeat))
                    {
                        DateTime start;
                        if (!scheduled.TryGetValue(sc.ID, out start))
                            start = new DateTime(Math.Max(sc.Time.Ticks, sc.LastRun.Ticks));

                        try
                        {
                            start = GetNextValidTime(start, sc.Repeat, sc.AllowedDays);
                        }
                        catch
                        {
                        }

                        //If time is exceeded, run it now
                        if (start <= DateTime.Now)
                        {
                            //TODO: Cache this to avoid frequent lookups
                            foreach(var id in Program.DataConnection.GetBackupIDsForTags(sc.Tags))
                            {
                                //See if it is already queued
                                var tmplst = from n in m_worker.CurrentTasks
                                                                     where n.Item2 == Duplicati.Server.Serialization.DuplicatiOperation.Backup
                                                                     select n.Item1;
                                var tastTemp = m_worker.CurrentTask;
                                if (tastTemp != null && tastTemp.Item2 == Duplicati.Server.Serialization.DuplicatiOperation.Backup)
                                    tmplst.Union(new long[] { tastTemp.Item1 });
                            
                                //If it is not already in queue, put it there
                                if (!tmplst.Any(x => x == id))
                                    m_worker.AddTask(new Tuple<long, Duplicati.Server.Serialization.DuplicatiOperation>(id, Duplicati.Server.Serialization.DuplicatiOperation.Backup));
                            }

                            //Caluclate next time, by adding the interval to the start until we have
                            // passed the current date and time
                            //TODO: Make this more efficient
                            int i = 50000;
                            while (start <= DateTime.Now && i-- > 0)
                                try
                                {
                                    start = GetNextValidTime(Timeparser.ParseTimeInterval(sc.Repeat, start), sc.Repeat, sc.AllowedDays);
                                }
                                catch
                                {
                                    //TODO: Report this somehow
                                    continue;
                                }

                            if (start < DateTime.Now)
                                continue;
                        }

                        scheduled[sc.ID] = start;
                    }
                }

                var existing = lst.ToDictionary(x => x.ID);
                Server.Serialization.Interface.ISchedule sc_tmp = null;
                //Sort them, lock as we assign the m_schedule variable
                lock(m_lock)
                    m_schedule = (from n in scheduled
                        where existing.TryGetValue(n.Key, out sc_tmp)
                        orderby n.Value
                        select existing[n.Key]).ToArray();

                // Remove unused entries                        
                foreach(var c in (from n in scheduled where !existing.ContainsKey(n.Key) select n.Key).ToArray())
                    scheduled.Remove(c);

                //Raise event if needed
                if (NewSchedule != null)
                    NewSchedule(this, null);

                int waittime = 0;

                //Figure out a sensible amount of time to sleep the thread
                if (scheduled.Count > 0)
                {
                    //When is the next run scheduled?
                    TimeSpan nextrun = scheduled.Min((x) => x.Value) - DateTime.Now;
                    if (nextrun.TotalMilliseconds < 0)
                        continue;

                    //Don't sleep for more than 5 minutes
                    waittime = (int)Math.Min(nextrun.TotalMilliseconds, 60 * 1000 * 5);
                }
                else
                {
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
        /// <returns>True if the backup is allowed to run, false otherwise</returns>
        private static bool IsDateAllowed(DateTime time, DayOfWeek[] allowedDays)
        {
            if (allowedDays == null || allowedDays.Length == 0)
                return true;
            else
                return Array.IndexOf<DayOfWeek>(allowedDays, time.DayOfWeek) >= 0; 
        }

    }
}
