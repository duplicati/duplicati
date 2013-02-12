using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public class Localdatabase : IDisposable
    {
        protected readonly System.Data.IDbConnection m_connection;
        protected readonly long m_operationid = -1;
        protected object m_lock = new object();

        private readonly System.Data.IDbCommand m_updateremotevolumeCommand;
        private readonly System.Data.IDbCommand m_selectremotevolumesCommand;
        private readonly System.Data.IDbCommand m_selectremotevolumeCommand;
        private readonly System.Data.IDbCommand m_removeremotevolumeCommand;

        private readonly System.Data.IDbCommand m_insertlogCommand;
        private readonly System.Data.IDbCommand m_insertremotelogCommand;

        public const long FOLDER_BLOCKSET_ID = -100;
        public const long SYMLINK_BLOCKSET_ID = -200;

        public DateTime OperationTimestamp { get; private set; }


        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        /// <param name="path">The path to the database</param>
        /// <param name="operation">The name of the operation</param>
        public Localdatabase(string path, string operation)
        {
            this.OperationTimestamp = DateTime.UtcNow;
            m_connection = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.Utility.SQLiteLoader.SQLiteConnectionType);

            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

            Utility.DatabaseUpgrader.UpgradeDatabase(m_connection, path, typeof(Localdatabase));

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES (?, ?)";
                cmd.AddParameter(operation);
                cmd.AddParameter(OperationTimestamp);

                cmd.ExecuteNonQuery();
                cmd.CommandText = "SELECT last_insert_rowid()";
                m_operationid = Convert.ToInt64(cmd.ExecuteScalar());
            }

            m_updateremotevolumeCommand = m_connection.CreateCommand();
            m_selectremotevolumesCommand = m_connection.CreateCommand();
            m_selectremotevolumeCommand = m_connection.CreateCommand();
            m_insertlogCommand = m_connection.CreateCommand();
            m_insertremotelogCommand = m_connection.CreateCommand();
            m_removeremotevolumeCommand = m_connection.CreateCommand();

            m_insertlogCommand.CommandText = @"INSERT INTO ""LogData"" (""OperationID"", ""Timestamp"", ""Type"", ""Message"", ""Exception"") VALUES (?, ?, ?, ?, ?)";
            m_insertlogCommand.AddParameter(m_operationid);
            m_insertlogCommand.AddParameters(4);

            m_insertremotelogCommand.CommandText = @"INSERT INTO ""RemoteOperation"" (""OperationID"", ""Timestamp"", ""Operation"", ""Path"", ""Data"") VALUES (?, ?, ?, ?, ?)";
            m_insertremotelogCommand.AddParameter(m_operationid);
            m_insertremotelogCommand.AddParameters(4);

            m_updateremotevolumeCommand.CommandText = @"UPDATE ""Remotevolume"" SET ""OperationID"" = ?, ""State"" = ?, ""Hash"" = ?, ""Size"" = ? WHERE ""Name"" = ?";
            m_updateremotevolumeCommand.AddParameter(m_operationid);
            m_updateremotevolumeCommand.AddParameters(4);

            m_selectremotevolumesCommand.CommandText = @"SELECT ""Name"", ""Type"", ""Size"", ""Hash"", ""State"" FROM ""Remotevolume""";

            m_selectremotevolumeCommand.CommandText = @"SELECT ""Type"", ""Size"", ""Hash"", ""State"" FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_selectremotevolumeCommand.AddParameter();

            m_removeremotevolumeCommand.CommandText = @"DELETE FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_removeremotevolumeCommand.AddParameter();
        }

        public void UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash)
        {
            lock (m_lock)
            {
                m_updateremotevolumeCommand.SetParameterValue(1, state.ToString());
                m_updateremotevolumeCommand.SetParameterValue(2, hash);
                m_updateremotevolumeCommand.SetParameterValue(3, size);
                m_updateremotevolumeCommand.SetParameterValue(4, name);
                m_updateremotevolumeCommand.ExecuteNonQuery();
            }
        }

        public bool GetRemoteVolume(string file, out string hash, out long size, out RemoteVolumeType type, out RemoteVolumeState state)
        {
            m_selectremotevolumeCommand.SetParameterValue(0, file);
            using (var rd = m_selectremotevolumeCommand.ExecuteReader())
                if (rd.Read())
                {
                    hash = (rd.GetValue(2) == null || rd.GetValue(2) == DBNull.Value) ? null : rd.GetValue(3).ToString();
                    size = (rd.GetValue(1) == null || rd.GetValue(1) == DBNull.Value) ? -1 : Convert.ToInt64(rd.GetValue(2));
                    type = (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(0).ToString());
                    state = (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(3).ToString());
                    return true;
                }

            hash = null;
            size = -1;
            type = (RemoteVolumeType)(-1);
            state = (RemoteVolumeState)(-1);
            return false;
        }

        public IList<RemoteVolumeEntry> GetRemoteVolumes()
        {
            var res = new List<RemoteVolumeEntry>();
            using (var rd = m_selectremotevolumesCommand.ExecuteReader())
            {
                while (rd.Read())
                {
                    res.Add(new RemoteVolumeEntry(
                        rd.GetValue(0).ToString(),
                        (rd.GetValue(3) == null || rd.GetValue(3) == DBNull.Value) ? null : rd.GetValue(3).ToString(),
                        (rd.GetValue(2) == null || rd.GetValue(2) == DBNull.Value) ? -1 : Convert.ToInt64(rd.GetValue(2)),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(1).ToString()),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(4).ToString())
                        )
                    );
                }
            }

            return res;
        }

        /// <summary>
        /// Log an operation performed on the remote backend
        /// </summary>
        /// <param name="operation">The operation performed</param>
        /// <param name="path">The path involved</param>
        /// <param name="data">Any data relating to the operation</param>
        public void LogRemoteOperation(string operation, string path, string data)
        {
            lock (m_lock)
            {
                m_insertremotelogCommand.SetParameterValue(1, DateTime.UtcNow);
                m_insertremotelogCommand.SetParameterValue(2, operation);
                m_insertremotelogCommand.SetParameterValue(3, path);
                m_insertremotelogCommand.SetParameterValue(4, data);
                m_insertremotelogCommand.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        /// <param name="type">The message type</param>
        /// <param name="message">The message</param>
        /// <param name="exception">An optional exception</param>
        public void LogMessage(string type, string message, Exception exception)
        {
            lock (m_lock)
            {
                m_insertlogCommand.SetParameterValue(1, DateTime.UtcNow);
                m_insertlogCommand.SetParameterValue(2, type);
                m_insertlogCommand.SetParameterValue(3, message);
                m_insertlogCommand.SetParameterValue(4, exception == null ? null : exception.ToString());
                m_insertlogCommand.ExecuteNonQuery();
            }
        }

        public void RemoveRemoteVolume(string name)
        {
            lock (m_lock)
            {
                using (var tr = m_connection.BeginTransaction())
                {
                    var deletecmd = m_connection.CreateCommand();
                    deletecmd.Transaction = tr;

                    var updatedfiles = deletecmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""BlocksetID"" = -1 WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""File"" = ?))", name);
                    var updatedmetadata = deletecmd.ExecuteNonQuery(@"UPDATE ""Metadataset"" SET ""BlocksetID"" = -1 WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""File"" = ?))", name);
                    var deletedblocksets = deletecmd.ExecuteNonQuery(@"DELETE FROM ""Blockset"" WHERE ""ID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""File"" = ?))", name);
                    var deletedblocksetentries = deletecmd.ExecuteNonQuery(@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""ID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""File"" = ?))", name);

                    var deletedblocks = deletecmd.ExecuteNonQuery(@"DELETE FROM ""Block"" WHERE ""File"" = ?", name);

                    ((System.Data.IDataParameter)m_removeremotevolumeCommand.Parameters[0]).Value = name;
                    m_removeremotevolumeCommand.Transaction = tr;
                    m_removeremotevolumeCommand.ExecuteNonQuery();

                    tr.Commit();
                }
            }
        }

        public long GetFilesetID(DateTime restoretime)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT ""ID"" FROM ""Operation"" WHERE (strftime(""%s"",?) - strftime(""%s"", ""Timestamp"")) >= 0 AND ""Description"" = ""Backup"" ORDER BY ""Timestamp"" DESC";
                cmd.AddParameter(restoretime.ToUniversalTime());
                object r = cmd.ExecuteScalar();
                if (r == null)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Operation"" WHERE ""Description"" = ""Backup"" ORDER BY ""Timestamp"" DESC ";
                    r = cmd.ExecuteScalar();
                    if (r == null)
                        throw new Exception("No backup at the specified date");
                }

                return Convert.ToInt64(r);
            }
        }

        private class LocalFileEntryEnumerable : IEnumerable<ILocalFileEntry>
        {
            private class LocalFileEntryEnumerator : IEnumerator<ILocalFileEntry>
            {
                private class LocalFileEntry : ILocalFileEntry
                {
                    private System.Data.IDataReader m_reader;
                    public LocalFileEntry(System.Data.IDataReader reader)
                    {
                        m_reader = reader;
                    }

                    public string Path
                    {
                        get 
                        {
                            var c = m_reader.GetValue(0);
                            if (c == null || c == DBNull.Value)
                                return null;
                            return c.ToString();
                        }
                    }

                    public long Length
                    {
                        get
                        {
                            var c = m_reader.GetValue(1);
                            if (c == null || c == DBNull.Value)
                                return -1;
                            return Convert.ToInt64(c);
                        }
                    }

                    public string Hash
                    {
                        get
                        {
                            var c = m_reader.GetValue(2);
                            if (c == null || c == DBNull.Value)
                                return null;
                            return c.ToString();
                        }
                    }

                    public string Metahash
                    {
                        get
                        {
                            var c = m_reader.GetValue(3);
                            if (c == null || c == DBNull.Value)
                                return null;
                            return c.ToString();
                        }
                    }
                }

                private System.Data.IDbConnection m_connection;
                private System.Data.IDbCommand m_command;
                private System.Data.IDataReader m_reader;
                private long m_filesetid;
                private LocalFileEntry m_current;

                public LocalFileEntryEnumerator(System.Data.IDbConnection connection, long filesetid)
                {
                    m_connection = connection;
                    m_filesetid = filesetid;
                    this.Reset();
                }

                public ILocalFileEntry Current { get { return m_current; } }
                object System.Collections.IEnumerator.Current { get { return this.Current; } }

                public void Dispose() 
                { 
                    if (m_reader != null)
                        try { m_reader.Dispose(); }
                        finally { m_reader = null; }

                    if (m_command != null)
                        try { m_command.Dispose(); }
                        finally { m_command = null; }
                }

                public bool MoveNext()
                {
                    return m_reader.Read();
                }

                public void Reset()
                {
                    this.Dispose();
                    m_command = m_connection.CreateCommand();
                    m_command.CommandText = @"SELECT ""A"".""Path"", ""B"".""Length"", ""B"".""FullHash"", ""D"".""FullHash"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""Blockset"" D WHERE ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""C"".""BlocksetID"" = ""D"".""ID"" AND ""A"".""OperationID"" = ? ";
                    m_command.AddParameter(m_filesetid);
                    m_reader = m_command.ExecuteReader();
                    m_current = new LocalFileEntry(m_reader);

                }
            }

            private System.Data.IDbConnection m_connection;
            private long m_filesetid;
            public LocalFileEntryEnumerable(System.Data.IDbConnection connection, long filesetid)
            {
                m_connection = connection;
                m_filesetid = filesetid;
            }

            public IEnumerator<ILocalFileEntry> GetEnumerator() { return new LocalFileEntryEnumerator(m_connection, m_filesetid); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
        }

        public IEnumerable<ILocalFileEntry> GetFiles(long filesetid)
        {
            return new LocalFileEntryEnumerable(m_connection, filesetid);
        }

        public virtual void Dispose()
        {
        }
    }
}
