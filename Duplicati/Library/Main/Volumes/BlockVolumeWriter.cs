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
