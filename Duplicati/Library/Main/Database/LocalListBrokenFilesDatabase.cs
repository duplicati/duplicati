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

#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListBrokenFilesDatabase : LocalDatabase
    {
        private static readonly string BLOCK_VOLUME_IDS = FormatInvariant($@"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = '{RemoteVolumeType.Blocks.ToString()}'");

        // Invalid blocksets include those that:
        // - Have BlocksetEntries with unknown/invalid blocks (meaning the data to rebuild the blockset isn't available)
        //   - Invalid blocks include those that appear to be in non-Blocks volumes (e.g., are listed as being in an Index or Files volume) or that appear in an unknown volume (-1)
        // - Have BlocklistHash entries with unknown/invalid blocks (meaning the data which defines the list of hashes that makes up the blockset isn't available)
        // - Are defined in the Blockset table but have no entries in the BlocksetEntries table (this can happen during recreate if Files volumes reference blocksets that are not found in any Index files)
        // However, blocksets with a length of 0 are excluded from this check, as the corresponding blocks for these are not needed.
        private static readonly string BROKEN_FILE_IDS = FormatInvariant($@"
SELECT DISTINCT ""ID"" FROM (
  SELECT ""ID"" AS ""ID"", ""BlocksetID"" AS ""BlocksetID"" FROM ""FileLookup"" WHERE ""BlocksetID"" != {FOLDER_BLOCKSET_ID} AND ""BlocksetID"" != {SYMLINK_BLOCKSET_ID}
UNION
  SELECT ""A"".""ID"" AS ""ID"", ""B"".""BlocksetID"" AS ""BlocksetID"" FROM ""FileLookup"" A LEFT JOIN ""Metadataset"" B ON ""A"".""MetadataID"" = ""B"".""ID""
)
WHERE ""BlocksetID"" IS NULL OR ""BlocksetID"" IN
  (
    SELECT DISTINCT ""BlocksetID"" FROM
    (
      SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" NOT IN
        (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN
          ({BLOCK_VOLUME_IDS}))
        UNION
      SELECT ""BlocksetID"" FROM ""BlocklistHash"" WHERE ""Hash"" NOT IN
        (SELECT ""Hash"" FROM ""Block"" WHERE ""VolumeID"" IN
          ({BLOCK_VOLUME_IDS}))
        UNION
      SELECT ""A"".""ID"" AS ""BlocksetID"" FROM ""Blockset"" A LEFT JOIN ""BlocksetEntry"" B ON ""A"".""ID"" = ""B"".""BlocksetID"" WHERE ""A"".""Length"" > 0 AND ""B"".""BlocksetID"" IS NULL
    )
    WHERE ""BlocksetID"" NOT IN (SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" == 0)
  )
");
        private static readonly string BROKEN_FILE_SETS = FormatInvariant($@"SELECT DISTINCT ""B"".""Timestamp"", ""A"".""FilesetID"", COUNT(""A"".""FileID"") AS ""FileCount"" FROM ""FilesetEntry"" A, ""Fileset"" B WHERE ""A"".""FilesetID"" = ""B"".""ID"" AND ""A"".""FileID"" IN ({BROKEN_FILE_IDS})");

        private static readonly string BROKEN_FILE_NAMES = FormatInvariant($@"SELECT ""A"".""Path"", ""B"".""Length"" FROM ""File"" A LEFT JOIN ""Blockset"" B ON (""A"".""BlocksetID"" = ""B"".""ID"") WHERE ""A"".""ID"" IN ({BROKEN_FILE_IDS}) AND ""A"".""ID"" IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId)");

        private static string INSERT_BROKEN_IDS(string tablename, string IDfieldname) => FormatInvariant($@"INSERT INTO ""{tablename}"" (""{IDfieldname}"") {BROKEN_FILE_IDS} AND ""ID"" IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId)");

        public static async Task<LocalListBrokenFilesDatabase> CreateAsync(string path, long pagecachesize)
        {
            var db = new LocalListBrokenFilesDatabase();

            db = (LocalListBrokenFilesDatabase)await CreateLocalDatabaseAsync(db, path, "ListBrokenFiles", false, pagecachesize);
            db.ShouldCloseConnection = true;

            return db;
        }

        public static async Task<LocalListBrokenFilesDatabase> CreateAsync(LocalDatabase dbparent)
        {
            var dbnew = new LocalListBrokenFilesDatabase();

            dbnew = (LocalListBrokenFilesDatabase)await CreateLocalDatabaseAsync(dbparent, dbnew);
            dbnew.ShouldCloseConnection = false;

            return dbnew;
        }

        public IEnumerable<(DateTime FilesetTime, long FilesetID, long RemoveFileCount)> GetBrokenFilesets(DateTime time, long[] versions, IDbTransaction transaction)
        {
            var query = BROKEN_FILE_SETS;
            var clause = GetFilelistWhereClause(time, versions);
            if (!string.IsNullOrWhiteSpace(clause.Item1))
                query += FormatInvariant($@" AND ""A"".""FilesetID"" IN (SELECT ""ID"" FROM ""Fileset"" {clause.Item1})");

            query += @" GROUP BY ""A"".""FilesetID""";

            using (var cmd = Connection.CreateCommand(transaction))
                foreach (var rd in cmd.SetCommandAndParameters(query).SetParameterValues(clause.Values).ExecuteReaderEnumerable())
                    if (!rd.IsDBNull(0))
                        yield return (ParseFromEpochSeconds(rd.ConvertValueToInt64(0, 0)), rd.ConvertValueToInt64(1, -1), rd.ConvertValueToInt64(2, 0));
        }

        public IEnumerable<Tuple<string, long>> GetBrokenFilenames(long filesetid, IDbTransaction transaction)
        {
            using (var cmd = Connection.CreateCommand(transaction, BROKEN_FILE_NAMES).SetParameterValue("@FilesetId", filesetid))
                foreach (var rd in cmd.ExecuteReaderEnumerable())
                    if (!rd.IsDBNull(0))
                        yield return new Tuple<string, long>(rd.ConvertValueToString(0) ?? throw new Exception("Filename was null"), rd.ConvertValueToInt64(1));
        }

        /// <summary>
        /// Returns all index files that are orphaned, i.e., not referenced by any block files.
        /// </summary>
        /// <param name="transaction">Transaction to use for the query.</param>
        /// <returns>>All index files that are orphaned.</returns>
        public IEnumerable<RemoteVolume> GetOrphanedIndexFiles(IDbTransaction transaction)
        {
            using var cmd = Connection.CreateCommand(transaction, FormatInvariant($@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""Type"" = '{RemoteVolumeType.Index.ToString()}' AND ""ID"" NOT IN (SELECT ""IndexVolumeID"" FROM ""IndexBlockLink"")"));

            foreach (var rd in cmd.ExecuteReaderEnumerable())
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0) ?? throw new Exception("Filename was null"),
                    rd.ConvertValueToString(1) ?? throw new Exception("Hash was null"),
                    rd.ConvertValueToInt64(2, -1)
                );
        }

        /// <summary>
        /// Inserts the broken file IDs into the given table. The table must have a single column with the same name as the ID field name.
        /// </summary>
        /// <param name="filesetid">The filset id for the current operation</param>
        /// <param name="tablename">The name of the table to insert into</param>
        /// <param name="IDfieldname">The name of the ID field in the table</param>
        /// <param name="transaction">The transaction to use for the query</param>
        public void InsertBrokenFileIDsIntoTable(long filesetid, string tablename, string IDfieldname, IDbTransaction transaction)
        {
            using var cmd = Connection.CreateCommand(transaction, INSERT_BROKEN_IDS(tablename, IDfieldname))
              .SetParameterValue("@FilesetId", filesetid);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Returns the ID of an empty metadata blockset. If no empty blockset is found, it returns the ID of the smallest blockset that is not in the given block volume IDs.
        /// If no such blockset is found, it returns -1.
        /// </summary>
        /// <param name="blockVolumeIds">The volume ids to ignore when searching for a suitable metadata block</param>
        /// <param name="emptyHash">The hash of the empty blockset</param>
        /// <param name="emptyHashSize">The size of the empty blockset</param>
        /// <param name="transaction">The transaction to use for the query</param>
        /// <returns>The ID of the empty metadata blockset, or -1 if no suitable blockset is found</returns>
        public long GetEmptyMetadataBlocksetId(IEnumerable<long> blockVolumeIds, string emptyHash, long emptyHashSize, IDbTransaction transaction)
        {
            using var cmd = Connection.CreateCommand(transaction, @"SELECT ""ID"" FROM ""Blockset"" WHERE ""FullHash"" = @EmptyHash AND ""Length"" == @EmptyHashSize AND ""ID"" NOT IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"", ""Block"" WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds))")
              .ExpandInClauseParameter("@BlockVolumeIds", blockVolumeIds)
              .SetParameterValue("@EmptyHash", emptyHash)
              .SetParameterValue("@EmptyHashSize", emptyHashSize);

            var res = cmd.ExecuteScalarInt64(-1);

            // No empty block found, try to find a zero-length block instead
            if (res < 0 && emptyHashSize != 0)
                res = cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" == @EmptyHashSize AND ""ID"" NOT IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"", ""Block"" WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds))")
                  .ExpandInClauseParameter("@BlockVolumeIds", blockVolumeIds)
                  .SetParameterValue("@EmptyHashSize", 0)
                  .ExecuteScalarInt64(-1);

            // No empty block found, pick the smallest one
            if (res < 0)
                res = cmd.SetCommandAndParameters(@"SELECT ""Blockset"".""ID"" FROM ""BlocksetEntry"", ""Blockset"", ""Metadataset"", ""Block"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds) ORDER BY ""Blockset"".""Length"" ASC LIMIT 1")
                  .ExpandInClauseParameter("@BlockVolumeIds", blockVolumeIds)
                  .ExecuteScalarInt64(-1);

            return res;
        }

        /// <summary>
        /// Replaces the metadata blockset ID in the Metadataset table with the empty blockset ID for all fileset entries that are not in any block volume.
        /// This is used to clean up the metadata blocksets that are now missing.
        /// </summary>
        /// <param name="filesetId">The filesetId to target</param>
        /// <param name="emptyBlocksetId">The empty blockset ID to replace with</param>
        /// <param name="transaction">The transaction to use for the query</param>
        /// <returns>The number of rows affected</returns>
        public int ReplaceMetadata(long filesetId, long emptyBlocksetId, IDbTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(transaction, @"
UPDATE ""Metadataset""
SET ""BlocksetID"" = @EmptyBlocksetID
WHERE
  ""ID"" IN (
    SELECT ""FileLookup"".""MetadataID""
    FROM ""FileLookup"", ""FilesetEntry""
    WHERE
      ""FilesetEntry"".""FilesetId"" = @FilesetId
      AND ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID""
    )
  AND ""BlocksetID"" NOT IN (
    SELECT ""BlocksetID""
    FROM ""BlocksetEntry"", ""Block""
    WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
  )")
              .SetParameterValue("@EmptyBlocksetId", emptyBlocksetId)
              .SetParameterValue("@FilesetId", filesetId);
            return cmd.ExecuteNonQuery();
        }

        public void RemoveMissingBlocks(IEnumerable<string> names, IDbTransaction transaction)
        {
            if (names == null || !names.Any()) return;
            if (transaction == null)
                throw new Exception("This function cannot be called when not in a transaction, as it leaves the database in an inconsistent state");

            using (var deletecmd = m_connection.CreateCommand(transaction))
            {
                var temptransguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var volidstable = "DelVolSetIds-" + temptransguid;

                // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
                deletecmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMP TABLE ""{volidstable}"" (""ID"" INTEGER PRIMARY KEY)"));

                using (var tmptable = new TemporaryDbValueList(m_connection, transaction, names))
                    deletecmd.SetCommandAndParameters(FormatInvariant($@"INSERT OR IGNORE INTO ""{volidstable}"" (""ID"") SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN (@Names)"))
                      .ExpandInClauseParameter("@Names", tmptable)
                      .ExecuteNonQuery();

                var volIdsSubQuery = FormatInvariant($@"SELECT ""ID"" FROM ""{volidstable}"" ");
                deletecmd.Parameters.Clear();

                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""IndexBlockLink"" WHERE ""BlockVolumeID"" IN ({volIdsSubQuery}) OR ""IndexVolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""Block"" WHERE ""VolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""DeletedBlock"" WHERE ""VolumeID"" IN ({volIdsSubQuery})"));
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""DuplicateBlock"" WHERE ""VolumeID"" IN ({volIdsSubQuery})"));

                // Clean up temp tables for subqueries. We truncate content and then try to delete.
                // Drop in try-block, as it fails in nested transactions (SQLite problem)
                // SQLite.SQLiteException (0x80004005): database table is locked
                deletecmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{volidstable}"" "));
                try
                {
                    deletecmd.CommandTimeout = 2;
                    deletecmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{volidstable}"" "));
                }
                catch { /* Ignore, will be deleted on close anyway. */ }
            }
        }

        public long GetFilesetFileCount(long filesetid, IDbTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(transaction, @"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId")
              .SetParameterValue("@FilesetId", filesetid);
            return cmd.ExecuteScalarInt64(0);
        }
    }
}
