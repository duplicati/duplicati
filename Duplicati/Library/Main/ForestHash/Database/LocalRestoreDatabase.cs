using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public partial class LocalRestoreDatabase : LocalDatabase
    {
        protected string m_tempfiletable;
        protected string m_tempblocktable;
        protected long m_blocksize;

        public LocalRestoreDatabase(string path, long blocksize)
            : this(new LocalDatabase(path, "Restore"), blocksize)
        {
        }

        public LocalRestoreDatabase(LocalDatabase dbparent, long blocksize)
            : base(dbparent)
        {
            //TODO: Should read this from DB?
            m_blocksize = blocksize;
        }

        public void PrepareRestoreFilelist(DateTime restoretime, string[] p, Utility.FilenameFilter filenameFilter, CommunicationStatistics stat)
        {
            var guid = Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            m_tempfiletable = "Fileset-" + guid;
            m_tempblocktable = "Blocks-" + guid;

            using (var cmd = m_connection.CreateCommand())
            {
                long filesetId = GetFilesetID(restoretime);

                cmd.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY, ""Path"" TEXT NOT NULL, ""BlocksetID"" INTEGER NOT NULL, ""MetadataID"" INTEGER NOT NULL, ""Targetpath"" TEXT NULL ) ", m_tempfiletable);
                cmd.ExecuteNonQuery();

                cmd.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY, ""FileID"" INTEGER NOT NULL, ""Index"" INTEGER NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Restored"" BOOLEAN NOT NULL)", m_tempblocktable);
                cmd.ExecuteNonQuery();

                if (filenameFilter == null && (p == null || p.Length == 0))
                {
                    // Simple case, restore everything
                    cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = ? ", m_tempfiletable);
                    cmd.AddParameter(filesetId);
                    cmd.ExecuteNonQuery();
                }
                else if (p != null && p.Length > 0)
                {
                    using(var tr = m_connection.BeginTransaction())
                    {
                        var m_filenamestable = "Filenames-" + guid;
                        cmd.Transaction = tr;
                        cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL) ", m_filenamestable));
                        cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"") VALUES (?)", m_filenamestable);
                        cmd.AddParameter();
                        
                        foreach (var s in p)
                        {
                            cmd.SetParameterValue(0, s);
                            cmd.ExecuteNonQuery();
                        }
                        
                        //TODO: Handle case-insensitive filename lookup
                        cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""Path"" IN (SELECT DISTINCT ""Path"" FROM ""{1}"") ", m_tempfiletable, m_filenamestable);
                        cmd.SetParameterValue(0, filesetId);
                        var c = cmd.ExecuteNonQuery();
                        
                        cmd.Parameters.Clear();
                        
                        if (c != p.Length)
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine();
                            
                            using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""Path"" FROM ""{0}"" WHERE ""Path"" NOT IN (SELECT ""Path"" FROM ""{1}"")", m_filenamestable, m_tempfiletable)))
                                while(rd.Read())
                                    sb.AppendLine(rd.GetValue(0).ToString());

                            DateTime actualrestoretime = Convert.ToDateTime(cmd.ExecuteScalar(@"SELECT ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" = ?", filesetId));
                            stat.LogWarning(string.Format("{0} File(s) were not found in list of files for backup at {1}, will not be restored: {2}", p.Length - c, actualrestoretime.ToLocalTime(), sb), null);
                            cmd.Parameters.Clear();
                        }
                        
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", m_filenamestable));
                        
                        tr.Commit();
                    }
                }
                else
                {
                    // Restore but filter elements based on regexp
                    cmd.CommandText = string.Format(@"SELECT ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetID"" = ?");
                    cmd.AddParameter(filesetId);

                    object[] values = new object[3];
                    using (var cmd2 = m_connection.CreateCommand())
                    {
                        cmd2.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""BlocksetID"", ""MetadataID"") VALUES (?,?,?)", m_tempfiletable);
                        cmd2.AddParameter();
                        cmd2.AddParameter();
                        cmd2.AddParameter();

                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                            {
                                rd.GetValues(values);
                                if (values[0] != null && filenameFilter.ShouldInclude("", values[0].ToString()))
                                {
                                    cmd2.SetParameterValue(0, values[0]);
                                    cmd2.SetParameterValue(1, values[0]);
                                    cmd2.SetParameterValue(2, values[0]);
                                    cmd2.ExecuteNonQuery();
                                }
                            }
                    }
                }
            }
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
                var filecount = Convert.ToInt64(cmd.ExecuteScalar());
                long foundfiles = -1;

                //TODO: Handle FS case-sensitive?
                cmd.CommandText = string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE SUBSTR(""Path"", 1, ?) = ?", m_tempfiletable);
                cmd.AddParameter();
                cmd.AddParameter();

                while (filecount != foundfiles && maxpath.Length > 0)
                {
                    var mp = Utility.Utility.AppendDirSeparator(maxpath);
                    cmd.SetParameterValue(0, mp.Length);
                    cmd.SetParameterValue(1, mp);
                    foundfiles = Convert.ToInt64(cmd.ExecuteScalar());

                    if (filecount != foundfiles)
                    {
                        var oldlen = maxpath.Length;
                        maxpath = System.IO.Path.GetDirectoryName(maxpath);
                        if (maxpath.Length == oldlen)
                            maxpath = "";
                    }
                }

                return maxpath == "" ? "" : Utility.Utility.AppendDirSeparator(maxpath);
            }

        }

        public void SetTargetPaths(string largest_prefix, string destination)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                destination = Utility.Utility.AppendDirSeparator(destination);
                largest_prefix = Utility.Utility.AppendDirSeparator(largest_prefix);

                cmd.CommandText = string.Format(@"UPDATE ""{0}"" SET ""Targetpath"" = ? || SUBSTR(""Path"", ?)", m_tempfiletable);
                cmd.AddParameter(destination);
                cmd.AddParameter(largest_prefix.Length + 1);
                cmd.ExecuteNonQuery();
            }
        }

        public void FindMissingBlocks()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Restored"") SELECT DISTINCT ""{1}"".""ID"", ""BlocksetEntry"".""Index"", ""Block"".""Hash"", ""Block"".""Size"", 0 FROM ""{1}"", ""BlocksetEntry"", ""Block"" WHERE ""{1}"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ", m_tempblocktable, m_tempfiletable);
                var p = cmd.ExecuteNonQuery();
                System.Diagnostics.Trace.WriteLine(string.Format("Blocks to restore: {0}", p));
            }
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
            long TargetFileID { get; }
            long Length { get; }
            IEnumerable<IExistingFileBlock> Blocks { get; }
        }


        public interface IBlockSource
        {
            string Path { get; }
            long Size { get; }
            long Offset { get; }
        }

        public interface IBlockDescriptor
        {
            string Hash { get; }
            long Size { get; }
            long Offset { get; }
            long Index { get; }
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
            IEnumerable<IPatchBlock> Blocks { get; }
        }

        public IEnumerable<IExistingFile> GetExistingFilesWithBlocks()
        {
            return new ExistingFileEnumerable(m_connection, m_tempfiletable);
        }

        public IEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks()
        {
            return new LocalBlockSourceEnumerable(m_connection, m_tempfiletable, m_tempblocktable, m_blocksize);
        }

        public IList<IRemoteVolume> GetMissingVolumes()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                List<IRemoteVolume> result = new List<IRemoteVolume>();
                cmd.CommandText = string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT DISTINCT ""Block"".""VolumeID"" FROM ""Block"" WHERE ""Block"".""Hash"" IN (SELECT DISTINCT ""{0}"".""Hash"" FROM ""{0}"" WHERE ""{0}"".""Restored"" = 0))", m_tempblocktable);
                using (var rd = cmd.ExecuteReader())
                {
                    object[] r = new object[3];
                    while (rd.Read())
                    {
                        rd.GetValues(r);
                        result.Add(new RemoteVolume(
                            r[0] == null ? null : r[0].ToString(),
                            r[1] == null ? null : r[1].ToString(),
                            r[2] == null ? -1 : Convert.ToInt64(r[2])
                        ));
                    }
                }

                return result;
            }
        }

        public IEnumerable<IVolumePatch> GetFilesWithMissingBlocks(BlockVolumeReader curvolume)
        {
            return new VolumePatchEnumerable(m_connection, m_tempfiletable, m_tempblocktable, m_blocksize, curvolume);
        }

        public IEnumerable<IFileToRestore> GetFilesToRestore()
        {
            return new FileToRestoreEnumerable(m_connection, m_tempfiletable);
        }

        public void DropRestoreTable()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_tempfiletable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE ""{0}""", m_tempfiletable);
                        cmd.ExecuteNonQuery();
                    }
                    finally { m_tempfiletable = null; }

                if (m_tempblocktable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE ""{0}""", m_tempblocktable);
                        cmd.ExecuteNonQuery();
                    }
                    finally { m_tempblocktable = null; }
            }
        }

        public interface IBlockMarker : IDisposable
        {
            void SetBlockRestored(long targetfileid, long index, string hash, long blocksize);
            void Commit();
            System.Data.IDbTransaction Transaction { get; }
        }

        private class BlockMarker : IBlockMarker
        {
            private System.Data.IDbCommand m_command;
            private string m_updateTable;
            private string m_blocktablename;
            
            public System.Data.IDbTransaction Transaction { get { return m_command.Transaction; } }

            public BlockMarker(System.Data.IDbConnection connection, string blocktablename, string filetablename)
            {
                m_command = connection.CreateCommand();
                m_command.Transaction = connection.BeginTransaction();
                
                m_blocktablename = blocktablename;
                m_updateTable = "UpdatedBlocks-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                
                m_command.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""FileID"" INTEGER NOT NULL, ""Index"" INTEGER NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)", m_updateTable));
                m_command.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"", ""Index"", ""Hash"", ""Size"") VALUES (?, ?, ?, ?) ", m_updateTable);
                m_command.AddParameters(4);
            }

            public void SetBlockRestored(long targetfileid, long index, string hash, long size)
            {
                m_command.SetParameterValue(0, targetfileid);
                m_command.SetParameterValue(1, index);
                m_command.SetParameterValue(2, hash);
                m_command.SetParameterValue(3, size);
                var r = m_command.ExecuteNonQuery();
                if (r != 1)
                    throw new Exception("Unexpected insert result");
            }

            public void Commit()
            {
            	m_command.Parameters.Clear();
            	var rc = m_command.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Restored"" = 1 WHERE ""ID"" IN (SELECT ""{0}"".""ID"" FROM ""{0}"", ""{1}"" WHERE ""{0}"".""FileID"" = ""{1}"".""FileID"" AND ""{0}"".""Index"" = ""{1}"".""Index"" AND ""{0}"".""Hash"" = ""{1}"".""Hash"" AND ""{0}"".""Size"" = ""{1}"".""Size"" )", m_blocktablename, m_updateTable));
            	var nc = Convert.ToInt64(m_command.ExecuteScalar(string.Format(@"SELECT COUNT(*) FROM ""{0}"" ", m_updateTable)));
            
        		m_command.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", m_updateTable));
        		m_updateTable = null;
        		
        		if (rc != nc)
        			throw new Exception(string.Format("Inconsistency while marking blocks as updated. Updated blocks: {0}, Registered blocks: {1}", rc, nc));

                var tr = m_command.Transaction;
                m_command.Dispose();
                m_command = null;
                tr.Commit();
                tr.Dispose();
            }

            public void Dispose()
            {
	            if (m_updateTable != null)
	            {
	            	try 
	            	{
	            		m_command.Parameters.Clear();
	            		m_command.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", m_updateTable));
	            	}
	            	catch { }
	            	finally { m_updateTable = null; }
	            }
            
                if (m_command != null)
                {
                    var t = m_command;
                    m_command = null;
                    t.Dispose();
                }
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
            return new FolderListEnumerable(m_connection, m_tempfiletable);
        }
    }
}
