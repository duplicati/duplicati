// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Common.IO
{
    /// <summary>
    /// The primary implementation of the file interface
    /// </summary>
    public class FileEntry : IFileEntry
    {
        private string m_name;
        private DateTime m_lastAccess;
        private DateTime m_lastModification;
        private DateTime m_created;
        private long m_size;
        private bool m_isFolder;

        /// <summary>
        /// Gets or sets the file or folder name
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or sets the time the file or folder was last accessed
        /// </summary>
        public DateTime LastAccess
        {
            get { return m_lastAccess; }
            set { m_lastAccess = value; }
        }

        /// <summary>
        /// Gets or sets the time the file or folder was last modified
        /// </summary>
        public DateTime LastModification
        {
            get { return m_lastModification; }
            set { m_lastModification = value; }
        }

        /// <summary>
        /// Gets or sets the time the file or folder was created
        /// </summary>
        public DateTime Created
        {
            get { return m_created; }
            set { m_created = value; }
        }

        /// <summary>
        /// Gets or sets the size of the file or folder
        /// </summary>
        public long Size
        {
            get { return m_size; }
            set { m_size = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the entry is a folder
        /// </summary>
        public bool IsFolder
        {
            get { return m_isFolder; }
            set { m_isFolder = value; }
        }

        /// <summary>
        /// Helper function to initialize the instance to default values
        /// </summary>
        private FileEntry()
        {
            m_name = null;
            m_lastAccess = new DateTime();
            m_lastModification = new DateTime();
            m_size = -1;
            m_isFolder = false;
        }

        /// <summary>
        /// Constructs an entry using only the name.
        /// The entry is assumed to be a file.
        /// </summary>
        /// <param name="filename">The name of the file</param>
        public FileEntry(string filename)
            : this()
        {
            m_name = filename;
        }

        /// <summary>
        /// Constructs an entry using only the name and size.
        /// The entry is assumed to be a file.
        /// </summary>
        /// <param name="filename">The name of the file</param>
        /// <param name="size">The size of the file</param>
        public FileEntry(string filename, long size)
            : this(filename)
        {
            m_size = size;
        }

        /// <summary>
        /// Constructs an entry supplying all information
        /// </summary>
        /// <param name="filename">The name of the file or folder</param>
        /// <param name="size">The size of the file or folder</param>
        /// <param name="lastAccess">The time the file or folder was last accessed</param>
        /// <param name="lastModified">The time the file or folder was last modified</param>
        public FileEntry(string filename, long size, DateTime lastAccess, DateTime lastModified)
            : this(filename, size)
        {
            m_lastModification = lastModified;
            m_lastAccess = lastAccess;
        }
    }
}
