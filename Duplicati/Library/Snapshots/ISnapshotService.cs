using System;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// An interface for a snapshot implementation
    /// </summary>
    public interface ISnapshotService : IDisposable
    {
        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        void EnumerateFilesAndFolders(Duplicati.Library.Core.FilenameFilter filter, Duplicati.Library.Core.Utility.EnumerationCallbackDelegate callback);

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        DateTime GetLastWriteTime(string file);

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read and seeked</returns>
        Stream OpenRead(string file);
    }
}
