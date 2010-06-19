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
        /// The name of the file or folder
        /// </summary>
        string Name { get; }
        /// <summary>
        /// The size of the file or folder
        /// </summary>
        long Size { get; }
    }
}
