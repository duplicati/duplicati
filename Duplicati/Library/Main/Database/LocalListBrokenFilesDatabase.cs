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
using System.Linq;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListBrokenFilesDatabase : LocalDatabase
    {
        private const string BLOCK_VOLUME_IDS = @"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = '{2}'";

        // Invalid blocksets include those that:
        // - Have BlocksetEntries with unknown/invalid blocks (meaning the data to rebuild the blockset isn't available)
        //   - Invalid blocks include those that appear to be in non-Blocks volumes (e.g., are listed as being in an Index or Files volume) or that appear in an unknown volume (-1)
        // - Have BlocklistHash entries with unknown/invalid blocks (meaning the data which defines the list of hashes that makes up the blockset isn't available)
        // - Are defined in the Blockset table but have no entries in the BlocksetEntries table (this can happen during recreate if Files volumes reference blocksets that are not found in any Index files)
        // However, blocksets with a length of 0 are excluded from this check, as the corresponding blocks for these are not needed.
        private const string BROKEN_FILE_IDS = @"
SELECT DISTINCT ""ID"" FROM (
  SELECT ""ID"" AS ""ID"", ""BlocksetID"" AS ""BlocksetID"" FROM ""FileLookup"" WHERE ""BlocksetID"" != {0} AND ""BlocksetID"" != {1}
UNION
  SELECT ""A"".""ID"" AS ""ID"", ""B"".""BlocksetID"" AS ""BlocksetID"" FROM ""FileLookup"" A LEFT JOIN ""Metadataset"" B ON ""A"".""MetadataID"" = ""B"".""ID""
)
WHERE ""BlocksetID"" IS NULL OR ""BlocksetID"" IN 
  (
    SELECT DISTINCT ""BlocksetID"" FROM
    (
      SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" NOT IN
        (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN
          (" + BLOCK_VOLUME_IDS + @"))
        UNION
      SELECT ""BlocksetID"" FROM ""BlocklistHash"" WHERE ""Hash"" NOT IN
        (SELECT ""Hash"" FROM ""Block"" WHERE ""VolumeID"" IN
          (" + BLOCK_VOLUME_IDS + @"))
        UNION
      SELECT ""A"".""ID"" AS ""BlocksetID"" FROM ""Blockset"" A LEFT JOIN ""BlocksetEntry"" B ON ""A"".""ID"" = ""B"".""BlocksetID"" WHERE ""A"".""Length"" > 0 AND ""B"".""BlocksetID"" IS NULL
    )
    WHERE ""BlocksetID"" NOT IN (SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" == 0)
  )
";
        private const string BROKEN_FILE_SETS = @"SELECT DISTINCT ""B"".""Timestamp"", ""A"".""FilesetID"", COUNT(""A"".""FileID"") AS ""FileCount"" FROM ""FilesetEntry"" A, ""Fileset"" B WHERE ""A"".""FilesetID"" = ""B"".""ID"" AND ""A"".""FileID"" IN (" + BROKEN_FILE_IDS + @")";

        private const string BROKEN_FILE_NAMES = @"
SELECT ""A"".""Path"", ""B"".""Length"" FROM ""File"" A, ""Blockset"" B WHERE ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""ID"" IN ("
+ BROKEN_FILE_IDS +
@") AND ""A"".""ID"" IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)";

        private const string INSERT_BROKEN_IDS = @"INSERT INTO ""{3}"" (""{4}"") " + BROKEN_FILE_IDS
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
            var query = string.Format(BROKEN_FILE_SETS, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, RemoteVolumeType.Blocks.ToString());
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
                foreach (var rd in cmd.ExecuteReaderEnumerable(string.Format(BROKEN_FILE_NAMES, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, RemoteVolumeType.Blocks.ToString()), filesetid))
                    if (!rd.IsDBNull(0))
                        yield return new Tuple<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
        }

        public void InsertBrokenFileIDsIntoTable(long filesetid, string tablename, string IDfieldname, System.Data.IDbTransaction transaction)
        {
            using (var cmd = Connection.CreateCommand(transaction))
                cmd.ExecuteNonQuery(string.Format(INSERT_BROKEN_IDS, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, RemoteVolumeType.Blocks.ToString(), tablename, IDfieldname), filesetid);
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
                deletecmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""DeletedBlock"" WHERE ""VolumeID"" IN ({0})", volIdsSubQuery));
                deletecmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""DuplicateBlock"" WHERE ""VolumeID"" IN ({0})", volIdsSubQuery));

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
