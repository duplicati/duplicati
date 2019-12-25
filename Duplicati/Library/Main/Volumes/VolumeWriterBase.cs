using System;
using System.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Volumes
{
    public abstract class VolumeWriterBase : VolumeBase, IDisposable
    {
        protected ICompression m_compression;
        protected Library.Utility.TempFile m_localfile;
        protected Stream m_localFileStream;
        protected string m_volumename;
        public string LocalFilename { get { return m_localfile; } }
        public string RemoteFilename { get { return m_volumename; } }
        public Library.Utility.TempFile TempFile { get { return m_localfile; } }

        public abstract RemoteVolumeType FileType { get; }

        public long VolumeID { get; set; }

        public void SetRemoteFilename(string name)
        {
            m_volumename = name;
        }

        protected VolumeWriterBase(Options options)
            : this(options, DateTime.UtcNow)
        {
        }

        public static string GenerateGuid()
        {
            var s = Guid.NewGuid().ToString("N");

            //We can choose shorter GUIDs here

            return s;

        }

        public void ResetRemoteFilename(Options options, DateTime timestamp)
        {
            m_volumename = GenerateFilename(this.FileType, options.Prefix, GenerateGuid(), timestamp, options.CompressionModule, options.NoEncryption ? null : options.EncryptionModule);
        }

        protected VolumeWriterBase(Options options, DateTime timestamp)
            : base(options)
        {
            if (!string.IsNullOrWhiteSpace(options.AsynchronousUploadFolder))
                m_localfile = Library.Utility.TempFile.CreateInFolder(options.AsynchronousUploadFolder, true);
            else
                m_localfile = new Library.Utility.TempFile();

            ResetRemoteFilename(options, timestamp);

            m_localFileStream = new System.IO.FileStream(m_localfile, FileMode.Create, FileAccess.Write, FileShare.Read);
            m_compression = DynamicLoader.CompressionLoader.GetModule(options.CompressionModule, m_localFileStream, ArchiveMode.Write, options.RawOptions);

            if (m_compression == null)
                throw new UserInformationException(string.Format("Unsupported compression module: {0}", options.CompressionModule), "UnsupportedCompressionModule");

            if ((this is IndexVolumeWriter || this is FilesetVolumeWriter) && this.m_compression is ICompressionHinting hinting)
                hinting.LowOverheadMode = true;

            AddManifestFile();
        }

        protected void AddManifestFile()
        {
            using (var sr = new StreamWriter(m_compression.CreateFile(MANIFEST_FILENAME, CompressionHint.Compressible, DateTime.UtcNow), ENCODING))
                sr.Write(ManifestData.GetManifestInstance(m_blocksize, m_blockhash, m_filehash));
        }

        internal BackendHandler.FileEntryItem CreateFileEntryForUpload(Options options)
        {
            var fileEntry = new BackendHandler.FileEntryItem(BackendActionType.Put, this.RemoteFilename);
            fileEntry.SetLocalfilename(this.LocalFilename);
            fileEntry.Encrypt(options);
            fileEntry.UpdateHashAndSize(options);
            return fileEntry;
        }

        public void CreateFilesetFile(bool isFullBackup)
        {
            using (var sr = new StreamWriter(m_compression.CreateFile(FILESET_FILENAME, CompressionHint.Compressible, DateTime.UtcNow), ENCODING))
            {
                sr.Write(FilesetData.GetFilesetInstance(isFullBackup));
            }
        }

        public virtual void Dispose()
        {
            if (m_compression != null)
                try { m_compression.Dispose(); }
                finally { m_compression = null; }

            m_localfile.Protected = false;
            if (m_localFileStream != null)
                try { m_localFileStream.Dispose(); }
                finally { m_localFileStream = null; }

            if (m_localfile != null)
                try { m_localfile.Dispose(); }
                finally { m_localfile = null; }

            m_volumename = null;
        }

        public virtual void Close()
        {
            if (m_compression != null)
                try { m_compression.Dispose(); }
                finally { m_compression = null; }

            if (m_localFileStream != null)
                try { m_localFileStream.Dispose(); }
                finally { m_localFileStream = null; }
        }

        public long Filesize { get { return m_compression.Size + m_compression.FlushBufferSize; } }

    }
}
