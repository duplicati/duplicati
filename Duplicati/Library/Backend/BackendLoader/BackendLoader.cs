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

namespace Duplicati.Backend
{
    public class BackendLoader : IBackendInterface
    {
        private static object m_lock = new object();
        private static Dictionary<string, IBackendInterface> m_backends;
        private IBackendInterface m_interface;

        public BackendLoader(string url)
            : this()
        {
            m_interface = this[url];
            if (m_interface == null)
                throw new ArgumentException("The supplied url is not supported");
        }

        public static string[] Backends
        {
            get
            {
                LoadBackends();
                return new List<string>(m_backends.Keys).ToArray();
            }
        }

        private static void LoadBackends()
        {
            if (m_backends == null)
                lock (m_lock)
                    if (m_backends == null)
                    {
                        Dictionary<string, IBackendInterface> backends = new Dictionary<string, IBackendInterface>();

                        string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        List<string> files = new List<string>();
                        files.AddRange(System.IO.Directory.GetFiles(path, "*.dll"));

                        //We can override with the backends path
                        path = System.IO.Path.Combine(path, "backends");
                        if (System.IO.Directory.Exists(path))
                            files.AddRange(System.IO.Directory.GetFiles(path, "*.dll"));

                        foreach (string s in files)
                        {
                            try
                            {
                                System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFile(s);
                                if (asm == System.Reflection.Assembly.GetExecutingAssembly())
                                    continue;

                                foreach (Type t in asm.GetExportedTypes())
                                    if (typeof(IBackendInterface).IsAssignableFrom(t) && t != typeof(IBackendInterface))
                                    {
                                        IBackendInterface i = Activator.CreateInstance(t) as IBackendInterface;
                                        backends[i.ProtocolKey] = i;
                                    }
                            }
                            catch
                            {
                            }
                        }

                        m_backends = backends;
                    }
        }

        public BackendLoader()
        {
            m_interface = null;
            LoadBackends();
        }

        public static IBackendInterface GetBackend(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("index");

            if (url.Contains(":"))
                url = new Uri(url).Scheme;

            if (m_backends.ContainsKey(url.ToLower()))
                return m_backends[url.ToLower()];
            else
                return null;
        }

        public IBackendInterface this[string index]
        {
            get { return GetBackend(index); }
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get 
            {
                if (m_interface == null)
                    throw new Exception("This instance is not bound to a particular backend");
                else
                    return m_interface.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                if (m_interface == null)
                    throw new Exception("This instance is not bound to a particular backend");
                else
                    return m_interface.ProtocolKey;
            }
        }

        public List<FileEntry> List(string url, Dictionary<string, string> options)
        {
            IBackendInterface i = m_interface;
            if (i == null)
                i = this[url];
            else if (i != this[url])
                throw new Exception("The supplied url matched a different backend");

            if (i == null)
                throw new ArgumentException("The supplied url is not supported");
            
            return i.List(url, options);
        }

        public void Put(string url, Dictionary<string, string> options, System.IO.Stream stream)
        {
            IBackendInterface i = m_interface;
            if (i == null)
                i = this[url];
            else if (i != this[url])
                throw new Exception("The supplied url matched a different backend");

            if (i == null)
                throw new ArgumentException("The supplied url is not supported");
            
            i.Put(url, options, stream);
        }

        public System.IO.Stream Get(string url, Dictionary<string, string> options)
        {
            IBackendInterface i = m_interface;
            if (i == null)
                i = this[url];
            else if (i != this[url])
                throw new Exception("The supplied url matched a different backend");

            if (i == null)
                throw new ArgumentException("The supplied url is not supported");

            return i.Get(url, options);
        }

        public void Delete(string url, Dictionary<string, string> options)
        {
            IBackendInterface i = m_interface;
            if (i == null)
                i = this[url];
            else if (i != this[url])
                throw new Exception("The supplied url matched a different backend");

            if (i == null)
                throw new ArgumentException("The supplied url is not supported");

            i.Delete(url, options);
        }

        #endregion
    }
}
