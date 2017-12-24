#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.IO;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Enumerates the possible modes for compression and decompression
    /// </summary>
    public enum ArchiveMode
    {
        /// <summary>
        /// Indicates compression i.e. write archive mode, which means that the stream must be writeable
        /// </summary>
        Write,
        /// <summary>
        /// Indicates decompression i.e. read archive mode, which means that the stream must be readable
        /// </summary>
        Read
    }

    /// <summary>
    /// A value that is given to the compressor as a hint
    /// to how compressible the file is
    /// </summary>
    public enum CompressionHint
    {
        /// <summary>
        /// Indicates that the compression module should decide
        /// </summary>
        Default,

        /// <summary>
        /// Indicates that the file is compressible
        /// </summary>
        Compressible,

        /// <summary>
        /// Indicates that the files is incompressible
        /// </summary>
        Noncompressible
    }

    /// <summary>
    /// An interface for passing additional hints to the compressor
    /// about the expected contents of the volume
    /// </summary>
    public interface ICompressionHinting : ICompression
    {
        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="Duplicati.Library.Interface.ICompression"/>
        /// instance is in low overhead mode.
        /// </summary>
        /// <value><c>true</c> if low overhead mode; otherwise, <c>false</c>.</value>
        bool LowOverheadMode { get; set; }
    }

    public interface IArchiveReader
    {
        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        string[] ListFiles(string prefix);

        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix);

        /// <summary>
        /// Returns a stream with data from the given file in archive.
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>A stream with data from the given file</returns>
        System.IO.Stream OpenRead(string file);

        /// <summary>
        /// Returns the last modification time for the file
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>The timestamp on the file</returns>
        DateTime GetLastWriteTime(string file);

        /// <summary>
        /// Returns a value indicating if the specified file exists
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>True if the file exists, false otherwise</returns>
        bool FileExists(string file);
    }

    public interface IArchiveWriter
    {
        /// <summary>
        /// Creates a file in the archive.
        /// </summary>
        /// <param name="file">The file to create in the archive</param>
        /// <param name="hint">A hint to the compressor as to how compressible the file data is</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A stream with the data to write into the file</returns>
        System.IO.Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite);

        /// <summary>
        /// The size in bytes of the buffer that will be written when flushed
        /// </summary>
        long FlushBufferSize { get; }
    }

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
    public interface ICompression : IDisposable, IArchiveReader, IArchiveWriter
    {
        /// <summary>
        /// The total size of the archive.
        /// </summary>
        long Size { get; }

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
