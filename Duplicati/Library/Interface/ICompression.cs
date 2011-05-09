#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for accessing files in an archive, such as a folder or compressed file.
    /// All modules that implements compression must implement this interface.
    /// The classes that implements this interface MUST also 
    /// implement a default constructor and a construtor that
    /// has the signature new(string file, Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// The input file may not exist or have zero length, in which case it should be created.
    /// </summary>
    public interface ICompression : IDisposable
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
        /// Creates a file in the archive
        /// </summary>
        /// <param name="file">The file to create</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A stream with the data to write into the file</returns>
        System.IO.Stream CreateFile(string file, DateTime lastWrite);

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

        /// <summary>
        /// Returns the last modification time for the file
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>The timestamp on the file</returns>
        DateTime GetLastWriteTime(string file);

        /// <summary>
        /// The size in bytes of the buffer that will be written when flushed
        /// </summary>
        long FlushBufferSize { get; }

        /// <summary>
        /// The extension that the compression implementation adds to the filename
        /// </summary>
        string FilenameExtension { get; }

        /// <summary>
        /// A localized string describing the compression module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the compression module
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }
    }
}
