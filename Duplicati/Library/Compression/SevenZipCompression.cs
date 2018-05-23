using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Compression
{
    public class SevenZipCompression : ICompressionHinting
    {
        // next file starts a new stream if the previous stream is larger than this
        private const int kStreamThreshold = 1 << 20; // 1 MB

        private Stream m_stream;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter m_writer;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.PlainEncoder m_copyEncoder;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.ThreadedEncoder m_lzma2Encoder;
        private master._7zip.Legacy.ArchiveReader m_reader;
        private master._7zip.Legacy.CArchiveDatabaseEx m_archive;
        private int m_threadCount;
        private bool m_lowOverheadMode;

        private ManagedLzma.LZMA.Master.LZMA.CLzma2EncProps m_encoderProps;

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="Duplicati.Library.Compression.SevenZipCompression"/>
        /// is in low overhead mode.
        /// </summary>
        /// <value><c>true</c> if low overhead mode; otherwise, <c>false</c>.</value>
        public bool LowOverheadMode
        {
            get { return m_lowOverheadMode; }
            set
            {
                if (m_lowOverheadMode != value)
                {
                    if (m_lzma2Encoder != null)
                        throw new NotSupportedException("Cannot change LowOverheadMode after writing has started.");

                    m_lowOverheadMode = value;
                }
            }
        }

        /// <summary>
        /// Default constructor, used to read file extension and supported commands.
        /// </summary>
        public SevenZipCompression() { }

        /// <summary>
        /// Constructs a new zip instance.
        /// Access mode is specified by mode parameter.
        /// Note that stream would not be disposed by FileArchiveZip instance so
        /// you may reuse it and have to dispose it yourself.
        /// </summary>
        /// <param name="stream">The stream to read or write depending access mode</param>
        /// <param name="mode">The archive acces mode</param>
        /// <param name="options">The options passed on the commandline</param>
        public SevenZipCompression(Stream stream, ArchiveMode mode, IDictionary<string, string> options)
        {
            InitializeCompression(options);
            // Preventing the stream being closed by ArchiveWriter so it could be reused.
            // Required for using with MemoryStream.
            m_stream = new ShaderStream(stream, true);
            if (mode == ArchiveMode.Write)
                InitializeArchiveWriter();
            else
            {
                stream.Position = 0;
                InitializeArchiveReader();
            }
        }

        private void InitializeCompression(IDictionary<string, string> options)
        {
            m_threadCount = DEFAULT_THREAD_COUNT;

            string threadCountSetting;
            int threadCountValue;
            if (options != null
                && options.TryGetValue(THREAD_COUNT_OPTION, out threadCountSetting)
                && Int32.TryParse(threadCountSetting, out threadCountValue)
                && threadCountValue > 0)
            {
                // arbitrary limit to avoid stupid mistakes
                if (threadCountValue > MAX_THREAD_COUNT)
                    threadCountValue = MAX_THREAD_COUNT;

                m_threadCount = threadCountValue;
            }

            string cplvl;
            int tmplvl;
            if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out cplvl) && int.TryParse(cplvl, out tmplvl))
                tmplvl = Math.Max(Math.Min(9, tmplvl), 0);
            else
                tmplvl = DEFAULT_COMPRESSION_LEVEL;

            m_encoderProps = new ManagedLzma.LZMA.Master.LZMA.CLzma2EncProps();
            m_encoderProps.Lzma2EncProps_Init();

            m_encoderProps.mLzmaProps.mLevel = tmplvl;
            m_encoderProps.mLzmaProps.mAlgo = Library.Utility.Utility.ParseBoolOption(options, COMPRESSION_FASTALGO_OPTION) ? 0 : 1;
        }

        private void InitializeArchiveReader()
        {
            m_archive = new master._7zip.Legacy.CArchiveDatabaseEx();
            m_reader = new master._7zip.Legacy.ArchiveReader();
            m_reader.Open(m_stream);
            m_reader.ReadDatabase(m_archive, null);
            m_archive.Fill();
        }

        private void InitializeArchiveWriter()
        {
            m_writer = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter(m_stream);
        }

        public void Dispose()
        {
            if (m_archive != null)
                m_archive = null;

            if (m_reader != null)
                try { m_reader.Close(); }
                finally { m_reader = null; }

            if (m_writer != null)
            {
                try
                {
                    if (m_copyEncoder != null)
                        try
                        {
                            m_writer.ConnectEncoder(m_copyEncoder);
                            m_copyEncoder.Dispose();
                        }
                        finally { m_copyEncoder = null; }

                    if (m_lzma2Encoder != null)
                        try
                        {
                            m_writer.ConnectEncoder(m_lzma2Encoder);
                            m_lzma2Encoder.Dispose();
                        }
                        finally { m_lzma2Encoder = null; }

                    m_writer.WriteFinalHeader();
                }
                finally { m_writer = null; }
            }

            if (m_stream != null)
                try { m_stream.Dispose(); }
                finally { m_stream = null; }
        }

        #region ICompression - Reader Methods

        public bool FileExists(string filename)
        {
            return m_reader.GetFiles(m_archive).Any(file => file.Name == filename);
        }

        public string[] ListFiles(string prefix)
        {
            return ListFilesWithSize(prefix).Select(x => x.Key).ToArray();
        }

        public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
        {
            if (m_reader == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoReaderError);

            StringComparison sc = Library.Utility.Utility.ClientFilenameStringComparison;

            return
                from n in m_reader.GetFiles(m_archive)
                where
                    !n.IsDir &&
                    (prefix == null || n.Name.StartsWith(prefix, sc))
                select new KeyValuePair<string, long>(n.Name, n.Size);
        }

        public DateTime GetLastWriteTime(string file)
        {
            var item = m_reader.GetFiles(m_archive).FirstOrDefault(x => x.Name == file);
            if (item == null)
                throw new FileNotFoundException(Strings.SevenZipCompression.FileNotFoundError, file);

            if (!item.MTime.HasValue)
                return new DateTime(0);

            return item.MTime.Value;
        }

        public Stream OpenRead(string file)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            if (m_reader == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoReaderError);

            var item = m_reader.GetFiles(m_archive).FirstOrDefault(x => x.Name == file);
            if (item == null)
                throw new FileNotFoundException(Strings.SevenZipCompression.FileNotFoundError, file);

            return m_reader.OpenStream(m_archive, m_reader.GetFileIndex(m_archive, item), null);
        }

        #endregion

        #region Write Utilities

        private void mLzma2Encoder_OnOutputThresholdReached(object sender, EventArgs e)
        {
            m_writer.ConnectEncoder(m_lzma2Encoder);
        }

        private sealed class WriterEntry : ManagedLzma.LZMA.Master.SevenZip.IArchiveWriterEntry
        {
            private string mName;
            private DateTime? mTimestamp;

            internal WriterEntry(string name, DateTime? timestamp)
            {
                mName = name;
                mTimestamp = timestamp;
            }

            public string Name
            {
                get { return mName; }
            }

            public FileAttributes? Attributes
            {
                get { return null; }
            }

            public DateTime? CreationTime
            {
                get { return null; }
            }

            public DateTime? LastWriteTime
            {
                get { return mTimestamp; }
            }

            public DateTime? LastAccessTime
            {
                get { return null; }
            }
        }

        #endregion

        #region ICompression - Writer Methods

        public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException(nameof(file));

            if (m_writer == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoWriterError);

            var entry = new WriterEntry(file, lastWrite);

            if (hint != CompressionHint.Noncompressible)
            {
                if (m_lzma2Encoder == null)
                {
                    if (m_lowOverheadMode)
                        m_lzma2Encoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.LzmaEncoder();
                    else
                        m_lzma2Encoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.Lzma2Encoder(m_threadCount, m_encoderProps);

                    m_lzma2Encoder.OnOutputThresholdReached += mLzma2Encoder_OnOutputThresholdReached;
                    m_lzma2Encoder.SetOutputThreshold(kStreamThreshold);
                }

                return m_lzma2Encoder.BeginWriteFile(entry);
            }
            else
            {
                if (m_copyEncoder == null)
                    m_copyEncoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.PlainEncoder();

                if (m_lzma2Encoder != null && m_lzma2Encoder == m_writer.CurrentEncoder)
                    m_lzma2Encoder.SetOutputThreshold(kStreamThreshold); // rearm threshold so we can switch back

                if (m_writer.CurrentEncoder != m_copyEncoder)
                    m_writer.ConnectEncoder(m_copyEncoder);

                return m_copyEncoder.BeginWriteFile(entry);
            }
        }

        public long Size
        {
            get
            {
                if (m_writer != null)
                    return m_writer.WrittenSize;
                else if (m_reader != null)
                    return m_stream.Length;
                else
                    throw new InvalidOperationException();
            }
        }

        public long FlushBufferSize
        {
            get
            {
                if (m_writer != null)
                {
                    long remaining = m_writer.CurrentSizeLimit - m_writer.WrittenSize;
                    if (m_copyEncoder != null && m_copyEncoder != m_writer.CurrentEncoder)
                        remaining += m_copyEncoder.UpperBound;
                    if (m_lzma2Encoder != null && m_lzma2Encoder != m_writer.CurrentEncoder)
                        remaining += m_lzma2Encoder.UpperBound;
                    return remaining;
                }
                else if (m_reader != null)
                    return m_stream.Length;
                else
                    throw new InvalidOperationException();
            }
        }

        #endregion

        #region ICompression - Methods which don't need an archive

        public string FilenameExtension
        {
            get { return "7z"; }
        }

        public string DisplayName
        {
            get { return Strings.SevenZipCompression.DisplayName; }
        }

        public string Description
        {
            get { return Strings.SevenZipCompression.Description; }
        }

        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string THREAD_COUNT_OPTION = "lzma-thread-count";

        /// <summary>
        /// The default compression level
        /// </summary>
        private static readonly int DEFAULT_THREAD_COUNT = Environment.ProcessorCount;

        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION = "7z-compression-level";

        /// <summary>
        /// The default compression level
        /// </summary>
        private const int DEFAULT_COMPRESSION_LEVEL = 5;

        /// <summary>
        /// The commandline option for toggling the compression algorithm
        /// </summary>
        private const string COMPRESSION_FASTALGO_OPTION = "7z-compression-fast-algorithm";

        /// <summary>
        /// The default setting for the fast-algorithm option
        /// </summary>
        private const bool DEFAULT_FASTALGO = false;

        /// <summary>
        /// Arbitrary limit to avoid problems with unreasonable user input.
        /// </summary>
        private const int MAX_THREAD_COUNT = 64;

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument> {
                    new CommandLineArgument(
                        THREAD_COUNT_OPTION,
                        CommandLineArgument.ArgumentType.Integer,
                        Strings.SevenZipCompression.ThreadcountShort,
                        Strings.SevenZipCompression.ThreadcountLong,
                        DEFAULT_THREAD_COUNT.ToString()),

                    new CommandLineArgument(
                        COMPRESSION_LEVEL_OPTION,
                        CommandLineArgument.ArgumentType.Enumeration,
                        Strings.SevenZipCompression.CompressionlevelShort,
                        Strings.SevenZipCompression.CompressionlevelLong,
                        DEFAULT_COMPRESSION_LEVEL.ToString(),
                        null,
                        new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}),

                    new CommandLineArgument(
                        COMPRESSION_FASTALGO_OPTION,
                        CommandLineArgument.ArgumentType.Boolean,
                        Strings.SevenZipCompression.FastalgoShort,
                        Strings.SevenZipCompression.FastalgoLong,
                        DEFAULT_FASTALGO.ToString()),
                };
            }
        }

        #endregion
    }
}
