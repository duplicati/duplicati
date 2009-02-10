using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
    /// <summary>
    /// An interface for accessing files in an archive, such as a folder or compressed file
    /// </summary>
    public interface IFileArchive : IDisposable
    {
        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        string[] ListFiles(string prefix);
        /// <summary>
        /// Returns all directories in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the directories returned</param>
        /// <returns>All directories in the archive, matching the prefix, if any</returns>
        string[] ListDirectories(string prefix);
        /// <summary>
        /// Returns all files and directories in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files and directories returned</param>
        /// <returns>All files and directories in the archive, matching the prefix, if any</returns>
        string[] ListEntries(string prefix);

        /// <summary>
        /// Returns all the bytes from a given file
        /// </summary>
        /// <param name="file">The file to read data from</param>
        /// <returns>All bytes from the given file</returns>
        byte[] ReadAllBytes(string file);
        /// <summary>
        /// Returns all lines in the given file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>All lines in the given file</returns>
        string[] ReadAllLines(string file);
        /// <summary>
        /// Returns a stream with data from the given file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>A stream with data from the given file</returns>
        System.IO.Stream OpenRead(string file);

        /// <summary>
        /// Returns a stream with data from the given file
        /// </summary>
        /// <param name="file">The file to write the data to</param>
        /// <returns>A stream with data from the given file</returns>
        System.IO.Stream OpenWrite(string file);

        /// <summary>
        /// Writes data to a file
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="data">The data to write</param>
        void WriteAllBytes(string file, byte[] data);
        /// <summary>
        /// Writes the given lines to the files
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="data">The data to write</param>
        void WriteAllLines(string file, string[] data);

        /// <summary>
        /// Removes a file from the archive
        /// </summary>
        /// <param name="file">The file to remove</param>
        void DeleteFile(string file);
        /// <summary>
        /// Creates a file in the archive
        /// </summary>
        /// <param name="file">The file to create</param>
        /// <returns>A stream with the data to write into the file</returns>
        System.IO.Stream CreateFile(string file);

        /// <summary>
        /// Deletes the named directory, if it is empty
        /// </summary>
        /// <param name="file">The directory to remove</param>
        void DeleteDirectory(string file);

        /// <summary>
        /// Adds a directory to the archive
        /// </summary>
        /// <param name="file">The name of the archive to create</param>
        void AddDirectory(string file);

        /// <summary>
        /// Returns a value indicating if the specified file exists
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>True if the file exists, false otherwise</returns>
        bool FileExists(string file);

        /// <summary>
        /// Returns a value indicating if the specified directory exists
        /// </summary>
        /// <param name="file">The name of the directory to examine</param>
        /// <returns>True if the directory exists, false otherwise</returns>
        bool DirectoryExists(string file);

        /// <summary>
        /// The total size of the archive
        /// </summary>
        long Size { get; }
    }
}
