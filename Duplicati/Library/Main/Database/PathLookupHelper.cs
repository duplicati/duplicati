//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    public class PathLookupHelper<T>
    {
        private static readonly char[] SPLIT_CHARS = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        private static readonly string DIR_SEP = Path.DirectorySeparatorChar.ToString();
        private readonly FolderEntry m_root = new FolderEntry(); 
        private readonly List<KeyValuePair<string, FolderEntry>> m_lookup;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Main.Database.PathLookupHelper`1"/> class.
        /// </summary>
        /// <param name="useHotPath">If set to <c>true</c> use hotpath optimization</param>
        public PathLookupHelper(bool useHotPath = true)
        {
            m_lookup = useHotPath ? new List<KeyValuePair<string, FolderEntry>>(128) : null;
        }
    
        /// <summary>
        /// Prepares the hotpath lookup.
        /// The idea behind the hotpath tracking is that paths tend to
        /// share a prefix. Instead of traversing the tree on each request,
        /// the tree entries for the previous request are stored.
        /// The next request is then matched to the state, and can skip
        /// the lookups if it has some of the same path prefix
        /// </summary>
        /// <param name="path">The path to look up</param>
        /// <param name="c">The entry to start looking for</param>
        /// <param name="paths">The path fragments to search for</param>
        /// <param name="prefix">The string prefix to use</param>
        private void PrepareHotLookup(string path, out FolderEntry c, out string[] paths, out string prefix)
        {
            c = m_root;

            paths = path.Split(SPLIT_CHARS, StringSplitOptions.RemoveEmptyEntries);
            prefix = DIR_SEP;
            
            if (m_lookup == null)
                return;

            var hotIx = Math.Min(m_lookup.Count, paths.Length) - 1;
            while (hotIx >= 0)
            {
                if (path.StartsWith(m_lookup[hotIx].Key, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                {
                    c = m_lookup[hotIx].Value;
                    
                    prefix = m_lookup[hotIx].Key;
                    if (m_lookup[hotIx].Key.Length == path.Length)
                        paths = new string[0];
                    else
                    {
                        paths = paths.Skip(hotIx + 1).ToArray();
                        m_lookup.RemoveRange(hotIx + 1, m_lookup.Count - (hotIx + 1));
                    }
                    return;
                }
                hotIx--;
            }

            //Fallback, no matches at all
            m_lookup.Clear();
        }
        
        /// <summary>
        /// Attempts to find the path
        /// </summary>
        /// <returns><c>true</c>, if the path was found, <c>false</c> otherwise.</returns>
        /// <param name="path">The path to look for</param>
        /// <param name="value">The value found, if any</param>
        public bool TryFind(string path, out T value)
        {
            value = default(T);
            if (path == null)
                return false;
                
            string[] paths;
            FolderEntry cur;
            string prefix;
            PrepareHotLookup(path, out cur, out paths, out prefix);
                        
            foreach(var p in paths)
                if (!cur.TryGetChild(p, out cur))
                    return false;
                else if (m_lookup != null)
                {
                    //Maintain the hotpath lookup information
                    prefix = Duplicati.Library.Utility.Utility.AppendDirSeparator(System.IO.Path.Combine(prefix, p));
                    m_lookup.Add(new KeyValuePair<string, FolderEntry>(prefix, cur));
                }

            value = cur.Value;
            return true;
        }
        
        /// <summary>
        /// Insert the specified path and value.
        /// </summary>
        /// <param name="path">The path to add</param>
        /// <param name="value">The value to associate the path with</param>
        public void Insert(string path, T value)
        {
            string[] paths;
            FolderEntry cur;
            string prefix;
            PrepareHotLookup(path, out cur, out paths, out prefix);
            
            foreach(var p in paths)
            {
                cur = cur.AddChild(p);
                if (m_lookup != null)
                {
                    //Maintain the hotpath lookup information
                    prefix = Duplicati.Library.Utility.Utility.AppendDirSeparator(System.IO.Path.Combine(prefix, p));
                    m_lookup.Add(new KeyValuePair<string, FolderEntry>(prefix, cur));
                }
            }
            
            cur.Value = value;
        }

        /// <summary>
        /// Class for holding a path fragment and sub-fragments
        /// </summary>
        private class FolderEntry
        {
            /// <summary>
            /// The value associated with the entry
            /// </summary>
            public T Value;
            
            /// <summary>
            /// The lookup table with sub-fragments
            /// </summary>
            private SortedList<string, FolderEntry> m_folders = null;
            
            /// <summary>
            /// Attempts to locate a path element
            /// </summary>
            /// <returns><c>true</c>, if get path element was found, <c>false</c> otherwise.</returns>
            /// <param name="name">Name of the path to look for</param>
            /// <param name="v">The entry that represents the element</param>
            public bool TryGetChild(string name, out FolderEntry v)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("Invalid pathname.", "name");
                    
                if (m_folders == null)
                {
                    v = null;
                    return false;
                }
                
                return m_folders.TryGetValue(name, out v);
            }
            
            /// <summary>
            /// Adds the child path element
            /// </summary>
            /// <returns>The child element</returns>
            /// <param name="name">The name of the path element to add</param>
            public FolderEntry AddChild(string name)
            {
                if (m_folders == null)
                    m_folders = new SortedList<string, FolderEntry>(1, Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);
                
                FolderEntry r;
                if (!m_folders.TryGetValue(name, out r))
                    m_folders.Add(name, r = new FolderEntry());
                return r;
            }
        }
    }
}

