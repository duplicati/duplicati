// Copyright (C) 2026, The Duplicati Team
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
using System.IO;
using System.IO.Compression;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Compression.TarZstdCompression;

/// <summary>
/// An ICompression implementation using Tar+GZip with EOF header for fast random access.
/// File format: [GZip Stream] containing [Tar Archive] + [.eof-header with dictionary]
/// </summary>
public class FileArchiveTarGzip : FileArchiveTarBased
{
    private const string COMPRESSION_LEVEL_OPTION = "tgz-compression-level";
    private const string MEMORY_BUFFER_OPTION = "tgz-memory-buffer";
    private const int DEFAULT_COMPRESSION_LEVEL = 2;
    private const int MIN_COMPRESSION_LEVEL = 0;
    private const int MAX_COMPRESSION_LEVEL = 3;

    /// <summary>
    /// Default constructor for module discovery
    /// </summary>
    public FileArchiveTarGzip() : base()
    {
    }

    /// <summary>
    /// Constructor with stream and options
    /// </summary>
    public FileArchiveTarGzip(Stream stream, ArchiveMode mode, IReadOnlyDictionary<string, string?> options)
        : base(stream, mode, options)
    {
    }

    public override string FilenameExtension => "tgz";

    public override string DisplayName => Strings.FileArchiveTarGzip.DisplayName;

    public override string Description => Strings.FileArchiveTarGzip.Description;

    protected override string CompressionLevelOption => COMPRESSION_LEVEL_OPTION;

    protected override int DefaultCompressionLevel => DEFAULT_COMPRESSION_LEVEL;

    protected override int MinCompressionLevel => MIN_COMPRESSION_LEVEL;

    protected override int MaxCompressionLevel => MAX_COMPRESSION_LEVEL;

    protected override string MemoryBufferOption => MEMORY_BUFFER_OPTION;

    public override IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(
            COMPRESSION_LEVEL_OPTION,
            CommandLineArgument.ArgumentType.Enumeration,
            Strings.FileArchiveTarGzip.CompressionlevelShort,
            Strings.FileArchiveTarGzip.CompressionlevelLong,
            DEFAULT_COMPRESSION_LEVEL.ToString(),
            null,
            ["0", "1", "2", "3"]
        ),
        new CommandLineArgument(
            MEMORY_BUFFER_OPTION,
            CommandLineArgument.ArgumentType.Boolean,
            Strings.FileArchiveTarGzip.MemorybufferShort,
            Strings.FileArchiveTarGzip.MemorybufferLong,
            "false"
        )
    ];

    protected override Stream CreateDecompressorStream(Stream inputStream)
        => new GZipStream(inputStream, CompressionMode.Decompress);

    protected override Stream CreateCompressorStream(Stream outputStream, int compressionLevel)
        => new GZipStream(outputStream, MapToGZipLevel(compressionLevel), leaveOpen: true);

    private static CompressionLevel MapToGZipLevel(int level)
        => level switch
        {
            0 => CompressionLevel.NoCompression,
            1 => CompressionLevel.Fastest,
            2 => CompressionLevel.Optimal,
            3 => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
}
