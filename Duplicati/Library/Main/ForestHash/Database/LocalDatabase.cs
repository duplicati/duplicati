using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public class LocalDatabase : IDisposable
    {
        protected readonly System.Data.IDbConnection m_connection;
        protected readonly long m_operationid = -1;
        protected object m_lock = new object();

        private readonly System.Data.IDbCommand m_updateremotevolumeCommand;
        private readonly System.Data.IDbCommand m_selectremotevolumesCommand;
        private readonly System.Data.IDbCommand m_selectremotevolumeCommand;
        private readonly System.Data.IDbCommand m_removeremotevolumeCommand;
		private readonly System.Data.IDbCommand m_selectremotevolumeIdCommand;
        private readonly System.Data.IDbCommand m_createremotevolumeCommand;

        private readonly System.Data.IDbCommand m_insertlogCommand;
        private readonly System.Data.IDbCommand m_insertremotelogCommand;

        public const long FOLDER_BLOCKSET_ID = -100;
        public const long SYMLINK_BLOCKSET_ID = -200;

        public DateTime OperationTimestamp { get; private set; }

        internal System.Data.IDbConnection Connection { get { return m_connection; } }

        protected static System.Data.IDbConnection CreateConnection(string path)
        {
            var c = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.Utility.SQLiteLoader.SQLiteConnectionType);
            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

            Utility.DatabaseUpgrader.UpgradeDatabase(c, path, typeof(LocalDatabase));
            
            return c;
        }

        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        /// <param name="path">The path to the database</param>
        /// <param name="operation">The name of the operation</param>
        public LocalDatabase(string path, string operation)
            : this(CreateConnection(path), operation)
        {
        }

        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        /// <param name="path">The path to the database</param>
        /// <param name="operation">The name of the operation</param>
        public LocalDatabase(System.Data.IDbConnection connection, string operation)
        {
            this.OperationTimestamp = DateTime.UtcNow;
            m_connection = connection;

            if (m_connection.State != System.Data.ConnectionState.Open)
                m_connection.Open();

            using (var cmd = m_connection.CreateCommand())
                m_operationid = Convert.ToInt64(cmd.ExecuteScalar( @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES (?, ?); SELECT last_insert_rowid();", operation, OperationTimestamp));

            m_updateremotevolumeCommand = m_connection.CreateCommand();
            m_selectremotevolumesCommand = m_connection.CreateCommand();
            m_selectremotevolumeCommand = m_connection.CreateCommand();
            m_insertlogCommand = m_connection.CreateCommand();
            m_insertremotelogCommand = m_connection.CreateCommand();
            m_removeremotevolumeCommand = m_connection.CreateCommand();
			m_selectremotevolumeIdCommand = m_connection.CreateCommand();
			m_createremotevolumeCommand = m_connection.CreateCommand();

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

			m_selectremotevolumeIdCommand.CommandText = @"SELECT ""ID"" FROM ""Remotevolume"" WHERE ""Name"" = ?";

			m_createremotevolumeCommand.CommandText = @"INSERT INTO ""Remotevolume"" (""OperationID"", ""Name"", ""Type"", ""State"") VALUES (?, ?, ?, ?); SELECT last_insert_rowid();";
			m_createremotevolumeCommand.AddParameter(m_operationid);
            m_createremotevolumeCommand.AddParameters(3);
		}
		
		public void UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash, System.Data.IDbTransaction transaction = null)
        {
            lock (m_lock)
            {
                m_updateremotevolumeCommand.Transaction = transaction;
                m_updateremotevolumeCommand.SetParameterValue(1, state.ToString());
                m_updateremotevolumeCommand.SetParameterValue(2, hash);
                m_updateremotevolumeCommand.SetParameterValue(3, size);
                m_updateremotevolumeCommand.SetParameterValue(4, name);
                m_updateremotevolumeCommand.ExecuteNonQuery();
            }
        }
        
        public long GetRemoteVolumeID(string file)
		{
			var o = m_selectremotevolumeIdCommand.ExecuteScalar(null, file);
			if (o == null || o == DBNull.Value)
				return -1;
			else
				return Convert.ToInt64(o);
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
                //TODO: It seems that SQLite with Mono has some issues with cross-thread db-access.
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

        public void RemoveRemoteVolume(string name, System.Data.IDbTransaction transaction = null)
        {
            lock (m_lock)
            {
                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    var deletecmd = m_connection.CreateCommand();
                    deletecmd.Transaction = tr.Parent;

					deletecmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""BlocksetID"" = -1 WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)))", name);
					deletecmd.ExecuteNonQuery(@"UPDATE ""Metadataset"" SET ""BlocksetID"" = -1 WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)))", name);
					deletecmd.ExecuteNonQuery(@"DELETE FROM ""Blockset"" WHERE ""ID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)))", name);
					deletecmd.ExecuteNonQuery(@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""ID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)))", name);
					
					deletecmd.ExecuteNonQuery(@"DELETE FROM ""Block"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)", name);
					deletecmd.ExecuteNonQuery(@"DELETE FROM ""DeletedBlock"" WHERE ""VolumeID"" IN (SELECT DISTINCT ID FROM ""RemoteVolume"" WHERE ""Name"" = ?)", name);

                    ((System.Data.IDataParameter)m_removeremotevolumeCommand.Parameters[0]).Value = name;
                    m_removeremotevolumeCommand.Transaction = tr.Parent;
                    m_removeremotevolumeCommand.ExecuteNonQuery();

                    tr.Commit();
                }
            }
        }

		public long RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, System.Data.IDbTransaction transaction = null)
		{
            lock (m_lock)
            {
            	using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            	{
	                m_createremotevolumeCommand.SetParameterValue(1, name);
	                m_createremotevolumeCommand.SetParameterValue(2, type.ToString());
	                m_createremotevolumeCommand.SetParameterValue(3, state.ToString());
	                m_createremotevolumeCommand.Transaction = tr.Parent;
	                var r = Convert.ToInt64(m_createremotevolumeCommand.ExecuteScalar());
	                tr.Commit();
	                return r;
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

        public System.Data.IDbTransaction BeginTransaction()
        {
            return m_connection.BeginTransaction();
        }

        protected class TemporaryTransactionWrapper : IDisposable
        {
            private System.Data.IDbTransaction m_parent;
            private bool m_isTemporary;

            public TemporaryTransactionWrapper(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
            {
                if (transaction != null)
                {
                    m_parent = transaction;
                    m_isTemporary = false;
                }
                else
                {
                    m_parent = connection.BeginTransaction();
                    m_isTemporary = true;
                }
            }

            public System.Data.IDbConnection Connection { get { return m_parent.Connection; } }
            public System.Data.IsolationLevel IsolationLevel { get { return m_parent.IsolationLevel; } }

            public void Commit() 
            { 
                if (m_isTemporary) 
                    m_parent.Commit(); 
            }

            public void Rollback()
            {
                if (m_isTemporary)
                    m_parent.Rollback(); 
            }

            public void Dispose() 
            {
                if (m_isTemporary)
                    m_parent.Dispose();
            }

            public System.Data.IDbTransaction Parent { get { return m_parent; } }
        }
        
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
        
        public IEnumerable<ILocalFileEntry> GetFiles(long filesetid)
        {
            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(@"SELECT ""A"".""Path"", ""B"".""Length"", ""B"".""FullHash"", ""D"".""FullHash"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""Blockset"" D WHERE ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""C"".""BlocksetID"" = ""D"".""ID"" AND ""A"".""OperationID"" = ? ", filesetid))
            while(rd.Read())
            	yield return new LocalFileEntry(rd);
        }

		private IEnumerable<KeyValuePair<string, string>> GetDbOptionList()
		{
            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(@"SELECT ""Key"", ""Value"" FROM ""Configuration"" "))
            while(rd.Read())
            	yield return new KeyValuePair<string, string>(rd.GetValue(0).ToString(), rd.GetValue(1).ToString());
		}
		
		public IDictionary<string, string> GetDbOptions()
		{
			return GetDbOptionList().ToDictionary(x => x.Key, x => x.Value);	
		}
		
		public void SetDbOptions(IDictionary<string, string> options, System.Data.IDbTransaction transaction = null)
		{
			using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = tr.Parent;
				cmd.ExecuteNonQuery(@"DELETE FROM ""Configuration"" ");
				foreach(var kp in options)
					cmd.ExecuteNonQuery(@"INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES (?, ?) ", kp.Key, kp.Value);
				
				tr.Commit();
			}
		}

		public long GetBlocksLargerThan(long fhblocksize)
		{
            using(var cmd = m_connection.CreateCommand())
            	return Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Block"" WHERE ""Size"" > ?", fhblocksize));
		}

        public virtual void Dispose()
        {
        }
    }
}
