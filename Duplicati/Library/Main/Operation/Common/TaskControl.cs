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
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Interface for async access to the current control state
    /// </summary>
    public interface ITaskReader
    {
        /// <summary>
        /// Gets a flag indicating if the process is stopped
        /// </summary>
        bool IsStopRequested { get; }

        /// <summary>
        /// A cancellation token that can be used to monitor progress
        /// </summary>
        CancellationToken ProgressToken { get; }

        /// <summary>
        /// Gets the progress state, waiting if the state is paused, throws if terminated
        /// </summary>
        /// <returns><c>true</c> if the progress should continue, <c>false</c> if it should stop</returns>
        Task<bool> ProgressRendevouz();

#if DEBUG
        /// <summary>
        /// Callback for testing the task control
        /// </summary>
        /// <param name="path">The path being processed</param>
        Action<string> TestMethodCallback { get; set; }
#endif

    }

    /// <summary>
    /// Interface for controlling the progress
    /// </summary>
    public interface ITaskCommander
    {
        /// <summary>
        /// Requests that progress should be paused
        /// </summary>
        void Pause();
        /// <summary>
        /// Resumes running a paused process
        /// </summary>
        void Resume();
        /// <summary>
        /// Requests that the progress should be stopped in an orderly manner,
        /// which allows current transfers to be completed.
        /// </summary>
        void Stop();
        /// <summary>
        /// Terminates the progress without allowing a flush
        /// </summary>
        void Terminate();
    }

    /// <summary>
    /// Interface for the task control
    /// </summary>
    public interface ITaskControl : ITaskReader, ITaskCommander
    {
    }

    /// <summary>
    /// Implementation of the task control
    /// </summary>
    public class TaskControl : ITaskControl, IDisposable
    {
        /// <summary>
        /// State tracking to avoid invalid requests
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
        /// The progress task completion source, cancelled if the operation is terminated
        /// </summary>
        private readonly CancellationTokenSource m_progressTcs = new();

        /// <summary>
        /// The control lock instance
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// The current progress state
        /// </summary>
        private State m_progressstate = State.Paused;

        /// <summary>
        /// Gets the current progress state.
        /// </summary>
        public bool IsStopRequested => m_progressstate == State.Stopped;

        /// <summary>
        /// A cancellation token that is cancelled if the operation is aborted
        /// </summary>
        public CancellationToken ProgressToken => m_progressTcs.Token;

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Main.Operation.Common.TaskControl"/> class.
        /// </summary>
        public TaskControl()
        {
            m_progress = new TaskCompletionSource<bool>();
            Resume();
        }

        /// <summary>
        /// Gets the progress state, waiting if the state is paused
        /// </summary>
        public async Task<bool> ProgressRendevouz()
        {
            var res = await m_progress.Task.ConfigureAwait(false);
            m_progressTcs.Token.ThrowIfCancellationRequested();
            return res;
        }

        /// <summary>
        /// Resumes running a paused process
        /// </summary>
        public void Resume()
        {
            lock (m_lock)
            {
                if (m_progressstate == State.Paused)
                {
                    m_progress.SetResult(true);
                    m_progressstate = State.Active;
                }
            }
        }

        /// <summary>
        /// Requests that progress should be paused
        /// </summary>
        public void Pause()
        {
            lock (m_lock)
            {
                if (m_progressstate == State.Active)
                {
                    m_progress = new TaskCompletionSource<bool>();
                    m_progressstate = State.Paused;
                }
            }
        }

        /// <summary>
        /// Requests that the progress should be stopped in an orderly manner,
        /// which allows current transfers to be completed.
        /// </summary>
        public void Stop()
        {
            lock (m_lock)
            {
                if (m_progressstate == State.Active || m_progressstate == State.Paused)
                {
                    if (m_progressstate != State.Paused)
                        m_progress = new TaskCompletionSource<bool>();

                    m_progress.SetResult(false);
                    m_progressstate = State.Stopped;
                }
            }
        }

        /// <summary>
        /// Terminates the progress without allowing a flush
        /// </summary>
        public void Terminate()
        {
            lock (m_lock)
            {
                if (m_progressstate != State.Terminated)
                {
                    if (m_progressstate != State.Paused)
                        m_progress = new TaskCompletionSource<bool>();

                    m_progress.SetCanceled();
                    m_progressstate = State.Terminated;
                    m_progressTcs.Cancel();
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Terminate();
        }

#if DEBUG
        /// <inheritdoc />
        public Action<string> TestMethodCallback { get; set; }
#endif
    }
}

