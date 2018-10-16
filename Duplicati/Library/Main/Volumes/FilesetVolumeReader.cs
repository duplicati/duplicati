using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class FilesetVolumeReader : VolumeReaderBase
    {
        private class FileEntryEnumerable : IEnumerable<IFileEntry>
        {
            private class FileEntryEnumerator : IEnumerator<IFileEntry>
            {
                private class FileEntry : IFileEntry
                {
                    private class BlocklistHashEnumerable : IEnumerable<string>
                    {
                        private class BlocklistHashEnumerator : IEnumerator<string>
                        {
                            private readonly JsonReader m_reader;
                            private string m_current;
                            private bool m_firstReset = true;
                            private bool m_done = false;
                            public BlocklistHashEnumerator(JsonReader reader)
                            {
                                m_reader = reader;
                            }


                            public string Current
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
                                m_firstReset = false;
                                if (m_reader == null || m_done)
                                    return false;

                                if (!m_reader.Read())
                                    throw new InvalidDataException("Got EOF while reading blocklist hashes");

                                if (m_reader.TokenType == JsonToken.EndArray)
                                {
                                    m_done = true;
                                    return false;
                                }

                                if (m_reader.TokenType != JsonToken.String)
                                    throw new InvalidDataException(string.Format("Invalid JSON, expected String, but found {0}, {1}", m_reader.TokenType, m_reader.Value));

                                m_current = m_reader.Value == null ? null : m_reader.Value.ToString();
                                return true;
                            }

                            public void Reset()
                            {
                                if (!m_firstReset)
                                    throw new NotImplementedException();
                            }
                        }

                        private readonly JsonReader m_reader;
                        public BlocklistHashEnumerable(JsonReader reader)
                        {
                            m_reader = reader;
                        }

                        public IEnumerator<string> GetEnumerator()
                        {
                            return new BlocklistHashEnumerator(m_reader);
                        }

                        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                        {
                            return this.GetEnumerator();
                        }
                    }

                    public FilelistEntryType Type { get; private set; }
                    public string TypeString { get; private set; }
                    public string Path { get; private set; }
                    public string Hash { get; private set; }
                    public long Size { get; private set; }
                    public DateTime Time { get; private set; }
                    public string Metahash { get; private set; }
                    public string Metablockhash { get; private set; }
                    public long Metasize { get; private set; }
                    public string Blockhash { get; private set; }
                    public long Blocksize { get; private set; }
                    public IEnumerable<string> BlocklistHashes { get; private set; }
                    public IEnumerable<string> MetaBlocklistHashes { get; private set; }
                    private readonly JsonReader m_reader;

                    public FileEntry(JsonReader reader)
                    {
                        m_reader = reader;
                        this.TypeString = ReadJsonStringProperty(m_reader, "type");

                        FilelistEntryType et;
                        if (!Enum.TryParse<FilelistEntryType>(this.TypeString, true, out et))
                            et = FilelistEntryType.Unknown;
                        this.Type = et;

                        if (this.Type == FilelistEntryType.Unknown)
                        {
                            //Skip this entry by finding the EndObject that matches the StartObject
                            long balance = 1;
                            while (balance > 0 && m_reader.Read())
                            {
                                if (m_reader.TokenType == JsonToken.StartObject)
                                    balance++;
                                else if (m_reader.TokenType == JsonToken.EndObject)
                                    balance--;
                            }

                            if (balance != 0)
                                throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry of type {0}", this.TypeString));
                        }
                        else
                        {

                            this.Path = ReadJsonStringProperty(m_reader, "path");

                            if (this.Type == FilelistEntryType.File || this.Type == FilelistEntryType.AlternateStream)
                            {
                                this.Hash = ReadJsonStringProperty(m_reader, "hash");
                                this.Size = ReadJsonInt64Property(m_reader, "size");
                                this.Time = ReadJsonDateTimeProperty(m_reader, "time");
                            }

                            if (!m_reader.Read())
                                throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));
                            if (m_reader.TokenType == JsonToken.PropertyName && m_reader.Value != null && m_reader.Value.ToString() == "metahash")
                            {
                                if (!m_reader.Read())
                                    throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));

                                this.Metahash = m_reader.Value.ToString();
                                this.Metasize = ReadJsonInt64Property(m_reader, "metasize");

                                if (!m_reader.Read())
                                    throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));

                                if (m_reader.TokenType == JsonToken.PropertyName && m_reader.Value != null && m_reader.Value.ToString() == "metablocklists")
                                {
                                    var metadatablocklisthashes = new List<string>();
                                    SkipJsonToken(m_reader, JsonToken.StartArray);

                                    if (!m_reader.Read())
                                        throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));
                                    
                                    while(m_reader.TokenType == JsonToken.String)
                                    {
                                        metadatablocklisthashes.Add(m_reader.Value.ToString());
                                        if (!m_reader.Read())
                                            throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));
                                    }
                                    
                                    if (m_reader.TokenType != JsonToken.EndArray)
                                        throw new InvalidDataException(string.Format("Invalid JSON, unexpected token {1} found while reading entry {0}", this.Path, m_reader.TokenType));
                                    if (!m_reader.Read())
                                        throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));

                                    this.MetaBlocklistHashes = metadatablocklisthashes;
                                    this.Metablockhash = null;
                                }
                                else if (m_reader.TokenType == JsonToken.PropertyName && m_reader.Value != null && m_reader.Value.ToString() == "metablockhash")
                                {
                                    if (!m_reader.Read())
                                        throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));
                                    this.Metablockhash = m_reader.Value.ToString();

                                    if (!m_reader.Read())
                                        throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));

                                    this.MetaBlocklistHashes = null;
                                }
                            }

                            if ((this.Type == FilelistEntryType.File || this.Type == FilelistEntryType.AlternateStream) && m_reader.TokenType == JsonToken.PropertyName && m_reader.Value != null && m_reader.Value.ToString() == "blocklists")
                            {
                                SkipJsonToken(m_reader, JsonToken.StartArray);
                                this.BlocklistHashes = new BlocklistHashEnumerable(m_reader);
                            }
                            else if ((this.Type == FilelistEntryType.File || this.Type == FilelistEntryType.AlternateStream) && m_reader.TokenType == JsonToken.PropertyName && m_reader.Value != null && m_reader.Value.ToString() == "blockhash")
                            {
                                if (!m_reader.Read())
                                    throw new InvalidDataException(string.Format("Invalid JSON, EOF found while reading entry {0}", this.Path));
                                
                                this.Blockhash = m_reader.Value.ToString();
                                this.Blocksize = ReadJsonInt64Property(m_reader, "blocksize");
                                this.BlocklistHashes = null;
                            }
                            else
                            {
                                this.BlocklistHashes = null;
                                if (m_reader.TokenType != JsonToken.EndObject)
                                    throw new InvalidDataException(string.Format("Invalid JSON, expected EndObject, but found {0}, \"{1}\" while reading entry {2}", m_reader.TokenType, m_reader.Value, this.Path));
                            }
                        }
                    }
                }

                private readonly ICompression m_compression;
                private FileEntry m_current;
                private StreamReader m_stream;
                private JsonReader m_reader;
                private bool m_done;
                private bool m_first;

                public FileEntryEnumerator(ICompression compression)
                {
                    m_compression = compression;
                    this.Reset();
                }

                public IFileEntry Current
                {
                    get { return m_current; }
                }

                public void Dispose()
                {
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

                    if (m_first)
                    {
                        m_first = false;
                    }
                    else
                    {
                        while (m_reader.TokenType != JsonToken.EndObject && m_reader.Read())
                        { /*skip */ }
                    }

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

                    m_current = new FileEntry(m_reader);

                    return true;
                }

                public void Reset()
                {
                    this.Dispose();
                    m_stream = new StreamReader(m_compression.OpenRead(FILELIST));
                    m_reader = new JsonTextReader(m_stream);

                    SkipJsonToken(m_reader, JsonToken.StartArray);

                    m_current = null;
                    m_done = false;
                    m_first = true;
                }
            }

            private readonly ICompression m_compression;
            public FileEntryEnumerable(ICompression compression)
            {
                m_compression = compression;
            }

            public IEnumerator<IFileEntry> GetEnumerator()
            {
                return new FileEntryEnumerator(m_compression);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        private class ControlFileEnumerable : IEnumerable<KeyValuePair<string, Stream>>
        {
            private class ControlFileEnumerator : IEnumerator<KeyValuePair<string, Stream>>
            {
                private readonly ICompression m_compression;
                private string[] m_files;
                private long m_index;
                private KeyValuePair<string, Stream>? m_current;

                public ControlFileEnumerator(ICompression compression)
                {
                    m_compression = compression;
                    this.Reset();
                }

                public KeyValuePair<string, Stream> Current { get { return m_current.Value; } }
                object System.Collections.IEnumerator.Current { get { return this.Current; } }

                public void Dispose() 
                {
                    if (m_current != null)
                        try { m_current.Value.Value.Dispose(); }
                        finally { m_current = null; }
                }

                public bool MoveNext()
                {
                    if (m_index + 1 >= m_files.Length)
                        return false;
                    m_index++;

                    if (m_current != null)
                        try { m_current.Value.Value.Dispose(); }
                        finally { m_current = null; }

                    m_current = new KeyValuePair<string, Stream>(m_files[m_index].Substring(CONTROL_FILES_FOLDER.Length), m_compression.OpenRead(m_files[m_index]));
                    return true;
                }

                public void Reset()
                {
                    m_files = m_compression.ListFiles(CONTROL_FILES_FOLDER);
                    m_index = -1;
                }
            }

            private readonly ICompression m_compression;
            public ControlFileEnumerable(ICompression compression)
            {
                m_compression = compression;
            }

            public IEnumerator<KeyValuePair<string, Stream>> GetEnumerator() { return new ControlFileEnumerator(m_compression); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }

        public FilesetVolumeReader(ICompression compression, Options options)
            : base(compression, options)
        {
        }

        public FilesetVolumeReader(string compressor, string file, Options options)
            : base(compressor, file, options)
        {
        }

        public IEnumerable<IFileEntry> Files { get { return new FileEntryEnumerable(m_compression); } }

        public IEnumerable<KeyValuePair<string, Stream>> ControlFiles { get { return new ControlFileEnumerable(m_compression); } }
    }
}
