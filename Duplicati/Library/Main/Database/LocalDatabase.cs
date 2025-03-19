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
using System.Data;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using Duplicati.Library.Utility;
using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;


// Expose internal classes to UnitTests, so that Database classes can be tested
[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Main.Database
{
    internal class LocalDatabase : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalDatabase));

        protected readonly IDbConnection m_connection;
        protected readonly long m_operationid = -1;
        private bool m_hasExecutedVacuum;

        private readonly IDbCommand m_updateremotevolumeCommand;
        private readonly IDbCommand m_selectremotevolumesCommand;
        private readonly IDbCommand m_selectremotevolumeCommand;
        private readonly IDbCommand m_removeremotevolumeCommand;
        private readonly IDbCommand m_removedeletedremotevolumeCommand;
        private readonly IDbCommand m_selectremotevolumeIdCommand;
        private readonly IDbCommand m_createremotevolumeCommand;
        private readonly IDbCommand m_selectduplicateRemoteVolumesCommand;

        private readonly IDbCommand m_insertlogCommand;
        private readonly IDbCommand m_insertremotelogCommand;
        private readonly IDbCommand m_insertIndexBlockLink;

        private readonly IDbCommand m_findpathprefixCommand;
        private readonly IDbCommand m_insertpathprefixCommand;

        protected BasicResults m_result;

        public const long FOLDER_BLOCKSET_ID = -100;
        public const long SYMLINK_BLOCKSET_ID = -200;

        public DateTime OperationTimestamp { get; private set; }

        internal IDbConnection Connection { get { return m_connection; } }

        public bool IsDisposed { get; private set; }

        public bool ShouldCloseConnection { get; set; }

        protected static IDbConnection CreateConnection(string path)
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path));

            var c = SQLiteHelper.SQLiteLoader.LoadConnection(path);

            try
            {
                SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(c, path, typeof(LocalDatabase));
            }
            catch
            {
                //Don't leak database connections when something goes wrong
                c.Dispose();
                throw;
            }

            return c;
        }

        /// <summary>
        /// Formats the string using the invariant culture
        /// </summary>
        /// <param name="formattable">The formattable string</param>
        /// <returns>The formatted string</returns>
        public static string FormatInvariant(FormattableString formattable)
            => Library.Utility.Utility.FormatInvariant(formattable);

        public static bool Exists(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        /// <param name="path">The path to the database</param>
        /// <param name="operation">The name of the operation. If null, continues last operation</param>
        public LocalDatabase(string path, string operation, bool shouldclose)
            : this(CreateConnection(path), operation)
        {
            ShouldCloseConnection = shouldclose;
        }

        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        public LocalDatabase(LocalDatabase db)
            : this(db.m_connection)
        {
            OperationTimestamp = db.OperationTimestamp;
            m_connection = db.m_connection;
            m_operationid = db.m_operationid;
            m_result = db.m_result;
        }

        /// <summary>
        /// Creates a new database instance and starts a new operation
        /// </summary>
        /// <param name="operation">The name of the operation. If null, continues last operation</param>
        public LocalDatabase(IDbConnection connection, string operation)
            : this(connection)
        {
            OperationTimestamp = DateTime.UtcNow;
            m_connection = connection;

            if (m_connection.State != ConnectionState.Open)
                m_connection.Open();

            if (operation != null)
            {
                using (var cmd = m_connection.CreateCommand())
                    m_operationid = cmd.ExecuteScalarInt64(@"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES (?, ?); SELECT last_insert_rowid();", -1, operation, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(OperationTimestamp));
            }
            else
            {
                // Get last operation
                using (var cmd = m_connection.CreateCommand())
                using (var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"" FROM ""Operation"" ORDER BY ""Timestamp"" DESC LIMIT 1"))
                {
                    if (!rd.Read())
                    {
                        throw new Exception("LocalDatabase does not contain a previous operation.");
                    }
                    m_operationid = rd.GetInt64(0);
                    OperationTimestamp = ParseFromEpochSeconds(rd.GetInt64(1));
                }
            }
        }

        private LocalDatabase(IDbConnection connection)
        {
            m_insertlogCommand = connection.CreateCommand(@"INSERT INTO ""LogData"" (""OperationID"", ""Timestamp"", ""Type"", ""Message"", ""Exception"") VALUES (?, ?, ?, ?, ?)");
            m_insertremotelogCommand = connection.CreateCommand(@"INSERT INTO ""RemoteOperation"" (""OperationID"", ""Timestamp"", ""Operation"", ""Path"", ""Data"") VALUES (?, ?, ?, ?, ?)");
            m_updateremotevolumeCommand = connection.CreateCommand(@"UPDATE ""Remotevolume"" SET ""OperationID"" = ?, ""State"" = ?, ""Hash"" = ?, ""Size"" = ? WHERE ""Name"" = ?");
            m_selectremotevolumesCommand = connection.CreateCommand(@"SELECT ""ID"", ""Name"", ""Type"", ""Size"", ""Hash"", ""State"", ""DeleteGraceTime"", ""ArchiveTime"" FROM ""Remotevolume""");
            m_selectremotevolumeCommand = connection.CreateCommand(m_selectremotevolumesCommand.CommandText + @" WHERE ""Name"" = ?");
            m_selectduplicateRemoteVolumesCommand = connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""Name"", ""State"" FROM ""Remotevolume"" WHERE ""Name"" IN (SELECT ""Name"" FROM ""Remotevolume"" WHERE ""State"" IN ('{RemoteVolumeState.Deleted.ToString()}', '{RemoteVolumeState.Deleting.ToString()}')) AND NOT ""State"" IN ('{RemoteVolumeState.Deleted.ToString()}', '{RemoteVolumeState.Deleting.ToString()}')"));
            m_removeremotevolumeCommand = connection.CreateCommand(@"DELETE FROM ""Remotevolume"" WHERE ""Name"" = ? AND (""DeleteGraceTime"" < ? OR ""State"" != ?)");
            m_removedeletedremotevolumeCommand = connection.CreateCommand(FormatInvariant($@"DELETE FROM ""Remotevolume"" WHERE ""State"" == '{RemoteVolumeState.Deleted.ToString()}' AND (""DeleteGraceTime"" < ? OR LENGTH(""DeleteGraceTime"") > 12) ")); // >12 is to handle removal of old records that were in ticks
            m_selectremotevolumeIdCommand = connection.CreateCommand(@"SELECT ""ID"" FROM ""Remotevolume"" WHERE ""Name"" = ?");
            m_createremotevolumeCommand = connection.CreateCommand(@"INSERT INTO ""Remotevolume"" (""OperationID"", ""Name"", ""Type"", ""State"", ""Size"", ""VerificationCount"", ""DeleteGraceTime"") VALUES (?, ?, ?, ?, ?, ?, ?); SELECT last_insert_rowid();");
            m_insertIndexBlockLink = connection.CreateCommand(@"INSERT INTO ""IndexBlockLink"" (""IndexVolumeID"", ""BlockVolumeID"") VALUES (?, ?)");
            m_findpathprefixCommand = connection.CreateCommand(@"SELECT ""ID"" FROM ""PathPrefix"" WHERE ""Prefix"" = ?");
            m_insertpathprefixCommand = connection.CreateCommand(@"INSERT INTO ""PathPrefix"" (""Prefix"") VALUES (?); SELECT last_insert_rowid(); ");
        }

        internal void SetResult(BasicResults result)
        {
            m_result = result;
        }

        /// <summary>
        /// Creates a DateTime instance by adding the specified number of seconds to the EPOCH value
        /// </summary>        
        public static DateTime ParseFromEpochSeconds(long seconds)
        {
            return Library.Utility.Utility.EPOCH.AddSeconds(seconds);
        }

        public void UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash, IDbTransaction transaction = null)
        {
            UpdateRemoteVolume(name, state, size, hash, false, transaction);
        }

        public void UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup, IDbTransaction transaction = null)
        {
            UpdateRemoteVolume(name, state, size, hash, suppressCleanup, new TimeSpan(0), null, transaction);
        }

        public void UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived, IDbTransaction transaction = null)
        {
            m_updateremotevolumeCommand.Transaction = transaction;
            m_updateremotevolumeCommand.SetParameterValue(0, m_operationid);
            m_updateremotevolumeCommand.SetParameterValue(1, state.ToString());
            m_updateremotevolumeCommand.SetParameterValue(2, hash);
            m_updateremotevolumeCommand.SetParameterValue(3, size);
            m_updateremotevolumeCommand.SetParameterValue(4, name);

            var c = m_updateremotevolumeCommand.ExecuteNonQuery();

            if (c != 1)
            {
                throw new Exception($"Unexpected number of remote volumes detected: {c}!");
            }

            if (deleteGraceTime.Ticks > 0)
            {
                using (var cmd = m_connection.CreateCommand(transaction))
                {
                    if ((c = cmd.ExecuteNonQuery(
                            @"UPDATE ""RemoteVolume"" SET ""DeleteGraceTime"" = ? WHERE ""Name"" = ? ",
                            Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow + deleteGraceTime),
                            name)) != 1)
                    {
                        throw new Exception($"Unexpected number of updates when recording remote volume grace-time updates: {c}!");
                    }
                }
            }

            if (setArchived.HasValue)
            {
                using (var cmd = m_connection.CreateCommand(transaction))
                {
                    if ((c = cmd.ExecuteNonQuery(
                            @"UPDATE ""RemoteVolume"" SET ""ArchiveTime"" = ? WHERE ""Name"" = ? ",
                            setArchived.Value ? Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow) : null,
                            name)) != 1)
                    {
                        throw new Exception($"Unexpected number of updates when recording remote volume archive-time updates: {c}!");
                    }
                }
            }

            if (!suppressCleanup && state == RemoteVolumeState.Deleted)
            {
                RemoveRemoteVolume(name, transaction);
            }
        }

        public IEnumerable<KeyValuePair<long, DateTime>> FilesetTimes
        {
            get
            {
                using (var cmd = m_connection.CreateCommand())
                using (var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC"))
                    while (rd.Read())
                        yield return new KeyValuePair<long, DateTime>(rd.GetInt64(0), ParseFromEpochSeconds(rd.GetInt64(1)).ToLocalTime());
            }
        }

        public Tuple<string, object[]> GetFilelistWhereClause(DateTime time, long[] versions, IEnumerable<KeyValuePair<long, DateTime>> filesetslist = null, bool singleTimeMatch = false)
        {
            var filesets = (filesetslist ?? FilesetTimes).ToArray();
            var query = new StringBuilder();
            var args = new List<object>();
            if (time.Ticks > 0 || (versions != null && versions.Length > 0))
            {
                var hasTime = false;
                if (time.Ticks > 0)
                {
                    if (time.Kind == DateTimeKind.Unspecified)
                        throw new Exception("Invalid DateTime given, must be either local or UTC");

                    query.Append(singleTimeMatch ? @" ""Timestamp"" = ?" : @" ""Timestamp"" <= ?");
                    // Make sure the resolution is the same (i.e. no milliseconds)
                    args.Add(Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(time));
                    hasTime = true;
                }

                if (versions != null && versions.Length > 0)
                {
                    var qs = new StringBuilder();
                    foreach (var v in versions)
                    {
                        if (v >= 0 && v < filesets.Length)
                        {
                            args.Add(filesets[v].Key);
                            qs.Append("?,");
                        }
                        else
                            Logging.Log.WriteWarningMessage(LOGTAG, "SkipInvalidVersion", null, "Skipping invalid version: {0}", v);
                    }

                    if (qs.Length > 0)
                    {
                        if (hasTime)
                            query.Append(" OR ");

                        query.Append(@" ""ID"" IN (" + qs.ToString(0, qs.Length - 1) + ")");
                    }
                }

                if (query.Length > 0)
                {
                    query.Insert(0, " WHERE ");
                }
            }

            return new Tuple<string, object[]>(query.ToString(), args.ToArray());
        }

        public long GetRemoteVolumeID(string file, IDbTransaction transaction = null)
        {
            m_selectremotevolumeIdCommand.Transaction = transaction;
            return m_selectremotevolumeIdCommand.ExecuteScalarInt64(null, -1, file);
        }

        public IEnumerable<KeyValuePair<string, long>> GetRemoteVolumeIDs(IEnumerable<string> files, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                cmd.CommandText = @"SELECT ""Name"", ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN (?)";
                cmd.AddParameters(1);
                cmd.SetParameterValue(0, files);

                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        yield return new KeyValuePair<string, long>(rd.GetString(0), rd.GetInt64(1));
            }
        }

        public RemoteVolumeEntry GetRemoteVolume(string file, IDbTransaction transaction = null)
        {
            m_selectremotevolumeCommand.Transaction = transaction;
            m_selectremotevolumeCommand.SetParameterValue(0, file);
            using (var rd = m_selectremotevolumeCommand.ExecuteReader())
                if (rd.Read())
                    return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0),
                        rd.GetValue(1).ToString(),
                        (rd.GetValue(4) == null || rd.GetValue(4) == DBNull.Value) ? null : rd.GetValue(4).ToString(),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(2).ToString()),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(5).ToString()),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0))
                    );

            return RemoteVolumeEntry.Empty;
        }

        public IEnumerable<KeyValuePair<string, RemoteVolumeState>> DuplicateRemoteVolumes()
        {
            foreach (var rd in m_selectduplicateRemoteVolumesCommand.ExecuteReaderEnumerable(null))
            {
                yield return new KeyValuePair<string, RemoteVolumeState>(
                    rd.GetValue(0).ToString(),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(1).ToString())
                );
            }
        }

        public IEnumerable<RemoteVolumeEntry> GetRemoteVolumes(IDbTransaction transaction = null)
        {
            m_selectremotevolumesCommand.Transaction = transaction;
            using (var rd = m_selectremotevolumesCommand.ExecuteReader())
            {
                while (rd.Read())
                {
                    yield return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0),
                        rd.GetValue(1).ToString(),
                        (rd.GetValue(4) == null || rd.GetValue(4) == DBNull.Value) ? null : rd.GetValue(4).ToString(),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(2).ToString()),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(5).ToString()),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0))
                    );
                }
            }
        }

        /// <summary>
        /// Log an operation performed on the remote backend
        /// </summary>
        /// <param name="operation">The operation performed</param>
        /// <param name="path">The path involved</param>
        /// <param name="data">Any data relating to the operation</param>
        public void LogRemoteOperation(string operation, string path, string data, IDbTransaction transaction)
        {
            m_insertremotelogCommand.Transaction = transaction;
            m_insertremotelogCommand.SetParameterValue(0, m_operationid);
            m_insertremotelogCommand.SetParameterValue(1, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            m_insertremotelogCommand.SetParameterValue(2, operation);
            m_insertremotelogCommand.SetParameterValue(3, path);
            m_insertremotelogCommand.SetParameterValue(4, data);
            m_insertremotelogCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        /// <param name="type">The message type</param>
        /// <param name="message">The message</param>
        /// <param name="exception">An optional exception</param>
        public void LogMessage(string type, string message, Exception exception, IDbTransaction transaction)
        {
            m_insertlogCommand.Transaction = transaction;
            m_insertlogCommand.SetParameterValue(0, m_operationid);
            m_insertlogCommand.SetParameterValue(1, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            m_insertlogCommand.SetParameterValue(2, type);
            m_insertlogCommand.SetParameterValue(3, message);
            m_insertlogCommand.SetParameterValue(4, exception == null ? null : exception.ToString());
            m_insertlogCommand.ExecuteNonQuery();
        }

        public void UnlinkRemoteVolume(string name, RemoteVolumeState state, IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                var c = cmd.ExecuteNonQuery(@"DELETE FROM ""RemoteVolume"" WHERE ""Name"" = ? AND ""State"" = ? ", name, state.ToString());
                if (c != 1)
                    throw new Exception($"Unexpected number of remote volumes deleted: {c}, expected {1}");

                tr.Commit();
            }
        }

        public void RemoveRemoteVolume(string name, IDbTransaction transaction = null)
        {
            RemoveRemoteVolumes([name], transaction);
        }

        public void RemoveRemoteVolumes(IEnumerable<string> names, IDbTransaction transaction = null)
        {
            if (names == null || !names.Any()) return;

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var deletecmd = m_connection.CreateCommand(tr.Parent))
            {
                string temptransguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var volidstable = "DelVolSetIds-" + temptransguid;
                var blocksetidstable = "DelBlockSetIds-" + temptransguid;

                // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
                deletecmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMP TABLE ""{volidstable}"" (""ID"" INTEGER PRIMARY KEY)"));
                deletecmd.CommandText = FormatInvariant($@"INSERT OR IGNORE INTO ""{volidstable}"" (""ID"") VALUES (?)");
                deletecmd.Parameters.Clear();
                deletecmd.AddParameters(1);
                foreach (var name in names)
                {
                    var volumeid = GetRemoteVolumeID(name, tr.Parent);
                    deletecmd.SetParameterValue(0, volumeid);
                    deletecmd.ExecuteNonQuery();
                }
                var volIdsSubQuery = FormatInvariant($@"SELECT ""ID"" FROM ""{volidstable}"" ");
                deletecmd.Parameters.Clear();


                var bsIdsSubQuery = FormatInvariant(@$"
SELECT DISTINCT ""BlocksetEntry"".""BlocksetID"" FROM ""BlocksetEntry"", ""Block""
WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Block"".""VolumeID"" IN ({volIdsSubQuery}) 
UNION ALL 
SELECT DISTINCT ""BlocksetID"" FROM ""BlocklistHash""
WHERE ""Hash"" IN (SELECT ""Hash"" FROM ""Block"" WHERE ""VolumeID"" IN ({volIdsSubQuery}))");

                // Create a temporary table to cache subquery result, as it might take long (SQLite does not cache at all). 
                deletecmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMP TABLE ""{blocksetidstable}"" (""ID"" INTEGER PRIMARY KEY)"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"INSERT OR IGNORE INTO ""{blocksetidstable}"" (""ID"") {bsIdsSubQuery}"));
                bsIdsSubQuery = FormatInvariant($@"SELECT DISTINCT ""ID"" FROM ""{blocksetidstable}"" ");
                deletecmd.Parameters.Clear();

                // Create a temp table to associate metadata that is being deleted to a fileset
                var metadataFilesetQuery = FormatInvariant($@"SELECT Metadataset.ID, FilesetEntry.FilesetID
FROM Metadataset
INNER JOIN FileLookup ON FileLookup.MetadataID = Metadataset.ID
INNER JOIN FilesetEntry ON FilesetEntry.FileID = FileLookup.ID
WHERE Metadataset.BlocksetID IN ({bsIdsSubQuery})
OR Metadataset.ID IN (SELECT MetadataID FROM FileLookup WHERE BlocksetID IN ({bsIdsSubQuery}))");

                var metadataFilesetTable = @"DelMetadataFilesetIds-" + temptransguid;
                deletecmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMP TABLE ""{metadataFilesetTable}"" (MetadataID INTEGER PRIMARY KEY, FilesetID INTEGER)"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"INSERT OR IGNORE INTO ""{metadataFilesetTable}"" (MetadataID, FilesetID) {metadataFilesetQuery}"));

                // Delete FilesetEntry rows that had their metadata deleted
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM FilesetEntry
WHERE FilesetEntry.FilesetID IN (SELECT DISTINCT FilesetID FROM ""{metadataFilesetTable}"")
AND FilesetEntry.FileID IN (
	SELECT FilesetEntry.FileID
	FROM FilesetEntry
	INNER JOIN FileLookup ON FileLookup.ID = FilesetEntry.FileID
	WHERE FileLookup.MetadataID IN (SELECT MetadataID FROM ""{metadataFilesetTable}""))"));

                // Delete FilesetEntry rows that had their blocks deleted
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM FilesetEntry WHERE FilesetEntry.FileID IN (
SELECT ID FROM FileLookup
WHERE FileLookup.BlocksetID IN ({bsIdsSubQuery}))"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM FileLookup WHERE FileLookup.MetadataID IN (SELECT MetadataID FROM ""{metadataFilesetTable}"")"));

                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""Metadataset"" WHERE ""BlocksetID"" IN ({bsIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""FileLookup"" WHERE ""BlocksetID"" IN ({bsIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""Blockset"" WHERE ""ID"" IN ({bsIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN ({bsIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""BlocklistHash"" WHERE ""BlocklistHash"".""BlocksetID"" IN ({bsIdsSubQuery})"));

                // If the volume is a block or index volume, this will update the crosslink table, otherwise nothing will happen
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""IndexBlockLink"" WHERE ""BlockVolumeID"" IN ({volIdsSubQuery}) OR ""IndexVolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""Block"" WHERE ""VolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""DeletedBlock"" WHERE ""VolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""ChangeJournalData"" WHERE ""FilesetID"" IN (SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ({volIdsSubQuery}))"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM FilesetEntry WHERE FilesetID IN (SELECT ID FROM Fileset WHERE VolumeID IN ({volIdsSubQuery}))"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM Fileset WHERE VolumeID IN ({volIdsSubQuery})"));

                // Delete from Fileset if FilesetEntry rows were deleted by related metadata and there are no references in FilesetEntry anymore
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM Fileset WHERE Fileset.ID IN
(SELECT DISTINCT FilesetID FROM ""{metadataFilesetTable}"")
AND Fileset.ID NOT IN
    (SELECT DISTINCT FilesetID FROM FilesetEntry)"));

                // Clean up temp tables for subqueries. We truncate content and then try to delete.
                // Drop in try-block, as it fails in nested transactions (SQLite problem)
                // SQLite.SQLiteException (0x80004005): database table is locked
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{blocksetidstable}"" "));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{volidstable}"" "));
                try
                {
                    deletecmd.CommandTimeout = 2;
                    deletecmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{blocksetidstable}"" "));
                    deletecmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{volidstable}"" "));
                    deletecmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{metadataFilesetTable}"" "));
                }
                catch { /* Ignore, will be deleted on close anyway. */ }

                m_removeremotevolumeCommand.Transaction = tr.Parent;
                m_removeremotevolumeCommand.SetParameterValue(1, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
                m_removeremotevolumeCommand.SetParameterValue(2, RemoteVolumeState.Deleted.ToString());
                foreach (var name in names)
                {
                    m_removeremotevolumeCommand.SetParameterValue(0, name);
                    m_removeremotevolumeCommand.ExecuteNonQuery();
                }

                tr.Commit();
            }
        }

        public void Vacuum()
        {
            m_hasExecutedVacuum = true;
            using (var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery("VACUUM");
        }

        public long RegisterRemoteVolume(string name, RemoteVolumeType type, long size, RemoteVolumeState state)
        {
            return RegisterRemoteVolume(name, type, state, size, new TimeSpan(0), null);
        }

        public long RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, IDbTransaction transaction)
        {
            return RegisterRemoteVolume(name, type, state, new TimeSpan(0), transaction);
        }

        public long RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, TimeSpan deleteGraceTime, IDbTransaction transaction)
        {
            return RegisterRemoteVolume(name, type, state, -1, deleteGraceTime, transaction);
        }

        public long RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, long size, TimeSpan deleteGraceTime, IDbTransaction transaction)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_createremotevolumeCommand.SetParameterValue(0, m_operationid);
                m_createremotevolumeCommand.SetParameterValue(1, name);
                m_createremotevolumeCommand.SetParameterValue(2, type.ToString());
                m_createremotevolumeCommand.SetParameterValue(3, state.ToString());
                m_createremotevolumeCommand.SetParameterValue(4, size);
                m_createremotevolumeCommand.SetParameterValue(5, 0);

                if (deleteGraceTime.Ticks <= 0)
                    m_createremotevolumeCommand.SetParameterValue(6, 0);
                else
                    m_createremotevolumeCommand.SetParameterValue(6, (DateTime.UtcNow + deleteGraceTime).Ticks);

                m_createremotevolumeCommand.Transaction = tr.Parent;
                var r = m_createremotevolumeCommand.ExecuteScalarInt64();
                tr.Commit();
                return r;
            }
        }

        public IEnumerable<long> GetFilesetIDs(DateTime restoretime, long[] versions)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            var tmp = GetFilelistWhereClause(restoretime, versions);
            string query = tmp.Item1;
            var args = tmp.Item2;

            var res = new List<long>();
            using (var cmd = m_connection.CreateCommand())
            {
                using (var rd = cmd.ExecuteReader(@"SELECT ""ID"" FROM ""Fileset"" " + query + @" ORDER BY ""Timestamp"" DESC", args))
                    while (rd.Read())
                        res.Add(rd.GetInt64(0));

                if (res.Count == 0)
                {
                    cmd.Parameters.Clear();
                    using (var rd = cmd.ExecuteReader(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC "))
                        while (rd.Read())
                            res.Add(rd.ConvertValueToInt64(0));

                    if (res.Count == 0)
                        throw new Duplicati.Library.Interface.UserInformationException("No backup at the specified date", "NoBackupAtDate");
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "RestoreTimeNoMatch", null, "Restore time or version did not match any existing backups, selecting newest backup");
                }

                return res;
            }
        }

        public IEnumerable<long> FindMatchingFilesets(DateTime restoretime, long[] versions)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            var tmp = GetFilelistWhereClause(restoretime, versions, singleTimeMatch: true);
            string query = tmp.Item1;
            var args = tmp.Item2;

            var res = new List<long>();
            using (var cmd = m_connection.CreateCommand())
            using (var rd = cmd.ExecuteReader(@"SELECT ""ID"" FROM ""Fileset"" " + query + @" ORDER BY ""Timestamp"" DESC", args))
                while (rd.Read())
                    res.Add(rd.GetInt64(0));

            return res;
        }

        public bool IsFilesetFullBackup(DateTime filesetTime)
        {
            using (var cmd = m_connection.CreateCommand())
            using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT ""IsFullBackup"" FROM ""Fileset"" WHERE ""Timestamp"" = {Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(filesetTime)}")))
            {
                if (!rd.Read())
                    return false;
                var isFullBackup = rd.GetInt32(0);
                return isFullBackup == BackupType.FULL_BACKUP;
            }
        }

        // TODO: Remove this
        public IDbTransaction BeginTransaction()
        {
            return m_connection.BeginTransaction();
        }

        protected class TemporaryTransactionWrapper : IDisposable
        {
            private readonly IDbTransaction m_parent;
            private readonly bool m_isTemporary;

            public TemporaryTransactionWrapper(IDbConnection connection, IDbTransaction transaction)
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

            public void Commit()
            {
                if (m_isTemporary)
                    m_parent.Commit();
            }

            public void Dispose()
            {
                if (m_isTemporary)
                    m_parent.Dispose();
            }

            public IDbTransaction Parent { get { return m_parent; } }
        }

        private IEnumerable<KeyValuePair<string, string>> GetDbOptionList(IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT ""Key"", ""Value"" FROM ""Configuration"" "))
                while (rd.Read())
                    yield return new KeyValuePair<string, string>(rd.GetValue(0).ToString(), rd.GetValue(1).ToString());
        }

        public IDictionary<string, string> GetDbOptions(IDbTransaction transaction = null)
        {
            return GetDbOptionList(transaction).ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Updates a database option
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="value">The value to set</param>
        private void UpdateDbOption(string key, bool value)
        {
            var opts = GetDbOptions();

            if (value)
                opts[key] = "true";
            else
                opts.Remove(key);

            SetDbOptions(opts);
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public bool RepairInProgress
        {
            get => GetDbOptions().ContainsKey("repair-in-progress");
            set => UpdateDbOption("repair-in-progress", value);
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public bool PartiallyRecreated
        {
            get => GetDbOptions().ContainsKey("partially-recreated");
            set => UpdateDbOption("partially-recreated", value);
        }

        /// <summary>
        /// Flag indicating if the database can contain partial uploads
        /// </summary>
        public bool TerminatedWithActiveUploads
        {
            get => GetDbOptions().ContainsKey("terminated-with-active-uploads");
            set => UpdateDbOption("terminated-with-active-uploads", value);
        }

        /// <summary>
        /// Sets the database options
        /// </summary>
        /// <param name="options">The options to set</param>
        /// <param name="transaction">An optional transaction</param>
        public void SetDbOptions(IDictionary<string, string> options, IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                cmd.ExecuteNonQuery(@"DELETE FROM ""Configuration"" ");
                foreach (var kp in options)
                    cmd.ExecuteNonQuery(@"INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES (?, ?) ", kp.Key, kp.Value);

                tr.Commit();
            }
        }

        public long GetBlocksLargerThan(long fhblocksize)
        {
            using (var cmd = m_connection.CreateCommand())
                return cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Block"" WHERE ""Size"" > ?", -1, fhblocksize);
        }

        public void VerifyConsistency(long blocksize, long hashsize, bool verifyfilelists, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                // Calculate the lengths for each blockset                
                var combinedLengths = @"
SELECT 
    ""A"".""ID"" AS ""BlocksetID"", 
    IFNULL(""B"".""CalcLen"", 0) AS ""CalcLen"", 
    ""A"".""Length""
FROM
    ""Blockset"" A
LEFT OUTER JOIN
    (
        SELECT 
            ""BlocksetEntry"".""BlocksetID"",
            SUM(""Block"".""Size"") AS ""CalcLen""
        FROM
            ""BlocksetEntry""
        LEFT OUTER JOIN
            ""Block""
        ON
            ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
        GROUP BY ""BlocksetEntry"".""BlocksetID""
    ) B
ON
    ""A"".""ID"" = ""B"".""BlocksetID""

";
                // For each blockset with wrong lengths, fetch the file path
                var reportDetails = @"SELECT ""CalcLen"", ""Length"", ""A"".""BlocksetID"", ""File"".""Path"" FROM (" + combinedLengths + @") A, ""File"" WHERE ""A"".""BlocksetID"" = ""File"".""BlocksetID"" AND ""A"".""CalcLen"" != ""A"".""Length"" ";

                using (var rd = cmd.ExecuteReader(reportDetails))
                    if (rd.Read())
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Found inconsistency in the following files while validating database: ");
                        var c = 0;
                        do
                        {
                            if (c < 5)
                                sb.AppendFormat("{0}, actual size {1}, dbsize {2}, blocksetid: {3}{4}", rd.GetValue(3), rd.GetValue(1), rd.GetValue(0), rd.GetValue(2), Environment.NewLine);
                            c++;
                        } while (rd.Read());

                        c -= 5;
                        if (c > 0)
                            sb.AppendFormat("... and {0} more", c);

                        sb.Append(". Run repair to fix it.");
                        throw new DatabaseInconsistencyException(sb.ToString());
                    }

                var real_count = cmd.ExecuteScalarInt64(@"SELECT Count(*) FROM ""BlocklistHash""", 0);
                var unique_count = cmd.ExecuteScalarInt64(@"SELECT Count(*) FROM (SELECT DISTINCT ""BlocksetID"", ""Index"" FROM ""BlocklistHash"")", 0);

                if (real_count != unique_count)
                    throw new DatabaseInconsistencyException($"Found {real_count} blocklist hashes, but there should be {unique_count}. Run repair to fix it.");

                var itemswithnoblocklisthash = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM (SELECT * FROM (SELECT ""N"".""BlocksetID"", ((""N"".""BlockCount"" + {blocksize / hashsize} - 1) / {blocksize / hashsize}) AS ""BlocklistHashCountExpected"", CASE WHEN ""G"".""BlocklistHashCount"" IS NULL THEN 0 ELSE ""G"".""BlocklistHashCount"" END AS ""BlocklistHashCountActual"" FROM (SELECT ""BlocksetID"", COUNT(*) AS ""BlockCount"" FROM ""BlocksetEntry"" GROUP BY ""BlocksetID"") ""N"" LEFT OUTER JOIN (SELECT ""BlocksetID"", COUNT(*) AS ""BlocklistHashCount"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"") ""G"" ON ""N"".""BlocksetID"" = ""G"".""BlocksetID"" WHERE ""N"".""BlockCount"" > 1) WHERE ""BlocklistHashCountExpected"" != ""BlocklistHashCountActual"")"), 0);
                if (itemswithnoblocklisthash != 0)
                    throw new DatabaseInconsistencyException($"Found {itemswithnoblocklisthash} file(s) with missing blocklist hashes");

                if (cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Blockset"" WHERE ""Length"" > 0 AND ""ID"" NOT IN (SELECT ""BlocksetId"" FROM ""BlocksetEntry"")") != 0)
                    throw new DatabaseInconsistencyException("Detected non-empty blocksets with no associated blocks!");

                if (cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""FileLookup"" WHERE ""BlocksetID"" != ? AND ""BlocksetID"" != ? AND NOT ""BlocksetID"" IN (SELECT ""ID"" FROM ""Blockset"")", 0, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID) != 0)
                    throw new DatabaseInconsistencyException("Detected files associated with non-existing blocksets!");

                var filesetsMissingVolumes = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Fileset"" WHERE ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" != ?)", 0, RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleted.ToString());
                if (filesetsMissingVolumes != 0)
                {
                    if (filesetsMissingVolumes == 1)
                        using (var reader = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"", ""VolumeID"" FROM ""Fileset"" WHERE ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" != ?)", 0, RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleted.ToString()))
                            if (reader.Read())
                                throw new DatabaseInconsistencyException($"Detected 1 fileset with missing volume: FilesetId = {reader.ConvertValueToInt64(0)}, Time = ({ParseFromEpochSeconds(reader.ConvertValueToInt64(1))}), unmatched VolumeID {reader.ConvertValueToInt64(2)}");

                    throw new DatabaseInconsistencyException($"Detected {filesetsMissingVolumes} filesets with missing volumes");
                }

                var volumesMissingFilests = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" != ? AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"")", 0, RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleted.ToString());
                if (volumesMissingFilests != 0)
                {
                    if (volumesMissingFilests == 1)
                        using (var reader = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""State"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" != ? AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"")", 0, RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleted.ToString()))
                            if (reader.Read())
                                throw new DatabaseInconsistencyException($"Detected 1 volume with missing filesets: VolumeId = {reader.ConvertValueToInt64(0)}, Name = {reader.ConvertValueToString(1)}, State = {reader.ConvertValueToString(2)}");

                    throw new DatabaseInconsistencyException($"Detected {volumesMissingFilests} volumes with missing filesets");
                }

                var nonAttachedFiles = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FileID"" NOT IN (SELECT ""ID"" FROM ""FileLookup"")");
                if (nonAttachedFiles != 0)
                    throw new DatabaseInconsistencyException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry");

                if (verifyfilelists)
                {
                    var anyError = new List<string>();
                    using (var cmd2 = m_connection.CreateCommand(transaction))
                    {
                        foreach (var filesetid in cmd.ExecuteReaderEnumerable(@"SELECT ""ID"" FROM ""Fileset"" ").Select(x => x.ConvertValueToInt64(0, -1)))
                        {
                            var expandedCmd = FormatInvariant($@"SELECT COUNT(*) FROM (SELECT DISTINCT ""Path"" FROM ({LocalDatabase.LIST_FILESETS}) UNION SELECT DISTINCT ""Path"" FROM ({LocalDatabase.LIST_FOLDERS_AND_SYMLINKS}))");
                            var expandedlist = cmd2.ExecuteScalarInt64(expandedCmd, 0, filesetid, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, filesetid);
                            //var storedfilelist = cmd2.ExecuteScalarInt64(FormatInvariant(@"SELECT COUNT(*) FROM ""FilesetEntry"", ""FileLookup"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FileLookup"".""BlocksetID"" != ? AND ""FileLookup"".""BlocksetID"" != ?"), 0, filesetid, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);
                            var storedlist = cmd2.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetEntry"".""FilesetID"" = ?", 0, filesetid);

                            if (expandedlist != storedlist)
                            {
                                var filesetname = filesetid.ToString();
                                var fileset = FilesetTimes.Zip(Enumerable.Range(0, FilesetTimes.Count()), (a, b) => new Tuple<long, long, DateTime>(b, a.Key, a.Value)).FirstOrDefault(x => x.Item2 == filesetid);
                                if (fileset != null)
                                    filesetname = $"version {fileset.Item1}: {fileset.Item3} (database id: {fileset.Item2})";
                                anyError.Add($"Unexpected difference in fileset {filesetname}, found {expandedlist} entries, but expected {storedlist}");
                            }
                        }
                    }
                    if (anyError.Any())
                    {
                        throw new DatabaseInconsistencyException(string.Join("\n\r", anyError), "FilesetDifferences");
                    }
                }
            }
        }

        public interface IBlock
        {
            string Hash { get; }
            long Size { get; }
        }

        internal class Block : IBlock
        {
            public string Hash { get; private set; }
            public long Size { get; private set; }

            public Block(string hash, long size)
            {
                Hash = hash;
                Size = size;
            }
        }

        public IEnumerable<IBlock> GetBlocks(long volumeid, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Hash"", ""Size"" FROM ""Block"" WHERE ""VolumeID"" = ?", volumeid))
                while (rd.Read())
                    yield return new Block(rd.GetValue(0).ToString(), rd.GetInt64(1));
        }

        private class BlocklistHashEnumerable : IEnumerable<string>
        {
            private class BlocklistHashEnumerator : IEnumerator<string>
            {
                private readonly IDataReader m_reader;
                private readonly BlocklistHashEnumerable m_parent;
                private string m_path = null;
                private bool m_first = true;
                private string m_current = null;

                public BlocklistHashEnumerator(BlocklistHashEnumerable parent, IDataReader reader)
                {
                    m_reader = reader;
                    m_parent = parent;
                }

                public string Current { get { return m_current; } }

                public void Dispose()
                {
                }

                object System.Collections.IEnumerator.Current { get { return Current; } }

                public bool MoveNext()
                {
                    m_first = false;

                    if (m_path == null)
                    {
                        m_path = m_reader.GetValue(0).ToString();
                        m_current = m_reader.GetValue(6).ToString();
                        return true;
                    }
                    else
                    {
                        if (m_current == null)
                            return false;

                        if (!m_reader.Read())
                        {
                            m_current = null;
                            m_parent.MoreData = false;
                            return false;
                        }

                        var np = m_reader.GetValue(0).ToString();
                        if (m_path != np)
                        {
                            m_current = null;
                            return false;
                        }

                        m_current = m_reader.GetValue(6).ToString();
                        return true;
                    }
                }

                public void Reset()
                {
                    if (!m_first)
                        throw new Exception("Iterator reset not supported");

                    m_first = false;
                }
            }

            private readonly IDataReader m_reader;

            public BlocklistHashEnumerable(IDataReader reader)
            {
                m_reader = reader;
                MoreData = true;
            }

            public bool MoreData { get; protected set; }

            public IEnumerator<string> GetEnumerator()
            {
                return new BlocklistHashEnumerator(this, m_reader);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public const string LIST_FILESETS = @"
SELECT
    ""L"".""Path"", 
    ""L"".""Lastmodified"", 
    ""L"".""Filelength"", 
    ""L"".""Filehash"", 
    ""L"".""Metahash"", 
    ""L"".""Metalength"",
    ""L"".""BlocklistHash"", 
    ""L"".""FirstBlockHash"",
    ""L"".""FirstBlockSize"",
    ""L"".""FirstMetaBlockHash"",
    ""L"".""FirstMetaBlockSize"",
    ""M"".""Hash"" AS ""MetaBlocklistHash""
FROM
    (
    SELECT 
        ""J"".""Path"", 
        ""J"".""Lastmodified"", 
        ""J"".""Filelength"", 
        ""J"".""Filehash"", 
        ""J"".""Metahash"", 
        ""J"".""Metalength"",
        ""K"".""Hash"" AS ""BlocklistHash"", 
        ""J"".""FirstBlockHash"",
        ""J"".""FirstBlockSize"",
        ""J"".""FirstMetaBlockHash"",
        ""J"".""FirstMetaBlockSize"",
        ""J"".""MetablocksetID""
    FROM 
        (
        SELECT 
	        ""A"".""Path"" AS ""Path"", 
	        ""D"".""Lastmodified"" AS ""Lastmodified"", 
	        ""B"".""Length"" AS ""Filelength"", 
	        ""B"".""FullHash"" AS ""Filehash"", 
	        ""E"".""FullHash"" AS ""Metahash"", 
	        ""E"".""Length"" AS ""Metalength"",
	        ""A"".""BlocksetID"" AS ""BlocksetID"",
	        ""F"".""Hash"" AS ""FirstBlockHash"",
	        ""F"".""Size"" AS ""FirstBlockSize"",
	        ""H"".""Hash"" AS ""FirstMetaBlockHash"",
	        ""H"".""Size"" AS ""FirstMetaBlockSize"",
	        ""C"".""BlocksetID"" AS ""MetablocksetID""
        FROM 
	        ""File"" A	
        LEFT JOIN ""Blockset"" B
          ON ""A"".""BlocksetID"" = ""B"".""ID"" 
        LEFT JOIN ""Metadataset"" C  
          ON ""A"".""MetadataID"" = ""C"".""ID""
        LEFT JOIN ""FilesetEntry"" D
          ON ""A"".""ID"" = ""D"".""FileID""
        LEFT JOIN ""Blockset"" E
          ON ""E"".""ID"" = ""C"".""BlocksetID""
        LEFT JOIN ""BlocksetEntry"" G
          ON ""B"".""ID"" = ""G"".""BlocksetID""
        LEFT JOIN ""Block"" F 
          ON ""G"".""BlockID"" = ""F"".""ID""  
        LEFT JOIN ""BlocksetEntry"" I
          ON ""E"".""ID"" = ""I"".""BlocksetID""
        LEFT JOIN ""Block"" H 
          ON ""I"".""BlockID"" = ""H"".""ID""
        WHERE 
          ""A"".""BlocksetId"" >= 0 AND
          ""D"".""FilesetID"" = ? AND
          (""I"".""Index"" = 0 OR ""I"".""Index"" IS NULL) AND  
          (""G"".""Index"" = 0 OR ""G"".""Index"" IS NULL)
        ) J
    LEFT OUTER JOIN 
        ""BlocklistHash"" K 
    ON 
        ""K"".""BlocksetID"" = ""J"".""BlocksetID"" 
    ORDER BY ""J"".""Path"", ""K"".""Index""
    ) L

LEFT OUTER JOIN
    ""BlocklistHash"" M
ON
    ""M"".""BlocksetID"" = ""L"".""MetablocksetID""
";

        public const string LIST_FOLDERS_AND_SYMLINKS = @"
SELECT
    ""G"".""BlocksetID"",
    ""G"".""ID"",
    ""G"".""Path"",
    ""G"".""Length"",
    ""G"".""FullHash"",
    ""G"".""Lastmodified"",
    ""G"".""FirstMetaBlockHash"",
    ""H"".""Hash"" AS ""MetablocklistHash""
FROM
    (
    SELECT
        ""B"".""BlocksetID"",
        ""B"".""ID"",
        ""B"".""Path"",
        ""D"".""Length"",
        ""D"".""FullHash"",
        ""A"".""Lastmodified"",
        ""F"".""Hash"" AS ""FirstMetaBlockHash"",
        ""C"".""BlocksetID"" AS ""MetaBlocksetID""
    FROM
        ""FilesetEntry"" A, 
        ""File"" B, 
        ""Metadataset"" C, 
        ""Blockset"" D,
        ""BlocksetEntry"" E,
        ""Block"" F
    WHERE 
        ""A"".""FileID"" = ""B"".""ID"" 
        AND ""B"".""MetadataID"" = ""C"".""ID"" 
        AND ""C"".""BlocksetID"" = ""D"".""ID"" 
        AND ""E"".""BlocksetID"" = ""C"".""BlocksetID""
        AND ""E"".""BlockID"" = ""F"".""ID""
        AND ""E"".""Index"" = 0
        AND (""B"".""BlocksetID"" = ? OR ""B"".""BlocksetID"" = ?) 
        AND ""A"".""FilesetID"" = ?
    ) G
LEFT OUTER JOIN
   ""BlocklistHash"" H
ON
   ""H"".""BlocksetID"" = ""G"".""MetaBlocksetID""
ORDER BY
   ""G"".""Path"", ""H"".""Index""

";

        public void WriteFileset(Volumes.FilesetVolumeWriter filesetvolume, long filesetId, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                cmd.CommandText = LIST_FOLDERS_AND_SYMLINKS;
                cmd.AddParameter(FOLDER_BLOCKSET_ID);
                cmd.AddParameter(SYMLINK_BLOCKSET_ID);
                cmd.AddParameter(filesetId);

                string lastpath = null;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        var blocksetID = rd.ConvertValueToInt64(0, -1);
                        var path = rd.GetValue(2).ToString();
                        var metalength = rd.ConvertValueToInt64(3, -1);
                        var metahash = rd.GetValue(4).ToString();
                        var metablockhash = rd.GetValue(6).ToString();
                        var metablocklisthash = rd.GetValue(7).ToString();

                        if (path == lastpath)
                            Logging.Log.WriteWarningMessage(LOGTAG, "DuplicatePathFound", null, "Duplicate path detected: {0}", path);

                        lastpath = path;

                        if (blocksetID == FOLDER_BLOCKSET_ID)
                            filesetvolume.AddDirectory(path, metahash, metalength, metablockhash, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                        else if (blocksetID == SYMLINK_BLOCKSET_ID)
                            filesetvolume.AddSymlink(path, metahash, metalength, metablockhash, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                    }

                // TODO: Perhaps run the above query after recreate and compare count(*) with count(*) from filesetentry where id = x

                cmd.CommandText = LIST_FILESETS;
                cmd.Parameters.Clear();
                cmd.AddParameter(filesetId);

                using (var rd = cmd.ExecuteReader())
                    if (rd.Read())
                    {
                        var more = false;
                        do
                        {
                            var path = rd.GetValue(0).ToString();
                            var filehash = rd.GetValue(3).ToString();
                            var size = rd.ConvertValueToInt64(2);
                            var lastmodified = new DateTime(rd.ConvertValueToInt64(1, 0), DateTimeKind.Utc);
                            var metahash = rd.GetValue(4).ToString();
                            var metasize = rd.ConvertValueToInt64(5, -1);
                            var p = rd.GetValue(6);
                            var blrd = (p == null || p == DBNull.Value) ? null : new BlocklistHashEnumerable(rd);
                            var blockhash = rd.GetValue(7).ToString();
                            var blocksize = rd.ConvertValueToInt64(8, -1);
                            var metablockhash = rd.GetValue(9).ToString();
                            //var metablocksize = rd.ConvertValueToInt64(10, -1);
                            var metablocklisthash = rd.GetValue(11).ToString();

                            if (blockhash == filehash)
                                blockhash = null;

                            if (metablockhash == metahash)
                                metablockhash = null;

                            filesetvolume.AddFile(path, filehash, size, lastmodified, metahash, metasize, metablockhash, blockhash, blocksize, blrd, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                            if (blrd == null)
                                more = rd.Read();
                            else
                                more = blrd.MoreData;

                        } while (more);
                    }
            }
        }

        public void LinkFilesetToVolume(long filesetid, long volumeid, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var c = cmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""VolumeID"" = ? WHERE ""ID"" = ?", volumeid, filesetid);
                if (c != 1)
                    throw new Exception($"Failed to link filesetid {filesetid} to volumeid {volumeid}");
            }
        }

        public void PushTimestampChangesToPreviousVersion(long filesetId, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var query = @"
UPDATE FilesetEntry AS oldVersion
SET Lastmodified = tempVersion.Lastmodified
FROM FilesetEntry AS tempVersion
WHERE oldVersion.FileID = tempVersion.FileID
AND tempVersion.FilesetID = ?
AND oldVersion.FilesetID = (SELECT ID FROM Fileset WHERE ID != ? ORDER BY Timestamp DESC LIMIT 1)";

                cmd.ExecuteNonQuery(query, filesetId, filesetId);
            }
        }

        /// <summary>
        /// Keeps a list of filenames in a temporary table with a single column Path
        ///</summary>
        public class FilteredFilenameTable : IDisposable
        {
            public string Tablename { get; private set; }
            private readonly IDbConnection m_connection;

            public FilteredFilenameTable(IDbConnection connection, IFilter filter, IDbTransaction transaction)
            {
                m_connection = connection;
                Tablename = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var type = FilterType.Regexp;
                if (filter is FilterExpression expression)
                    type = expression.Type;

                // Bugfix: SQLite does not handle case-insensitive LIKE with non-ascii characters
                if (type != FilterType.Regexp && !Library.Utility.Utility.IsFSCaseSensitive && filter.ToString().Any(x => x > 127))
                    type = FilterType.Regexp;

                if (filter.Empty)
                {
                    using (var cmd = m_connection.CreateCommand(transaction))
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{Tablename}"" AS SELECT DISTINCT ""Path"" FROM ""File"" "));
                        return;
                    }
                }

                if (type == FilterType.Regexp || type == FilterType.Group)
                {
                    using (var cmd = m_connection.CreateCommand(transaction))
                    {
                        // TODO: Optimize this to not rely on the "File" view, and not instantiate the paths in full
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{Tablename}"" (""Path"" TEXT NOT NULL)"));
                        using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                        {
                            cmd.CommandText = FormatInvariant($@"INSERT INTO ""{Tablename}"" (""Path"") VALUES (?)");
                            cmd.AddParameter();
                            cmd.Transaction = tr.Parent;
                            using (var c2 = m_connection.CreateCommand())
                            using (var rd = c2.ExecuteReader(@"SELECT DISTINCT ""Path"" FROM ""File"" "))
                                while (rd.Read())
                                {
                                    var p = rd.GetValue(0).ToString();
                                    if (FilterExpression.Matches(filter, p))
                                    {
                                        cmd.SetParameterValue(0, p);
                                        cmd.ExecuteNonQuery();
                                    }
                                }


                            tr.Commit();
                        }
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    var args = new List<object>();
                    foreach (var f in ((FilterExpression)filter).GetSimpleList())
                    {
                        if (type == FilterType.Wildcard)
                        {
                            sb.Append(@"""Path"" LIKE ? OR ");
                            args.Add(f.Replace('*', '%').Replace('?', '_'));
                        }
                        else
                        {
                            sb.Append(@"""Path"" = ? OR ");
                            args.Add(f);
                        }
                    }

                    sb.Length = sb.Length - " OR ".Length;

                    using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                    using (var cmd = m_connection.CreateCommand(tr.Parent))
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{Tablename}"" (""Path"" TEXT NOT NULL)"));
                        cmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""{Tablename}"" SELECT DISTINCT ""Path"" FROM ""File"" WHERE {sb}"), args.ToArray());
                        tr.Commit();
                    }
                }
            }

            public void Dispose()
            {
                if (Tablename != null)
                    try
                    {
                        using (var cmd = m_connection.CreateCommand())
                            cmd.ExecuteNonQuery(FormatInvariant(@$"DROP TABLE IF EXISTS ""{Tablename}"" "));
                    }
                    catch { }
                    finally { Tablename = null; }
            }
        }

        public void RenameRemoteFile(string oldname, string newname, IDbTransaction transaction)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                //Rename the old entry, to preserve ID links
                var c = cmd.ExecuteNonQuery(@"UPDATE ""Remotevolume"" SET ""Name"" = ? WHERE ""Name"" = ?", newname, oldname);
                if (c != 1)
                    throw new Exception($"Unexpected result from renaming \"{oldname}\" to \"{newname}\", expected {1} got {c}");

                // Grab the type of entry
                var type = (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), cmd.ExecuteScalar(@"SELECT ""Type"" FROM ""Remotevolume"" WHERE ""Name"" = ?", newname).ToString(), true);

                //Create a fake new entry with the old name and mark as deleting
                // as this ensures we will remove it, if it shows up in some later listing
                RegisterRemoteVolume(oldname, type, RemoteVolumeState.Deleting, tr.Parent);

                tr.Commit();
            }
        }

        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update</param>
        /// <param name="timestamp">The timestamp of the operation to create</param>
        /// <param name="transaction">An optional external transaction</param>
        public virtual long CreateFileset(long volumeid, DateTime timestamp, IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                var id = cmd.ExecuteScalarInt64(@"INSERT INTO ""Fileset"" (""OperationID"", ""Timestamp"", ""VolumeID"", ""IsFullBackup"") VALUES (?, ?, ?, ?); SELECT last_insert_rowid();", -1, m_operationid, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp), volumeid, BackupType.PARTIAL_BACKUP);
                tr.Commit();
                return id;
            }
        }

        public void AddIndexBlockLink(long indexVolumeID, long blockVolumeID, IDbTransaction transaction)
        {
            m_insertIndexBlockLink.Transaction = transaction;
            m_insertIndexBlockLink.SetParameterValue(0, indexVolumeID);
            m_insertIndexBlockLink.SetParameterValue(1, blockVolumeID);
            m_insertIndexBlockLink.ExecuteNonQuery();
        }

        /// <summary>
        /// Returns all unique blocklists for a given volume
        /// </summary>
        /// <param name="volumeid">The volume ID to get blocklists for</param>
        /// <param name="blocksize">The blocksize</param>
        /// <param name="hashsize">The size of the hash</param>
        /// <param name="transaction">An optional external transaction</param>
        /// <returns>An enumerable of tuples containing the blocklist hash, the blocklist data and the length of the data</returns>
        public IEnumerable<Tuple<string, byte[], int>> GetBlocklists(long volumeid, long blocksize, int hashsize, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                // Group subquery by hash to ensure that each blocklist hash appears only once in the result
                var sql = FormatInvariant($@"SELECT ""A"".""Hash"", ""C"".""Hash"" FROM 
(SELECT ""BlocklistHash"".""BlocksetID"", ""Block"".""Hash"", ""BlocklistHash"".""Index"" FROM  ""BlocklistHash"",""Block"" WHERE  ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""Block"".""VolumeID"" = ? GROUP BY ""Block"".""Hash"", ""Block"".""Size"") A,
 ""BlocksetEntry"" B, ""Block"" C WHERE ""B"".""BlocksetID"" = ""A"".""BlocksetID"" AND 
 ""B"".""Index"" >= (""A"".""Index"" * {blocksize / hashsize}) AND ""B"".""Index"" < ((""A"".""Index"" + 1) * {blocksize / hashsize}) AND ""C"".""ID"" = ""B"".""BlockID"" 
 ORDER BY ""A"".""BlocksetID"", ""B"".""Index""");

                string curHash = null;
                int count = 0;
                byte[] buffer = new byte[blocksize];

                using (var rd = cmd.ExecuteReader(sql, volumeid))
                    while (rd.Read())
                    {
                        var blockhash = rd.GetValue(0).ToString();
                        if ((blockhash != curHash && curHash != null) || count + hashsize > buffer.Length)
                        {
                            yield return new Tuple<string, byte[], int>(curHash, buffer, count);
                            buffer = new byte[blocksize];
                            count = 0;
                        }

                        var hash = Convert.FromBase64String(rd.GetValue(1).ToString());
                        Array.Copy(hash, 0, buffer, count, hashsize);
                        curHash = blockhash;
                        count += hashsize;
                    }

                if (curHash != null)
                    yield return new Tuple<string, byte[], int>(curHash, buffer, count);
            }
        }

        /// <summary>
        /// Update fileset with full backup state
        /// </summary>
        /// <param name="fileSetId">Existing file set to update</param>
        /// <param name="isFullBackup">Full backup state</param>
        /// <param name="transaction">An optional external transaction</param>
        public void UpdateFullBackupStateInFileset(long fileSetId, bool isFullBackup, IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                cmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""IsFullBackup"" = ? WHERE ""ID"" = ?;", isFullBackup, fileSetId);
                tr.Commit();
            }
        }

        /// <summary>
        /// Gets the last previous fileset that was incomplete
        /// </summary>
        /// <param name="transaction">The transaction to use</param>
        /// <returns>The last incomplete fileset or default</returns>
        public RemoteVolumeEntry GetLastIncompleteFilesetVolume(IDbTransaction transaction)
        {
            var candidates = GetIncompleteFilesets(transaction).OrderBy(x => x.Value).ToArray();
            if (candidates.Any())
                return GetRemoteVolumeFromFilesetID(candidates.Last().Key, transaction);

            return default;
        }

        /// <summary>
        /// Gets a list of incomplete filesets
        /// </summary>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>A list of fileset IDs and timestamps</returns>
        public IEnumerable<KeyValuePair<long, DateTime>> GetIncompleteFilesets(IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(FormatInvariant(@$"SELECT DISTINCT ""Fileset"".""ID"", ""Fileset"".""Timestamp"" FROM ""Fileset"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" IN (SELECT ""FilesetID"" FROM ""FilesetEntry"")  AND (""RemoteVolume"".""State"" = '{RemoteVolumeState.Uploading}' OR ""RemoteVolume"".""State"" = '{RemoteVolumeState.Temporary}')")))
                while (rd.Read())
                {
                    yield return new KeyValuePair<long, DateTime>(
                        rd.GetInt64(0),
                        ParseFromEpochSeconds(rd.GetInt64(1)).ToLocalTime()
                    );
                }
        }

        /// <summary>
        /// Gets the remote volume entry from the fileset ID
        /// </summary>
        /// <param name="filesetID">The fileset ID</param>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>The remote volume entry or default</returns>
        public RemoteVolumeEntry GetRemoteVolumeFromFilesetID(long filesetID, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT ""RemoteVolume"".""ID"", ""Name"", ""Type"", ""Size"", ""Hash"", ""State"", ""DeleteGraceTime"", ""ArchiveTime"" FROM ""RemoteVolume"", ""Fileset"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Fileset"".""ID"" = ?", filesetID))
                if (rd.Read())
                    return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0, -1),
                        rd.GetValue(1).ToString(),
                        (rd.GetValue(4) == null || rd.GetValue(4) == DBNull.Value) ? null : rd.GetValue(4).ToString(),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(2).ToString()),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(5).ToString()),
                        new DateTime(rd.ConvertValueToInt64(6, 0), DateTimeKind.Utc),
                        new DateTime(rd.ConvertValueToInt64(7, 0), DateTimeKind.Utc)
                    );
                else
                    return default(RemoteVolumeEntry);
        }

        public void PurgeLogData(DateTime threshold)
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                var t = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold);
                cmd.ExecuteNonQuery(@"DELETE FROM ""LogData"" WHERE ""Timestamp"" < ?", t);
                cmd.ExecuteNonQuery(@"DELETE FROM ""RemoteOperation"" WHERE ""Timestamp"" < ?", t);

                tr.Commit();
            }
        }

        public void PurgeDeletedVolumes(DateTime threshold)
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                m_removedeletedremotevolumeCommand.Transaction = tr;
                m_removedeletedremotevolumeCommand.SetParameterValue(0, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold));
                m_removedeletedremotevolumeCommand.ExecuteNonQuery();
                tr.Commit();
            }
        }

        public virtual void Dispose()
        {
            if (IsDisposed)
                return;

            DisposeAllFields<IDbCommand>(this, false);

            if (ShouldCloseConnection && m_connection != null)
            {
                if (m_connection.State == ConnectionState.Open && !m_hasExecutedVacuum)
                {
                    using (var transaction = m_connection.BeginTransaction())
                    using (var command = m_connection.CreateCommand(transaction))
                    {
                        // SQLite recommends that PRAGMA optimize is run just before closing each database connection.
                        command.ExecuteNonQuery("PRAGMA optimize");

                        try
                        {
                            transaction.Commit();
                        }
                        catch (SQLite.SQLiteException ex)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToCommitTransaction", ex, "Failed to commit transaction after pragma optimize, usually caused by the a no-op transaction");
                        }
                    }

                    m_connection.Close();
                }

                m_connection.Dispose();
            }

            IsDisposed = true;
        }

        /// <summary>
        /// Disposes all fields of a certain type, in the instance and its bases
        /// </summary>
        /// <typeparam name="T">The type of fields to find</typeparam>
        /// <param name="item">The item to dispose</param>
        /// <param name="throwExceptions"><c>True</c> if an aggregate exception should be thrown, or <c>false</c> if exceptions are silently captured</param>
        public static void DisposeAllFields<T>(object item, bool throwExceptions)
            where T : IDisposable
        {
            var typechain = new List<Type>();
            var cur = item.GetType();
            var exceptions = new List<Exception>();

            while (cur != null && cur != typeof(object))
            {
                typechain.Add(cur);
                cur = cur.BaseType;
            }

            var fields =
                typechain.SelectMany(x =>
                    x.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)
                ).Distinct().Where(x => x.FieldType.IsAssignableFrom(typeof(T)));

            foreach (var p in fields)
                try
                {
                    var val = p.GetValue(item);
                    if (val != null)
                        ((T)val).Dispose();
                }
                catch (Exception ex)
                {
                    if (throwExceptions)
                        exceptions.Add(ex);
                }

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);
        }

        public void WriteResults()
        {
            if (IsDisposed)
                return;

            if (m_connection != null && m_result != null)
            {
                m_result.FlushLog();
                if (m_result.EndTime.Ticks == 0)
                    m_result.EndTime = DateTime.UtcNow;

                var serializer = new JsonFormatSerializer();
                LogMessage("Result",
                    serializer.SerializeResults(m_result),
                    null,
                    null
                );
            }
        }

        /// <summary>
        /// The current index into the path prefix buffer
        /// </summary>
        private int m_pathPrefixIndex = 0;
        /// <summary>
        /// The path prefix lookup list
        /// </summary>
        private readonly KeyValuePair<string, long>[] m_pathPrefixLookup = new KeyValuePair<string, long>[5];

        /// <summary>
        /// Gets the path prefix ID, optionally creating it in the process.
        /// </summary>
        /// <returns>The path prefix ID.</returns>
        /// <param name="prefix">The path to get the prefix for.</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public long GetOrCreatePathPrefix(string prefix, IDbTransaction transaction)
        {
            // Ring-buffer style lookup
            for (var i = 0; i < m_pathPrefixLookup.Length; i++)
            {
                var ix = (i + m_pathPrefixIndex) % m_pathPrefixLookup.Length;
                if (string.Equals(m_pathPrefixLookup[ix].Key, prefix, StringComparison.Ordinal))
                    return m_pathPrefixLookup[ix].Value;
            }

            m_findpathprefixCommand.Transaction = transaction;
            m_findpathprefixCommand.SetParameterValue(0, prefix);
            var id = m_findpathprefixCommand.ExecuteScalarInt64();
            if (id < 0)
            {
                m_insertpathprefixCommand.Transaction = transaction;
                m_insertpathprefixCommand.SetParameterValue(0, prefix);
                id = m_insertpathprefixCommand.ExecuteScalarInt64();
            }

            m_pathPrefixIndex = (m_pathPrefixIndex + 1) % m_pathPrefixLookup.Length;
            m_pathPrefixLookup[m_pathPrefixIndex] = new KeyValuePair<string, long>(prefix, id);

            return id;
        }

        /// <summary>
        /// The path separators on this system
        /// </summary>
        private static readonly char[] _pathseparators = new char[] {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        };

        /// <summary>
        /// Helper method that splits a path on the last path separator
        /// </summary>
        /// <returns>The prefix and name.</returns>
        /// <param name="path">The path to split.</param>
        public static KeyValuePair<string, string> SplitIntoPrefixAndName(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException($"Invalid path: {path}", nameof(path));

            int nLast = path.TrimEnd(_pathseparators).LastIndexOfAny(_pathseparators);
            if (nLast >= 0)
                return new KeyValuePair<string, string>(path.Substring(0, nLast + 1), path.Substring(nLast + 1));

            return new KeyValuePair<string, string>(string.Empty, path);
        }
    }

    /// <summary>
    /// Defines the backups types
    /// </summary>
    public static class BackupType
    {
        public const int PARTIAL_BACKUP = 0;
        public const int FULL_BACKUP = 1;
    }
}
