// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Compression.Strings {
    internal static class FileArchiveZip {
        public static string Description { get { return LC.L(@"This module provides the industry standard ZIP compression. Files created with this module can be read by any standard-compliant ZIP application."); } }
        public static string DisplayName { get { return LC.L(@"ZIP compression"); } }
        public static string CompressionlevelDeprecated(string optionname) { return LC.L(@"Use the option --{0} instead.", optionname); }
        public static string CompressionlevelLong { get { return LC.L(@"This option controls the compression level used. A setting of zero gives no compression, and a setting of 9 gives maximum compression."); } }
        public static string CompressionlevelShort { get { return LC.L(@"Set the ZIP compression level"); } }
        public static string CompressionmethodLong(string optionname) { return LC.L(@"Use this option to set an alternative compressor method, such as LZMA. Note that using another value than Deflate will cause the option --{0} to be ignored.", optionname); }
        public static string CompressionmethodShort { get { return LC.L(@"Set the ZIP compression method"); } }
        public static string Compressionzip64Long { get { return LC.L(@"The ZIP64 format is required for files larger than 4GiB. Use this option to toggle it."); } }
        public static string Compressionzip64Short { get { return LC.L(@"Toggle ZIP64 support"); } }
        public static string FileNotFoundError(string filename) { return LC.L(@"File not found: {0}", filename); }
    }
    internal static class SevenZipCompression {
        public static string Description { get { return LC.L(@"*Experimental*: 7z Archive with LZMA2 support."); } }
        public static string DisplayName { get { return LC.L(@"Experimental - 7z Archive"); } }
        public static string NoWriterError { get { return LC.L(@"Archive not opened for writing"); } }
        public static string NoReaderError { get { return LC.L(@"Archive not opened for reading"); } }
        public static string FileNotFoundError { get { return LC.L(@"The given file is not part of this archive"); } }
        public static string ThreadcountLong { get { return LC.L(@"The number of threads used in LZMA 2 compression. Defaults to the number of processor cores."); } }
        public static string ThreadcountShort { get { return LC.L(@"Number of threads used in compression"); } }
        public static string CompressionlevelLong { get { return LC.L(@"This option controls the compression level used. A setting of zero gives no compression, and a setting of 9 gives maximum compression."); } }
        public static string CompressionlevelShort { get { return LC.L(@"Set the 7z compression level"); } }
        public static string FastalgoLong { get { return LC.L(@"This option controls the compression algorithm used. Enabling this option will cause 7z to use the fast algorithm, which produces slightly less compression."); } }
        public static string FastalgoShort { get { return LC.L(@"Set the 7z fast algorithm usage"); } }
    }
}
