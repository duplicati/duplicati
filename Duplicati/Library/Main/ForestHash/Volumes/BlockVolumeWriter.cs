using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Volumes
{
    public class BlockVolumeWriter : VolumeWriterBase
    {
        private long m_blocks;
        private long m_sourcesize;

        public long Blocks { get { return m_blocks; } }
        public long SourceSize { get { return m_sourcesize; } }

        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Blocks; } }

        public BlockVolumeWriter(FhOptions options)
            : base(options)
        {
        }

        public void AddBlock(string hash, byte[] data, int size)
        {
            m_blocks++;
            m_sourcesize += size;

            //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
            // to prevent clashes with filename paths where forward slash has special meaning
            using (var s = m_compression.CreateFile(hash.Replace('+', '-').Replace('/', '_'), DateTime.UtcNow))
                s.Write(data, 0, size);
        }
    }
}
