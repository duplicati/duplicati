using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using System.IO;

namespace Duplicati.Library.Main.Volumes
{
    public class BlockVolumeReader : VolumeReaderBase
    {
        public BlockVolumeReader(IArchiveReader compression, Options options)
            : base(compression, options)
        {
        }
        
        public BlockVolumeReader(string compressor, string file, Options options)
            : base(compressor, file, options)
        {
        }
        
        public int ReadBlock(string hash, byte[] blockbuffer)
        {
            using (var fs = m_compression.OpenRead(Library.Utility.Utility.Base64PlainToBase64Url(hash)))
                return Library.Utility.Utility.ForceStreamRead(fs, blockbuffer, blockbuffer.Length);
        }

        public IEnumerable<string> ReadBlocklist(string hash, long hashsize)
        {
            return ReadBlocklist(m_compression, Library.Utility.Utility.Base64PlainToBase64Url(hash), hashsize);
        }

        public Stream ReadBlocklistRaw(string hash)
        {
            return m_compression.OpenRead(Library.Utility.Utility.Base64PlainToBase64Url(hash));
        }

        //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
        // to prevent clashes with filename paths where forward slash has special meaning
        private readonly System.Text.RegularExpressions.Regex m_base64_urlsafe_detector = new System.Text.RegularExpressions.Regex("[a-zA-Z0-9-_]+={0,2}");
        public IEnumerable<KeyValuePair<string, long>> Blocks
        {
            get
            {
                return
                    from n in m_compression.ListFilesWithSize(null)
                    let valid = m_base64_urlsafe_detector.Match(n.Key)
                    where valid.Success && valid.Length == n.Key.Length && n.Key.Length % 4 == 0 && n.Key != MANIFEST_FILENAME
                    select new KeyValuePair<string, long>(Library.Utility.Utility.Base64UrlToBase64Plain(n.Key), n.Value);
            }
        }
    }
}
