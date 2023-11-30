using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class IndexVolumeReader : VolumeReaderBase
    {
        public interface IIndexBlocklist
        {
            string Hash { get; }
            long Length { get; }
            Stream Data { get; }
            IEnumerable<string> Blocklist { get; }
        }

        public interface IIndexBlockVolume
        {
            string Filename { get; }
            long Length { get; }
            string Hash { get; }
            IEnumerable<KeyValuePair<string, long>> Blocks { get; }
        }

        private class IndexBlockVolumeEnumerable : IEnumerable<IIndexBlockVolume>
        {
            private readonly ICompression m_compression;

            public IndexBlockVolumeEnumerable(ICompression compression)
            {
                m_compression = compression;
            }

            private class IndexBlockVolumeEnumerator : IEnumerator<IIndexBlockVolume>
            {
                private class BlockEnumerable : IEnumerable<KeyValuePair<string, long>>
                {
                    private class BlockEnumerator : IEnumerator<KeyValuePair<string, long>>
                    {
                        private readonly ICompression m_compression;
                        private readonly string m_filename;
                        private KeyValuePair<string, long>? m_current;
                        private System.IO.StreamReader m_stream;
                        private JsonReader m_reader;
                        private bool m_done;
                        private KeyValuePair<string, long>? m_volumeProps = null;

                        public BlockEnumerator(ICompression compression, string filename)
                        {
                            m_compression = compression;
                            m_filename = filename;
                            this.Reset();
                        }

                        public KeyValuePair<string, long> Current
                        {
                            get { return m_current.Value; }
                        }

                        public void Dispose()
                        {
                            if (m_reader != null)
                                this.ReadVolumeProps();

                            if (m_reader != null)
                                try { m_reader.Close(); }
                                finally { m_reader = null; }

                            if (m_stream != null)
                                try { m_stream.Dispose(); }
                                finally { m_stream = null; }
                        }

                        object System.Collections.IEnumerator.Current
                        {
                            get { return this.Current; }
                        }

                        public bool MoveNext()
                        {
                            if (m_done)
                                return false;

                            if (!m_reader.Read())
                                throw new InvalidDataException("Invalid JSON, EOF found while reading hashes");

                            if (m_reader.TokenType == JsonToken.EndArray)
                            {
                                m_done = true;
                                m_current = null;
                                return false;
                            }

                            if (m_reader.TokenType != JsonToken.StartObject)
                                throw new InvalidDataException(string.Format("Invalid JSON, expected StartObject, but got {0}, {1}", m_reader.TokenType, m_reader.Value));

                            var hash = ReadJsonStringProperty(m_reader, "hash");
                            var size = ReadJsonInt64Property(m_reader, "size");

                            m_current = new KeyValuePair<string, long>(hash, size);

                            while (m_reader.Read() && m_reader.TokenType != JsonToken.EndObject)
                            { /* skip */ }

                            return true;
                        }

                        public KeyValuePair<string, long> ReadVolumeProps()
                        {
                            if (m_volumeProps != null)
                                return m_volumeProps.Value;

                            while (this.MoveNext())
                            { /*skip*/ }

                            var hash = ReadJsonStringProperty(m_reader, "volumehash");
                            var size = ReadJsonInt64Property(m_reader, "volumesize");

                            return (m_volumeProps = new KeyValuePair<string, long>(hash, size)).Value;
                        }

                        public void Reset()
                        {
                            this.Dispose();
                            m_stream = new StreamReader(m_compression.OpenRead(m_filename));
                            m_reader = new JsonTextReader(m_stream);
                            SkipJsonToken(m_reader, JsonToken.StartObject);
                            var p = SkipJsonToken(m_reader, JsonToken.PropertyName);
                            if (p == null || p.ToString() != "blocks")
                                throw new InvalidDataException(string.Format("Invalid JSON, expected property \"blocks\", but got {0}, {1}", m_reader.TokenType, m_reader.Value));
                            SkipJsonToken(m_reader, JsonToken.StartArray);

                            m_current = null;
                            m_done = false;
                        }
                    }

                    private readonly ICompression m_compression;
                    private readonly string m_filename;
                    private BlockEnumerator m_enumerator;

                    public BlockEnumerable(ICompression compression, string filename)
                    {
                        m_compression = compression;
                        m_filename = filename;
                    }

                    public KeyValuePair<string, long> ReadVolumeProps()
                    {
                        if (m_enumerator == null)
                            m_enumerator = (BlockEnumerator)this.GetEnumerator();

                        return m_enumerator.ReadVolumeProps();
                    }

                    public IEnumerator<KeyValuePair<string, long>> GetEnumerator()
                    {
                        if (m_enumerator != null)
                            throw new NotSupportedException("Cannot read block stream twice");

                        return m_enumerator = new BlockEnumerator(m_compression, m_filename);
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                    {
                        return this.GetEnumerator();
                    }
                }

                private class IndexBlockVolume : IIndexBlockVolume
                {
                    private readonly ICompression m_compression;
                    private readonly string m_filename;
                    private long? m_length;
                    private string m_hash;
                    private BlockEnumerable m_blocks;

                    public IndexBlockVolume(ICompression compression, string filename)
                    {
                        m_compression = compression;
                        m_filename = filename;
                    }

                    public string Filename { get { return m_filename.Substring(INDEX_VOLUME_FOLDER.Length); } }

                    private void ReadVolumeProps()
                    {
                        if (m_length == null || m_hash == null)
                        {
                            if (m_blocks == null)
                                m_blocks = (BlockEnumerable)this.Blocks;

                            var kp = m_blocks.ReadVolumeProps();
                            m_length = kp.Value;
                            m_hash = kp.Key;
                        }
                    }

                    public long Length
                    {
                        get
                        {
                            this.ReadVolumeProps();
                            return m_length.Value;
                        }
                    }

                    public string Hash
                    {
                        get
                        {
                            this.ReadVolumeProps();
                            return m_hash;
                        }
                    }

                    public IEnumerable<KeyValuePair<string, long>> Blocks
                    {
                        get
                        {
                            var n = new BlockEnumerable(m_compression, m_filename);
                            if (m_blocks == null)
                                m_blocks = n;
                                
                            return n;
                        }
                    }
                }

                private readonly ICompression m_compression;
                private IndexBlockVolume m_current;
                private string[] m_files;
                private long m_index;

                public IndexBlockVolumeEnumerator(ICompression compression)
                {
                    m_compression = compression;
                    this.Reset();
                }

                public IIndexBlockVolume Current
                {
                    get { return m_current; }
                }

                public void Dispose()
                {
                }

                object System.Collections.IEnumerator.Current
                {
                    get { return this.Current; }
                }

                public bool MoveNext()
                {
                    if (m_index + 1 >= m_files.Length)
                        return false;
                    m_index++;

                    while (m_index < m_files.Length && ParseFilename(m_files[m_index]) == null)
                        m_index++;

                    m_current = new IndexBlockVolume(m_compression, m_files[m_index]);

                    return true;
                }

                public void Reset()
                {
                    m_files = m_compression.ListFiles(INDEX_VOLUME_FOLDER);
                    m_index = -1;
                    m_current = null;
                }
            }

            public IEnumerator<IIndexBlockVolume> GetEnumerator() { return new IndexBlockVolumeEnumerator(m_compression); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }

        private class IndexBlocklistEnumerable : IEnumerable<IIndexBlocklist>
        {
            private readonly ICompression m_compression;
            private readonly long m_hashsize;
            private readonly string m_blockHashAlgorithm;


            public IndexBlocklistEnumerable(ICompression compression, long hashsize, string blockHashAlgorithm)
            {
                m_compression = compression;
                m_hashsize = hashsize;
                m_blockHashAlgorithm = blockHashAlgorithm;
            }

            private class IndexBlocklistEnumerator : IEnumerator<IIndexBlocklist>
            {
                private class IndexBlocklist : IIndexBlocklist
                {
                    private readonly ICompression m_compression;
                    private readonly string m_filename;
                    private readonly string m_blockHashAlgorithm;
                    private readonly long m_size;
                    private readonly long m_hashsize;

                    public IndexBlocklist(ICompression compression, string filename, long size, long hashsize, string blockHashAlgorithm)
                    {
                        m_compression = compression;
                        m_filename = filename;
                        m_size = size;
                        m_hashsize = hashsize;
                        m_blockHashAlgorithm = blockHashAlgorithm;
                    }

                    public string Hash
                    {
                        //Filenames are encoded with "modified Base64 for URL" https://en.wikipedia.org/wiki/Base64#URL_applications, 
                        // to prevent clashes with filename paths where forward slash has special meaning
                        get { return Library.Utility.Utility.Base64UrlToBase64Plain(m_filename.Substring(INDEX_BLOCKLIST_FOLDER.Length)); }
                    }

                    public long Length
                    {
                        get { return m_size; }
                    }
                    
                    public Stream Data
                    {
                        get { return m_compression.OpenRead(m_filename); }
                    }

                    public IEnumerable<string> Blocklist
                    {
                        get { return VolumeReaderBase.ReadBlocklistVerified(m_compression, m_filename, m_hashsize, Hash, m_blockHashAlgorithm); }
                    }
                }

                private readonly ICompression m_compression;
                private long m_index;
                private KeyValuePair<string, long>[] m_files;
                private IndexBlocklist m_current;
                private readonly long m_hashsize;
                private readonly string m_blockHashAlgorithm;

                public IndexBlocklistEnumerator(ICompression compression, long hashsize, string blockHashAlgorithm)
                {
                    m_compression = compression;
                    m_hashsize = hashsize;
                    m_blockHashAlgorithm = blockHashAlgorithm;
                    this.Reset();
                }

                public IIndexBlocklist Current { get { return m_current; } }
                public void Dispose() { }
                object System.Collections.IEnumerator.Current { get { return this.Current; } }

                private readonly System.Text.RegularExpressions.Regex m_base64_urlsafe_detector = new System.Text.RegularExpressions.Regex("[a-zA-Z0-9-_]+={0,2}");
                private bool IsValidBase64Hash(string value, long hashsize)
                {
                    if (value.Length != ((hashsize + 2) / 3) * 4)
                        return false;

                    var m = m_base64_urlsafe_detector.Match(value);
                    return m.Success && m.Length == value.Length;
                }

                public bool MoveNext()
                {
                    if (m_index + 1 >= m_files.Length)
                        return false;
                    m_index++;

                    while (m_index < m_files.Length && IsValidBase64Hash(m_files[m_index].Key, m_hashsize))
                        m_index++;

                    m_current = new IndexBlocklist(m_compression, m_files[m_index].Key, m_files[m_index].Value, m_hashsize, m_blockHashAlgorithm);

                    return true;
                }

                public void Reset()
                {
                    m_files = m_compression.ListFilesWithSize(INDEX_BLOCKLIST_FOLDER).ToArray();
                    m_index = -1;
                    m_current = null;
                }
            }

            public IEnumerator<IIndexBlocklist> GetEnumerator() { return new IndexBlocklistEnumerator(m_compression, m_hashsize, m_blockHashAlgorithm); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }

        private readonly long m_hashsize;

        public IndexVolumeReader(ICompression compression, Options options, long hashsize)
            : base(compression, options)
        {
            m_hashsize = hashsize;
        }

        public IndexVolumeReader(string compressor, string file, Options options, long hashsize)
            : base(compressor, file, options)
        {
            m_hashsize = hashsize;
        }

        public IEnumerable<IIndexBlockVolume> Volumes { get { return new IndexBlockVolumeEnumerable(m_compression); } }
        public IEnumerable<IIndexBlocklist> BlockLists { get { return new IndexBlocklistEnumerable(m_compression, m_hashsize, m_blockhash); } }
    }
}
