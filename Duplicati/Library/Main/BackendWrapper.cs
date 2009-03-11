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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Internal class that ensures retry operations, tracks statistics and performs asyncronous operations
    /// </summary>
    internal class BackendWrapper : Backend.IBackend 
    {
        private Backend.IBackend m_backend;
        private CommunicationStatistics m_statistics;
        private Dictionary<string, string> m_options;

        private int m_retries;
        private TimeSpan m_retrydelay;
        private bool m_asyncOperation;

        //These keep track of async operations
        private Queue<string[]> m_pendingOperations;
        //TODO: Figure out if the linux implementation uses the pthreads model, where signals
        //are lost if there are no waiters at the signaling time
        private System.Threading.ManualResetEvent m_asyncWait;
        private object m_queuelock;

        //Temporary variable for progress reporting
        private string m_statusmessage;

        public event RSync.RSyncDir.ProgressEventDelegate ProgressEvent;

        public BackendWrapper(CommunicationStatistics statistics, Backend.IBackend backend, Dictionary<string, string> options)
        {
            m_statistics = statistics;
            m_backend = backend;
            m_retries = 5;
            m_options = options;

            if (options.ContainsKey("number-of-retries"))
                int.TryParse(options["number-of-retries"], out m_retries);

            m_retrydelay = new TimeSpan(TimeSpan.TicksPerSecond * 10);
            if (options.ContainsKey("retry-delay"))
                try { Core.Timeparser.ParseTimeSpan(options["retry-delay"]); }
                catch { }

            m_asyncOperation = options.ContainsKey("asynchronous-upload");
            if (m_asyncOperation)
            {
                //If we are using async operations, the entire class is actually threadsafe,
                //utilizing a common exclusive lock on all operations. But the implementation does
                //not prevent starvation, so it should not be called by multiple threads.
                m_pendingOperations = new Queue<string[]>();
                m_asyncWait = new System.Threading.ManualResetEvent(true);
                m_queuelock = new object();
            }
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return m_backend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return m_backend.ProtocolKey; }
        }

        public bool SupportsStreaming
        {
            get { return m_backend is Backend.IStreamingBackend ? ((Backend.IStreamingBackend)m_backend).SupportsStreaming : false; }
        }

        public List<Duplicati.Library.Backend.FileEntry> List()
        {
            return (List<Duplicati.Library.Backend.FileEntry>)ProtectedInvoke("ListInternal");
        }

        public void Put(string remotename, string filename)
        {
            if (!m_asyncOperation)
                PutInternal(remotename, filename);
            else
            {
                lock (m_queuelock)
                {
                    m_pendingOperations.Enqueue(new string[] { remotename, filename });
                    if (m_asyncWait.WaitOne(0))
                    {
                        m_asyncWait.Reset();
                        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(ProcessQueue));
                    }
                }
            }
        }

        public void Get(string remotename, string filename)
        {
            ProtectedInvoke("GetInternal", remotename, filename);
        }

        public void Delete(string remotename)
        {
            ProtectedInvoke("DeleteInternal", remotename);
        }

        #endregion

        /// <summary>
        /// This method invokes another method in such a way that no more than one thread is ever 
        /// executing on the same backend at any time.
        /// </summary>
        /// <param name="methodname">The method to invoke</param>
        /// <param name="arguments">The methods arguments</param>
        /// <returns>The return value of the invoked function</returns>
        private object ProtectedInvoke(string methodname, params object[] arguments)
        {
            System.Reflection.MethodInfo method = this.GetType().GetMethod(methodname);
            if (method == null)
                method = this.GetType().GetMethod(methodname, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
                throw new Exception("Failed to find function: " + methodname);


            //If the code is not async, just invoke it
            if (!m_asyncOperation)
            {
                try
                {
                    return method.Invoke(this, arguments);
                }
                catch (System.Reflection.TargetInvocationException tex)
                {
                    //Unwrap those annoying invocation exceptions
                    if (tex.InnerException != null)
                        throw tex.InnerException;
                    else
                        throw;
                }
            }
            else
            {
                while (true)
                {
                    //Get the lock
                    lock (m_queuelock)
                    {
                        //If the lock is free, just run, but keep the lock
                        if (m_asyncWait.WaitOne(0))
                        {
                            try
                            {
                                return method.Invoke(this, arguments);
                            }
                            catch (System.Reflection.TargetInvocationException tex)
                            {
                                //Unwrap those annoying invocation exceptions
                                if (tex.InnerException != null)
                                    throw tex.InnerException;
                                else
                                    throw;
                            }
                        }
                    }

                    //Otherwise, wait for the worker to signal completion
                    //We wait without holding the lock, because the worker cannot signal completion while
                    //we hold the lock
                    m_asyncWait.WaitOne();
                    //Now re-acquire the lock, and re-check the state of the event
                }
            }

        }

        private List<Duplicati.Library.Backend.FileEntry> ListInternal()
        {
            int retries = m_retries;
            Exception lastEx = null;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    return m_backend.List();
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (retries > 0);

            throw new Exception("Failed to retrieve file listing: " + lastEx.Message, lastEx);
        }

        private void DeleteInternal(string remotename)
        {
            int retries = m_retries;
            Exception lastEx = null;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Delete(remotename);
                    lastEx = null;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                throw new Exception("Failed to delete file: " + lastEx.Message, lastEx);
        }

        private void GetInternal(string remotename, string filename)
        {
            int retries = m_retries;
            Exception lastEx = null;
            m_statusmessage = "Downloading: " + remotename;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    if (!this.SupportsStreaming)
                    {
                        if (!m_asyncOperation && ProgressEvent != null)
                            ProgressEvent(50, m_statusmessage);
                        m_backend.Get(remotename, filename);
                        if (!m_asyncOperation && ProgressEvent != null)
                            ProgressEvent(100, m_statusmessage);
                    }
                    else
                    {
                        //TODO: How can we guess the remote file size for progress reporting?
                        using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                            ((Backend.IStreamingBackend)m_backend).Get(remotename, ThrottleStream(fs, m_options));
                    }

                    lastEx = null;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                throw new Exception("Failed to download file: " + lastEx.Message, lastEx);

            m_statistics.NumberOfBytesDownloaded += new System.IO.FileInfo(filename).Length;
        }

        private void PutInternal(string remotename, string filename)
        {
            m_statusmessage = "Uploading: " + remotename + " (" + Core.Utility.FormatSizeString(new System.IO.FileInfo(filename).Length) + ")";

            try
            {
                int retries = m_retries;
                bool success = false;

                do
                {
                    try
                    {
                        m_statistics.NumberOfRemoteCalls++;
                        if (!this.SupportsStreaming)
                        {
                            if (!m_asyncOperation && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                            m_backend.Put(remotename, filename);
                            if (!m_asyncOperation && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                        }
                        else
                        {
#if DEBUG
                            DateTime begin = DateTime.Now;
                            long l;
                            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (Core.ProgressReportingStream pgs = new Duplicati.Library.Core.ProgressReportingStream(fs, fs.Length))
                            {
                                l = pgs.Length;
                                if (!m_asyncOperation)
                                    pgs.Progress += new Duplicati.Library.Core.ProgressReportingStream.ProgressDelegate(pgs_Progress);
                                ((Backend.IStreamingBackend)m_backend).Put(remotename, ThrottleStream(pgs, m_options));
                            }

                            TimeSpan duration = DateTime.Now - begin;

                            Console.WriteLine("Transferred " + l.ToString() + " bytes in " + duration.TotalSeconds.ToString() + ", yielding : " + (l / (double)1024 / duration.TotalSeconds) + " kb/s");
#else
                            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (Core.ProgressReportingStream pgs = new Duplicati.Library.Core.ProgressReportingStream(fs, fs.Length))
                            {
                                if (!m_asyncOperation)
                                    pgs.Progress += new Duplicati.Library.Core.ProgressReportingStream.ProgressDelegate(pgs_Progress);
                                ((Backend.IStreamingBackend)m_backend).Put(remotename, ThrottleStream(pgs, m_options));
                            }
#endif
                        }

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        m_statistics.LogError(ex.Message);

                        retries--;
                        if (retries > 0 && m_retrydelay.Ticks > 0)
                            System.Threading.Thread.Sleep(m_retrydelay);
                    }
                } while (!success && retries > 0);

                if (!success)
                    throw new Exception("Failed to upload file");

                m_statistics.NumberOfBytesUploaded += new System.IO.FileInfo(filename).Length;
            }
            finally
            {
                //We delete the file here, because the backend leaves the file, 
                //in the case where we use async methods
                try
                {
                    if (System.IO.File.Exists(filename))
                        System.IO.File.Delete(filename);
                }
                catch { }
            }

        }

        private void pgs_Progress(int progress)
        {
            if (ProgressEvent != null)
                ProgressEvent(progress, m_statusmessage);
        }

        /// <summary>
        /// Worker Thread entry that empties the request queue
        /// </summary>
        /// <param name="dummy">Unused required parameter</param>
        private void ProcessQueue(object dummy)
        {
            while (true)
            {
                string[] args;

                //Obtain the lock for the queue
                lock (m_queuelock)
                {
                    if (m_pendingOperations.Count == 0)
                    {
                        //Signal that we are done
                        m_asyncWait.Set();
                        return;
                    }
                    else
                        args = m_pendingOperations.Dequeue();

                }

                PutInternal(args[0], args[1]);
            }
        }

        private void DisposeInternal()
        {
            if (m_backend != null)
            {
                m_backend.Dispose();
                m_backend = null;
            }
        }


        private Core.ThrottledStream ThrottleStream(System.IO.Stream basestream, Dictionary<string, string> options)
        {
            return new Core.ThrottledStream(basestream,
                options.ContainsKey("max-upload-pr-second") ? Core.Sizeparser.ParseSize(options["max-upload-pr-second"], "kb") : 0,
                options.ContainsKey("max-download-pr-second") ? Core.Sizeparser.ParseSize(options["max-download-pr-second"], "kb") : 0);
        }

        private Core.ThrottledStream ThrottleStream(System.IO.Stream basestream, string upspeed, string downspeed)
        {
            return new Core.ThrottledStream(basestream,
                upspeed == null ? 0 : Core.Sizeparser.ParseSize(upspeed, "kb"),
                downspeed == null ? 0 : Core.Sizeparser.ParseSize(downspeed, "kb"));
        }

        #region IDisposable Members
        
        public void Dispose()
        {
            if (m_backend != null)
                ProtectedInvoke("DisposeInternal");
        }

        #endregion
    }
}
