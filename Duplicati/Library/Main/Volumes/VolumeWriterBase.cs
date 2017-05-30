using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public abstract class VolumeWriterBase : VolumeBase, IDisposable
    {
        protected ICompression m_compression;
        protected Library.Utility.TempFile m_localfile;
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

        public VolumeWriterBase(Options options)
            : this(options, DateTime.UtcNow)
        {
        }
        
        public static string GenerateGuid(Options options)
        {
            var s = Guid.NewGuid().ToString("N");
            
            //We can choose shorter GUIDs here
            
            return s;
            
        }

        public void ResetRemoteFilename(Options options, DateTime timestamp)
        {
            m_volumename = GenerateFilename(this.FileType, options.Prefix, GenerateGuid(options), timestamp, options.CompressionModule, options.NoEncryption ? null : options.EncryptionModule);
        }

        public VolumeWriterBase(Options options, DateTime timestamp)
            : base(options)
        {
            if (!string.IsNullOrWhiteSpace(options.AsynchronousUploadFolder))
                m_localfile = Library.Utility.TempFile.CreateInFolder(options.AsynchronousUploadFolder);
            else
                m_localfile = new Library.Utility.TempFile();

            ResetRemoteFilename(options, timestamp);
            m_compression = DynamicLoader.CompressionLoader.GetModule(options.CompressionModule, m_localfile, options.RawOptions);
            
            if(m_compression == null)
                throw new UserInformationException(string.Format("Unsupported compression module: {0}", options.CompressionModule));
            
            if ((this is IndexVolumeWriter || this is FilesetVolumeWriter) && m_compression is Library.Interface.ICompressionHinting)
                ((Library.Interface.ICompressionHinting)m_compression).LowOverheadMode = true;
                
            AddManifestfile();
        }

        protected void AddManifestfile()
        {
            using (var sr = new StreamWriter(m_compression.CreateFile(MANIFEST_FILENAME, CompressionHint.Compressible, DateTime.UtcNow), ENCODING))
                sr.Write(ManifestData.GetManifestInstance(m_blocksize, m_blockhash, m_filehash));
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

        public void SetTempFile(Library.Utility.TempFile tmpfile)
        {
            if (m_localfile != null)
                m_localfile.Dispose();

            m_localfile = tmpfile;
        }
    }
}
