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
using System.Collections.Generic;

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
        /// <remarks>This method has slightly strange logic, in that it will return a null value if the file does not exist, instead of throwing an exception</remarks>
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
    /// implement a default constructor and a constructor that
    /// has the signature new(string file, Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// The input file may not exist or have zero length, in which case it should be created.
    /// </summary>
    public interface ICompression : IDynamicModule, IDisposable, IArchiveReader, IArchiveWriter
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
    }
}
