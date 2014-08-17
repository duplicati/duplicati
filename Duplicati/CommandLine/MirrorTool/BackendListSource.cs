//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library;
using System.Collections.Generic;

namespace Duplicati.CommandLine.MirrorTool
{
    public class BackendListSource : IEnumerable<IFileEntry>, IDisposable
    {
        private readonly string m_backendurl;
        private readonly Options m_options;
        private Tuple<string, IBackend> m_backend;
        private TimeSpan m_drift;
        private DateTime m_lastCheck = new DateTime(0);

        public BackendListSource(string url, Options options)
        {
            m_backendurl = url;
            m_options = options;
        }

        #region IEnumerable implementation

        public IEnumerator<IFileEntry> GetEnumerator()
        {
            var worklist = new Queue<string>();
            var visited = new Dictionary<string, string>();
            worklist.Enqueue("");

            while (worklist.Count > 0)
            {
                var p = worklist.Dequeue();
                if (visited.ContainsKey(p))
                    continue;
                visited.Add(p, null);

                foreach(var n in RemoteList(p).OrderBy(x => x.Name, Library.Utility.Utility.ClientFilenameStringComparer))
                {
                    var name = p + (p.EndsWith("/") ? "" : "/") + n.Name;
                    if (n.IsFolder)
                        worklist.Enqueue(name);
                    else if (name.StartsWith(m_options.TempFilePrefix))
                    {
                        // TODO: Logging, and setting
                        if (DateTime.Now.AddMonths(-3) > n.LastModification)
                            Delete(name);
                    }
                    else
                        yield return new FileEntry(name, n.Size, n.LastAccess, n.LastModification);
                }
            }
        }

        #endregion

        #region IEnumerable implementation

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
            try
            {
                if (m_backend != null)
                    m_backend.Item2.Dispose();
            }
            finally
            {
                m_backend = null;
            }
        }

        #endregion

        private IBackend GetBackend(string folder)
        {
            var newurl = m_backendurl + (m_backendurl.EndsWith("/") ? "" : "/") + folder;
            if (m_backend != null && m_backend.Item1 != newurl)
                try
                {
                    m_backend.Item2.Dispose();
                }
                catch
                {
                }
                finally
                {
                    m_backend = null;
                }

            if (m_backend == null)
                m_backend = new Tuple<string, IBackend>(newurl, Library.DynamicLoader.BackendLoader.GetBackend(newurl, m_options.ToDict()));

            return m_backend.Item2;
        }

        private void PerformRemoteActionWithRetry(Action<IBackend> action, string path)
        {
            var retries = 0;
            Exception lastex = null;

            do
            {
                try
                {
                    action(GetBackend(path));
                    return;
                }
                catch (Exception ex)
                {
                    lastex = ex;
                    Library.Logging.Log.WriteMessage("Remote op failed", Duplicati.Library.Logging.LogMessageType.Warning, ex);
                    if (m_backend != null)
                        try
                        {
                            m_backend.Item2.Dispose();
                        }
                        catch
                        {
                        }
                        finally
                        {
                            m_backend = null;
                        }
                }
            } while(retries++ < m_options.Retries);

            if (lastex != null)
                throw lastex;
        }

        private List<IFileEntry> RemoteList(string folder)
        {
            List<IFileEntry> result = null;
            PerformRemoteActionWithRetry(x => {
                result = x.List();
            }, folder);
         
            return result;   
        }

        private string GenerateTempFilename(string folder)
        {
            return folder + (folder.EndsWith("/") ? "" : "/") + m_options.TempFilePrefix + Guid.NewGuid().ToString();
        }

        private DateTime GetTimestamp(string path, DateTime dt)
        {
            if ((DateTime.Now - m_lastCheck) > TimeSpan.FromHours(1))
            {
                var split = SplitPath(path);
                var t = RemoteList(split.Item1).Where(x => x.Name == split.Item2).First().LastModification;
                m_drift = dt - t;
                m_lastCheck = DateTime.Now;

            }

            return dt + m_drift;
        }

        internal static Tuple<string, string> SplitPath(string path)
        {
            var lix = path.LastIndexOf('/');
            return new Tuple<string, string>(path.Substring(0, lix), path.Substring(lix + 1));
        }

        public void Delete(string path)
        {
            var split = SplitPath(path);

            PerformRemoteActionWithRetry(x => {
                x.Delete(split.Item2);
            }, split.Item1);
        }

        public DateTime Rename(string oldpath, string newpath)
        {
            var splitold = SplitPath(oldpath);
            var splitnew = SplitPath(newpath);
            var dt = DateTime.Now;
            PerformRemoteActionWithRetry(x => {
                ((IRenameEnabledBackend)x).Rename(splitold.Item2, splitnew.Item2);
                dt = DateTime.Now;
            }, splitold.Item1);

            return GetTimestamp(newpath, dt);
        }

        public Library.Utility.TempFile Download(string path)
        {
            string tmp = null;
            var split = SplitPath(path);
            PerformRemoteActionWithRetry(x => {
                using(var tf = new Duplicati.Library.Utility.TempFile())
                {
                    x.Get(split.Item2, tf);
                    tf.Protected = true;
                    tmp = tf;
                }
            }, split.Item1);

            return (Duplicati.Library.Utility.TempFile)tmp;
        }

        public DateTime Upload(string path, string filename)
        {
            // Upload as temp, then rename
            var targetsplit = SplitPath(path);

            var tmpfilename = GenerateTempFilename(targetsplit.Item1);

            var tmpsplit = SplitPath(tmpfilename);
            var dt = DateTime.Now;

            PerformRemoteActionWithRetry(x => {
                x.Put(tmpsplit.Item2, filename);
                dt = DateTime.Now;
            }, tmpsplit.Item1);

            Rename(tmpfilename, path);

            return GetTimestamp(path, dt);
        }
    }
}

