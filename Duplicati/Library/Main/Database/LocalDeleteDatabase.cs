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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    internal class LocalDeleteDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocalDeleteDatabase>();

        /// <summary>
        /// Flag for toggling temporary tables; set to empty string if debugging
        /// </summary>
        private const string TEMPORARY = "TEMPORARY";

        private const string REGISTER_COMMAND = @"INSERT OR IGNORE INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"") SELECT ""ID"", @VolumeId FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size ";

        private SqliteCommand m_registerDuplicateBlockCommand = null!;

        public static async Task<LocalDeleteDatabase> CreateAsync(string path, string operation, long pagecachesize, LocalDeleteDatabase? dbnew = null)
        {
            dbnew ??= new LocalDeleteDatabase();

            dbnew = (LocalDeleteDatabase)
                await CreateLocalDatabaseAsync(path, operation, true, pagecachesize, dbnew)
                    .ConfigureAwait(false);

            dbnew.m_registerDuplicateBlockCommand =
                await dbnew.Connection.CreateCommandAsync(REGISTER_COMMAND)
                    .ConfigureAwait(false);

            return dbnew;
        }

        public static async Task<LocalDeleteDatabase> CreateAsync(LocalDatabase dbparent, LocalDeleteDatabase? dbnew = null)
        {
            dbnew ??= new LocalDeleteDatabase();

            dbnew = (LocalDeleteDatabase)
                await CreateLocalDatabaseAsync(dbparent, dbnew)
                    .ConfigureAwait(false);

            dbnew.m_registerDuplicateBlockCommand =
                await dbnew.Connection.CreateCommandAsync(REGISTER_COMMAND)
                    .ConfigureAwait(false);

            return dbnew;
        }

        /// <summary>
        /// Drops all entries related to operations listed in the table.
        /// </summary>
        /// <param name="toDelete">The fileset entries to delete</param>
        /// <param name="transaction">The transaction to execute the commands in</param>
        /// <returns>A list of filesets to delete</returns>
        public async IAsyncEnumerable<KeyValuePair<string, long>> DropFilesetsFromTable(DateTime[] toDelete)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var deleted = 0;

                using (var tempTable = await TemporaryDbValueList.CreateAsync(this, toDelete.Select(Library.Utility.Utility.NormalizeDateTimeToEpochSeconds)).ConfigureAwait(false))
                    deleted += await (
                        await cmd.SetCommandAndParameters(@"
                            DELETE FROM ""Fileset""
                            WHERE ""Timestamp"" IN (@Timestamps)
                        ")
                        .ExpandInClauseParameterMssqliteAsync("@Timestamps", tempTable)
                        .ConfigureAwait(false)
                    )
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                if (deleted != toDelete.Length)
                    throw new Exception($"Unexpected number of deleted filesets {deleted} vs {toDelete.Length}");

                //Then we delete anything that is no longer being referenced
                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""FilesetEntry""
                    WHERE ""FilesetID"" NOT IN (
                        SELECT DISTINCT ""ID""
                        FROM ""Fileset""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""ChangeJournalData""
                    WHERE ""FilesetID"" NOT IN (
                        SELECT DISTINCT ""ID""
                        FROM ""Fileset""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""FileLookup""
                    WHERE ""ID"" NOT IN (
                        SELECT DISTINCT ""FileID""
                        FROM ""FilesetEntry""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""Metadataset""
                    WHERE ""ID"" NOT IN (
                        SELECT DISTINCT ""MetadataID""
                        FROM ""FileLookup""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""Blockset""
                    WHERE ""ID"" NOT IN (
                        SELECT DISTINCT ""BlocksetID""
                        FROM ""FileLookup""
                        UNION
                            SELECT DISTINCT ""BlocksetID""
                            FROM ""Metadataset""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""BlocksetEntry""
                    WHERE ""BlocksetID"" NOT IN (
                        SELECT DISTINCT ""ID""
                        FROM ""Blockset""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""BlocklistHash""
                    WHERE ""BlocksetID"" NOT IN (
                        SELECT DISTINCT ""ID""
                        FROM ""Blockset""
                    )
                ")
                    .ConfigureAwait(false);

                //We save the block info for the remote files, before we delete it
                await cmd.ExecuteNonQueryAsync(@"
                    INSERT INTO ""DeletedBlock"" (
                        ""Hash"",
                        ""Size"",
                        ""VolumeID""
                    )
                    SELECT
                        ""Hash"",
                        ""Size"",
                        ""VolumeID""
                    FROM ""Block""
                    WHERE ""ID"" NOT IN (
                        SELECT DISTINCT ""BlockID"" AS ""BlockID""
                        FROM ""BlocksetEntry""
                        UNION
                            SELECT DISTINCT ""ID""
                            FROM
                                ""Block"",
                                ""BlocklistHash""
                            WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash""
                    )
                ")
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync(@"
                    DELETE FROM ""Block""
                    WHERE ""ID"" NOT IN (
                        SELECT DISTINCT ""BlockID""
                        FROM ""BlocksetEntry""
                        UNION
                            SELECT DISTINCT ""ID""
                            FROM
                                ""Block"",
                                ""BlocklistHash""
                            WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash""
                    )
                ")
                    .ConfigureAwait(false);

                //Find all remote filesets that are no longer required, and mark them as deleting
                var updated = await cmd.SetCommandAndParameters(@"
                    UPDATE ""RemoteVolume""
                    SET ""State"" = @NewState
                    WHERE
                        ""Type"" = @CurrentType
                        AND ""State"" IN (@AllowedStates)
                        AND ""ID"" NOT IN (
                            SELECT ""VolumeID""
                            FROM ""Fileset""
                        )
                ")
                    .SetParameterValue("@NewState", RemoteVolumeState.Deleting.ToString())
                    .SetParameterValue("@CurrentType", RemoteVolumeType.Files.ToString())
                    .ExpandInClauseParameterMssqlite("@AllowedStates", [
                        RemoteVolumeState.Uploaded.ToString(),
                        RemoteVolumeState.Verified.ToString(),
                        RemoteVolumeState.Temporary.ToString(),
                        RemoteVolumeState.Deleting.ToString()
                    ])
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);

                if (deleted != updated)
                    throw new Exception($"Unexpected number of remote volumes marked as deleted. Found {deleted} filesets, but {updated} volumes");

                cmd.SetCommandAndParameters(@"
                    SELECT
                        ""Name"",
                        ""Size""
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = @Type
                        AND ""State"" = @State
                ")
                    .SetParameterValue("@Type", RemoteVolumeType.Files.ToString())
                    .SetParameterValue("@State", RemoteVolumeState.Deleting.ToString());

                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                        yield return new KeyValuePair<string, long>(
                            rd.ConvertValueToString(0) ?? "",
                            rd.ConvertValueToInt64(1)
                        );
            }
        }

        /// <summary>
        /// Returns a collection of IListResultFilesets, where the Version is the backup version number
        /// exposed to the user. This is in contrast to other cases where the Version is the ID in the
        /// Fileset table.
        /// </summary>
        internal async IAsyncEnumerable<IListResultFileset> FilesetsWithBackupVersion()
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                // TODO check if this is still the case? (shouldn't be with new sqlite driver):
                // We can also use the ROW_NUMBER() window function to generate the backup versions,
                // but this requires at least SQLite 3.25, which is not available in some common
                // distributions (e.g., Debian) currently.
                cmd.SetCommandAndParameters(@"
                    SELECT
                        ""IsFullBackup"",
                        ""Timestamp""
                    FROM ""Fileset""
                    ORDER BY ""Timestamp"" DESC
                ");

                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    int version = 0;
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        yield return new ListResultFileset(
                            version++,
                            reader.GetInt32(0),
                            ParseFromEpochSeconds(reader.ConvertValueToInt64(1)).ToLocalTime(),
                            -1L,
                            -1L
                        );
                    }
                }
            }
        }

        private struct VolumeUsage
        {
            public readonly string Name;
            public readonly long DataSize;
            public readonly long WastedSize;
            public readonly long CompressedSize;

            public VolumeUsage(string name, long datasize, long wastedsize, long compressedsize)
            {
                Name = name;
                DataSize = datasize;
                WastedSize = wastedsize;
                CompressedSize = compressedsize;
            }
        }

        /// <summary>
        /// Returns the number of bytes stored in each volume,
        /// and the number of bytes no longer needed in each volume.
        /// The sizes are the uncompressed values.
        /// </summary>
        /// <returns>A list of tuples with name, datasize, wastedbytes.</returns>
        private async IAsyncEnumerable<VolumeUsage> GetWastedSpaceReport()
        {
            var tmptablename = "UsageReport-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            var usedBlocks = @"
                SELECT
                    SUM(""Block"".""Size"") AS ""ActiveSize"",
                    ""Block"".""VolumeID"" AS ""VolumeID"" FROM ""Block"",
                    ""Remotevolume""
                WHERE
                    ""Block"".""VolumeID"" = ""Remotevolume"".""ID""
                    AND ""Block"".""ID"" NOT IN (
                        SELECT ""Block"".""ID""
                        FROM
                            ""Block"",
                            ""DeletedBlock""
                        WHERE
                            ""Block"".""Hash"" = ""DeletedBlock"".""Hash""
                            AND ""Block"".""Size"" = ""DeletedBlock"".""Size""
                            AND ""Block"".""VolumeID"" = ""DeletedBlock"".""VolumeID""
                    )
                    GROUP BY ""Block"".""VolumeID""
            ";

            var lastmodifiedFile = @"
                SELECT
                    ""Block"".""VolumeID"" AS ""VolumeID"",
                    ""Fileset"".""Timestamp"" AS ""Sorttime""
                FROM
                    ""Fileset"",
                    ""FilesetEntry"",
                    ""FileLookup"",
                    ""BlocksetEntry"",
                    ""Block""
                WHERE
                    ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                    AND ""FileLookup"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                    AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
            ";

            var lastmodifiedMetadata = @"
                SELECT
                    ""Block"".""VolumeID"" AS ""VolumeID"",
                    ""Fileset"".""Timestamp"" AS ""Sorttime""
                FROM
                    ""Fileset"",
                    ""FilesetEntry"",
                    ""FileLookup"",
                    ""BlocksetEntry"",
                    ""Block"",
                    ""Metadataset""
                WHERE
                    ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                    AND ""FileLookup"".""MetadataID"" = ""Metadataset"".""ID""
                    AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                    AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
            ";

            var scantime = @$"
                SELECT
                    ""VolumeID"" AS ""VolumeID"",
                    MIN(""Sorttime"") AS ""Sorttime""
                FROM (
                    {lastmodifiedFile}
                    UNION {lastmodifiedMetadata}
                )
                GROUP BY ""VolumeID""
            ";

            var active = @$"
                SELECT
                    ""A"".""ActiveSize"" AS ""ActiveSize"",
                    0 AS ""InactiveSize"",
                    ""A"".""VolumeID"" AS ""VolumeID"",
                    CASE
                        WHEN ""B"".""Sorttime"" IS NULL
                        THEN 0
                        ELSE ""B"".""Sorttime""
                    END AS ""Sorttime""
                FROM ({usedBlocks}) A
                LEFT OUTER JOIN ({scantime}) B
                    ON ""B"".""VolumeID"" = ""A"".""VolumeID""
            ";

            var inactive = @"
                SELECT
                    0 AS ""ActiveSize"",
                    SUM(""Size"") AS ""InactiveSize"",
                    ""VolumeID"" AS ""VolumeID"",
                    0 AS ""SortScantime""
                FROM ""DeletedBlock""
                GROUP BY ""VolumeID""
            ";

            var empty = @"
                SELECT
                    0 AS ""ActiveSize"",
                    0 AS ""InactiveSize"",
                    ""Remotevolume"".""ID"" AS ""VolumeID"",
                    0 AS ""SortScantime""
                FROM ""Remotevolume""
                WHERE
                    ""Remotevolume"".""Type"" = @Type
                    AND ""Remotevolume"".""State"" IN (@AllowedStates)
                    AND ""Remotevolume"".""ID"" NOT IN (
                        SELECT ""VolumeID""
                        FROM ""Block""
                    )
            ";

            var combined = $"{active}  UNION {inactive} UNION {empty}";
            var collected = @$"
                SELECT
                    ""VolumeID"" AS ""VolumeID"",
                    SUM(""ActiveSize"") AS ""ActiveSize"",
                    SUM(""InactiveSize"") AS ""InactiveSize"",
                    MAX(""Sorttime"") AS ""Sorttime""
                FROM ({combined})
                GROUP BY ""VolumeID""
            ";

            var createtable = $"{@$"CREATE {TEMPORARY} TABLE ""{tmptablename}"" AS "}{collected}";

            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                try
                {
                    await cmd
                        .SetCommandAndParameters(createtable)
                        .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                        .ExpandInClauseParameterMssqlite("@AllowedStates", [
                            RemoteVolumeState.Uploaded.ToString(),
                            RemoteVolumeState.Verified.ToString()
                        ])
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    cmd.SetCommandAndParameters($@"
                        SELECT
                            ""A"".""Name"",
                            ""B"".""ActiveSize"",
                            ""B"".""InactiveSize"",
                            ""A"".""Size""
                        FROM
                            ""Remotevolume"" A,
                            ""{tmptablename}"" B
                        WHERE ""A"".""ID"" = ""B"".""VolumeID""
                        ORDER BY ""B"".""Sorttime"" ASC
                    ");

                    using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                        while (await rd.ReadAsync().ConfigureAwait(false))
                            yield return new VolumeUsage(
                                rd.ConvertValueToString(0) ?? "",
                                rd.ConvertValueToInt64(1, 0) + rd.ConvertValueToInt64(2, 0),
                                rd.ConvertValueToInt64(2, 0),
                                rd.ConvertValueToInt64(3, 0)
                            );
                }
                finally
                {
                    try
                    {
                        await cmd
                            .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tmptablename}"" ")
                            .ConfigureAwait(false);
                    }
                    catch { }
                }
            }
        }

        public interface ICompactReport
        {
            IEnumerable<string> DeleteableVolumes { get; }
            IEnumerable<string> CompactableVolumes { get; }
            bool ShouldReclaim { get; }
            bool ShouldCompact { get; }
            void ReportCompactData();
        }

        private class CompactReport : ICompactReport
        {
            private readonly IEnumerable<VolumeUsage> m_report;
            private readonly IEnumerable<VolumeUsage> m_cleandelete;
            private readonly IEnumerable<VolumeUsage> m_wastevolumes;
            private readonly IEnumerable<VolumeUsage> m_smallvolumes;

            private readonly long m_deletablevolumes;
            private readonly long m_wastedspace;
            private readonly long m_smallspace;
            private readonly long m_fullsize;
            private readonly long m_smallvolumecount;

            private readonly long m_wastethreshold;
            private readonly long m_volsize;
            private readonly long m_maxsmallfilecount;

            public CompactReport(long volsize, long wastethreshold, long smallfilesize, long maxsmallfilecount, IEnumerable<VolumeUsage> report)
            {
                m_report = report;

                m_cleandelete = (from n in m_report where n.DataSize <= n.WastedSize select n).ToArray();
                m_wastevolumes = from n in m_report where ((((n.WastedSize / (float)n.DataSize) * 100) >= wastethreshold) || (((n.WastedSize / (float)volsize) * 100) >= wastethreshold)) && !m_cleandelete.Contains(n) select n;
                m_smallvolumes = from n in m_report where n.CompressedSize <= smallfilesize && !m_cleandelete.Contains(n) select n;

                m_wastethreshold = wastethreshold;
                m_volsize = volsize;
                m_maxsmallfilecount = maxsmallfilecount;

                m_deletablevolumes = m_cleandelete.Count();
                m_fullsize = report.Select(x => x.DataSize).Sum();

                m_wastedspace = m_wastevolumes.Select(x => x.WastedSize).Sum();
                m_smallspace = m_smallvolumes.Select(x => x.CompressedSize).Sum();
                m_smallvolumecount = m_smallvolumes.Count();
            }

            public void ReportCompactData()
            {
                var wastepercentage = ((m_wastedspace / (float)m_fullsize) * 100);
                Logging.Log.WriteVerboseMessage(LOGTAG, "FullyDeletableCount", "Found {0} fully deletable volume(s)", m_deletablevolumes);
                Logging.Log.WriteVerboseMessage(LOGTAG, "SmallVolumeCount", "Found {0} small volumes(s) with a total size of {1}", m_smallvolumes.Count(), Library.Utility.Utility.FormatSizeString(m_smallspace));
                Logging.Log.WriteVerboseMessage(LOGTAG, "WastedSpaceVolumes", "Found {0} volume(s) with a total of {1:F2}% wasted space ({2} of {3})", m_wastevolumes.Count(), wastepercentage, Library.Utility.Utility.FormatSizeString(m_wastedspace), Library.Utility.Utility.FormatSizeString(m_fullsize));

                if (m_deletablevolumes > 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "CompactReason", "Compacting because there are {0} fully deletable volume(s)", m_deletablevolumes);
                else if (wastepercentage >= m_wastethreshold && m_wastevolumes.Count() >= 2)
                    Logging.Log.WriteInformationMessage(LOGTAG, "CompactReason", "Compacting because there is {0:F2}% wasted space and the limit is {1}%", wastepercentage, m_wastethreshold);
                else if (m_smallspace > m_volsize)
                    Logging.Log.WriteInformationMessage(LOGTAG, "CompactReason", "Compacting because there are {0} in small volumes and the volume size is {1}", Library.Utility.Utility.FormatSizeString(m_smallspace), Library.Utility.Utility.FormatSizeString(m_volsize));
                else if (m_smallvolumecount > m_maxsmallfilecount)
                    Logging.Log.WriteInformationMessage(LOGTAG, "CompactReason", "Compacting because there are {0} small volumes and the maximum is {1}", m_smallvolumecount, m_maxsmallfilecount);
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "CompactReason", "Compacting not required");
            }

            public bool ShouldReclaim
            {
                get
                {
                    return m_deletablevolumes > 0;
                }
            }

            public bool ShouldCompact
            {
                get
                {
                    return (((m_wastedspace / (float)m_fullsize) * 100) >= m_wastethreshold && m_wastevolumes.Count() >= 2) || m_smallspace > m_volsize || m_smallvolumecount > m_maxsmallfilecount;
                }
            }

            public IEnumerable<string> DeleteableVolumes
            {
                get { return from n in m_cleandelete select n.Name; }
            }

            public IEnumerable<string> CompactableVolumes
            {
                get
                {
                    //The order matters, we compact old volumes together first,
                    // as we anticipate old data will stay around, where never data
                    // is more likely to be discarded again
                    return m_wastevolumes.Union(m_smallvolumes).Select(x => x.Name).Distinct();
                }
            }
        }

        public async Task<ICompactReport> GetCompactReport(long volsize, long wastethreshold, long smallfilesize, long maxsmallfilecount)
        {
            return new CompactReport(
                volsize,
                wastethreshold,
                smallfilesize,
                maxsmallfilecount,
                await GetWastedSpaceReport()
                    .ToListAsync()
                    .ConfigureAwait(false)
            );
        }


        public interface IBlockQuery : IDisposable
        {
            /// <summary>
            /// Checks if a block is in use. If volumeId is not -1, check specific volume
            /// </summary>
            /// <param name="hash">The hash of the block</param>
            /// <param name="size">The size of the block</param>
            /// <param name="volumeId">The volume ID to check, or -1 to check all volumes</param>
            /// <param name="transaction">The transaction to execute the command in</param>
            /// <returns>True if the block is in use</returns>
            Task<bool> UseBlock(string hash, long size, long volumeId);
        }

        private class BlockQuery : IBlockQuery
        {
            private LocalDatabase m_db = null!;

            private SqliteCommand m_command = null!;

            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync() instead.")]
            public BlockQuery(SqliteConnection connection, SqliteTransaction? transaction)
            {
                throw new NotImplementedException("Use CreateAsync() instead");
            }

            private BlockQuery()
            {
                // Private constructor to prevent instantiation without CreateAsync
            }

            public static async Task<BlockQuery> CreateAsync(LocalDatabase db)
            {
                return new BlockQuery
                {
                    m_db = db,
                    m_command = await db.Connection.CreateCommandAsync(@"
                        SELECT ""VolumeID""
                        FROM ""Block""
                        WHERE
                            ""Hash"" = @Hash
                            AND ""Size"" = @Size
                    ")
                        .ConfigureAwait(false)
                };
            }

            /// <inheritdoc />
            public async Task<bool> UseBlock(string hash, long size, long volumeId)
            {
                var r = await m_command
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .ExecuteScalarInt64Async(-1)
                    .ConfigureAwait(false);

                if (r == -1)
                {
                    return false;
                }
                else if (volumeId == -1)
                {
                    return true;
                }
                else
                {
                    // Check that the volume id matches
                    return r == volumeId;
                }
            }

            public void Dispose()
            {
                if (m_command != null)
                    try { m_command.Dispose(); }
                    finally { m_command = null!; }
            }
        }

        /// <summary>
        /// Builds a lookup table to enable faster response to block queries
        /// </summary>
        public async Task<IBlockQuery> CreateBlockQueryHelper()
        {
            return await BlockQuery.CreateAsync(this).ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a block as moved to a new volume
        /// </summary>
        /// <param name="hash">The hash of the block</param>
        /// <param name="size">The size of the block</param>
        /// <param name="volumeID">The new volume ID</param>
        public async Task RegisterDuplicatedBlock(string hash, long size, long volumeID)
        {
            // Using INSERT OR IGNORE to avoid duplicate entries, result may be 1 or 0
            await m_registerDuplicateBlockCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@VolumeId", volumeID)
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// After new volumes are uploaded, this method will update the blocks from the old volumes to point to the new volumes
        /// </summary>
        /// <param name="filename">The file to remove</param>
        /// <param name="volumeIdsToBeRemoved">The volume IDs that will be removed</param>
        /// <param name="transaction">The transaction to execute the command in</param>
        public async Task PrepareForDelete(string filename, IEnumerable<long> volumeIdsToBeRemoved)
        {
            var deletedVolume = await GetRemoteVolume(filename)
                .ConfigureAwait(false);
            if (deletedVolume.Type != RemoteVolumeType.Blocks)
                return;

            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var updatedBlocks = "BlocksToUpdate-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var replacementBlocks = "ReplacementBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    await cmd.SetCommandAndParameters($@"
                        CREATE {TEMPORARY} TABLE ""{updatedBlocks}"" AS
                        SELECT ""ID""
                        FROM ""Block""
                        WHERE ""VolumeID"" = @VolumeId
                    ")
                        .SetParameterValue("@VolumeId", deletedVolume.ID)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    using (var tempTable = await TemporaryDbValueList.CreateAsync(this, volumeIdsToBeRemoved).ConfigureAwait(false))
                        await (
                            await cmd.SetCommandAndParameters($@"
                                CREATE {TEMPORARY} TABLE ""{replacementBlocks}"" AS
                                SELECT
                                    ""BlockID"",
                                    MAX(""VolumeID"") AS ""VolumeID""
                                FROM ""DuplicateBlock""
                                WHERE
                                    ""VolumeID"" NOT IN (@VolumeIds)
                                    AND ""BlockID"" IN (
                                        SELECT ""ID""
                                        FROM ""{updatedBlocks}""
                                    )
                                    GROUP BY ""BlockID""
                            ")
                            .ExpandInClauseParameterMssqliteAsync("@VolumeIds", tempTable)
                            .ConfigureAwait(false)
                        )
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                    var targetCount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""{updatedBlocks}""
                    ")
                        .ConfigureAwait(false);

                    if (targetCount == 0)
                        return;

                    var replacementCount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""{replacementBlocks}""
                    ")
                        .ConfigureAwait(false);

                    var updateCount = await cmd.SetCommandAndParameters(@$"
                        UPDATE ""Block""
                        SET ""VolumeID"" = (
                            SELECT ""VolumeID""
                            FROM ""{replacementBlocks}""
                            WHERE
                                ""{replacementBlocks}"".""BlockID"" = ""Block"".""ID""
                                AND ""Block"".""VolumeID"" = @VolumeId
                        )
                        WHERE ""Block"".""VolumeID"" = @VolumeId
                    ")
                        .SetParameterValue("@VolumeId", deletedVolume.ID)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    var deleteCount = await cmd.ExecuteNonQueryAsync(@$"
                        DELETE FROM ""DuplicateBlock""
                        WHERE
                            (
                                ""DuplicateBlock"".""BlockID""
                                || ':'
                                || ""DuplicateBlock"".""VolumeID""
                            ) IN (
                                SELECT
                                    ""RB"".""BlockID""
                                    || ':'
                                    || ""RB"".""VolumeID""
                                FROM ""{replacementBlocks}"" RB
                            )
                    ")
                        .ConfigureAwait(false);

                    if (targetCount != updateCount
                        || replacementCount != deleteCount
                        || updateCount != deleteCount)
                    {
                        throw new Exception($"Unexpected number of rows updated. Expected {targetCount} but got updated {updateCount}, deleted {deleteCount}, and replaced {replacementCount}");
                    }

                    // Remove knowledge of any old blocks
                    await cmd.SetCommandAndParameters(@$"
                        DELETE FROM ""DuplicateBlock""
                        WHERE ""VolumeID"" = @VolumeId
                    ")
                        .SetParameterValue("@VolumeId", deletedVolume.ID)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{updatedBlocks}"" ")
                            .ConfigureAwait(false);
                    }
                    catch { }
                    try
                    {
                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{replacementBlocks}"" ")
                            .ConfigureAwait(false);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Calculates the sequence in which files should be deleted based on their relations.
        /// </summary>
        /// <param name="deleteableVolumes">Block volumes slated for deletion.</param>
        /// <param name="transaction">The transaction to execute the command in</param>
        /// <returns>The deletable volumes.</returns>
        public async IAsyncEnumerable<IRemoteVolume> ReOrderDeleteableVolumes(IEnumerable<IRemoteVolume> deleteableVolumes)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                // Although the generated index volumes are always in pairs,
                // this code handles many-to-many relations between
                // index files and block volumes, should this be added later
                var lookupBlock = new Dictionary<string, List<IRemoteVolume>>();
                var lookupIndexfiles = new Dictionary<string, List<string>>();

                cmd.SetCommandAndParameters(@"
                    SELECT
                        ""C"".""Name"",
                        ""B"".""Name"",
                        ""B"".""Hash"",
                        ""B"".""Size""
                    FROM
                        ""IndexBlockLink"" A,
                        ""RemoteVolume"" B,
                        ""RemoteVolume"" C
                    WHERE
                        ""A"".""IndexVolumeID"" = ""B"".""ID""
                        AND ""A"".""BlockVolumeID"" = ""C"".""ID""
                        AND ""B"".""Hash"" IS NOT NULL
                        AND ""B"".""Size"" IS NOT NULL
                ");

                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                    {
                        var name = rd.ConvertValueToString(0) ?? "";
                        if (!lookupBlock.TryGetValue(name, out var indexfileList))
                        {
                            indexfileList = new List<IRemoteVolume>();
                            lookupBlock.Add(name, indexfileList);
                        }

                        var v = new RemoteVolume(
                            rd.ConvertValueToString(1),
                            rd.ConvertValueToString(2),
                            rd.ConvertValueToInt64(3)
                        );
                        indexfileList.Add(v);

                        if (!lookupIndexfiles.TryGetValue(v.Name, out var blockList))
                        {
                            blockList = new List<string>();
                            lookupIndexfiles.Add(v.Name, blockList);
                        }
                        blockList.Add(name);
                    }

                foreach (var r in deleteableVolumes.Distinct())
                {
                    // Return the input
                    yield return r;
                    if (lookupBlock.TryGetValue(r.Name, out var indexfileList))
                        foreach (var sh in indexfileList)
                        {
                            if (lookupIndexfiles.TryGetValue(sh.Name, out var backref))
                            {
                                //If this is the last reference,
                                // remove the index file as well
                                if (backref.Remove(r.Name) && backref.Count == 0)
                                    yield return sh;
                            }
                        }
                }
            }
        }

    }
}

