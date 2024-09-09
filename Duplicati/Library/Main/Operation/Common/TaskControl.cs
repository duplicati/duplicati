// Copyright (C) 2024, The Duplicati Team
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

using System;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Interface for async access to the current control state
    /// </summary>
    public interface ITaskReader
    {
        /// <summary>
        /// Processing tasks can regularly await this,
        /// which will pause the task or stop it if required.
        /// The return value is <c>true</c> if the program should continue and <c>false</c> if a stop is requested
        /// </summary>
        Task<bool> ProgressAsync { get; }
        /// <summary>
        /// Gets the transfer progress async control.
        /// The transfer handler should await this instead of the <see cref="ProgressAsync"/> event.
        /// </summary>
        Task<bool> TransferProgressAsync { get; }
    }

    /// <summary>
    /// Interface for controlling the progress
    /// </summary>
    public interface ITaskCommander
    {
        /// <summary>
        /// Requests that progress should be paused
        /// </summary>
        /// <param name="alsoTransfers">If set to <c>true</c> transfer are also suspended.</param>
        void Pause(bool alsoTransfers = false);
        /// <summary>
        /// Resumes running a paused process
        /// </summary>
        void Resume();
        /// <summary>
        /// Requests that the progress should be stopped in an orderly manner,
        /// which allows current transfers to be completed.
        /// </summary>
        /// <param name="alsoTransfers">If set to <c>true</c> active transfer are also stopped.</param>
        void Stop(bool alsoTransfers = false);
        /// <summary>
        /// Terminates the progress without allowing a flush
        /// </summary>
        void Terminate();
    }

    /// <summary>
    /// Implementation of the task control
    /// </summary>
    public class TaskControl : ITaskReader, ITaskCommander, IDisposable
    {
        /// <summary>
        /// Internal state tracking to avoid invalid requests
        /// </summary>
        private enum State
        {
            /// <summary>
            /// The task is running
            /// </summary>
            Active,
            /// <summary>
            /// The task is paused
            /// </summary>
            Paused,
            /// <summary>
            /// The task is stopped
            /// </summary>
            Stopped,
            /// <summary>
            /// The task is terminated
            /// </summary>
            Terminated
        }

        /// <summary>
        /// Internal control state for progress event
        /// </summary>
        private TaskCompletionSource<bool> m_progress;
        /// <summary>
        /// Internal control state for the transfer event
        /// </summary>
        private TaskCompletionSource<bool> m_transfer;

        /// <summary>
        /// The control lock instance
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// The current progress state
        /// </summary>
        private State m_progressstate = State.Paused;
        /// <summary>
        /// The current transfer state
        /// </summary>
        private State m_transferstate = State.Paused;

        /// <summary>
        /// Processing tasks can regularly await this,
        /// which will pause the task or stop it if required.
        /// The return value is <c>true</c> if the program should continue and <c>false</c> if a stop is requested
        /// </summary>
        public Task<bool> ProgressAsync { get { return m_progress.Task; } }
        /// <summary>
        /// Gets the transfer progress async control.
        /// The transfer handler should await this instead of the <see cref="ProgressAsync"/> event.
        /// </summary>
        public Task<bool> TransferProgressAsync { get { return m_transfer.Task; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> class.
        /// </summary>
        public TaskControl()
        {
            m_progress = new TaskCompletionSource<bool>();
            m_transfer = new TaskCompletionSource<bool>();
            Resume();
        }

        /// <summary>
        /// Resumes running a paused process
        /// </summary>
        public void Resume()
        {
            lock(m_lock)
            {
                if (m_progressstate == State.Paused)
                {
                    m_progress.SetResult(true);
                    m_progressstate = State.Active;
                }
                if (m_transferstate == State.Paused)
                {
                    m_transfer.SetResult(true);
                    m_transferstate = State.Active;
                }
            }
        }

        /// <summary>
        /// Requests that progress should be paused
        /// </summary>
        /// <param name="alsoTransfers">If set to <c>true</c> also transfers.</param>
        public void Pause(bool alsoTransfers = false)
        {
            lock(m_lock)
            {
                if (m_progressstate == State.Active)
                {
                    m_progress = new TaskCompletionSource<bool>();
                    m_progressstate = State.Paused;
                }

                if (alsoTransfers && m_transferstate == State.Active)
                {
                    m_transfer = new TaskCompletionSource<bool>();
                    m_transferstate = State.Paused;
                }
            }
        }

        /// <summary>
        /// Requests that the progress should be stopped in an orderly manner,
        /// which allows current transfers to be completed.
        /// </summary>
        /// <param name="alsoTransfers">If set to <c>true</c> also transfers.</param>
        public void Stop(bool alsoTransfers = false)
        {
            lock(m_lock)
            {
                if (m_progressstate == State.Active || m_progressstate == State.Paused)
                {
                    if (m_progressstate != State.Paused)
                        m_progress = new TaskCompletionSource<bool>();
                    
                    m_progress.SetResult(false);
                    m_progressstate = State.Stopped;
                }

                if (alsoTransfers && (m_transferstate == State.Active || m_transferstate == State.Paused))
                {
                    if (m_transferstate != State.Paused)
                        m_transfer = new TaskCompletionSource<bool>();
                    
                    m_transfer.SetResult(false);
                    m_transferstate = State.Stopped;
                }
            }
        }

        /// <summary>
        /// Terminates the progress without allowing a flush
        /// </summary>
        public void Terminate()
        {
            lock(m_lock)
            {
                if (m_progressstate != State.Terminated)
                {
                    if (m_progressstate != State.Paused)
                        m_progress = new TaskCompletionSource<bool>();
                    
                    m_progress.SetCanceled();
                    m_progressstate = State.Terminated;
                }
                if (m_transferstate != State.Terminated)
                {
                    if (m_transferstate != State.Paused)
                        m_transfer = new TaskCompletionSource<bool>();
                    
                    m_transfer.SetCanceled();
                    m_transferstate = State.Terminated;
                }
            }
        }

        /// <summary>
        /// Releases all resource used by the <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/>. The <see cref="Dispose"/> method leaves
        /// the <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> so the garbage collector can reclaim the
        /// memory that the <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> was occupying.</remarks>
        public void Dispose()
        {
            Terminate();
        }
    }
}

