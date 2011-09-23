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
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Duplicati.Datamodel;
using System.Data.LightDatamodel;
using Duplicati.Library.Utility;

namespace Duplicati.GUI
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
        /// The connection to the database
        /// </summary>
        private IDataFetcherCached m_connection;
        /// <summary>
        /// A termination flag
        /// </summary>
        private volatile bool m_terminate;
        /// <summary>
        /// The worker thread that is invoked to do work
        /// </summary>
        private WorkerThread<IDuplicityTask> m_worker;
        /// <summary>
        /// The wait event
        /// </summary>
        private AutoResetEvent m_event;
        /// <summary>
        /// The data syncronization lock
        /// </summary>
        private object m_lock = new object();
        /// <summary>
        /// The program lock for the database connection
        /// </summary>
        private object m_datalock;

        /// <summary>
        /// An event that is raised when the schedule changes
        /// </summary>
        public event EventHandler NewSchedule;

        /// <summary>
        /// The currently scheduled items
        /// </summary>
        private Schedule[] m_schedule;

        /// <summary>
        /// Constructs a new scheduler
        /// </summary>
        /// <param name="connection">The database connection</param>
        /// <param name="worker">The worker thread</param>
        /// <param name="datalock">The database lock object</param>
        public Scheduler(IDataFetcherCached connection, WorkerThread<IDuplicityTask> worker, object datalock)
        {
            m_datalock = datalock;
            m_connection = connection;
            m_thread = new Thread(new ThreadStart(Runner));
            m_worker = worker;
            m_schedule = new Schedule[0];
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
        /// A copy of the current schedule list
        /// </summary>
        public List<Schedule> Schedule 
        { 
            get 
            {
                lock (m_lock)
                    return new List<Schedule>(m_schedule);
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
            while (!m_terminate)
            {
                List<Schedule> reps = new List<Schedule>();
                List<Schedule> tmp;
                lock (m_datalock)
                    tmp = new List<Schedule>(m_connection.GetObjects<Schedule>());

                //Determine schedule list
                foreach (Schedule sc in tmp)
                {
                    if (!string.IsNullOrEmpty(sc.Repeat))
                    {
                        DateTime start = sc.NextScheduledTime;

                        try { start = GetNextValidTime(sc.NextScheduledTime, sc.Repeat, sc.AllowedWeekdays); }
                        catch { } //TODO: Report this somehow

                        //If time is exceeded, run it now
                        if (start <= DateTime.Now)
                        {
                            //See if it is already queued
                            List<IDuplicityTask> tmplst = m_worker.CurrentTasks;
                            IDuplicityTask tastTemp = m_worker.CurrentTask;
                            if (tastTemp != null)
                                tmplst.Add(tastTemp);

                            bool found = false;
                            foreach (IDuplicityTask t in tmplst)
                                if (t != null && t is IncrementalBackupTask && ((IncrementalBackupTask)t).Schedule.ID == sc.ID)
                                {
                                    found = true;
                                    break;
                                }

                            //If it is not already in queue, put it there
                            if (!found)
                                m_worker.AddTask(new IncrementalBackupTask(sc));

                            //Caluclate next time, by adding the interval to the start until we have
                            // passed the current date and time
                            //TODO: Make this more efficient
                            int i = 50000;
                            while (start <= DateTime.Now && i-- > 0)
                                try { start = GetNextValidTime(Timeparser.ParseTimeInterval(sc.Repeat, start), sc.Repeat, sc.AllowedWeekdays); }
                                catch
                                {
                                    //TODO: Report this somehow
                                    continue;
                                }

                            if (start < DateTime.Now)
                                continue;
                        }

                        //Add to schedule list at the new time
                        reps.Add(sc);
                        sc.NextScheduledTime = start;
                    }
                }

                System.Data.LightDatamodel.QueryModel.OperationOrParameter op = System.Data.LightDatamodel.QueryModel.Parser.ParseQuery("ORDER BY When ASC");

                //Sort them, lock as we assign the m_schedule variable
                lock(m_lock)
                    m_schedule = op.EvaluateList<Schedule>(reps).ToArray();

                //Raise event if needed
                if (NewSchedule != null)
                    NewSchedule(this, null);

                int waittime = 0;

                //Figure out a sensible amount of time to sleep the thread
                if (m_schedule.Length > 0)
                {
                    //When is the next run scheduled?
                    TimeSpan nextrun = m_schedule[0].NextScheduledTime - DateTime.Now;
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
