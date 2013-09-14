using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Database
{
    internal partial class LocalRestoreDatabase
    {
        private class ExistingFileEnumerable : IEnumerable<IExistingFile>
        {
            private class ExistingFileEnumerator : IEnumerator<IExistingFile>, IDisposable
            {
                private class ExistingFile : IExistingFile
                {
                    private class ExistingFileBlockEnumerable : IEnumerable<IExistingFileBlock>
                    {
                        private class ExistingFileBlockEnumerator : IEnumerator<IExistingFileBlock>
                        {
                            private class ExistingFileBlock : IExistingFileBlock
                            {
                                private System.Data.IDataReader m_reader;

                                public ExistingFileBlock(System.Data.IDataReader reader)
                                {
                                    m_reader = reader;
                                }

                                public string Hash
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(4);
                                        if (v == null || v == DBNull.Value)
                                            return null;
                                        return v.ToString();
                                    }
                                }

                                public long Index
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(5);
                                        if (v == null || v == DBNull.Value)
                                            return -1;
                                        return Convert.ToInt64(v);
                                    }
                                }

                                public long Size
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(6);
                                        if (v == null || v == DBNull.Value)
                                            return -1;
                                        return Convert.ToInt64(v);
                                    }
                                }
                            }

                            private System.Data.IDataReader m_reader;
                            private ExistingFile m_file;
                            private ExistingFileBlock m_block;
                            private string m_current;

                            public ExistingFileBlockEnumerator(System.Data.IDataReader reader, ExistingFile file)
                            {
                                m_reader = reader;
                                m_file = file;
                                this.Reset();
                            }

                            public IExistingFileBlock Current
                            {
                                get
                                {
                                    return m_block;
                                }
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
                                if (!m_file.HasMoreData)
                                    return false;

                                if (m_current == null)
                                    m_current = m_file.TargetPath;
                                else
                                    m_file.HasMoreData = m_reader.Read();

                                if (m_file.HasMoreData && m_current != m_file.TargetPath)
                                    return false;

                                return m_file.HasMoreData;
                            }

                            public void Reset()
                            {
                                m_block = new ExistingFileBlock(m_reader);
                                m_current = null;
                            }
                        }

                        private System.Data.IDataReader m_reader;
                        private ExistingFile m_file;

                        public ExistingFileBlockEnumerable(System.Data.IDataReader reader, ExistingFile file)
                        {
                            m_reader = reader;
                            m_file = file;
                        }

                        public IEnumerator<IExistingFileBlock> GetEnumerator()
                        {
                            return new ExistingFileBlockEnumerator(m_reader, m_file);
                        }

                        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                        {
                            return this.GetEnumerator();
                        }
                    }

                    private System.Data.IDataReader m_reader;
                    public bool HasMoreData { get; set; }

                    public ExistingFile(System.Data.IDataReader reader)
                    {
                        m_reader = reader;
                        HasMoreData = m_reader.Read();
                    }

                    public string TargetPath
                    {
                        get
                        {
                            var v = m_reader.GetValue(0);
                            if (v == null || v == DBNull.Value)
                                return null;

                            return v.ToString();
                        }
                    }
                    
                    public string TargetHash
                    {
                        get
                        {
                            var v = m_reader.GetValue(1);
                            if (v == null || v == DBNull.Value)
                                return null;

                            return v.ToString();
                        }
                    }
                    
                    public long TargetFileID
                    {
                        get
                        {
                            var v = m_reader.GetValue(2);
                            if (v == null || v == DBNull.Value)
                                return -1;

                            return Convert.ToInt64(v);
                        }
                    }
                    

                    public long Length
                    {
                        get
                        {
                            var v = m_reader.GetValue(3);
                            if (v == null || v == DBNull.Value)
                                return -1;

                            return Convert.ToInt64(v);
                        }
                    }

                    public IEnumerable<IExistingFileBlock> Blocks
                    {
                        get { return new ExistingFileBlockEnumerable(m_reader, this); }
                    }
                }

                private System.Data.IDbCommand m_command;
                private System.Data.IDataReader m_reader;
                private System.Data.IDbConnection m_connection;
                private string m_tablename;
                private ExistingFile m_file;
                private string m_current;

                public ExistingFileEnumerator(System.Data.IDbConnection con, string tablename)
                {
                    m_connection = con;
                    m_tablename = tablename;
                    this.Reset();
                }

                public void Dispose()
                {
                    if (m_reader != null)
                    {
                        m_reader.Dispose();
                        m_reader = null;
                    }

                    if (m_command != null)
                    {
                        m_command.Dispose();
                        m_command = null;
                    }
                }

                public IExistingFile Current
                {
                    get { return m_file; }
                }

                object System.Collections.IEnumerator.Current
                {
                    get { return m_file; }
                }

                public bool MoveNext()
                {
                    if (m_current != null)
                    {
                        while (m_file.HasMoreData && m_file.TargetPath == m_current)
                            m_file.HasMoreData = m_reader.Read();
                    }

                    m_current = m_file.HasMoreData ? m_file.TargetPath : null;

                    return m_file.HasMoreData;
                }

                public void Reset()
                {
                    m_file = null;
                    m_command = m_connection.CreateCommand();
                    m_command.CommandText = string.Format(@"SELECT ""{0}"".""TargetPath"", ""Blockset"".""FullHash"", ""{0}"".""ID"", ""Blockset"".""Length"", ""Block"".""Hash"", ""BlocksetEntry"".""Index"", ""Block"".""Size"" FROM ""{0}"", ""Blockset"", ""BlocksetEntry"", ""Block"" WHERE ""{0}"".""BlocksetID"" = ""Blockset"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""{0}"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ORDER BY ""{0}"".""TargetPath"", ""BlocksetEntry"".""Index""", m_tablename);
                    m_reader = m_command.ExecuteReader();
                    m_file = new ExistingFile(m_reader);
                    m_current = null;

                }
            }

            private System.Data.IDbConnection m_connection;
            private string m_tablename;

            public ExistingFileEnumerable(System.Data.IDbConnection connection, string tablename)
            {
                m_connection = connection;
                m_tablename = tablename;
            }

            public IEnumerator<IExistingFile> GetEnumerator()
            {
                return new ExistingFileEnumerator(m_connection, m_tablename);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }


        private class LocalBlockSourceEnumerable : IEnumerable<ILocalBlockSource>
        {
            private class LocalBlockSourceEnumerator : IEnumerator<ILocalBlockSource>
            {
                private class LocalBlockSource : ILocalBlockSource
                {
                    private class BlockDescriptorEnumerable : IEnumerable<IBlockDescriptor>
                    {
                        private class BlockDescriptorEnumerator : IEnumerator<IBlockDescriptor>
                        {
                            private class BlockDescriptor : IBlockDescriptor
                            {
                                private class BlockSourceEnumerable : IEnumerable<IBlockSource>
                                {
                                    private class BlockSourceEnumerator : IEnumerator<IBlockSource>
                                    {
                                        private class BlockSource : IBlockSource
                                        {
                                            private System.Data.IDataReader m_reader;
                                            public BlockSource(System.Data.IDataReader reader)
                                            {
                                                m_reader = reader;
                                            }

                                            public string Path
                                            {
                                                get
                                                {
                                                    var v = m_reader.GetValue(6);
                                                    if (v == null || v == DBNull.Value)
                                                        return null;
                                                    return v.ToString();
                                                }
                                            }

                                            public long Offset
                                            {
                                                get
                                                {
                                                    var v = m_reader.GetValue(7);
                                                    if (v == null || v == DBNull.Value)
                                                        return -1;
                                                    return Convert.ToInt64(v);
                                                }
                                            }

                                            public long Size
                                            {
                                                get
                                                {
                                                    var v = m_reader.GetValue(8);
                                                    if (v == null || v == DBNull.Value)
                                                        return -1;
                                                    return Convert.ToInt64(v);
                                                }
                                            }
                                        }

                                        private System.Data.IDataReader m_reader;
                                        private LocalBlockSource m_localsource;
                                        private BlockSource m_source;
                                        private BlockDescriptor m_descriptor;
                                        private string m_currenthash;
                                        private string m_currentfile;

                                        public BlockSourceEnumerator(System.Data.IDataReader reader, LocalBlockSource localsource, BlockDescriptor descriptor)
                                        {
                                            m_reader = reader;
                                            m_localsource = localsource;
                                            m_descriptor = descriptor;
                                            this.Reset();
                                        }

                                        public IBlockSource Current
                                        {
                                            get { return m_source; }
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
                                            if (!m_localsource.HasMoreData)
                                                return false;

                                            if (m_currenthash != null)
                                                m_localsource.HasMoreData = m_reader.Read();

                                            if (!m_localsource.HasMoreData)
                                                return false;

                                            if (m_currenthash != null && m_currentfile != null)
                                                if (m_currentfile != m_localsource.TargetPath || m_currenthash != m_descriptor.Hash)
                                                    return false;

                                            m_currenthash = m_descriptor.Hash;
                                            m_currentfile = m_localsource.TargetPath;

                                            return m_localsource.HasMoreData;
                                        }

                                        public void Reset()
                                        {
                                            m_source = new BlockSource(m_reader);
                                            m_currenthash = null;
                                            m_currentfile = null;
                                        }
                                    }

                                    private System.Data.IDataReader m_reader;
                                    private LocalBlockSource m_localsource;
                                    private BlockDescriptor m_descriptor;

                                    public BlockSourceEnumerable(System.Data.IDataReader reader, LocalBlockSource localsource, BlockDescriptor descriptor)
                                    {
                                        m_reader = reader;
                                        m_localsource = localsource;
                                        m_descriptor = descriptor;
                                    }

                                    public IEnumerator<IBlockSource> GetEnumerator()
                                    {
                                        return new BlockSourceEnumerator(m_reader, m_localsource, m_descriptor);
                                    }

                                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                                    {
                                        return this.GetEnumerator();
                                    }
                                }

                                private System.Data.IDataReader m_reader;
                                private LocalBlockSource m_localsource;

                                public BlockDescriptor(System.Data.IDataReader reader, LocalBlockSource localsource)
                                {
                                    m_reader = reader;
                                    m_localsource = localsource;
                                }

                                public string Hash
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(2);
                                        if (v == null || v == DBNull.Value)
                                            return null;
                                        return v.ToString();
                                    }
                                }


                                public long Offset
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(3);
                                        if (v == null || v == DBNull.Value)
                                            return -1;
                                        return Convert.ToInt64(v);
                                    }
                                }

                                public long Index
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(4);
                                        if (v == null || v == DBNull.Value)
                                            return -1;
                                        return Convert.ToInt64(v);
                                    }
                                }

                                public long Size
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(5);
                                        if (v == null || v == DBNull.Value)
                                            return -1;
                                        return Convert.ToInt64(v);
                                    }
                                }

                                public IEnumerable<IBlockSource> Blocksources
                                {
                                    get { return new BlockSourceEnumerable(m_reader, m_localsource, this); }
                                }
                            }

                            private System.Data.IDataReader m_reader;
                            private LocalBlockSource m_localsource;
                            private BlockDescriptor m_descriptor;
                            private string m_currenthash;
                            private string m_currentfile;
                            

                            public BlockDescriptorEnumerator(System.Data.IDataReader reader, LocalBlockSource localsource)
                            {
                                m_reader = reader;
                                m_localsource = localsource;
                                this.Reset();
                            }

                            public IBlockDescriptor Current
                            {
                                get { return m_descriptor; }
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
                                if (!m_localsource.HasMoreData)
                                    return false;

                                if (m_currenthash != null)
                                {
                                    while (m_localsource.HasMoreData && m_currenthash == m_descriptor.Hash && m_currentfile == m_localsource.TargetPath)
                                        m_localsource.HasMoreData = m_reader.Read();

                                    if (m_currentfile != m_localsource.TargetPath)
                                        return false;
                                }

                                m_currenthash = m_localsource.HasMoreData ? m_descriptor.Hash : null;
                                m_currentfile = m_localsource.HasMoreData ? m_localsource.TargetPath : null;

                                return m_localsource.HasMoreData && m_currenthash == m_descriptor.Hash;

                            }

                            public void Reset()
                            {
                                m_descriptor = new BlockDescriptor(m_reader, m_localsource);
                                m_currenthash = null;
                                m_currentfile = null;
                            }
                        }

                        private System.Data.IDataReader m_reader;
                        private LocalBlockSource m_localsource;
                        public BlockDescriptorEnumerable(System.Data.IDataReader reader, LocalBlockSource localsource)
                        {
                            m_reader = reader;
                            m_localsource = localsource;
                        }

                        public IEnumerator<IBlockDescriptor> GetEnumerator()
                        {
                            return new BlockDescriptorEnumerator(m_reader, m_localsource);
                        }

                        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                        {
                            return this.GetEnumerator();
                        }
                    }

                    private System.Data.IDataReader m_reader;
                    public bool HasMoreData { get; set; }

                    public LocalBlockSource(System.Data.IDataReader reader)
                    {
                        m_reader = reader;
                        HasMoreData = reader.Read();
                    }

                    public string TargetPath
                    {
                        get
                        {
                            var v = m_reader.GetValue(0);
                            if (v == null || v == DBNull.Value)
                                return null;

                            return v.ToString();
                        }
                    }

                    public long TargetFileID
                    {
                        get
                        {
                            var v = m_reader.GetValue(1);
                            if (v == null || v == DBNull.Value)
                                return -1;

                            return Convert.ToInt64(v);
                        }
                    }

                    public IEnumerable<IBlockDescriptor> Blocks
                    {
                        get { return new BlockDescriptorEnumerable(m_reader, this); }
                    }
                }

                private System.Data.IDbCommand m_command;
                private System.Data.IDataReader m_reader;
                private System.Data.IDbConnection m_connection;
                private LocalBlockSource m_localsource;
                private string m_filetablename;
                private string m_blocktablename;
                private string m_current;
                private long m_blocksize;

                public LocalBlockSourceEnumerator(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize)
                {
                    m_connection = connection;
                    m_filetablename = filetablename;
                    m_blocktablename = blocktablename;
                    m_blocksize = blocksize;
                    this.Reset();
                }

                public ILocalBlockSource Current
                {
                    get { return m_localsource; }
                }

                public void Dispose()
                {
                    if (m_reader != null)
                    {
                        m_reader.Dispose();
                        m_reader = null;
                    }

                    if (m_command != null)
                    {
                        m_command.Dispose();
                        m_command = null;
                    }
                }

                object System.Collections.IEnumerator.Current
                {
                    get { throw new NotImplementedException(); }
                }

                public bool MoveNext()
                {
                    if (!m_localsource.HasMoreData)
                        return false;

                    if (m_current != null)
                    {
                        while (m_localsource.HasMoreData && m_current == m_localsource.TargetPath)
                            m_localsource.HasMoreData = m_reader.Read();
                    }

                    m_current = m_localsource.HasMoreData ? m_localsource.TargetPath : null;

                    return m_localsource.HasMoreData;
                }

                public void Reset()
                {
                    m_command = m_connection.CreateCommand();

                    m_command.CommandText = string.Format(@"SELECT DISTINCT ""A"".""TargetPath"", ""A"".""ID"", ""B"".""Hash"", (""B"".""Index"" * {2}), ""B"".""Index"", ""B"".""Size"", ""C"".""Path"", (""D"".""Index"" * {2}), ""E"".""Size"" FROM ""{0}"" ""A"", ""{1}"" ""B"", ""File"" ""C"", ""BlocksetEntry"" ""D"", ""Block"" E WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""C"".""BlocksetID"" = ""D"".""BlocksetID"" AND ""D"".""BlockID"" = ""E"".""ID"" AND ""B"".""Hash"" = ""E"".""Hash"" AND ""B"".""Size"" = ""E"".""Size"" AND ""B"".""Restored"" = 0 ", m_filetablename, m_blocktablename, m_blocksize);
                    m_reader = m_command.ExecuteReader();
                    m_current = null;
                    m_localsource = new LocalBlockSource(m_reader);
                }
            }

            private System.Data.IDbConnection m_connection;
            private string m_filetablename;
            private string m_blocktablename;
            private long m_blocksize;

            public LocalBlockSourceEnumerable(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize)
            {
                m_connection = connection;
                m_filetablename = filetablename;
                m_blocktablename = blocktablename;
                m_blocksize = blocksize;
            }

            public IEnumerator<ILocalBlockSource> GetEnumerator()
            {
                return new LocalBlockSourceEnumerator(m_connection, m_filetablename, m_blocktablename, m_blocksize);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        private class VolumePatchEnumerable : IEnumerable<IVolumePatch>
        {
            private class VolumePatchEnumerator : IEnumerator<IVolumePatch>
            {
                private class VolumePatch : IVolumePatch
                {
                    private class PatchBlockEnumerable : IEnumerable<IPatchBlock>
                    {
                        private class PatchBlockEnumerator : IEnumerator<IPatchBlock>
                        {
                            private class PatchBlock : IPatchBlock
                            {
                                private System.Data.IDataReader m_reader;

                                public PatchBlock(System.Data.IDataReader reader)
                                {
                                    m_reader = reader;
                                }

                                public long Offset
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(2);
                                        if (v == null || v == DBNull.Value)
                                            return -1;

                                        return Convert.ToInt64(v);
                                    }
                                }

                                public long Size
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(3);
                                        if (v == null || v == DBNull.Value)
                                            return -1;

                                        return Convert.ToInt64(v);
                                    }
                                }

                                public string Key
                                {
                                    get
                                    {
                                        var v = m_reader.GetValue(4);
                                        if (v == null || v == DBNull.Value)
                                            return null;

                                        return v.ToString();
                                    }
                                }
                            }

                            private System.Data.IDataReader m_reader;
                            private VolumePatch m_patch;
                            private PatchBlock m_block;
                            private string m_currentfile;
                            private bool m_first;

                            public PatchBlockEnumerator(System.Data.IDataReader reader, VolumePatch patch)
                            {
                                m_reader = reader;
                                m_patch = patch;
                                m_currentfile = patch.Path;
                                this.Reset();
                            }

                            public IPatchBlock Current
                            {
                                get { return m_block; }
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
                                if (!m_patch.HasMoreData)
                                    return false;

                                if (m_first)
                                    m_first = false;
                                else
                                    m_patch.HasMoreData = m_reader.Read();

                                var more = m_patch.HasMoreData && m_currentfile == m_patch.Path;
                                return more;
                            }

                            public void Reset()
                            {
                                m_block = new PatchBlock(m_reader);
                                m_first = true;
                            }
                        }

                        private System.Data.IDataReader m_reader;
                        private VolumePatch m_patch;

                        public PatchBlockEnumerable(System.Data.IDataReader reader, VolumePatch patch)
                        {
                            m_reader = reader;
                            m_patch = patch;
                        }

                        public IEnumerator<IPatchBlock> GetEnumerator()
                        {
                            return new PatchBlockEnumerator(m_reader, m_patch);
                        }

                        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                        {
                            return this.GetEnumerator();
                        }
                    }

                    private System.Data.IDataReader m_reader;
                    public bool HasMoreData { get; set; }

                    public VolumePatch(System.Data.IDataReader reader)
                    {
                        m_reader = reader;
                    }

                    public string Path
                    {
                        get
                        {
                            var v = m_reader.GetValue(0);
                            if (v == null || v == DBNull.Value)
                                return null;

                            return v.ToString();
                        }
                    }
                    
                    public long FileID
                    {
                        get
                        {
                            var v = m_reader.GetValue(1);
                            if (v == null || v == DBNull.Value)
                                return 0;

                            return Convert.ToInt64(v);
                        }
                    }

                    public IEnumerable<IPatchBlock> Blocks
                    {
                        get { return new PatchBlockEnumerable(m_reader, this); }
                    }
                }

                private System.Data.IDbConnection m_connection;
                private System.Data.IDataReader m_reader;
                private System.Data.IDbCommand m_command;
                private string m_filetablename;
                private string m_blocktablename;
                private BlockVolumeReader m_curvolume;
                private VolumePatch m_patch;
                private string m_current = null;
                private string m_tmptable = null;
                private long m_blocksize;

                public VolumePatchEnumerator(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
                {
                    m_connection = connection;
                    m_filetablename = filetablename;
                    m_blocktablename = blocktablename;
                    m_curvolume = curvolume;
                    m_blocksize = blocksize;
                    this.Reset();
                }

                public IVolumePatch Current
                {
                    get { return m_patch; }
                }

                public void Dispose()
                {
                    if (m_reader != null)
                    {
                        m_reader.Dispose();
                        m_reader = null;
                    }

                    if (m_command != null)
                    {
                        m_command.Dispose();
                        m_command = null;
                    }

                    if (m_tmptable != null)
                    {
                        try
                        {
                            using (var c = m_connection.CreateCommand())
                            {
                                c.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tmptable);
                                c.ExecuteNonQuery();
                            }
                        }
                        catch { }
                        finally { m_tmptable = null; }
                    }
                }

                object System.Collections.IEnumerator.Current
                {
                    get { return this.Current; }
                }

                public bool MoveNext()
                {
                    if (!m_patch.HasMoreData)
                        return false;

                    if (m_current != null)
                    {
                        while (m_patch.HasMoreData && m_current == m_patch.Path)
                            m_patch.HasMoreData = m_reader.Read();
                    }

                    m_current = m_patch.Path;

                    return m_patch.HasMoreData;
                }

                public void Reset()
                {
                    this.Dispose();
                    using (var c = m_connection.CreateCommand())
                    {
                        var tablename = "VolumeFiles-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        c.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" ( ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL )", tablename);
                        c.ExecuteNonQuery();
                        m_tmptable = tablename;

                        c.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Hash"", ""Size"") VALUES (?,?)", m_tmptable);
                        c.AddParameters(2);
                        foreach (var s in m_curvolume.Blocks)
                        {
                            c.SetParameterValue(0, s.Key);
                            c.SetParameterValue(1, s.Value);
                            c.ExecuteNonQuery();
                        }
                    }

                    m_command = m_connection.CreateCommand();
                    m_command.CommandText = string.Format(@"SELECT DISTINCT ""A"".""TargetPath"", ""B"".""FileID"", (""B"".""Index"" * {3}), ""B"".""Size"", ""C"".""Hash"" FROM ""{0}"" A, ""{1}"" B, ""{2}"" C WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""Hash"" = ""C"".""Hash"" AND ""B"".""Size"" = ""C"".""Size"" AND ""B"".""Restored"" = 0 ORDER BY ""A"".""TargetPath"", ""B"".""Index""", m_filetablename, m_blocktablename, m_tmptable, m_blocksize);
                    m_reader = m_command.ExecuteReader();
                    m_patch = new VolumePatch(m_reader);
                    m_patch.HasMoreData = m_reader.Read();
                    m_current = null;
                }
            }

            private System.Data.IDbConnection m_connection;
            private string m_filetablename;
            private string m_blocktablename;
            private BlockVolumeReader m_curvolume;
            private long m_blocksize;

            public VolumePatchEnumerable(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
            {
                m_connection = connection;
                m_filetablename = filetablename;
                m_blocktablename = blocktablename;
                m_blocksize = blocksize;
                m_curvolume = curvolume;
            }

            public IEnumerator<IVolumePatch> GetEnumerator()
            {
                return new VolumePatchEnumerator(m_connection, m_filetablename, m_blocktablename, m_blocksize, m_curvolume);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }
	}
}
