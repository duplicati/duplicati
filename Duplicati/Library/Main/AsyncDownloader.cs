using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main
{
    internal interface IAsyncDownloadedFile : IRemoteVolume
    {
        Library.Utility.TempFile TempFile { get; }
    }

    internal class AsyncDownloader : IEnumerable<IAsyncDownloadedFile>
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<AsyncDownloader>();

        private class AsyncDownloaderEnumerator : IEnumerator<IAsyncDownloadedFile>
        {
            private class AsyncDownloadedFile : IAsyncDownloadedFile
            {
                private readonly Exception m_exception;
                private Library.Utility.TempFile m_file;
                
                public string Name { get; private set; }
                public string Hash { get; private set; }
                public long Size { get; private set; }
                
                public void DisposeTempFile()
                {
                    if (m_file != null)
                        try { m_file.Dispose(); }
                        finally { m_file = null; }
                }
                

                public Library.Utility.TempFile TempFile
                {
                    get
                    {
                        if (m_exception != null)
                            throw m_exception;

                        return m_file;
                    }
                }
                
                public AsyncDownloadedFile(string name, string hash, long size, Library.Utility.TempFile tempfile, Exception exception)
                {
                    this.Name = name;
                    this.Hash = hash;
                    this.Size = size;
                    m_exception = exception;
                    m_file = tempfile;
                }
            }
        
            private readonly IList<IRemoteVolume> m_volumes;
            private BackendManager.IDownloadWaitHandle m_handle;
            private readonly BackendManager m_backend;
            private int m_index;
            private AsyncDownloadedFile m_current;

            public AsyncDownloaderEnumerator(IList<IRemoteVolume> volumes, BackendManager backend)
            {
                m_volumes = volumes;
                m_backend = backend;
                m_index = 0;
            }

            public IAsyncDownloadedFile Current
            {
                get { return m_current; }
            }

            public void Dispose()
            {
                if (m_current != null)
                {
                    m_current.DisposeTempFile();
                    m_current = null;
                }
            }

            object System.Collections.IEnumerator.Current
            {
                get { return this.Current; }
            }

            public bool MoveNext()
            {
                if (m_current != null)
                {
                    m_current.DisposeTempFile();
                    m_current = null;
                }

                if (m_index >= m_volumes.Count)
                    return false;

                if (m_handle == null)
                    m_handle = m_backend.GetAsync(m_volumes[m_index].Name, m_volumes[m_index].Size, m_volumes[m_index].Hash);
                
                string hash = null;
                long size = -1;
                Library.Utility.TempFile file = null;
                Exception exception = null;
                try
                {
                    file = m_handle.Wait(out hash, out size);
                    
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FailedToRetrieveFile", ex, "Failed to retrieve file {0}", m_volumes[m_index].Name);
                    exception = ex;
                }
                
                m_current = new AsyncDownloadedFile(m_volumes[m_index].Name, hash, size, file, exception);
                m_handle = null;

                m_index++;
                if (m_index < m_volumes.Count)
                    m_handle = m_backend.GetAsync(m_volumes[m_index].Name, m_volumes[m_index].Size, m_volumes[m_index].Hash);

                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException("Cannot reset " + this.GetType().FullName);
            }
        }

        private readonly IList<IRemoteVolume> m_volumes;
        private readonly BackendManager m_backend;

        public AsyncDownloader(IList<IRemoteVolume> volumes, BackendManager backend)
        {
            m_volumes = volumes;
            m_backend = backend;
        }

        public IEnumerator<IAsyncDownloadedFile> GetEnumerator()
        {
            return new AsyncDownloaderEnumerator(m_volumes, m_backend);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
