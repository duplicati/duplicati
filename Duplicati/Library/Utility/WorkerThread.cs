#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Class to encapsulate a thread that runs a list of queued operations
    /// </summary>
    /// <typeparam name="Tx">The type to operate on</typeparam>
    public class WorkerThread<Tx> where Tx : class
    {
        /// <summary>
        /// Locking object for shared data
        /// </summary>
        private readonly object m_lock = new object();
        /// <summary>
        /// The wait event
        /// </summary>
        private readonly AutoResetEvent m_event;
        /// <summary>
        /// The internal list of tasks to perform
        /// </summary>
        private Queue<Tx> m_tasks;
        /// <summary>
        /// A flag used to terminate the thread
        /// </summary>
        private volatile bool m_terminate;
        /// <summary>
        /// The coordinating thread
        /// </summary>
        private Thread m_thread;

        /// <summary>
        /// A value indicating if the coordinating thread is running
        /// </summary>
        private volatile bool m_active;

        /// <summary>
        /// The current task being processed
        /// </summary>
        private Tx m_currentTask;
        /// <summary>
        /// A callback that performs the actual work on the item
        /// </summary>
        private readonly Action<Tx> m_delegate;

        /// <summary>
        /// An event that is raised when the runner state changes
        /// </summary>
        public event Action<WorkerThread<Tx>, RunState> WorkerStateChanged;

        /// <summary>
        /// Event that occurs when a new operation is being processed
        /// </summary>
        public event Action<WorkerThread<Tx>, Tx> StartingWork;
        /// <summary>
        /// Event that occurs when an operation has completed
        /// </summary>
        public event Action<WorkerThread<Tx>, Tx> CompletedWork;
        /// <summary>
        /// Event that occurs when an error is detected
        /// </summary>
        public event Action<WorkerThread<Tx>, Tx, Exception> OnError;
        /// <summary>
        /// An event that occurs when a new task is added to the queue or an existing one is removed
        /// </summary>
        public event Action<WorkerThread<Tx>> WorkQueueChanged;

        /// <summary>
        /// The internal state
        /// </summary>
        private volatile RunState m_state;

        /// <summary>
        /// The states the scheduler can take
        /// </summary>
        public enum RunState
        {
            /// <summary>
            /// The program is running as normal
            /// </summary>
            Run,
            /// <summary>
            /// The program is suspended by the user
            /// </summary>
            Paused
        }

        /// <summary>
        /// Constructs a new WorkerThread
        /// </summary>
        /// <param name="item">The callback that performs the work</param>
        public WorkerThread(Action<Tx> item, bool paused)
        {
            m_delegate = item;
            m_event = new AutoResetEvent(paused);
            m_terminate = false;
            m_tasks = new Queue<Tx>();
            m_state = paused ? WorkerThread<Tx>.RunState.Paused : WorkerThread<Tx>.RunState.Run;

            m_thread = new Thread(new ThreadStart(Runner));
            m_thread.IsBackground = true;
            m_thread.Name = "WorkerThread<" + typeof(Tx).Name + ">";
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

            if (WorkQueueChanged != null)
                WorkQueueChanged(this);
        }

        /// <summary>
        /// An overloaded AddTask method that allows a task to skip to the front of a queue
        /// It does this by creating a new queue, adding the new task first, and then adding
        /// all the old tasks to the new queue. It's cleaner to use a linked list,
        /// but the performance difference is negligible on such a small queue.
        /// </summary>
        /// <param name="task">Task.</param>
        /// <param name="skipQueue">If set to <c>true</c> skip queue.</param>
        public void AddTask(Tx task, bool skipQueue)
        {
            if (!skipQueue) {
                // Fall back to default AddTask method
                AddTask(task);
                return;
            }

            lock (m_lock)
            {
                Queue<Tx> newQueue = new Queue<Tx>();
                newQueue.Enqueue(task);
                while (m_tasks.Count > 0)
                {
                    Tx n = m_tasks.Dequeue();
                    newQueue.Enqueue(n);
                }
                m_tasks = newQueue;
                m_event.Set();
            }

            if (WorkQueueChanged != null)
                WorkQueueChanged(this);
        }


        /// <summary>
        /// Removes a task from the queue, does not remove the task if it is currently running
        /// </summary>
        /// <param name="task">The task to remove</param>
        public void RemoveTask(Tx task)
        {
            lock (m_lock)
            {
                Queue<Tx> tmp = new Queue<Tx>();
                while (m_tasks.Count > 0)
                {
                    Tx n = m_tasks.Dequeue();
                    if (n != task)
                        tmp.Enqueue(n);
                }

                m_tasks = tmp;
            }

            if (WorkQueueChanged != null)
                WorkQueueChanged(this);
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

                lock(m_lock)
                    if (m_state == WorkerThread<Tx>.RunState.Run && m_tasks.Count > 0)
                        m_currentTask = m_tasks.Dequeue();

                if (m_currentTask == null && !m_terminate)
                if (m_state == WorkerThread<Tx>.RunState.Run)
                    m_event.WaitOne(); //Sleep until signaled
                    else
                {
                    if (WorkerStateChanged != null)
                        WorkerStateChanged(this, m_state);

                    //Sleep for brief periods, until signaled
                    while (!m_terminate && m_state != WorkerThread<Tx>.RunState.Run)
                        m_event.WaitOne(1000 * 60 * 5, false);

                    //If we were not terminated, we are now ready to run
                    if (!m_terminate)
                    {
                        m_state = WorkerThread<Tx>.RunState.Run;
                        if (WorkerStateChanged != null)
                            WorkerStateChanged(this, m_state);
                    }
                }

                if (m_terminate)
                    return;

                if (m_currentTask == null && m_state == WorkerThread<Tx>.RunState.Run)
                    lock(m_lock)
                        if (m_tasks.Count > 0)
                            m_currentTask = m_tasks.Dequeue();

                if (m_currentTask == null)
                    continue;

                if (StartingWork != null)
                    StartingWork(this, m_currentTask);

                try
                {
                    m_active = true;
                    m_delegate(m_currentTask);
                }
                catch (Exception ex)
                {
                    try { System.Threading.Thread.ResetAbort(); }
                    catch { }

                    if (OnError != null)
                        try { OnError(this, m_currentTask, ex); }
                        catch { }
                }
                finally
                {
                    try { System.Threading.Thread.ResetAbort(); }
                    catch { }

                    m_active = false;
                }

                var task = m_currentTask;
                m_currentTask = null;

                if (CompletedWork != null)
                    try { CompletedWork(this, task); }
                    catch (Exception ex) 
                    {
                        try { OnError(this, task, ex); }
                        catch { }
                    }
            }
        }

        /// <summary>
        /// Gets the current run state
        /// </summary>
        public RunState State { get { return m_state; } }

        /// <summary>
        /// Instructs Duplicati to run scheduled backups
        /// </summary>
        public void Resume()
        {
            m_state = RunState.Run;
            m_event.Set();
        }

        /// <summary>
        /// Instructs Duplicati to pause scheduled backups
        /// </summary>
        public void Pause()
        {
            m_state = RunState.Paused;
            m_event.Set();
        }

        /// <summary>
        /// Waits the specified number of milliseconds for the thread to terminate
        /// </summary>
        /// <param name="millisecondTimeout">The number of milliseconds to wait</param>
        /// <returns>True if the thread is terminated, false if a timeout occured</returns>
        public bool Join(int millisecondTimeout)
        {
            if (m_thread != null)
                return m_thread.Join(millisecondTimeout);
            return true;
        }
    }
}
