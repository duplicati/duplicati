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
        /// <summary>
        /// Gets or sets the file or folder name
        /// </summary>
        public string Name { get; set; } = null;

        /// <summary>
        /// Gets or sets the time the file or folder was last accessed
        /// </summary>
        public DateTime LastAccess { get; set; } = new DateTime();

        /// <summary>
        /// Gets or sets the time the file or folder was last modified
        /// </summary>
        public DateTime LastModification { get; set; } = new DateTime();

        /// <summary>
        /// Gets or sets the time the file or folder was created
        /// </summary>
        public DateTime Created { get; set; } = new DateTime();

        /// <summary>
        /// Gets or sets the size of the file or folder
        /// </summary>
        public long Size { get; set; } = -1;

        /// <summary>
        /// Gets or sets a value indicating if the entry is a folder
        /// </summary>
        public bool IsFolder { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating if the entry is archived (not readable)
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// Helper function to initialize the instance to default values
        /// </summary>
        private FileEntry() { }

        /// <summary>
        /// Constructs an entry supplying all information
        /// </summary>
        /// <param name="filename">The name of the file or folder</param>
        /// <param name="size">The size of the file or folder</param>
        /// <param name="lastAccess">The time the file or folder was last accessed</param>
        /// <param name="lastModified">The time the file or folder was last modified</param>
        /// <param name="isFolder">A value indicating if the entry is a folder</param>
        /// <param name="isArchived">A value indicating if the entry is archived</param>
        public FileEntry(string filename, long size = -1, DateTime lastAccess = default, DateTime lastModified = default, bool isFolder = false, bool isArchived = false)
        {
            Name = filename;
            Size = size;
            LastAccess = lastAccess;
            LastModification = lastModified;
            IsFolder = isFolder;
            IsArchived = isArchived;
        }
    }
}
