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
using System.IO;
using Newtonsoft.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class IndexVolumeWriter : VolumeWriterBase
    {
        private StreamWriter m_streamwriter = null;
        private JsonWriter m_writer = null;
#if DEBUG
        private readonly HashSet<string> m_knownBlocklisthashes = new();
#endif

        private long m_volumes = 0;
        private long m_blocks = 0;
        private long m_blocklists = 0;

        public long VolumeCount { get { return m_volumes; } }
        public long BlockCount { get { return m_blocks; } }
        public long Blocklists { get { return m_blocklists; } }

        public IndexVolumeWriter(Options options)
            : base(options)
        {
        }

        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Index; } }

        public void StartVolume(string filename)
        {
            if (m_writer != null || m_streamwriter != null)
                throw new InvalidOperationException("Previous volume not finished, call FinishVolume before starting a new volume");

            m_volumes++;
            m_streamwriter = new StreamWriter(m_compression.CreateFile(INDEX_VOLUME_FOLDER + filename, CompressionHint.Compressible, DateTime.UtcNow));
            m_writer = new JsonTextWriter(m_streamwriter);
            m_writer.WriteStartObject();
            m_writer.WritePropertyName("blocks");
            m_writer.WriteStartArray();
        }

        public void AddBlock(string hash, long size)
        {
            m_writer.WriteStartObject();
            m_writer.WritePropertyName("hash");
            m_writer.WriteValue(hash);
            m_writer.WritePropertyName("size");
            m_writer.WriteValue(size);
            m_writer.WriteEndObject();
            m_blocks++;
        }

        public void FinishVolume(string volumehash, long volumesize)
        {
            m_writer.WriteEndArray();
            m_writer.WritePropertyName("volumehash");
            m_writer.WriteValue(volumehash);
            m_writer.WritePropertyName("volumesize");
            m_writer.WriteValue(volumesize);
            m_writer.WriteEndObject();

            try { m_writer.Close(); }
            finally { m_writer = null; }
            try { m_streamwriter.Dispose(); }
            finally { m_streamwriter = null; }
        }

        public void WriteBlocklist(string hash, byte[] data, int offset, int size)
        {
#if DEBUG
            // Nothing breaks with duplicates, but it should not be there
            if (m_knownBlocklisthashes.Contains(hash))
                throw new InvalidOperationException($"Attempted to write a blocklist with hash {hash} more than once");
            m_knownBlocklisthashes.Add(hash);
#endif

            if (size % m_blockhashsize != 0)
                throw new InvalidDataException($"Attempted to write a blocklist with {size} bytes which is not evenly divisible with {m_blockhashsize}");
            m_blocklists++;
            //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
            using (var s = m_compression.CreateFile(INDEX_BLOCKLIST_FOLDER + Library.Utility.Utility.Base64PlainToBase64Url(hash), CompressionHint.Noncompressible, DateTime.UtcNow))
                s.Write(data, offset, size);
        }

        public void WriteBlocklist(string hash, Stream source)
        {
#if DEBUG
            // Nothing breaks with duplicates, but it should not be there
            if (m_knownBlocklisthashes.Contains(hash))
                throw new InvalidOperationException($"Attempted to write a blocklist with hash {hash} more than once");
            m_knownBlocklisthashes.Add(hash);
#endif

            m_blocklists++;
            //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
            using (var s = m_compression.CreateFile(INDEX_BLOCKLIST_FOLDER + Library.Utility.Utility.Base64PlainToBase64Url(hash), CompressionHint.Noncompressible, DateTime.UtcNow))
            {
                var size = Library.Utility.Utility.CopyStream(source, s);
                if (size % m_blockhashsize != 0)
                    throw new InvalidDataException($"Wrote a blocklist with {size} bytes which is not evenly divisible with {m_blockhashsize}");
            }
        }

        public void CopyFrom(IndexVolumeReader rd, Func<string, string> filename_mapping)
        {
            foreach (var n in rd.Volumes)
            {
                this.StartVolume(filename_mapping(n.Filename));
                foreach (var x in n.Blocks)
                    this.AddBlock(x.Key, x.Value);
                this.FinishVolume(n.Hash, n.Length);
            }

            foreach (var b in rd.BlockLists)
                using (var s = b.Data)
                    this.WriteBlocklist(b.Hash, s);
        }

        public override void Dispose()
        {
            if (m_writer != null || m_streamwriter != null)
                throw new InvalidOperationException("Attempted to dispose an index volume that was being written");
            base.Dispose();
        }

    }
}
