using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Volumes
{
    public class FilesetVolumeWriter : VolumeWriterBase
    {
        private StreamWriter m_streamwriter;
        private JsonWriter m_writer;
        private long m_filecount;
        private long m_foldercount;

        public override RemoteVolumeType FileType { get { return RemoteVolumeType.Files; } }

        public FilesetVolumeWriter(Options options, DateTime timestamp)
            : base(options, timestamp)
        {
            m_streamwriter = new StreamWriter(m_compression.CreateFile(FILELIST, CompressionHint.Compressible, DateTime.UtcNow));
            m_writer = new JsonTextWriter(m_streamwriter);
            m_writer.WriteStartArray();
        }

        public void AddFile(string name, string filehash, long size, DateTime scantime, string metahash, long metasize, IEnumerable<string> blocklisthashes)
        {
            AddFileEntry(FilelistEntryType.File, name, filehash, size, scantime, metahash, metasize, blocklisthashes);
        }

        public void AddAlternateStream(string name, string filehash, long size, DateTime scantime, string metahash, long metasize, IEnumerable<string> blocklisthashes)
        {
            AddFileEntry(FilelistEntryType.AlternateStream, name, filehash, size, scantime, metahash, metasize, blocklisthashes);
        }

        private void AddFileEntry(FilelistEntryType type, string name, string filehash, long size, DateTime scantime, string metahash, long metasize, IEnumerable<string> blocklisthashes)
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
            m_writer.WriteValue(Library.Utility.Utility.SerializeDateTime(scantime));
            if (metahash != null)
            {
                m_writer.WritePropertyName("metahash");
                m_writer.WriteValue(metahash);
                m_writer.WritePropertyName("metasize");
                m_writer.WriteValue(metasize);
            }

            if (blocklisthashes != null)
            {
                //Slightly akward, but we avoid writing if there are no entries 
                var en = blocklisthashes.GetEnumerator();
                if (en.MoveNext() && !string.IsNullOrEmpty(en.Current))
                {
                    m_writer.WritePropertyName("blocklists");
                    m_writer.WriteStartArray();
                    m_writer.WriteValue(en.Current);
                    while(en.MoveNext())
                        m_writer.WriteValue(en.Current);
                    m_writer.WriteEndArray();
                }
            }

            m_writer.WriteEndObject();
        }

        public void AddDirectory(string name, string metahash, long metasize)
        {
            AddMetaEntry(FilelistEntryType.Folder, name, metahash, metasize);
        }

        public void AddMetaEntry(FilelistEntryType type, string name, string metahash, long metasize)
        {
            m_foldercount++;
            m_writer.WriteStartObject();
            m_writer.WritePropertyName("type");
            m_writer.WriteValue(type.ToString());
            m_writer.WritePropertyName("path");
            m_writer.WriteValue(name);
            if (metahash != null)
            {
                m_writer.WritePropertyName("metahash");
                m_writer.WriteValue(metahash);
                m_writer.WritePropertyName("metasize");
                m_writer.WriteValue(metasize);
            }
            m_writer.WriteEndObject();
        }

        public override void Close()
        {
            if (m_streamwriter != null)
            {
                m_writer.WriteEndArray();
                m_writer.Close();
                m_streamwriter.Dispose();
                m_streamwriter = null;
            }

            base.Close();
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
        }

        public long FileCount { get { return m_filecount; } }
        public long FolderCount { get { return m_foldercount; } }

        public void AddSymlink(string name, string metahash, long metasize)
        {
            AddMetaEntry(FilelistEntryType.Symlink, name, metahash, metasize);
        }
    }
}
