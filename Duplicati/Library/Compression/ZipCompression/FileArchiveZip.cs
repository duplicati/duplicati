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

using System;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using SharpCompress.Common;

namespace Duplicati.Library.Compression.ZipCompression
{
    /// <summary>
    /// An abstraction of a ZIP archive as a FileArchive, based on either SharpCompress or System.IO.Compression.
    /// Please note, duplicati does not require both Read &amp; Write access at the same time so this has not been implemented.
    /// </summary>
    public class FileArchiveZip : ICompression
    {
        /// <summary>
        /// The compression type used by the ZIP archive
        /// </summary>
        public enum CompressionLibrary
        {
            /// <summary>
            /// Automatically select the best library
            /// </summary>
            Auto = 0,
            /// <summary>
            /// Use the SharpCompress library
            /// </summary>
            SharpCompress = 1,
            /// <summary>
            /// Use the built-in library
            /// </summary>
            BuiltIn = 2
        }

        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileArchiveZip>();

        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION = "zip-compression-level";

        /// <summary>
        /// The old commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION_ALIAS = "compression-level";

        /// <summary>
        /// The commandline option for toggling the compression method
        /// </summary>
        private const string COMPRESSION_METHOD_OPTION = "zip-compression-method";
        /// <summary>
        /// The commandline option for toggling the ZIP64 support
        /// </summary>
        private const string COMPRESSION_ZIP64_OPTION = "zip-compression-zip64";
        /// <summary>
        /// The commandline option for toggling the compression library
        /// </summary>
        private const string COMPRESSION_LIBRARY_OPTION = "zip-compression-library";

        /// <summary>
        /// The default compression level
        /// </summary>
        private const SharpCompress.Compressors.Deflate.CompressionLevel DEFAULT_COMPRESSION_LEVEL = SharpCompress.Compressors.Deflate.CompressionLevel.Level9;

        /// <summary>
        /// The default compression method
        /// </summary>
        private const CompressionType DEFAULT_COMPRESSION_METHOD = CompressionType.Deflate;

        /// <summary>
        /// The default setting for the ZIP64 support
        /// </summary>
        private const bool DEFAULT_ZIP64 = false;

        /// <summary>
        /// The archive to use
        /// </summary>
        private readonly IZipArchive m_archive;

        /// <summary>
        /// The fallback archive to use, if any
        /// </summary>
        private IZipArchive? m_fallbackArchive = null;

        /// <summary>
        /// The parameters used to create the archive
        /// </summary>
        private readonly (Stream Stream, ParsedZipOptions Options, CompressionLibrary Library) m_creationParams;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public FileArchiveZip() { m_archive = null!; }

        /// <summary>
        /// Constructs a new Zip instance.
        /// Access mode is specified by mode parameter.
        /// Note that stream would not be disposed by FileArchiveZip instance so
        /// you may reuse it and have to dispose it yourself.
        /// </summary>
        /// <param name="stream">The stream to read or write depending access mode</param>
        /// <param name="mode">The archive access mode</param>
        /// <param name="options">The options passed on the commandline</param>
        public FileArchiveZip(Stream stream, ArchiveMode mode, IDictionary<string, string?> options)
        {
            var compressionType = DEFAULT_COMPRESSION_METHOD;
            var compressionLevel = DEFAULT_COMPRESSION_LEVEL;

            var usingZip64 = options.ContainsKey(COMPRESSION_ZIP64_OPTION)
                ? Utility.Utility.ParseBoolOption(options.AsReadOnly(), COMPRESSION_ZIP64_OPTION)
                : DEFAULT_ZIP64;

            CompressionType tmptype;
            if (options.TryGetValue(COMPRESSION_METHOD_OPTION, out var cpmethod) && Enum.TryParse<SharpCompress.Common.CompressionType>(cpmethod, true, out tmptype))
                compressionType = tmptype;

            if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out var cplvl) && int.TryParse(cplvl, out var tmplvl))
                compressionLevel = (SharpCompress.Compressors.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);
            else if (options.TryGetValue(COMPRESSION_LEVEL_OPTION_ALIAS, out cplvl) && int.TryParse(cplvl, out tmplvl))
                compressionLevel = (SharpCompress.Compressors.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);

            var compressionLibrary = CompressionLibrary.Auto;
            if (options.ContainsKey(COMPRESSION_LIBRARY_OPTION) && options.TryGetValue(COMPRESSION_LIBRARY_OPTION, out var cplib) && Enum.TryParse<CompressionLibrary>(cplib, true, out var tmpcplib))
                compressionLibrary = tmpcplib;

            var unittestMode = Utility.Utility.ParseBoolOption(options.AsReadOnly(), "unittest-mode");
            var parsedOptions = new ParsedZipOptions(compressionLevel, compressionType, usingZip64, unittestMode);

            var userCompressionLibrary = compressionLibrary;
            if (compressionLibrary == CompressionLibrary.Auto)
            {
                if (compressionType != CompressionType.Deflate || usingZip64)
                    compressionLibrary = CompressionLibrary.SharpCompress;
                else
                    compressionLibrary = CompressionLibrary.BuiltIn;
            }

            IZipArchive? archive = null;
            try
            {
                if (compressionLibrary == CompressionLibrary.BuiltIn)
                    archive = new BuiltinZipArchive(stream, mode, parsedOptions);
            }
            catch (Exception ex)
            {
                if (mode == ArchiveMode.Write)
                    throw;

                Log.WriteWarningMessage(LOGTAG, "system-io-compression-error", ex, "Failed to create built-in ZIP archive, falling back to SharpCompress");
            }

            m_archive = archive ?? new SharpCompressZipArchive(stream, mode, parsedOptions);
            m_creationParams = (stream, parsedOptions, userCompressionLibrary);
        }

        #region IFileArchive Members
        /// <summary>
        /// Gets the filename extension used by the compression module
        /// </summary>
        public string FilenameExtension { get { return "zip"; } }
        /// <summary>
        /// Gets a friendly name for the compression module
        /// </summary>
        public string DisplayName { get { return Strings.FileArchiveZip.DisplayName; } }
        /// <summary>
        /// Gets a description of the compression module
        /// </summary>
        public string Description { get { return Strings.FileArchiveZip.Description; } }

        /// <summary>
        /// Gets a list of commands supported by the compression module
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                // Various compression methods to use
                var methods = new[]
                {
                    CompressionType.None.ToString(),
                    CompressionType.Deflate.ToString(),
                    CompressionType.BZip2.ToString(),
                    CompressionType.LZMA.ToString(),
                    CompressionType.PPMd.ToString(),
                    CompressionType.GZip.ToString(),
                    CompressionType.Xz.ToString(),
                    CompressionType.Deflate64.ToString(),
                };

                return new List<ICommandLineArgument>([
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}),
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION_ALIAS, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}, Strings.FileArchiveZip.CompressionlevelDeprecated(COMPRESSION_LEVEL_OPTION)),
                    new CommandLineArgument(COMPRESSION_METHOD_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionmethodShort, Strings.FileArchiveZip.CompressionmethodLong(COMPRESSION_LEVEL_OPTION), DEFAULT_COMPRESSION_METHOD.ToString(), null, methods),
                    new CommandLineArgument(COMPRESSION_ZIP64_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.FileArchiveZip.Compressionzip64Short, Strings.FileArchiveZip.Compressionzip64Long, DEFAULT_ZIP64.ToString()),
                    new CommandLineArgument(COMPRESSION_LIBRARY_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlibraryShort, Strings.FileArchiveZip.CompressionlibraryLong, CompressionLibrary.Auto.ToString(), null, Enum.GetNames(typeof(CompressionLibrary)))
                ]);
            }
        }

        public long Size => m_archive.Size;

        public long FlushBufferSize => m_archive.FlushBufferSize;

        #endregion

        public void Dispose()
        {
            if (m_fallbackArchive == null)
                m_archive.Dispose();
            m_fallbackArchive?.Dispose();
        }

        public string[] ListFiles(string prefix)
            => m_archive.ListFiles(prefix);

        public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
            => m_archive.ListFilesWithSize(prefix);

        public Stream? OpenRead(string file)
        {
            // If we have started the fallback archive, we should continue using it
            if (m_fallbackArchive != null)
                return m_fallbackArchive.OpenRead(file);

            try
            {
                return m_archive.OpenRead(file);
            }
            catch (Exception ex)
            {
                if (m_creationParams.Library == CompressionLibrary.Auto && m_archive is not SharpCompressZipArchive && m_creationParams.Stream.CanSeek)
                {
                    Log.WriteWarningMessage(LOGTAG, "CompressionReadErrorFallback", ex, "Failed to open file with built-in ZIP archive, falling back to SharpCompress");

                    try
                    {
                        m_creationParams.Item1.Seek(0, SeekOrigin.Begin);
                        m_archive.Dispose();
                    }
                    catch
                    {

                    }

                    m_fallbackArchive = new SharpCompressZipArchive(m_creationParams.Stream, ArchiveMode.Read, m_creationParams.Options);
                    return m_fallbackArchive.OpenRead(file);
                }

                throw;
            }
        }

        public DateTime GetLastWriteTime(string file)
            => m_archive.GetLastWriteTime(file);

        public bool FileExists(string file)
            => m_archive.FileExists(file);

        public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
            => m_archive.CreateFile(file, hint, lastWrite);
    }
}
