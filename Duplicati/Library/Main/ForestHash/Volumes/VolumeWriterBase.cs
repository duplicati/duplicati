using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.ForestHash.Volumes
{
    public abstract class VolumeWriterBase : VolumeBase, IDisposable
    {
        protected ICompression m_compression;
        protected Library.Utility.TempFile m_localfile;
        protected string m_volumename;
        public string LocalFilename { get { return m_localfile; } }
        public string RemoteFilename { get { return m_volumename; } }

        public abstract RemoteVolumeType FileType { get; }

        public VolumeWriterBase(FhOptions options)
            : this(options, DateTime.UtcNow)
        {
        }

        public VolumeWriterBase(FhOptions options, DateTime timestamp)
            : base(options)
        {
            if (this.FileType == RemoteVolumeType.Files)
                m_volumename = options.BackupPrefix + "-" + (this.FileType.ToString().ToLowerInvariant()) + "-" + Utility.Utility.SerializeDateTime(timestamp) + "." + options.CompressionModule;
            else
                m_volumename = options.BackupPrefix + "-" + (this.FileType.ToString().ToLowerInvariant()) + "-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray()) + "." + options.CompressionModule;

            var tempfolder = options.AsynchronousUpload ? options.AsynchronousUploadFolder : (options.TempDir ?? Utility.TempFolder.SystemTempPath);
            m_localfile = new Utility.TempFile(System.IO.Path.Combine(tempfolder, m_volumename));

            if (!options.NoEncryption)
                m_volumename += "." + options.EncryptionModule;

            m_compression = DynamicLoader.CompressionLoader.GetModule(options.CompressionModule, m_localfile, options.RawOptions);
            AddManifestfile();
        }

        protected void AddManifestfile()
        {
            using (var sr = new StreamWriter(m_compression.CreateFile(MANIFEST_FILENAME, CompressionHint.Compressible, DateTime.UtcNow), ENCODING))
                sr.Write(ManifestData.GetManifestInstance(m_blocksize));
        }

        public virtual void Dispose()
        {
            if (m_compression != null)
                try { m_compression.Dispose(); }
                finally { m_compression = null; }

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
        }

        public long Filesize { get { return m_compression.Size + m_compression.FlushBufferSize; } }

        public void SetTempFile(Utility.TempFile tmpfile)
        {
            if (m_localfile != null)
                m_localfile.Dispose();

            m_localfile = tmpfile;
        }
    }
}
