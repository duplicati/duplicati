#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.GUI
{
    public class WorkerThread<Tx> where Tx : class
    {
        private object m_lock = new object();
        private AutoResetEvent m_event;
        private Queue<Tx> m_tasks;
        private volatile bool m_terminate;
        private Thread m_thread;

        private volatile bool m_active;

        private Tx m_currentTask;
        private ProcessItemDelegate m_delegate;

        public event EventHandler StartingWork;
        public event EventHandler CompletedWork;
        public event EventHandler AddedWork;

        public delegate void ProcessItemDelegate(Tx item);

        public WorkerThread(ProcessItemDelegate item)
        {
            m_delegate = item;
            m_event = new AutoResetEvent(false);
            m_terminate = false;
            m_tasks = new Queue<Tx>();
            m_thread = new Thread(new ThreadStart(Runner));
            m_thread.IsBackground = true;
            m_thread.Start();
        }

        /// <summary>
        /// Gets a copy of the current queue
        /// </summary>
        public List<Tx> CurrentTasks
        {
            get
            {
                lock(m_lock)
                    return new List<Tx>(m_tasks);
            }

        }

        /// <summary>
        /// Gets a value indicating if the worker is running
        /// </summary>
        public bool Active
        {
            get { return m_active; }
        }

        /// <summary>
        /// Adds a task to the queue
        /// </summary>
        /// <param name="task">The task to add</param>
        public void AddTask(Tx task)
        {
            lock (m_lock)
            {
                m_tasks.Enqueue(task);
                m_event.Set();
            }

            if (AddedWork != null)
                AddedWork(this, null);
        }

        /// <summary>
        /// This will clear the pending queue
        /// <param name="abortThread">True if the current running thread should be aborted</param>
        /// </summary>
        public void ClearQueue(bool abortThread)
        {
            lock (m_lock)
                m_tasks.Clear();

            if (abortThread)
            {
                try
                {
                    m_thread.Abort();
                    m_thread.Join(500);
                }
                catch
                {
                }

                m_thread = new Thread(new ThreadStart(Runner));
                m_thread.Start();
            }
        }

        /// <summary>
        /// Gets a reference to the currently executing task.
        /// BEWARE: This is not protected by a mutex, DO NOT MODIFY IT!!!!
        /// </summary>
        public Tx CurrentTask
        {
            get
            {
                return m_currentTask;
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
        /// This is the thread entry point
        /// </summary>
        private void Runner()
        {
            while (!m_terminate)
            {
                m_currentTask = null;

                lock (m_lock)
                    if (m_tasks.Count > 0)
                        m_currentTask = m_tasks.Dequeue();

                if (m_currentTask == null && !m_terminate)
                    m_event.WaitOne();

                if (m_terminate)
                    return;

                if (m_currentTask == null)
                    lock (m_lock)
                        if (m_tasks.Count > 0)
                            m_currentTask = m_tasks.Dequeue();

                if (m_currentTask == null)
                    continue;

                if (StartingWork != null)
                    StartingWork(this, null);

                m_active = true;
                m_delegate(m_currentTask);
                m_active = false;

                if (CompletedWork != null)
                    CompletedWork(this, null);
            }
        }
    }
}
