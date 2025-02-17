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

namespace Duplicati.Library.Backend.pCloud;

/// <summary>
/// Represents a file entry or folder in pCloud
/// </summary>
internal class pCloudFileEntry : IFileEntry
{
    /// <summary>
    /// Indicates it is a folder on the remote filesystem
    /// </summary>
    public bool IsFolder { get; set; }

    /// <summary>
    /// Last access date of the file
    /// </summary>
    public DateTime LastAccess { get; set; }

    /// <summary>
    /// Last modification of the file
    /// </summary>
    public DateTime LastModification { get; set; }

    /// <summary>
    /// Last modification of the file
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// Filename or folder name
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Size of the object in bytes
    /// </summary>
    public long Size { get; set; }
}
