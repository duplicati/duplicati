// Copyright (C) 2025, The Duplicati Team
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
        public BlockVolumeReader(ICompression compression, Options options)
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
            return ReadBlocklistUnverified(m_compression, Library.Utility.Utility.Base64PlainToBase64Url(hash), hashsize);
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
