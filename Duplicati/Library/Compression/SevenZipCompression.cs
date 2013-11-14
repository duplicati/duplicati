using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Compression
{
    public class SevenZipCompression: ICompressionHinting
    {
        // next file starts a new stream if the previous stream is larger than this
        private const int kStreamThreshold = 1 << 20; // 1 MB

        private FileStream m_file;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter m_writer;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.PlainEncoder m_copyEncoder;
        private ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.ThreadedEncoder m_lzma2Encoder;
        private master._7zip.Legacy.ArchiveReader m_reader;
        private master._7zip.Legacy.CArchiveDatabaseEx m_archive;
        private int m_threadCount;
        private bool m_lowOverheadMode;

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
                if(m_lowOverheadMode != value)
                {
                    if(m_lzma2Encoder != null)
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
        /// If the file exists and has a non-zero length we read it,
        /// otherwise we create a new archive.
        /// </summary>
        /// <param name="filename">The name of the file to read or write</param>
        /// <param name="options">The options passed on the commandline</param>
        public SevenZipCompression(string filename, Dictionary<string, string> options)
        {
            m_threadCount = DEFAULT_THREAD_COUNT;

            string threadCountSetting;
            int threadCountValue;
            if(options != null
                && options.TryGetValue(THREAD_COUNT_OPTION, out threadCountSetting)
                && Int32.TryParse(threadCountSetting, out threadCountValue)
                && threadCountValue > 0)
            {
                // arbitrary limit to avoid stupid mistakes
                if(threadCountValue > MAX_THREAD_COUNT)
                    threadCountValue = MAX_THREAD_COUNT;

                m_threadCount = threadCountValue;
            }

            var file = new FileInfo(filename);
            if(file.Exists && file.Length > 0)
                InitializeArchiveReader(filename);
            else
                InitializeArchiveWriter(filename);
        }

        private void InitializeArchiveReader(string file)
        {
            m_file = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            m_archive = new master._7zip.Legacy.CArchiveDatabaseEx();
            m_reader = new master._7zip.Legacy.ArchiveReader();
            m_reader.Open(m_file);
            m_reader.ReadDatabase(m_archive, null);
            m_archive.Fill();
        }

        private void InitializeArchiveWriter(string file)
        {
            m_file = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Delete);
            m_writer = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter(m_file);
        }

        public void Dispose()
        {
            if(m_archive != null)
                m_archive = null;

            if(m_reader != null)
                try { m_reader.Close(); }
                finally { m_reader = null; }

            if(m_writer != null)
            {
                try
                {
                    if(m_copyEncoder != null)
                        try
                        {
                            m_writer.ConnectEncoder(m_copyEncoder);
                            m_copyEncoder.Dispose();
                        }
                        finally { m_copyEncoder = null; }
    
                    if(m_lzma2Encoder != null)
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

            if(m_file != null)
                try { m_file.Dispose(); }
                finally { m_file = null; }
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
            if(m_reader == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoReaderError);

            StringComparison sc = Library.Utility.Utility.ClientFilenameStringComparision;

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
            if(item == null)
                throw new FileNotFoundException(Strings.SevenZipCompression.FileNotFoundError, file);

            if(!item.MTime.HasValue)
                return new DateTime(0);

            return item.MTime.Value;
        }

        public Stream OpenRead(string file)
        {
            if(string.IsNullOrEmpty(file))
                throw new ArgumentNullException("file");

            if(m_reader == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoReaderError);

            var item = m_reader.GetFiles(m_archive).FirstOrDefault(x => x.Name == file);
            if(item == null)
                throw new FileNotFoundException(Strings.SevenZipCompression.FileNotFoundError, file);

            return m_reader.OpenStream(m_archive, m_reader.GetFileIndex(m_archive, item), null);
        }

        #endregion

        #region Write Utilities

        private void mLzma2Encoder_OnOutputThresholdReached(object sender, EventArgs e)
        {
            m_writer.ConnectEncoder(m_lzma2Encoder);
        }

        private sealed class WriterEntry: ManagedLzma.LZMA.Master.SevenZip.IArchiveWriterEntry
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
            if(string.IsNullOrEmpty(file))
                throw new ArgumentNullException("file");

            if(m_writer == null)
                throw new InvalidOperationException(Strings.SevenZipCompression.NoWriterError);

            var entry = new WriterEntry(file, lastWrite);

            if(hint != CompressionHint.Noncompressible)
            {
                if(m_lzma2Encoder == null)
                {
                    if(m_lowOverheadMode)
                        m_lzma2Encoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.LzmaEncoder();
                    else
                        m_lzma2Encoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.Lzma2Encoder(m_threadCount);

                    m_lzma2Encoder.OnOutputThresholdReached += mLzma2Encoder_OnOutputThresholdReached;
                    m_lzma2Encoder.SetOutputThreshold(kStreamThreshold);
                }

                return m_lzma2Encoder.BeginWriteFile(entry);
            }
            else
            {
                if(m_copyEncoder == null)
                    m_copyEncoder = new ManagedLzma.LZMA.Master.SevenZip.ArchiveWriter.PlainEncoder();

                if(m_lzma2Encoder != null && m_lzma2Encoder == m_writer.CurrentEncoder)
                    m_lzma2Encoder.SetOutputThreshold(kStreamThreshold); // rearm threshold so we can switch back

                if(m_writer.CurrentEncoder != m_copyEncoder)
                    m_writer.ConnectEncoder(m_copyEncoder);

                return m_copyEncoder.BeginWriteFile(entry);
            }
        }

        public long Size
        {
            get
            {
                if(m_writer != null)
                    return m_writer.WrittenSize;
                else if(m_reader != null)
                    return m_file.Length;
                else
                    throw new InvalidOperationException();
            }
        }

        public long FlushBufferSize
        {
            get
            {
                if(m_writer != null)
                {
                    long remaining = m_writer.CurrentSizeLimit - m_writer.WrittenSize;
                    if(m_copyEncoder != null && m_copyEncoder != m_writer.CurrentEncoder)
                        remaining += m_copyEncoder.UpperBound;
                    if(m_lzma2Encoder != null && m_lzma2Encoder != m_writer.CurrentEncoder)
                        remaining += m_lzma2Encoder.UpperBound;
                    return remaining;
                }
                else if(m_reader != null)
                    return m_file.Length;
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
                        DEFAULT_THREAD_COUNT.ToString())
                };
            }
        }

        #endregion
    }
}
