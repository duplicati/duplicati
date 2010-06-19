#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// This class supports dynamic loading of instances of a given interface
    /// </summary>
    /// <typeparam name="T">The interface that the class loads</typeparam>
    internal abstract class DynamicLoader<T> where T : class
    {
        /// <summary>
        /// A lock used to guarantee threadsafe access to the interface lookup table
        /// </summary>
        protected object m_lock = new object();

        /// <summary>
        /// A cached list of interfaces
        /// </summary>
        protected Dictionary<string, T> m_interfaces;

        /// <summary>
        /// Function to extract the key value from the interface
        /// </summary>
        /// <param name="item">The interface to extract the key from</param>
        /// <returns>The key for the interface</returns>
        protected abstract string GetInterfaceKey(T item);

        /// <summary>
        /// Gets a list of subfolders to search for interfaces
        /// </summary>
        protected abstract string[] Subfolders { get; }

        /// <summary>
        /// Construcst a new instance of the dynamic loader,
        ///  does not load anything
        /// </summary>
        public DynamicLoader()
        {
        }

        /// <summary>
        /// Provides threadsafe doublelocking loading of the interface table
        /// </summary>
        protected void LoadInterfaces()
        {
            if (m_interfaces == null)
                lock (m_lock)
                    if (m_interfaces == null)
                    {
                        Dictionary<string, T> interfaces = new Dictionary<string, T>();
                        foreach (T b in FindInterfaceImplementors(Subfolders))
                            interfaces[GetInterfaceKey(b)] = b;

                        m_interfaces = interfaces;
                    }
        }

        /// <summary>
        /// Searches the base folder of this dll for classes in other assemblies that 
        /// implements a certain interface, and has a default constructor
        /// </summary>
        /// <param name="additionalfolders">Any additional folders besides the assembly path to search in</param>
        /// <returns>A list of instanciated classes which implements the interface</returns>
        private List<T> FindInterfaceImplementors(string[] additionalfolders) 
        {
            List<T> interfaces = new List<T>();

            string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            List<string> files = new List<string>();
            files.AddRange(System.IO.Directory.GetFiles(path, "*.dll"));

            //We can override with subfolders
            if (additionalfolders != null)
                foreach (string s in additionalfolders)
                {
                    string subpath = System.IO.Path.Combine(path, "backends");
                    if (System.IO.Directory.Exists(subpath))
                        files.AddRange(System.IO.Directory.GetFiles(subpath, "*.dll"));
                }

            foreach (string s in files)
            {
                try
                {
                    System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFile(s);
                    if (asm != System.Reflection.Assembly.GetExecutingAssembly())
                    {
                        foreach (Type t in asm.GetExportedTypes())
                            if (typeof(T).IsAssignableFrom(t) && t != typeof(T))
                            {
                                //TODO: Figure out how to support types with no default constructors
                                if (t.GetConstructor(Type.EmptyTypes) != null)
                                {
                                    T i = Activator.CreateInstance(t) as T;
                                    if (i != null)
                                        interfaces.Add(i);
                                }
                            }
                    }
                }
                catch
                {
                }
            }

            return interfaces;
        }

        /// <summary>
        /// Gets a list of loaded interfaces
        /// </summary>
        public T[] Interfaces
        {
            get
            {
                LoadInterfaces();
                lock(m_lock)
                    return new List<T>(m_interfaces.Values).ToArray();
            }
        }

        /// <summary>
        /// Gets a list of the keys of loaded interfaces
        /// </summary>
        public string[] Keys
        {
            get
            {
                LoadInterfaces();
                lock (m_lock)
                    return new List<string>(m_interfaces.Keys).ToArray();
            }
        }

    }
}
