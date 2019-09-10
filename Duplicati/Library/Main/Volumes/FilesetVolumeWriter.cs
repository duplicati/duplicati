using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class FilesetVolumeWriter : VolumeWriterBase
    {
        private MemoryStream m_memorystream;
        private StreamWriter m_streamwriter;
        private readonly JsonWriter m_writer;
        private long m_filecount;
        private long m_foldercount;

        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Files; } }

        public FilesetVolumeWriter(Options options, DateTime timestamp)
            : base(options, timestamp)
        {
            m_memorystream = new MemoryStream();
            m_streamwriter = new StreamWriter(m_memorystream, ENCODING);
            m_writer = new JsonTextWriter(m_streamwriter);
            m_writer.WriteStartArray();
        }

        private void WriteMetaProperties(string metahash, long metasize, string metablockhash, IEnumerable<string> metablocklisthashes)
        {
            m_writer.WritePropertyName("metahash");
            m_writer.WriteValue(metahash);
            m_writer.WritePropertyName("metasize");
            m_writer.WriteValue(metasize);

            if (metablocklisthashes != null)
            {
                // Slightly awkward, but we avoid writing if there are no entries.
                using (var en = metablocklisthashes.GetEnumerator())
                {
                    if (en.MoveNext() && !string.IsNullOrEmpty(en.Current))
                    {
                        m_writer.WritePropertyName("metablocklists");
                        m_writer.WriteStartArray();
                        m_writer.WriteValue(en.Current);
                        while (en.MoveNext())
                            m_writer.WriteValue(en.Current);
                        m_writer.WriteEndArray();
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(metablockhash))
            {
                m_writer.WritePropertyName("metablockhash");
                m_writer.WriteValue(metablockhash);
            }
        }

        public void AddFile(string name, string filehash, long size, DateTime lastmodified, string metahash, long metasize, string metablockhash, string blockhash, long blocksize, IEnumerable<string> blocklisthashes, IEnumerable<string> metablocklisthashes)
        {
            AddFileEntry(FilelistEntryType.File, name, filehash, size, lastmodified, metahash, metasize, metablockhash, blockhash, blocksize, blocklisthashes, metablocklisthashes);
        }

        public void AddAlternateStream(string name, string filehash, long size, DateTime lastmodified, string metahash, string metablockhash, long metasize, string blockhash, long blocksize, IEnumerable<string> blocklisthashes, IEnumerable<string> metablocklisthashes)
        {
            AddFileEntry(FilelistEntryType.AlternateStream, name, filehash, size, lastmodified, metahash, metasize, metablockhash, blockhash, blocksize, blocklisthashes, metablocklisthashes);
        }

        private void AddFileEntry(FilelistEntryType type, string name, string filehash, long size, DateTime lastmodified, string metahash, long metasize, string metablockhash, string blockhash, long blocksize, IEnumerable<string> blocklisthashes, IEnumerable<string> metablocklisthashes)
        {
            m_filecount++;
            m_writer.WriteStartObject();
            m_writer.WritePropertyName("type");
            m_writer.WriteValue(type.ToString());
            m_writer.WritePropertyName("path");
            m_writer.WriteValue(name);
            m_writer.WritePropertyName("hash");
            m_writer.WriteValue(filehash);
            m_writer.WritePropertyName("size");
            m_writer.WriteValue(size);
            m_writer.WritePropertyName("time");
            m_writer.WriteValue(Library.Utility.Utility.SerializeDateTime(lastmodified));
            if (metahash != null)
                WriteMetaProperties(metahash, metasize, metablockhash, metablocklisthashes);

            if (blocklisthashes != null)
            {
                //Slightly awkward, but we avoid writing if there are no entries 
                using (var en = blocklisthashes.GetEnumerator())
                {
                    if (en.MoveNext() && !string.IsNullOrEmpty(en.Current))
                    {
                        m_writer.WritePropertyName("blocklists");
                        m_writer.WriteStartArray();
                        m_writer.WriteValue(en.Current);
                        while (en.MoveNext())
                            m_writer.WriteValue(en.Current);
                        m_writer.WriteEndArray();
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(blockhash))
            {
                m_writer.WritePropertyName("blockhash");
                m_writer.WriteValue(blockhash);
                m_writer.WritePropertyName("blocksize");
                m_writer.WriteValue(blocksize);
            }

            m_writer.WriteEndObject();
        }

        public void AddDirectory(string name, string metahash, long metasize, string metablockhash, IEnumerable<string> metablocklisthashes)
        {
            AddMetaEntry(FilelistEntryType.Folder, name, metahash, metasize, metablockhash, metablocklisthashes);
        }

        public void AddMetaEntry(FilelistEntryType type, string name, string metahash, long metasize, string metablockhash, IEnumerable<string> metablocklisthashes)
        {
            m_foldercount++;
            m_writer.WriteStartObject();
            m_writer.WritePropertyName("type");
            m_writer.WriteValue(type.ToString());
            m_writer.WritePropertyName("path");
            m_writer.WriteValue(name);
            if (metahash != null)
                WriteMetaProperties(metahash, metasize, metablockhash, metablocklisthashes);
            
            m_writer.WriteEndObject();
        }

        public override void Close()
        {
            if (m_streamwriter != null)
            {
                m_writer.Close();
                m_streamwriter.Dispose();
                m_streamwriter = null;
            }

            base.Close();
        }

        public void AddFilelistFile()
        {
            if (m_streamwriter != null)
            {
                m_writer.WriteEndArray();
                m_writer.Flush();
                m_streamwriter.Flush();
            }

            using (Stream sr = m_compression.CreateFile(FILELIST, CompressionHint.Compressible, DateTime.UtcNow))
            {
                m_memorystream.Seek(0, SeekOrigin.Begin);
                m_memorystream.CopyTo(sr);
                sr.Flush();
            }
        }

        public void AddControlFile(string localfile, CompressionHint hint, string filename = null)
        {
            filename = filename ?? System.IO.Path.GetFileName(localfile);
            using (var t = m_compression.CreateFile(CONTROL_FILES_FOLDER + filename, hint, DateTime.UtcNow))
            using (var s = System.IO.File.OpenRead(localfile))
                Library.Utility.Utility.CopyStream(s, t);
        }

        public override void Dispose()
        {
            this.Close();
            base.Dispose();
        }

        public long FileCount { get { return m_filecount; } }
        public long FolderCount { get { return m_foldercount; } }

        public void AddSymlink(string name, string metahash, long metasize, string metablockhash, IEnumerable<string> metablocklisthashes)
        {
            AddMetaEntry(FilelistEntryType.Symlink, name, metahash, metasize, metablockhash, metablocklisthashes);
        }
    }
}
