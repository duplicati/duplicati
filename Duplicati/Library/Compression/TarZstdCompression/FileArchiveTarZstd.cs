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
using Duplicati.Library.Interface;
using ZstdSharp;

namespace Duplicati.Library.Compression.TarZstdCompression;

/// <summary>
/// An ICompression implementation using Tar+Zstd with EOF header for fast random access.
/// File format: [Zstd Stream] containing [Tar Archive] + [.eof-header with dictionary]
/// </summary>
public class FileArchiveTarZstd : FileArchiveTarBased
{
    private const string COMPRESSION_LEVEL_OPTION = "tzstd-compression-level";
    private const string MEMORY_BUFFER_OPTION = "tzstd-memory-buffer";
    private const int DEFAULT_COMPRESSION_LEVEL = 10;
    private const int MIN_COMPRESSION_LEVEL = 1;
    private const int MAX_COMPRESSION_LEVEL = 22;

    /// <summary>
    /// Default constructor for module discovery
    /// </summary>
    public FileArchiveTarZstd() : base()
    {
    }

    /// <summary>
    /// Constructor with stream and options
    /// </summary>
    public FileArchiveTarZstd(Stream stream, ArchiveMode mode, IReadOnlyDictionary<string, string?> options)
        : base(stream, mode, options)
    {
    }

    public override string FilenameExtension => "tzstd";

    public override string DisplayName => Strings.FileArchiveTarZstd.DisplayName;

    public override string Description => Strings.FileArchiveTarZstd.Description;

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
            Strings.FileArchiveTarZstd.CompressionlevelShort,
            Strings.FileArchiveTarZstd.CompressionlevelLong,
            DEFAULT_COMPRESSION_LEVEL.ToString(),
            null,
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22"]
        ),
        new CommandLineArgument(
            MEMORY_BUFFER_OPTION,
            CommandLineArgument.ArgumentType.Boolean,
            Strings.FileArchiveTarZstd.MemorybufferShort,
            Strings.FileArchiveTarZstd.MemorybufferLong,
            "false"
        )
    ];

    protected override Stream CreateDecompressorStream(Stream inputStream)
        => new DecompressionStream(inputStream);

    protected override Stream CreateCompressorStream(Stream outputStream, int compressionLevel)
        => new CompressionStream(outputStream, compressionLevel, leaveOpen: true);
}
