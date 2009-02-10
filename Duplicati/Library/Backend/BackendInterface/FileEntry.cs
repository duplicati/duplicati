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
    public class FileEntry
    {
        private string m_name;
        private DateTime m_lastAccess;
        private DateTime m_lastModification;
        private long m_size;
        private bool m_isFolder;

        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        public DateTime LastAccess
        {
            get { return m_lastAccess; }
            set { m_lastAccess = value; }
        }

        public DateTime LastModification
        {
            get { return m_lastModification; }
            set { m_lastModification = value; }
        }

        public long Size
        {
            get { return m_size; }
            set { m_size = value; }
        }

        public bool IsFolder
        {
            get { return m_isFolder; }
            set { m_isFolder = value; }
        }

        private FileEntry()
        {
            m_name = null;
            m_lastAccess = new DateTime();
            m_lastModification = new DateTime();
            m_size = 0;
            m_isFolder = false;
        }

        public FileEntry(string filename)
            : this()
        {
            m_name = filename;
        }

        public FileEntry(string filename, long size)
            : this(filename)
        {
            m_size = size;
        }

        public FileEntry(string filename, long size, DateTime lastAccess, DateTime lastModified)
            : this(filename, size)
        {
            m_lastModification = lastModified;
            m_lastAccess = lastAccess;
        }
    }
}
