using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Database
{
    internal partial class LocalRestoreDatabase : LocalDatabase
    {
        protected string m_tempfiletable;
        protected string m_tempblocktable;
        protected DateTime m_restoreTime;

		public DateTime RestoreTime { get { return m_restoreTime; } } 

        public LocalRestoreDatabase(string path)
            : this(new LocalDatabase(path, "Restore"))
        {
        }

        public LocalRestoreDatabase(LocalDatabase dbparent)
            : base(dbparent)
        {
        }

        public Tuple<long, long> PrepareRestoreFilelist(DateTime restoretime, long[] versions, Library.Utility.IFilter filter, ILogWriter log)
        {
            var guid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            m_tempfiletable = "Fileset-" + guid;
            m_tempblocktable = "Blocks-" + guid;

            using(var cmd = m_connection.CreateCommand())
            {
                var filesetIds = GetFilesetIDs(NormalizeDateTime(restoretime), versions).ToList();
                while(filesetIds.Count > 0)
                {
                    var filesetId = filesetIds[0];
                    filesetIds.RemoveAt(0);
                    
                    m_restoreTime = ParseFromEpochSeconds(cmd.ExecuteScalarInt64(@"SELECT ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" = ?", 0, filesetId));
                    
                    var ix = this.FilesetTimes.Select((value, index) => new { value.Key, index })
                            .Where(n => n.Key == filesetId)
                            .Select(pair => pair.index + 1)
                            .FirstOrDefault() - 1;
                            
                    log.AddMessage(string.Format("Searching backup {0} ({1}) ...", ix, m_restoreTime));
                    
                    cmd.Parameters.Clear();
    
                    cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_tempfiletable));
                    cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_tempblocktable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY, ""Path"" TEXT NOT NULL, ""BlocksetID"" INTEGER NOT NULL, ""MetadataID"" INTEGER NOT NULL, ""Targetpath"" TEXT NULL ) ", m_tempfiletable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY, ""FileID"" INTEGER NOT NULL, ""Index"" INTEGER NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Restored"" BOOLEAN NOT NULL, ""Metadata"" BOOLEAN NOT NULL)", m_tempblocktable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_Index"" ON ""{0}"" (""TargetPath"")", m_tempfiletable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_HashSizeIndex"" ON ""{0}"" (""Hash"", ""Size"")", m_tempblocktable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_IndexIndex"" ON ""{0}"" (""Index"")", m_tempblocktable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_RestoredMetadataIndex"" ON ""{0}"" (""Restored"", ""Metadata"")", m_tempblocktable));


                    if (filter == null || filter.Empty)
                    {
                        // Simple case, restore everything
                        cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = ? ", m_tempfiletable);
                        cmd.AddParameter(filesetId);
                        cmd.ExecuteNonQuery();
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is Library.Utility.FilterExpression && (filter as Library.Utility.FilterExpression).Type == Duplicati.Library.Utility.FilterType.Simple)
                    {
                        // If we get a list of filenames, the lookup table is faster
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        using(var tr = m_connection.BeginTransaction())
                        {
                            var p = (filter as Library.Utility.FilterExpression).GetSimpleList();
                            var m_filenamestable = "Filenames-" + guid;
                            cmd.Transaction = tr;
                            cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL) ", m_filenamestable));
                            cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"") VALUES (?)", m_filenamestable);
                            cmd.AddParameter();
                            
                            foreach(var s in p)
                            {
                                cmd.SetParameterValue(0, s);
                                cmd.ExecuteNonQuery();
                            }
                            
                            cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""Path"" IN (SELECT DISTINCT ""Path"" FROM ""{1}"") ", m_tempfiletable, m_filenamestable);
                            cmd.SetParameterValue(0, filesetId);
                            var c = cmd.ExecuteNonQuery();
                            
                            cmd.Parameters.Clear();
                            
                            if (c != p.Length && c != 0)
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine();
                                
                                using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""Path"" FROM ""{0}"" WHERE ""Path"" NOT IN (SELECT ""Path"" FROM ""{1}"")", m_filenamestable, m_tempfiletable)))
                                    while (rd.Read())
                                        sb.AppendLine(rd.GetValue(0).ToString());
    
                                var actualrestoretime = ParseFromEpochSeconds(cmd.ExecuteScalarInt64(@"SELECT ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" = ?", 0, filesetId));
                                log.AddWarning(string.Format("{0} File(s) were not found in list of files for backup at {1}, will not be restored: {2}", p.Length - c, actualrestoretime.ToLocalTime(), sb), null);
                                cmd.Parameters.Clear();
                            }
                            
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_filenamestable));
                            
                            using(new Logging.Timer("CommitPrepareFileset"))
                                tr.Commit();
                        }
                    }
                    else
                    {
                        // Restore but filter elements based on the filter expression
                        // If this is too slow, we could add a special handler for wildcard searches too
                        cmd.CommandText = string.Format(@"SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetID"" = ?");
                        cmd.AddParameter(filesetId);
    
                        object[] values = new object[3];
                        using(var cmd2 = m_connection.CreateCommand())
                        {
                            cmd2.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") VALUES (?,?,?)", m_tempfiletable);
                            cmd2.AddParameter();
                            cmd2.AddParameter();
                            cmd2.AddParameter();
    
                            using(var rd = cmd.ExecuteReader())
                                while (rd.Read())
                                {
                                    rd.GetValues(values);
                                    if (values[0] != null && values[0] != DBNull.Value && Library.Utility.FilterExpression.Matches(filter, values[0].ToString()))
                                    {
                                        cmd2.SetParameterValue(0, values[0]);
                                        cmd2.SetParameterValue(1, values[1]);
                                        cmd2.SetParameterValue(2, values[2]);
                                        cmd2.ExecuteNonQuery();
                                    }
                                }
                        }
                    }
                    
                    
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT COUNT(DISTINCT ""{0}"".""Path""), SUM(""Blockset"".""Length"") FROM ""{0}"", ""Blockset"" WHERE ""{0}"".""BlocksetID"" = ""Blockset"".""ID"" ", m_tempfiletable)))
                    {
                        var filecount = 0L;
                        var filesize = 0L;

                        if (rd.Read())
                        {
                            filecount = rd.ConvertValueToInt64(0, 0);
                            filesize = rd.ConvertValueToInt64(1, 0);
                        }

                        if (filecount > 0)
                        {
                            log.AddVerboseMessage("Needs to restore {0} files ({1})", filecount, Library.Utility.Utility.FormatSizeString(filesize));
                            return new Tuple<long, long>(filecount, filesize);
                        }
                    }                
                }
            }
            
            return new Tuple<long, long>(0, 0);
        }

        public string GetLargestPrefix()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = string.Format(@"SELECT ""Path"" FROM ""{0}"" ORDER BY LENGTH(""Path"") DESC LIMIT 1", m_tempfiletable);
                var v0 = cmd.ExecuteScalar();
                string maxpath = "";
                if (v0 != null)
                    maxpath = v0.ToString();

                cmd.CommandText = string.Format(@"SELECT COUNT(*) FROM ""{0}""", m_tempfiletable);
                var filecount = cmd.ExecuteScalarInt64(-1);
                long foundfiles = -1;

                //TODO: Handle FS case-sensitive?
                cmd.CommandText = string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE SUBSTR(""Path"", 1, ?) = ?", m_tempfiletable);
                cmd.AddParameter();
                cmd.AddParameter();

                while (filecount != foundfiles && maxpath.Length > 0)
                {
                    var mp = Library.Utility.Utility.AppendDirSeparator(maxpath);
                    cmd.SetParameterValue(0, mp.Length);
                    cmd.SetParameterValue(1, mp);
                    foundfiles = cmd.ExecuteScalarInt64(-1);

                    if (filecount != foundfiles)
                    {
                        var oldlen = maxpath.Length;
                        maxpath = Duplicati.Library.Snapshots.SnapshotUtility.SystemIO.PathGetDirectoryName(maxpath);
                        if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen)
                            maxpath = "";
                    }
                }

                return maxpath == "" ? "" : Library.Utility.Utility.AppendDirSeparator(maxpath);
            }

        }

        public void SetTargetPaths(string largest_prefix, string destination)
		{
			using(var cmd = m_connection.CreateCommand())
			{
				if (string.IsNullOrEmpty(destination))
				{
					//The string fixing here is meant to provide some non-random
					// defaults when restoring cross OS, e.g. backup on Linux, restore on Windows
					//This is mostly meaningless, and the user really should use --restore-path
				
					if (Library.Utility.Utility.IsClientLinux)
	                	// For Win -> Linux, we remove the colon from the drive letter, and use the drive letter as root folder
						cmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Targetpath"" = CASE WHEN SUBSTR(""Path"", 2, 1) == "":"" THEN ""/"" || SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3) ELSE ""Path"" END", m_tempfiletable));
					else
	                	// For Linux -> Win, we use the temporary folder's drive as the root path
						cmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Targetpath"" = CASE WHEN SUBSTR(""Path"", 1, 1) == ""/"" THEN ? || SUBSTR(""Path"", 2) ELSE ""Path"" END", m_tempfiletable), Library.Utility.Utility.AppendDirSeparator(System.IO.Path.GetPathRoot(Library.Utility.TempFolder.SystemTempPath)));
	                
				}
				else
				{						
					if (string.IsNullOrEmpty(largest_prefix))
					{
						//Special case, restoring to new folder, but files are from different drives
						// So we use the format <restore path> / <drive letter> / <source path>
						// To avoid generating paths with a colon
						cmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Targetpath"" = ? || CASE WHEN SUBSTR(""Path"", 2, 1) == "":"" THEN SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3) ELSE ""Path"" END", m_tempfiletable), destination);
					}
					else
					{
						largest_prefix = Library.Utility.Utility.AppendDirSeparator(largest_prefix);
						cmd.CommandText = string.Format(@"UPDATE ""{0}"" SET ""Targetpath"" = ? || SUBSTR(""Path"", ?)", m_tempfiletable);
						cmd.AddParameter(destination);
						cmd.AddParameter(largest_prefix.Length + 1);
						cmd.ExecuteNonQuery();
					}
               	}
            }
        }

        public void FindMissingBlocks(ILogWriter log, bool skipMetadata)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Restored"", ""Metadata"") SELECT DISTINCT ""{1}"".""ID"", ""BlocksetEntry"".""Index"", ""Block"".""Hash"", ""Block"".""Size"", 0, 0 FROM ""{1}"", ""BlocksetEntry"", ""Block"" WHERE ""{1}"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ", m_tempblocktable, m_tempfiletable);
                var p1 = cmd.ExecuteNonQuery();

                int p2 = 0;
                if (!skipMetadata)
                {
                    cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Restored"", ""Metadata"") SELECT DISTINCT ""{1}"".""ID"", ""BlocksetEntry"".""Index"", ""Block"".""Hash"", ""Block"".""Size"", 0, 1 FROM ""{1}"", ""BlocksetEntry"", ""Block"", ""Metadataset"" WHERE ""{1}"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ", m_tempblocktable, m_tempfiletable);
                    p2 = cmd.ExecuteNonQuery();
                }

                if (log.VerboseOutput)
                {
                    var size = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""Size"") FROM ""{0}"" ", m_tempblocktable), 0);
                    log.AddVerboseMessage("Restore list contains {0} blocks with a total size of {1}", p1 + p2, Library.Utility.Utility.FormatSizeString(size));
                }
            }
        }

		public void UpdateTargetPath(long ID, string newname)
		{
            using (var cmd = m_connection.CreateCommand())
            	cmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""TargetPath"" = ? WHERE ""ID"" = ?", m_tempfiletable), newname, ID);
		}

        public interface IExistingFileBlock
        {
            string Hash { get; }
            long Index { get; }
            long Size { get; }
        }

        public interface IExistingFile
        {
            string TargetPath { get; }
            string TargetHash { get; }
            long TargetFileID { get; }
            long Length { get; }
            IEnumerable<IExistingFileBlock> Blocks { get; }
        }


        public interface IBlockSource
        {
            string Path { get; }
            long Size { get; }
            long Offset { get; }
            bool IsMetadata { get; }
        }

        public interface IBlockDescriptor
        {
            string Hash { get; }
            long Size { get; }
            long Offset { get; }
            long Index { get; }
            bool IsMetadata { get; }
            IEnumerable<IBlockSource> Blocksources { get; }
        }

        public interface ILocalBlockSource
        {
            string TargetPath { get; }
            long TargetFileID { get; }
            IEnumerable<IBlockDescriptor> Blocks { get; }
        }

        public interface IFileToRestore
        {
            string Path { get; }
            string Hash { get; }
            long ID { get; }
            long Length { get; }
        }

        public interface IPatchBlock
        {
            long Offset { get; }
            long Size { get; }
            string Key { get; }
        }

        public interface IVolumePatch
        {
            string Path { get; }
            long FileID { get; }
            IEnumerable<IPatchBlock> Blocks { get; }
        }

        private class ExistingFile : IExistingFile
        {
            private System.Data.IDataReader m_reader;

            public ExistingFile(System.Data.IDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0); } }
            public string TargetHash { get { return m_reader.ConvertValueToString(1); } }
            public long TargetFileID { get { return  m_reader.ConvertValueToInt64(2); } }
            public long Length { get { return m_reader.ConvertValueToInt64(3); } }

            public bool HasMore { get; private set; }

            private class ExistingFileBlock : IExistingFileBlock
            {
                private System.Data.IDataReader m_reader;

                public ExistingFileBlock(System.Data.IDataReader rd) { m_reader = rd; }

                public string Hash { get { return m_reader.ConvertValueToString(4); } }
                public long Index { get { return m_reader.ConvertValueToInt64(5); } }
                public long Size { get { return m_reader.ConvertValueToInt64(6); } }
            }

            public IEnumerable<IExistingFileBlock> Blocks 
            {
                get
                {
                    string p = this.TargetPath;
                    while (HasMore && p == this.TargetPath)
                    {
                        yield return new ExistingFileBlock(m_reader);
                        HasMore = m_reader.Read();
                    }
                }
            }

            public static IEnumerable<IExistingFile> GetExistingFilesWithBlocks(System.Data.IDbConnection connection, string tablename)
            {
                using(var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT ""{0}"".""TargetPath"", ""Blockset"".""FullHash"", ""{0}"".""ID"", ""Blockset"".""Length"", ""Block"".""Hash"", ""BlocksetEntry"".""Index"", ""Block"".""Size"" FROM ""{0}"", ""Blockset"", ""BlocksetEntry"", ""Block"" WHERE ""{0}"".""BlocksetID"" = ""Blockset"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""{0}"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ORDER BY ""{0}"".""TargetPath"", ""BlocksetEntry"".""Index""", tablename);
                    using(var rd = cmd.ExecuteReader())
                        if (rd.Read())
                        {
                            var more = true;
                            while(more)
                            {
                                var f = new ExistingFile(rd);
                                string current = f.TargetPath;
                                yield return f;

                                more = f.HasMore;
                                while (more && current == f.TargetPath)
                                    more = rd.Read();
                            }
                        }
                }
            }
        }

        public IEnumerable<IExistingFile> GetExistingFilesWithBlocks()
        {
            return ExistingFile.GetExistingFilesWithBlocks(m_connection, m_tempfiletable);
        }

        private class LocalBlockSource : ILocalBlockSource
        {
            private class BlockDescriptor : IBlockDescriptor
            {
                private class BlockSource : IBlockSource
                {
                    private System.Data.IDataReader m_reader;
                    public BlockSource(System.Data.IDataReader rd) { m_reader = rd; }

                    public string Path { get { return m_reader.ConvertValueToString(6); } }
                    public long Offset { get { return m_reader.ConvertValueToInt64(7); } }
                    public long Size { get { return m_reader.ConvertValueToInt64(8); } }
                    public bool IsMetadata { get { return false; } }
                }

                private System.Data.IDataReader m_reader;
                public BlockDescriptor(System.Data.IDataReader rd) { m_reader = rd; HasMore = true; }

                private string TargetPath { get { return m_reader.ConvertValueToString(0); } }

                public string Hash { get { return m_reader.ConvertValueToString(2); } }
                public long Offset { get { return m_reader.ConvertValueToInt64(3); } }
                public long Index { get { return m_reader.ConvertValueToInt64(4); } }
                public long Size { get { return m_reader.ConvertValueToInt64(5); } }
                public bool IsMetadata { get { return !(m_reader.ConvertValueToInt64(9) == 0); } }

                public bool HasMore { get; private set; }

                public IEnumerable<IBlockSource> Blocksources
                {
                    get
                    {
                        var p = this.TargetPath;
                        var h = this.Hash;
                        var s = this.Size;

                        while (HasMore && p == this.TargetPath && h == this.Hash && s == this.Size)
                        {
                            yield return new BlockSource(m_reader);
                            HasMore = m_reader.Read();
                        }
                    }
                }
            }

            private System.Data.IDataReader m_reader;
            public LocalBlockSource(System.Data.IDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0); } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(1); } }

            public bool HasMore { get; private set; }

            public IEnumerable<IBlockDescriptor> Blocks
            {
                get 
                {
                    var p = this.TargetPath;
                    while (HasMore && p == this.TargetPath)
                    {
                        var c = new BlockDescriptor(m_reader);
                        var h = c.Hash;
                        var s = c.Size;

                        yield return c;

                        HasMore = c.HasMore;
                        while (HasMore && c.Hash == h && c.Size == s && this.TargetPath == p)
                            HasMore = m_reader.Read();
                    }
                }
            }

            public static IEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize, bool skipMetadata)
            {
                using(var cmd = connection.CreateCommand())
                {
                    // TODO: Skip metadata as required
                    cmd.CommandText = string.Format(@"SELECT DISTINCT ""A"".""TargetPath"", ""A"".""ID"", ""B"".""Hash"", (""B"".""Index"" * {2}), ""B"".""Index"", ""B"".""Size"", ""C"".""Path"", (""D"".""Index"" * {2}), ""E"".""Size"", ""B"".""Metadata"" FROM ""{0}"" ""A"", ""{1}"" ""B"", ""File"" ""C"", ""BlocksetEntry"" ""D"", ""Block"" E WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""C"".""BlocksetID"" = ""D"".""BlocksetID"" AND ""D"".""BlockID"" = ""E"".""ID"" AND ""B"".""Hash"" = ""E"".""Hash"" AND ""B"".""Size"" = ""E"".""Size"" AND ""B"".""Restored"" = 0", filetablename, blocktablename, blocksize);
                    using(var rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            var more = true;
                            while(more)
                            {
                                var f = new LocalBlockSource(rd);
                                string current = f.TargetPath;
                                yield return f;

                                more = f.HasMore;
                                while (more && current == f.TargetPath)
                                    more = rd.Read();
                            }
                        }
                    }
                }
            }
        }

        public IEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(bool skipMetadata, long blocksize)
        {
            return LocalBlockSource.GetFilesAndSourceBlocks(m_connection, m_tempfiletable, m_tempblocktable, blocksize, skipMetadata);
        }

        public IEnumerable<IRemoteVolume> GetMissingVolumes()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT DISTINCT ""Block"".""VolumeID"" FROM ""Block"" WHERE ""Block"".""Hash"" IN (SELECT DISTINCT ""{0}"".""Hash"" FROM ""{0}"" WHERE ""{0}"".""Restored"" = 0))", m_tempblocktable);
                using (var rd = cmd.ExecuteReader())
                {
                    object[] r = new object[3];
                    while (rd.Read())
                    {
                        rd.GetValues(r);
                        yield return new RemoteVolume(
                            rd.ConvertValueToString(0),
                            rd.ConvertValueToString(1),
                            rd.ConvertValueToInt64(2, -1)
                        );
                    }
                }
            }
        }

        public interface IFilesAndMetadata : IDisposable
        {
            IEnumerable<IVolumePatch> FilesWithMissingBlocks { get; }
            IEnumerable<IVolumePatch> MetadataWithMissingBlocks { get; }
        }

        private class FilesAndMetadata : IFilesAndMetadata
        {
            private string m_tmptable;
            private string m_filetablename;
            private string m_blocktablename;
            private long m_blocksize;

            private System.Data.IDbConnection m_connection;

            public FilesAndMetadata(System.Data.IDbConnection connection, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
            {
                m_filetablename = filetablename;
                m_blocktablename = blocktablename;
                m_blocksize = blocksize;
                m_connection = connection;

                using (var c = m_connection.CreateCommand())
                {
                    m_tmptable = "VolumeFiles-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    c.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" ( ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL )", m_tmptable);
                    c.ExecuteNonQuery();


                    c.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Hash"", ""Size"") VALUES (?,?)", m_tmptable);
                    c.AddParameters(2);
                    foreach (var s in curvolume.Blocks)
                    {
                        c.SetParameterValue(0, s.Key);
                        c.SetParameterValue(1, s.Value);
                        c.ExecuteNonQuery();
                    }

                    c.CommandText = string.Format(@"CREATE INDEX ""{0}_HashSizeIndex"" ON ""{0}"" (""Hash"", ""Size"")", m_tmptable);
                    c.Parameters.Clear();
                    c.ExecuteNonQuery();

                }
            }

            public void Dispose()
            {
                if (m_tmptable != null)
                    using (var c = m_connection.CreateCommand())
                    {
                        c.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tmptable);
                        c.ExecuteNonQuery();
                    }
            }

            private class VolumePatch : IVolumePatch
            {
                private class PatchBlock : IPatchBlock
                {
                    private System.Data.IDataReader m_reader;
                    public PatchBlock(System.Data.IDataReader rd) { m_reader = rd; }

                    public long Offset { get { return m_reader.ConvertValueToInt64(2); } }
                    public long Size { get { return m_reader.ConvertValueToInt64(3); } }
                    public string Key { get { return m_reader.ConvertValueToString(4); } }
                }

                private System.Data.IDataReader m_reader;
                public VolumePatch(System.Data.IDataReader rd) { m_reader = rd; HasMore = true; }

                public string Path { get { return m_reader.ConvertValueToString(0); } }
                public long FileID { get { return m_reader.ConvertValueToInt64(1); } }
                public bool HasMore { get; private set; } 

                public IEnumerable<IPatchBlock> Blocks
                {
                    get
                    {
                        string p = this.Path;
                        while (HasMore && p == this.Path)
                        {
                            yield return new PatchBlock(m_reader);
                            HasMore = m_reader.Read();
                        }
                    }
                }
            }

            public IEnumerable<IVolumePatch> FilesWithMissingBlocks
            {
                get
                {
                    using(var cmd = m_connection.CreateCommand())
                    {
                        cmd.CommandText = string.Format(@"SELECT DISTINCT ""A"".""TargetPath"", ""B"".""FileID"", (""B"".""Index"" * {3}), ""B"".""Size"", ""C"".""Hash"" FROM ""{0}"" A, ""{1}"" B, ""{2}"" C WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""Hash"" = ""C"".""Hash"" AND ""B"".""Size"" = ""C"".""Size"" AND ""B"".""Restored"" = 0 AND ""B"".""Metadata"" = 0 ORDER BY ""A"".""TargetPath"", ""B"".""Index""", m_filetablename, m_blocktablename, m_tmptable, m_blocksize);
                        using(var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                var more = true;
                                while(more)
                                {
                                    var f = new VolumePatch(rd);
                                    string current = f.Path;
                                    yield return f;

                                    more = f.HasMore;
                                    while (more && current == f.Path)
                                        more = rd.Read();
                                }
                            }
                        }
                    }
                }
            }

            public IEnumerable<IVolumePatch> MetadataWithMissingBlocks
            {
                get
                { 
                    using(var cmd = m_connection.CreateCommand())
                    {
                        cmd.CommandText = string.Format(@"SELECT DISTINCT ""A"".""TargetPath"", ""B"".""FileID"", (""B"".""Index"" * {3}), ""B"".""Size"", ""C"".""Hash"" FROM ""{0}"" A, ""{1}"" B, ""{2}"" C WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""Hash"" = ""C"".""Hash"" AND ""B"".""Size"" = ""C"".""Size"" AND ""B"".""Restored"" = 0 AND ""B"".""Metadata"" = 1 ORDER BY ""A"".""TargetPath"", ""B"".""Index""", m_filetablename, m_blocktablename, m_tmptable, m_blocksize);
                        using(var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                var more = true;
                                while (more)
                                {
                                    var f = new VolumePatch(rd);
                                    string current = f.Path;
                                    yield return f;

                                    more = f.HasMore;
                                    while (more && current == f.Path)
                                        more = rd.Read();
                                }
                            }
                        }
                    }
                }
            }

            public string Tablename { get { return m_tmptable; } }
        }

        public IFilesAndMetadata GetMissingBlockData(BlockVolumeReader curvolume, long blocksize)
        {
            return new FilesAndMetadata(m_connection, m_tempfiletable, m_tempblocktable, blocksize, curvolume);
        }

		private class FileToRestore : IFileToRestore
		{
			public string Path { get; private set; }
			public string Hash { get; private set; }
			public long ID { get; private set; }
            public long Length { get; private set; }
			
            public FileToRestore(long id, string path, string hash, long length)
			{
				this.ID = id;
				this.Path = path;
				this.Hash = hash;
                this.Length = length;
			}
		}

        public IEnumerable<IFileToRestore> GetFilesToRestore()
        {
            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""{0}"".""ID"", ""{0}"".""TargetPath"", ""Blockset"".""FullHash"", ""Blockset"".""Length"" FROM ""{0}"",""Blockset"" WHERE ""{0}"".""BlocksetID"" = ""Blockset"".""ID"" ", m_tempfiletable)))
                while (rd.Read())
                    yield return new FileToRestore(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt64(3));
        }

        public void DropRestoreTable()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_tempfiletable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tempfiletable);
                        cmd.ExecuteNonQuery();
                    }
                    catch(Exception ex) { if (m_result != null) m_result.AddWarning(string.Format("Cleanup error: {0}", ex.Message),  ex); }
                    finally { m_tempfiletable = null; }

                if (m_tempblocktable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tempblocktable);
                        cmd.ExecuteNonQuery();
                    }
                    catch(Exception ex) { if (m_result != null) m_result.AddWarning(string.Format("Cleanup error: {0}", ex.Message),  ex); }
                    finally { m_tempblocktable = null; }
            }
        }

        public interface IBlockMarker : IDisposable
        {
            void SetBlockRestored(long targetfileid, long index, string hash, long blocksize, bool metadata);
            void SetAllBlocksMissing(long targetfileid);
            void SetAllBlocksRestored(long targetfileid);
            void Commit(ILogWriter log);
            void UpdateProcessed(IOperationProgressUpdater writer);
            System.Data.IDbTransaction Transaction { get; }
        }

        private class BlockMarker : IBlockMarker
        {
            private System.Data.IDbCommand m_insertblockCommand;
            private System.Data.IDbCommand m_resetfileCommand;
            private System.Data.IDbCommand m_updateAsRestoredCommand;
            private System.Data.IDbCommand m_statUpdateCommand;
            private bool m_hasUpdates = false;
            
            private string m_updateTable;
            private string m_blocktablename;
            
            public System.Data.IDbTransaction Transaction { get { return m_insertblockCommand.Transaction; } }

            public BlockMarker(System.Data.IDbConnection connection, string blocktablename, string filetablename)
            {
                m_insertblockCommand = connection.CreateCommand();
                m_resetfileCommand  = connection.CreateCommand();
                m_updateAsRestoredCommand = connection.CreateCommand();
                m_statUpdateCommand = connection.CreateCommand();
                
                m_insertblockCommand.Transaction = connection.BeginTransaction();
                m_resetfileCommand.Transaction = m_insertblockCommand.Transaction;
                m_updateAsRestoredCommand.Transaction = m_insertblockCommand.Transaction;
                m_statUpdateCommand.Transaction = m_insertblockCommand.Transaction;
                
                m_blocktablename = blocktablename;
                m_updateTable = "UpdatedBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                
                m_insertblockCommand.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""FileID"" INTEGER NOT NULL, ""Index"" INTEGER NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Metadata"" BOOLEAN NOT NULL)", m_updateTable));
                m_insertblockCommand.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_HashSizeIndex"" ON ""{0}"" (""Hash"", ""Size"")", m_updateTable));
                m_insertblockCommand.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_IndexIndex"" ON ""{0}"" (""Index"")", m_updateTable));

                m_insertblockCommand.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Metadata"") VALUES (?, ?, ?, ?, ?) ", m_updateTable);
                m_insertblockCommand.AddParameters(5);
                                
                m_resetfileCommand.CommandText = string.Format(@"DELETE FROM ""{0}"" WHERE ""FileID"" = ?", m_updateTable);
                m_resetfileCommand.AddParameters(1);
                
                m_updateAsRestoredCommand.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"") SELECT ""FileID"", ""Index"", ""Hash"", ""Size"" FROM ""{1}"" WHERE ""{1}"".""FileID"" = ?", m_updateTable, m_blocktablename);
                m_updateAsRestoredCommand.AddParameters(1);
                
                m_statUpdateCommand.CommandText = string.Format(@"SELECT COUNT(DISTINCT ""FileID""), SUM(""Size"") FROM ""{0}"" WHERE ""Restored"" = 1 OR ""ID"" IN (SELECT ""{0}"".""ID"" FROM ""{0}"", ""{1}"" WHERE ""{0}"".""FileID"" = ""{1}"".""FileID"" AND ""{0}"".""Index"" = ""{1}"".""Index"" AND ""{0}"".""Hash"" = ""{1}"".""Hash"" AND ""{0}"".""Size"" = ""{1}"".""Size"" )", m_blocktablename, m_updateTable);
            }
            
            public void UpdateProcessed(IOperationProgressUpdater updater)
            {
                if (!m_hasUpdates)
                    return;
                    
                m_hasUpdates = false;
                using(var rd = m_statUpdateCommand.ExecuteReader())
                {                    
                    var filesprocessed = 0L;
                    var processedsize = 0L;
                    
                    if (rd.Read())
                    {
                        filesprocessed = rd.ConvertValueToInt64(0, 0);
                        processedsize = rd.ConvertValueToInt64(1, 0);
                    }

                    updater.UpdatefilesProcessed(filesprocessed, processedsize);
                }
            }
            
            public void SetAllBlocksMissing(long targetfileid)
            {
                m_hasUpdates = true;
                m_resetfileCommand.SetParameterValue(0, targetfileid);
                var r = m_resetfileCommand.ExecuteNonQuery();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            public void SetAllBlocksRestored(long targetfileid)
            {
                m_hasUpdates = true;
                m_updateAsRestoredCommand.SetParameterValue(0, targetfileid);
                var r = m_updateAsRestoredCommand.ExecuteNonQuery();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }
            
            public void SetBlockRestored(long targetfileid, long index, string hash, long size, bool metadata)
            {
                m_hasUpdates = true;
                m_insertblockCommand.SetParameterValue(0, targetfileid);
                m_insertblockCommand.SetParameterValue(1, index);
                m_insertblockCommand.SetParameterValue(2, hash);
                m_insertblockCommand.SetParameterValue(3, size);
                m_insertblockCommand.SetParameterValue(4, metadata);
                var r = m_insertblockCommand.ExecuteNonQuery();
                if (r != 1)
                    throw new Exception("Unexpected insert result");
            }
            
            public void Commit(ILogWriter log)
            {
                m_insertblockCommand.Parameters.Clear();
                var rc = m_insertblockCommand.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Restored"" = 1 WHERE ""ID"" IN (SELECT ""{0}"".""ID"" FROM ""{0}"", ""{1}"" WHERE ""{0}"".""FileID"" = ""{1}"".""FileID"" AND ""{0}"".""Index"" = ""{1}"".""Index"" AND ""{0}"".""Hash"" = ""{1}"".""Hash"" AND ""{0}"".""Size"" = ""{1}"".""Size"" AND ""{0}"".""Metadata"" = ""{1}"".""Metadata"" )", m_blocktablename, m_updateTable));
                var nc = m_insertblockCommand.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}"" ", m_updateTable), 0);
                    		
                if (rc != nc)
                    log.AddWarning(string.Format("Inconsistency while marking blocks as updated. Updated blocks: {0}, Registered blocks: {1}", rc, nc), null);
                
                m_insertblockCommand.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_updateTable));
                m_updateTable = null;

                var tr = m_insertblockCommand.Transaction;
                m_insertblockCommand.Dispose();
                m_insertblockCommand = null;
                using(new Logging.Timer("CommitBlockMarker"))
                    tr.Commit();
                tr.Dispose();
            }

            public void Dispose()
            {
	            if (m_updateTable != null)
	            {
	            	try 
	            	{
	            		m_insertblockCommand.Parameters.Clear();
	            		m_insertblockCommand.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_updateTable));
	            	}
	            	catch { }
	            	finally { m_updateTable = null; }
	            }
                            
                if (m_insertblockCommand != null)
                    try { m_insertblockCommand.Dispose(); }
                    catch { }
                    finally { m_insertblockCommand = null; }
                    
                if (m_resetfileCommand != null)
                    try { m_resetfileCommand.Dispose(); }
                    catch { }
                    finally { m_resetfileCommand = null; }
                    
                if (m_updateAsRestoredCommand != null)
                    try { m_updateAsRestoredCommand.Dispose(); }
                    catch { }
                    finally { m_updateAsRestoredCommand = null; }
                    
                if (m_statUpdateCommand != null)
                    try { m_statUpdateCommand.Dispose(); }
                    catch { }
                    finally { m_statUpdateCommand = null; }
                
            }
        }

        public IBlockMarker CreateBlockMarker()
        {
            return new BlockMarker(m_connection, m_tempblocktable, m_tempfiletable);
        }

        public override void Dispose()
        {
            base.Dispose();
            DropRestoreTable();
        }

        public IEnumerable<string> GetTargetFolders()
        {
            using (var cmd = m_connection.CreateCommand())
            using (var rd = cmd.ExecuteReader(string.Format(@"SELECT ""TargetPath"" FROM ""{0}"" WHERE ""BlocksetID"" == ?", m_tempfiletable), FOLDER_BLOCKSET_ID))
            	while(rd.Read())
            		yield return rd.GetValue(0).ToString();
        }

		public interface IFastSource
		{
			string TargetPath { get; }
			long TargetFileID { get; }
			string SourcePath { get; }
			IEnumerable<IBlockEntry> Blocks { get; }
		}
		
		public interface IBlockEntry
		{
			long Offset { get; }
			long Size { get; }
			long Index { get; }
			string Hash { get; }
		}

		private class FastSource : IFastSource
		{
			private class BlockEntry : IBlockEntry
			{
				private System.Data.IDataReader m_rd;
				private long m_blocksize;
				public BlockEntry(System.Data.IDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; }
                public long Offset { get { return m_rd.GetInt64(3) * m_blocksize; } }
                public long Index { get { return m_rd.GetInt64(3); } }
                public long Size { get { return m_rd.GetInt64(5); } }
                public string Hash { get { return m_rd.GetString(4); } }
			}
		
			private System.Data.IDataReader m_rd;
			private long m_blocksize;
			public FastSource(System.Data.IDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; MoreData = true; }
			public bool MoreData { get; private set; }
			public string TargetPath { get { return m_rd.GetValue(0).ToString(); } }
            public long TargetFileID { get { return m_rd.GetInt64(2); } }
			public string SourcePath { get { return m_rd.GetValue(1).ToString(); } }
			
			public IEnumerable<IBlockEntry> Blocks
			{
				get
				{
					var tid = this.TargetFileID;
					
					do
					{
						yield return new BlockEntry(m_rd, m_blocksize);
					} while((MoreData = m_rd.Read()) && tid == this.TargetFileID);
					
				}
			}
		}

        public IEnumerable<IFastSource> GetFilesAndSourceBlocksFast(long blocksize)
		{
            var whereclause = string.Format(@" ""{0}"".""ID"" = ""{1}"".""FileID"" AND ""{1}"".""Restored"" = 0 AND ""{1}"".""Metadata"" = 0 AND ""{0}"".""TargetPath"" != ""{0}"".""Path"" ", m_tempfiletable, m_tempblocktable);		
			var sourepaths = string.Format(@"SELECT DISTINCT ""{0}"".""Path"" FROM ""{0}"", ""{1}"" WHERE " + whereclause, m_tempfiletable, m_tempblocktable);
			var latestBlocksetIds = @"SELECT ""File"".""Path"", ""File"".""BlocksetID"", MAX(""Fileset"".""Timestamp"") FROM ""Fileset"", ""FilesetEntry"", ""File"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" = ""Fileset"".""ID"" AND ""File"".""Path"" IN (" + sourepaths + @") GROUP BY ""File"".""Path"" ";
			var sources = string.Format(@"SELECT DISTINCT ""{0}"".""TargetPath"", ""{0}"".""Path"", ""{0}"".""ID"", ""{1}"".""Index"", ""{1}"".""Hash"", ""{1}"".""Size"" FROM ""{0}"", ""{1}"", ""File"", (" + latestBlocksetIds + @") S, ""Block"", ""BlocksetEntry"" WHERE ""BlocksetEntry"".""BlocksetID"" = ""S"".""BlocksetID"" AND ""BlocksetEntry"".""BlocksetID"" = ""File"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""{1}"".""Index"" = ""BlocksetEntry"".""Index"" AND ""{1}"".""Hash"" = ""Block"".""Hash"" AND ""{1}"".""Size"" = ""Block"".""Size"" AND ""S"".""Path"" = ""{0}"".""Path"" AND " + whereclause + @" ORDER BY ""{0}"".""ID"", ""{1}"".""Index"" ", m_tempfiletable, m_tempblocktable);
			using(var cmd = m_connection.CreateCommand())
			using(var rd = cmd.ExecuteReader(sources))
			{
				if (rd.Read())
				{
					var more = false;
					do
					{
						var n = new FastSource(rd, blocksize);
						var tid = n.TargetFileID;
						yield return n;
						
						more = n.MoreData;
						while(more && n.TargetFileID == tid)
							more = rd.Read();

					} while (more);
				}
			}	
		}

    }
}
