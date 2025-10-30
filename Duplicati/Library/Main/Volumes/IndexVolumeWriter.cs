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
using Duplicati.Library.Utility;
using System.Diagnostics;

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
        public bool IsVolumeActive => m_writer != null && m_streamwriter != null;
        public bool IsDisposed = false;
        public bool IsReadyForFinish = true;
        public string CallerId = string.Empty;

        private readonly List<string> m_calls = new List<string>();

        public IndexVolumeWriter(Options options)
            : base(options)
        {
            m_calls.Add($"Created IndexVolumeWriter - {CallerId} - {new StackTrace(true)}");
        }

        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Index; } }

        public void StartVolume(string filename)
        {

            m_calls.Add($"StartVolume {filename} - {CallerId} - {new StackTrace(true)}");
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
            if (!IsReadyForFinish)
                throw new InvalidOperationException("Index volume is not ready for finish");

            if (IsDisposed)
                throw new ObjectDisposedException("IndexVolumeWriter", "Cannot finish a disposed IndexVolumeWriter");

            if (m_writer == null || m_streamwriter == null)
                throw new InvalidOperationException($"No volume started, call StartVolume before finishing a volume: {(m_writer == null ? "m_writer is null" : "-")}, {(m_streamwriter == null ? "m_streamwriter is null" : "-")}, calls = {Environment.NewLine}{string.Join(Environment.NewLine, m_calls)}");

            m_calls.Add($"FinishVolume {volumehash} - {CallerId} - {new StackTrace(true)}");

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

            using var hashalg = HashFactory.CreateHasher(m_blockhash);
            var hash2 = Convert.ToBase64String(hashalg.ComputeHash(data, offset, size));
            if (hash2 != hash)
                throw new InvalidOperationException($"Attempted to write a blocklist with hash {hash} but data has hash {hash2}");
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
            m_calls.Add($"CopyFrom - {new StackTrace(true)}");

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
            m_calls.Add($"Dispose - {new StackTrace(true)}");
            if (m_writer != null || m_streamwriter != null)
                throw new InvalidOperationException("Attempted to dispose an index volume that was being written");
            IsDisposed = true;
            base.Dispose();
        }

    }
}
