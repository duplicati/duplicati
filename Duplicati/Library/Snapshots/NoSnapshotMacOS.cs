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

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots.MacOS;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    /// <param name="sources">The list of source paths</param>
    /// <param name="ignoreAdvisoryLocks">Whether to ignore advisory locks</param
    /// <param name="followSymlinks">Whether to follow symlinks</param>
    /// <param name="photosLibraryPath">The user specified MacOS Photos library path</param>
    [SupportedOSPlatform("macOS")]
    public sealed class NoSnapshotMacOS(IEnumerable<string> sources, bool ignoreAdvisoryLocks, bool followSymlinks, MacOSPhotosHandling macOSPhotosHandling, string? photosLibraryPath)
        : NoSnapshotLinux(sources, ignoreAdvisoryLocks, followSymlinks)
    {
        /// <inheritdoc/>
        public override IEnumerable<ISourceProviderEntry> EnumerateFilesystemEntries(ISourceProviderEntry source)
            => base.EnumerateFilesystemEntries(source).Select(b => MacOS.MacOSPhotosLibrary.TryWrap(b, macOSPhotosHandling, photosLibraryPath));
    }
}

