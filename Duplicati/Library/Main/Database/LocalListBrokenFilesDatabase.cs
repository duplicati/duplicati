//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListBrokenFilesDatabase : LocalDatabase
    {
        private const string BROKEN_FILE_IDS = @"
SELECT DISTINCT ""ID"" FROM (
  SELECT ""ID"" AS ""ID"", ""BlocksetID"" AS ""BlocksetID"" FROM ""File"" WHERE ""BlocksetID"" != {0} AND ""BlocksetID"" != {1}
UNION
  SELECT ""A"".""ID"" AS ""ID"", ""B"".""BlocksetID"" AS ""BlocksetID"" FROM ""File"" A LEFT JOIN ""Metadataset"" B ON ""A"".""MetadataID"" = ""B"".""ID""
)
WHERE ""BlocksetID"" IS NULL OR ""BlocksetID"" IN 
  (
    SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" NOT IN (SELECT ""ID"" FROM ""Block"")
  )
";
        private const string BROKEN_FILE_SETS = @"SELECT DISTINCT ""B"".""Timestamp"", ""A"".""FilesetID"", COUNT(""A"".""FileID"") AS ""FileCount"" FROM ""FilesetEntry"" A, ""Fileset"" B WHERE ""A"".""FilesetID"" = ""B"".""ID"" AND ""A"".""FileID"" IN (" + BROKEN_FILE_IDS + @")";

        private const string BROKEN_FILE_NAMES = @"
SELECT ""A"".""Path"", ""B"".""Length"" FROM ""File"" A, ""Blockset"" B WHERE ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""ID"" IN ("
+ BROKEN_FILE_IDS +
@") AND ""A"".""ID"" IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)";

        private const string INSERT_BROKEN_IDS = @"INSERT INTO ""{2}"" (""{3}"") " + BROKEN_FILE_IDS 
            + @" AND ""ID"" IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)";

        public LocalListBrokenFilesDatabase(string path)
            : base(path, "ListBrokenFiles", false)
        {
            ShouldCloseConnection = true;
        }

        public LocalListBrokenFilesDatabase(LocalDatabase parent)
            : base(parent)
        {
            ShouldCloseConnection = false;
        }

        public IEnumerable<Tuple<DateTime, long, long>> GetBrokenFilesets(DateTime time, long[] versions, System.Data.IDbTransaction transaction)
        {
            var query = string.Format(BROKEN_FILE_SETS, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);
            var clause = GetFilelistWhereClause(time, versions);
            if (!string.IsNullOrWhiteSpace(clause.Item1))
                query += @" AND ""A"".""FilesetID"" IN (SELECT ""ID"" FROM ""Fileset"" " + clause.Item1 + ")";

            query += @" GROUP BY ""A"".""FilesetID""";

            using (var cmd = Connection.CreateCommand(transaction))
                 foreach (var rd in cmd.ExecuteReaderEnumerable(query, clause.Item2))
                    if (!rd.IsDBNull(0))
                        yield return new Tuple<DateTime, long, long>(ParseFromEpochSeconds(rd.ConvertValueToInt64(0, 0)), rd.ConvertValueToInt64(1, -1), rd.ConvertValueToInt64(2, 0));
        }

        public IEnumerable<Tuple<string, long>> GetBrokenFilenames(long filesetid, System.Data.IDbTransaction transaction)
        {
            using (var cmd = Connection.CreateCommand(transaction))
                foreach (var rd in cmd.ExecuteReaderEnumerable(string.Format(BROKEN_FILE_NAMES, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID), filesetid))
                    if (!rd.IsDBNull(0))
                        yield return new Tuple<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
        }

        public void InsertBrokenFileIDsIntoTable(long filesetid, string tablename, string IDfieldname, System.Data.IDbTransaction transaction)
        {
            using (var cmd = Connection.CreateCommand(transaction))
                cmd.ExecuteNonQuery(string.Format(INSERT_BROKEN_IDS, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, tablename, IDfieldname), filesetid);
        }

        public void RemoveMissingBlocks(IEnumerable<string> names, System.Data.IDbTransaction transaction)
        {
            if (names == null || !names.Any()) return;
            if (transaction == null)
                throw new Exception("This function cannot be called when not in a transaction, as it leaves the database in an inconsistent state");

            using (var deletecmd = m_connection.CreateCommand(transaction))
            {
                string temptransguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var volidstable = "DelVolSetIds-" + temptransguid;

                // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
                deletecmd.ExecuteNonQuery(string.Format(@"CREATE TEMP TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY)", volidstable));
                deletecmd.CommandText = string.Format(@"INSERT OR IGNORE INTO ""{0}"" (""ID"") VALUES (?)", volidstable);
                deletecmd.Parameters.Clear();
                deletecmd.AddParameters(1);
                foreach (var name in names)
                {
                    var volumeid = GetRemoteVolumeID(name, transaction);
                    deletecmd.SetParameterValue(0, volumeid);
                    deletecmd.ExecuteNonQuery();
                }
                var volIdsSubQuery = string.Format(@"SELECT ""ID"" FROM ""{0}"" ", volidstable);
                deletecmd.Parameters.Clear();

                deletecmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""IndexBlockLink"" WHERE ""BlockVolumeID"" IN ({0}) OR ""IndexVolumeID"" IN ({0})", volIdsSubQuery));
                deletecmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""Block"" WHERE ""VolumeID"" IN ({0})", volIdsSubQuery));

                // Clean up temp tables for subqueries. We truncate content and then try to delete.
                // Drop in try-block, as it fails in nested transactions (SQLite problem)
                // System.Data.SQLite.SQLiteException (0x80004005): database table is locked
                deletecmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" ", volidstable));
                try
                {
                    deletecmd.CommandTimeout = 2;
                    deletecmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", volidstable));
                }
                catch { /* Ignore, will be deleted on close anyway. */ }
            }
        }

        public long GetFilesetFileCount(long filesetid, System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
                return cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?", 0, filesetid);
        }
    }
}
