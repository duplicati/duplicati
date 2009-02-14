#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using Duplicati.Library.Core;

namespace Duplicati.GUI
{
    public class Scheduler
    {
        private Thread m_thread;
        private IDataFetcherCached m_connection;
        private volatile bool m_terminate;
        private WorkerThread<IDuplicityTask> m_worker;
        private AutoResetEvent m_event;
        private object m_lock = new object();
        private object m_datalock;

        public event EventHandler NewSchedule;

        private Schedule[] m_schedule;

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

        public void Reschedule()
        {
            m_event.Set();
        }

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

        private void Runner()
        {
            while (!m_terminate)
            {
                List<Schedule> reps = new List<Schedule>();
                List<Schedule> tmp;
                lock (m_datalock)
                    tmp = new List<Schedule>(m_connection.GetObjects<Schedule>());

                foreach (Schedule sc in tmp)
                {
                    if (!string.IsNullOrEmpty(sc.Repeat))
                    {
                        DateTime start = sc.When;

                        if (start <= DateTime.Now)
                        {
                            m_worker.AddTask(new IncrementalBackupTask(sc));

                            int i = 0;
                            while (start <= DateTime.Now && i++ < 50000)
                                try
                                {
                                    start = Timeparser.ParseTimeInterval(sc.Repeat, start);
                                }
                                catch
                                {
                                    continue;
                                }

                            if (start < DateTime.Now)
                                continue;
                        }

                        reps.Add(sc);
                        sc.When = start;
                    }
                }

                System.Data.LightDatamodel.QueryModel.Operation op = System.Data.LightDatamodel.QueryModel.Parser.ParseQuery("ORDER BY When ASC");

                lock(m_lock)
                    m_schedule = op.EvaluateList<Schedule>(reps).ToArray();

                if (NewSchedule != null)
                    NewSchedule(this, null);

                int waittime = 0;

                if (m_schedule.Length > 0)
                {
                    TimeSpan nextrun = m_schedule[0].When - DateTime.Now;
                    if (nextrun.TotalMilliseconds < 0)
                        continue;

                    /*lock (m_datalock)
                        m_connection.CommitAll();*/

                    waittime = (int)Math.Min(nextrun.TotalMilliseconds, 60 * 1000 * 5);
                }
                else
                {
                    //No tasks, check back later
                    waittime = 60 * 1000;
                }

                //Waiting on the event, enables a wakeup call from termination
                // never use waittime = 0
                m_event.WaitOne(Math.Max(100, waittime), true);
            }
        }
    }
}
