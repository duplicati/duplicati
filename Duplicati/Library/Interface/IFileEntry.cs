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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// The interface for an instance of a file, as seen by a backend
    /// </summary>
    public interface IFileEntry
    {
        /// <summary>
        /// True if the entry represents a folder, false otherwise
        /// </summary>
        bool IsFolder { get; }
        /// <summary>
        /// The time the file or folder was last accessed
        /// </summary>
        DateTime LastAccess { get; }
        /// <summary>
        /// The time the file or folder was last modified
        /// </summary>
        DateTime LastModification { get; }
        /// <summary>
        /// The time the file or folder was created
        /// </summary>
        DateTime Created { get; }
        /// <summary>
        /// The name of the file or folder
        /// </summary>
        string Name { get; }
        /// <summary>
        /// The size of the file or folder
        /// </summary>
        long Size { get; }
    }
}
