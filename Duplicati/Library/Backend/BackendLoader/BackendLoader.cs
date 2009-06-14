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

namespace Duplicati.Library.Backend
{
    public class BackendLoader : IBackend
    {
        private static object m_lock = new object();
        private static Dictionary<string, Type> m_backends;
        private IBackend m_interface;

        public BackendLoader(string url, Dictionary<string, string> options)
            : this()
        {
            m_interface = GetBackend(url, options);
            if (m_interface == null)
                throw new ArgumentException(Strings.BackendLoader.UrlNotSupportedError);
        }

        public static IBackend[] LoadedBackends
        {
            get
            {
                LoadBackends();
                List<IBackend> backends = new List<IBackend>();
                foreach (Type t in m_backends.Values)
                    if (t.GetConstructor(System.Type.EmptyTypes) != null)
                        backends.Add((IBackend)Activator.CreateInstance(t));

                //TODO: Deal with backends that have no default constructors

                return backends.ToArray();
            }
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
                        Dictionary<string, Type> backends = new Dictionary<string, Type>();

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
                                    if (typeof(IBackend).IsAssignableFrom(t) && t != typeof(IBackend))
                                    {
                                        IBackend i = Activator.CreateInstance(t) as IBackend;
                                        backends[i.ProtocolKey] = t;
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

        public static IBackend GetBackend(string url, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("index");

            string scheme = new Uri(url).Scheme.ToLower();

            if (m_backends == null)
                LoadBackends();

            if (m_backends.ContainsKey(scheme))
                return (IBackend)Activator.CreateInstance(m_backends[scheme], url, options);
            else
                return null;
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get 
            {
                if (m_interface == null)
                    throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

                return m_interface.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                if (m_interface == null)
                    throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

                return m_interface.ProtocolKey;
            }
        }

        public List<FileEntry> List()
        {
            if (m_interface == null)
                throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);
            
            return m_interface.List();
        }

        public void Put(string remotename, string filename)
        {
            if (m_interface == null)
                throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);
            
            m_interface.Put(remotename, filename);
        }

        public void Get(string remotename, string filename)
        {
            if (m_interface == null)
                throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

            m_interface.Get(remotename, filename);
        }

        public void Delete(string remotename)
        {
            if (m_interface == null)
                throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

            m_interface.Delete(remotename);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_interface == null)
                    throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

                return m_interface.SupportedCommands;
            }
        }

        public string Description
        {
            get
            {
                if (m_interface == null)
                    throw new ArgumentException(Strings.BackendLoader.NoURLSuppliedError);

                return m_interface.Description;
            }
        }

        #endregion


        #region IDisposable Members

        public void Dispose()
        {
            if (m_interface != null)
            {
                m_interface.Dispose();
                m_interface = null;
            }
        }

        #endregion
    }
}
