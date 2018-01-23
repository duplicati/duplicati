using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Newtonsoft.Json;
using Duplicati.Library.Localization.Short;
using CoCoL;
using System.Threading.Tasks;

namespace Duplicati.Library.Main
{
    internal class BackendManager : IDisposable
    {
        /// <summary>
        /// Legacy interface to wait for a file download
        /// </summary>
        public interface IDownloadWaitHandle
        {
            /// <summary>
            /// Waits for a file to download and returns it once it has completed
            /// </summary>
            /// <returns>The temporary file.</returns>
            TempFile Wait();
            /// <summary>
            /// Waits for a file to download and returns it once it has completed
            /// </summary>
            /// <returns>The temporary file.</returns>
            /// <param name="hash">The downloaded file hash.</param>
            /// <param name="size">The downloaded file size.</param>
            TempFile Wait(out string hash, out long size);
        }

        /// <summary>
        /// Implementation of <see cref="IDownloadWaitHandle"/> on a Task based result
        /// </summary>
        private class WaitHandleWrapper : IDownloadWaitHandle
        {
            /// <summary>
            /// The task to wrap
            /// </summary>
            private readonly Task<Tuple<Library.Utility.TempFile, long, string>> m_task;

            /// <summary>
            /// The parent instance
            /// </summary>
            private readonly BackendManager m_parent;

            /// <summary>
            /// Initializes a new instance of the
            /// <see cref="T:Duplicati.Library.Main.BackendManager.WaitHandleWrapper"/> class.
            /// </summary>
            /// <param name="task">The task to wrap.</param>
            /// <param name="parent">The parent instance</param>
            public WaitHandleWrapper(Task<Tuple<Library.Utility.TempFile, long, string>> task, BackendManager parent)
            {
                m_task = task;
                m_parent = parent;
            }

            /// <summary>
            /// Waits for a file to download and returns it once it has completed
            /// </summary>
            /// <returns>The temporary file.</returns>
            public TempFile Wait()
            {
                string hash;
                long size;
                return Wait(out hash, out size);
            }

            /// <summary>
            /// Waits for a file to download and returns it once it has completed
            /// </summary>
            /// <returns>The temporary file.</returns>
            /// <param name="hash">The downloaded file hash.</param>
            /// <param name="size">The downloaded file size.</param>
            public TempFile Wait(out string hash, out long size)
            {
                do
                {
                    m_task.Wait(500);
                    m_parent.m_db.ProcessAllPendingOperations();
                }
                while (!m_task.IsCompleted);

                m_task.WaitForTaskOrThrow();
                size = m_task.Result.Item2;
                hash = m_task.Result.Item3;
                return m_task.Result.Item1;
            }
        }


        private volatile Exception m_lastException;
        private readonly string m_backendurl;
        private readonly Options m_options;
        private readonly Operation.Common.BackendHandlerDatabaseGuard m_db;
        private readonly Operation.Common.BackendHandler m_backend;
        private readonly Operation.Common.StatsCollector m_stats;

        private readonly object m_lock = new object();
        private readonly Queue<Task> m_pendingTasks = new Queue<Task>();

        public string BackendUrl { get { return m_backendurl; } }
        public bool HasDied { get { return m_lastException != null; } }
        public Exception LastException { get { return m_lastException; } }

        public BackendManager(string backendurl, Options options, IBackendWriter statwriter, LocalDatabase database, Operation.Common.BackendHandler backend)
        {
            m_db = new Operation.Common.BackendHandlerDatabaseGuard(database, options.Dryrun);
            m_backendurl = backendurl;
            m_stats = new Operation.Common.StatsCollector(statwriter);
            m_backend = backend;
            m_options = options;
        }

        public BackendManager(string backendurl, Options options, IBackendWriter statwriter, LocalDatabase database, BasicResults res)
        {
            m_db = new Operation.Common.BackendHandlerDatabaseGuard(database, options.Dryrun);
            m_backendurl = backendurl;
            m_stats = new Operation.Common.StatsCollector(statwriter);
            m_backend = new Operation.Common.BackendHandler(options, backendurl, m_db, m_stats, res.TaskReader);
            m_options = options;
        }

        private Task WaitForTaskIfRequired(bool forcesync, Func<Task> cb)
        {
            if (m_lastException != null)
                throw m_lastException;

            m_db.ProcessAllPendingOperations();

            var task = cb();

            m_db.ProcessAllPendingOperations();

            if (forcesync || m_options.SynchronousUpload)
            {
                m_stats.SetBlocking(true);
                try
                {
                    do
                    {
                        task.Wait(500);
                        m_db.ProcessAllPendingOperations();
                    }
                    while (!task.IsCompleted);                        
                    task.WaitForTaskOrThrow();
                }
                catch (Exception ex) 
                { 
                    m_lastException = ex; 
                    throw; 
                }
                finally 
                { 
                    m_stats.SetBlocking(false); 
                }

                m_db.ProcessAllPendingOperations();
            }
            else
            {
                // Keep these so we can check for errors
                lock (m_lock)
                    m_pendingTasks.Enqueue(task);
            }

            lock (m_lock)
                while (m_pendingTasks.Count > 0 && m_pendingTasks.Peek().IsCompleted)
                {
                    var p = m_pendingTasks.Dequeue();
                    if (p.IsFaulted)
                        m_lastException = p.Exception;
                    else if (p.IsCanceled)
                        m_lastException = new TaskCanceledException();
                }

            if (m_lastException != null)
                throw m_lastException;

            return task;
        }

        public void PutUnencrypted(string remotename, string localpath)
        {
            WaitForTaskIfRequired(false, () => m_backend.PutUnencryptedAsync(remotename, localpath));
        }

        public void Put(VolumeWriterBase item, IndexVolumeWriter indexfile = null, bool synchronous = false)
        {
            WaitForTaskIfRequired(synchronous, () => m_backend.UploadFileAsync(item, async (remotename) => {
                return indexfile; 
            }));
        }

        public Library.Utility.TempFile GetWithInfo(string remotename, out long size, out string hash)
        {
            return 
                new WaitHandleWrapper(
                    (Task<Tuple<Library.Utility.TempFile, long, string>>)WaitForTaskIfRequired(true, () => m_backend.GetFileWithInfoAsync(remotename)),
                    this
                ).Wait(out hash, out size);
        }

        public Library.Utility.TempFile Get(string remotename, long size, string hash)
        {
            var t = (Task<Library.Utility.TempFile>)WaitForTaskIfRequired(true, () => m_backend.GetFileAsync(remotename, size, hash));
            return t.Result;
        }

        public IDownloadWaitHandle GetAsync(string remotename, long size, string hash)
        {
            return 
                new WaitHandleWrapper(
                    (Task<Tuple<Library.Utility.TempFile, long, string>>)WaitForTaskIfRequired(false, () => m_backend.GetFileWithInfoAsync(remotename)),
                    this
                );
        }

        public void GetForTesting(string remotename, long size, string hash)
        {
            WaitForTaskIfRequired(true, async () =>
            {
                using (await m_backend.GetFileForTestingAsync(remotename, size, hash))
                { }
            });
        }

        public IList<Library.Interface.IFileEntry> List()
        {
            return
                ((Task<IList<Interface.IFileEntry>>)
                 WaitForTaskIfRequired(true, () => m_backend.ListFilesAsync()))
                .Result;
        }

        public void WaitForComplete()
        {
            WaitForEmpty();
            m_backend.Dispose();
        }

        public void WaitForEmpty()
        {
            WaitForTaskIfRequired(true, () => {
                lock (m_lock)
                {
                    while (m_pendingTasks.Count > 0)
                        if (m_pendingTasks.Peek().Wait(1000))
                            m_pendingTasks.Dequeue();
                }
                return Task.FromResult(true);
            });
        }

        public void CreateFolder(string remotename)
        {
            WaitForTaskIfRequired(true, () => m_backend.CreateFolder(remotename));
        }

        public void Delete(string remotename, long size, bool synchronous = false)
        {
            WaitForTaskIfRequired(synchronous, () => m_backend.DeleteFileAsync(remotename));
        }

        public void FlushDbMessages()
        {
            m_db.ProcessAllPendingOperations();
        }

        public void Dispose()
        {
            if (m_backend != null)
                m_backend.Dispose();
            if (m_db != null)
                m_db.Dispose();
        }
    }
}
