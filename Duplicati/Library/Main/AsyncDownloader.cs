using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main
{
    public class AsyncDownloader : IEnumerable<KeyValuePair<IRemoteVolume, Library.Utility.TempFile>>
    {
        private class AsyncDownloaderEnumerator : IEnumerator<KeyValuePair<IRemoteVolume, Library.Utility.TempFile>>
        {
        	private class RemoteVolume : IRemoteVolume
        	{
        		public string Name { get; set; }
        		public string Hash { get; set; }
        		public long Size { get; set; }
        		
        		public RemoteVolume(string name, string hash, long size)
        		{
        			this.Name = name;
        			this.Hash = hash;
        			this.Size = size;
        		}
        	}
        
            private IList<IRemoteVolume> m_volumes;
            private BackendManager.IDownloadWaitHandle m_handle;
            private BackendManager m_backend;
            private int m_index;
            private KeyValuePair<IRemoteVolume, Library.Utility.TempFile>? m_current;

            public AsyncDownloaderEnumerator(IList<IRemoteVolume> volumes, BackendManager backend)
            {
                m_volumes = volumes;
                m_backend = backend;
                m_index = 0;
            }

            public KeyValuePair<IRemoteVolume, Library.Utility.TempFile> Current
            {
                get { return m_current.Value; }
            }

            public void Dispose()
            {
                if (m_current != null)
                {
                    m_current.Value.Value.Dispose();
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
                    m_current.Value.Value.Dispose();
                    m_current = null;
                }

                if (m_index >= m_volumes.Count)
                    return false;

                if (m_handle == null)
                	m_handle = m_backend.GetAsync(m_volumes[m_index].Name, m_volumes[m_index].Size, m_volumes[m_index].Hash);
                
                string hash;
                long size;
                var file = m_handle.Wait(out hash, out size);
                
                m_current = new KeyValuePair<IRemoteVolume, Library.Utility.TempFile>(new RemoteVolume(m_volumes[m_index].Name, hash, size), file);
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

        private IList<IRemoteVolume> m_volumes;
        private BackendManager m_backend;

        public AsyncDownloader(IList<IRemoteVolume> volumes, BackendManager backend)
        {
            m_volumes = volumes;
            m_backend = backend;
        }

        public IEnumerator<KeyValuePair<IRemoteVolume, Library.Utility.TempFile>> GetEnumerator()
        {
            return new AsyncDownloaderEnumerator(m_volumes, m_backend);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
