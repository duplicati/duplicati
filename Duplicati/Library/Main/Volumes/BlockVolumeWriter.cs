using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class BlockVolumeWriter : VolumeWriterBase
    {
        private long m_blocks;
        private long m_sourcesize;

        public long Blocks { get { return m_blocks; } }
        public long SourceSize { get { return m_sourcesize; } }
        
        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Blocks; } }

        public BlockVolumeWriter(Options options)
            : base(options)
        {
        }

        public void AddBlock(string hash, byte[] data, int offset, int size, CompressionHint hint)
        {
            m_blocks++;
            m_sourcesize += size;

            //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
            using (var s = m_compression.CreateFile(Library.Utility.Utility.Base64PlainToBase64Url(hash), hint, DateTime.UtcNow))
                s.Write(data, offset, size);
        }
    }
}
