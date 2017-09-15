using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Compression.Strings {
    internal static class FileArchiveZip {
        public static string CompressionlevelDeprecated(string optionname) { return LC.L(@"Please use the {0} option instead", optionname); }
        public static string CompressionlevelLong { get { return LC.L(@"This option controls the compression level used. A setting of zero gives no compression, and a setting of 9 gives maximum compression."); } }
        public static string CompressionlevelShort { get { return LC.L(@"Sets the Zip compression level"); } }
        public static string CompressionmethodLong(string optionname) { return LC.L(@"This option can be used to set an alternative compressor method, such as LZMA. Note that using another value than Deflate will cause the {0} option to be ignored.", optionname); }
        public static string CompressionmethodShort { get { return LC.L(@"Sets the Zip compression method"); } }
		public static string Compressionzip64Short { get { return LC.L(@"Toggles Zip64 support"); } }
		public static string Compressionzip64Long { get { return LC.L(@"The zip64 format is required for files larger than 4GiB, use this flag to toggle it"); } }
		public static string Description { get { return LC.L(@"This module provides the industry standard Zip compression. Files created with this module can be read by any standard-compliant zip application."); } }
        public static string DisplayName { get { return LC.L(@"Zip compression"); } }
        public static string FileNotFoundError(string filename) { return LC.L(@"File not found: {0}", filename); }
    }
    internal static class SevenZipCompression {
        public static string NoWriterError { get { return LC.L(@"Archive not opened for writing"); } }
        public static string NoReaderError { get { return LC.L(@"Archive not opened for reading"); } }
        public static string FileNotFoundError { get { return LC.L(@"The given file is not part of this archive"); } }
        public static string Description { get { return LC.L(@"7z Archive with LZMA2 support."); } }
        public static string DisplayName { get { return LC.L(@"7z Archive"); } }
        public static string ThreadcountLong { get { return LC.L(@"The number of threads used in LZMA 2 compression. Defaults to the number of processor cores."); } }
        public static string ThreadcountShort { get { return LC.L(@"Number of threads used in compression"); } }
        public static string CompressionlevelLong { get { return LC.L(@"This option controls the compression level used. A setting of zero gives no compression, and a setting of 9 gives maximum compression."); } }
        public static string CompressionlevelShort { get { return LC.L(@"Sets the 7z compression level"); } }
        public static string FastalgoLong { get { return LC.L(@"This option controls the compression algorithm used. Enabling this option will cause 7z to use the fast algorithm, which produces slightly less compression."); } }
        public static string FastalgoShort { get { return LC.L(@"Sets the 7z fast algorithm usage"); } }
    }
}
