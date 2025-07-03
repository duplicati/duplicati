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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Operation.Restore;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Represents a local database used during restore.
    /// Provides methods for preparing and tracking restore file lists, managing temporary tables for files and blocks,
    /// tracking restore progress, and efficiently querying and updating restore-related metadata.
    /// This class supports advanced restore scenarios, including filtering, progress tracking via triggers,
    /// cross-platform path mapping, and efficient block and file restoration workflows.
    /// </summary>
    internal class LocalRestoreDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRestoreDatabase));

        /// <summary>
        /// A unique identifier for the temporary table set, used to ensure that temporary tables do not conflict with others.
        /// </summary>
        protected readonly string m_temptabsetguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
        /// <summary>
        /// The name of the temporary table in the database, which is used to store the list of files to restore.
        /// </summary>
        protected string? m_tempfiletable;
        /// <summary>
        /// The name of the temporary table in the database, which is used to store the blocks associated with the files to restore.
        /// </summary>
        protected string? m_tempblocktable;
        /// <summary>
        /// A pool of connections and transactions that are used for concurrent access to the database.
        /// </summary>
        protected ConcurrentBag<(SqliteConnection, ReusableTransaction)> m_connection_pool = [];
        /// <summary>
        /// The latest block table used to track the most recent blocks processed during a restore operation.
        /// </summary>
        protected string? m_latestblocktable;
        /// <summary>
        /// The name of the temporary table used to track progress of file restoration.
        /// </summary>
        protected string? m_fileprogtable;
        /// <summary>
        /// The name of the temporary table used to track overall restoration progress across all files.
        /// </summary>
        protected string? m_totalprogtable;
        /// <summary>
        /// The name of the temporary table used to track files that have been fully restored (all blocks restored).
        /// </summary>
        protected string? m_filesnewlydonetable;

        /// <summary>
        /// A semaphore used to ensure that only one thread can access the database at a time.
        /// </summary>
        private SemaphoreSlim m_dbLock = new(1, 1);

        /// <summary>
        /// The time at which the restore operation begins.
        /// </summary>
        protected DateTime m_restoreTime;

        /// <summary>
        /// Gets the time that the restore operation was initiated.
        /// </summary>
        public DateTime RestoreTime { get { return m_restoreTime; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalRestoreDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="dbnew">An optional existing instance of <see cref="LocalRestoreDatabase"/> to use, or null to create a new one. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited on, returns a new instance of <see cref="LocalRestoreDatabase"/>.</returns>
        public static async Task<LocalRestoreDatabase> CreateAsync(string path, LocalRestoreDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalRestoreDatabase();

            dbnew = (LocalRestoreDatabase)
                await CreateLocalDatabaseAsync(path, "Restore", false, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalRestoreDatabase"/> class using an existing parent database.
        /// </summary>
        /// <param name="dbparent">The parent database from which to create the restore database.</param>
        /// <param name="dbnew">An optional existing instance of <see cref="LocalRestoreDatabase"/> to use, or null to create a new one. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited on, returns a new instance of <see cref="LocalRestoreDatabase"/>.</returns>
        public static async Task<LocalRestoreDatabase> CreateAsync(LocalDatabase dbparent, LocalRestoreDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalRestoreDatabase();

            return (LocalRestoreDatabase)
                await CreateLocalDatabaseAsync(dbparent, dbnew, token)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Create tables and triggers for automatic tracking of progress during a restore operation.
        /// This replaces continuous requerying of block progress by iterating over blocks table.
        /// SQLite is much faster keeping information up to date with internal triggers.
        /// </summary>
        /// <param name="createFilesNewlyDoneTracker">This allows to create another table that keeps track
        /// of all files that are done (all data blocks restored).</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <remarks>
        /// The method is prepared to create a table that keeps track of all files being done completely.
        /// That means, it fires for files where the number of restored blocks equals the number of all blocks.
        /// It is intended to be used for fast identification of fully restored files to trigger their verification.
        /// It should be read after a commit and truncated after putting the files to a verification queue.
        /// Note: If a file is done once and then set back to a none restored state, the file is not automatically removed.
        ///       But if it reaches a restored state later, it will be re-added (trigger will fire)
        /// </remarks>
        /// <returns>A task that completes when the progress tracker is created.</returns>
        public async Task CreateProgressTracker(bool createFilesNewlyDoneTracker, CancellationToken token)
        {
            m_fileprogtable = "FileProgress-" + m_temptabsetguid;
            m_totalprogtable = "TotalProgress-" + m_temptabsetguid;
            m_filesnewlydonetable = createFilesNewlyDoneTracker ? "FilesNewlyDone-" + m_temptabsetguid : null;

            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            // How to handle METADATA?
            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_fileprogtable}"" ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{m_fileprogtable}"" (
                    ""FileId"" INTEGER PRIMARY KEY,
                    ""TotalBlocks"" INTEGER NOT NULL,
                    ""TotalSize"" INTEGER NOT NULL,
                    ""BlocksRestored"" INTEGER NOT NULL,
                    ""SizeRestored"" INTEGER NOT NULL
                )
            ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_totalprogtable}"" ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{m_totalprogtable}"" (
                    ""TotalFiles"" INTEGER NOT NULL,
                    ""TotalBlocks"" INTEGER NOT NULL,
                    ""TotalSize"" INTEGER NOT NULL,
                    ""FilesFullyRestored"" INTEGER NOT NULL,
                    ""FilesPartiallyRestored"" INTEGER NOT NULL,
                    ""BlocksRestored"" INTEGER NOT NULL,
                    ""SizeRestored"" INTEGER NOT NULL
                )
            ", token)
                .ConfigureAwait(false);

            if (createFilesNewlyDoneTracker)
            {
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}"" ", token)
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{m_filesnewlydonetable}"" (
                        ""ID"" INTEGER PRIMARY KEY
                    )
                ", token)
                    .ConfigureAwait(false);
            }

            try
            {
                // Initialize statistics with File- and Block-Data (it is valid to already have restored blocks in files)
                // A rebuild with this function should be valid anytime.
                // Note: FilesNewlyDone is NOT initialized, as in initialization nothing is really new.

                // We use a LEFT JOIN to allow for empty files (no data Blocks)

                // Will be one row per file.
                await cmd.ExecuteNonQueryAsync($@"
                    INSERT INTO ""{m_fileprogtable}"" (
                        ""FileId"",
                        ""TotalBlocks"",
                        ""TotalSize"",
                        ""BlocksRestored"",
                        ""SizeRestored""
                    )
                    SELECT
                        ""F"".""ID"",
                        IFNULL(COUNT(""B"".""ID""), 0),
                        IFNULL(SUM(""B"".""Size""), 0),
                        IFNULL(
                            COUNT(
                                CASE ""B"".""Restored""
                                    WHEN 1
                                    THEN ""B"".""ID""
                                    ELSE NULL
                                END
                            ),
                            0
                        ),
                        IFNULL(
                            SUM(
                                CASE ""B"".""Restored""
                                    WHEN 1
                                    THEN ""B"".""Size""
                                    ELSE 0
                                END
                            ),
                            0
                        )
                    FROM ""{m_tempfiletable}"" ""F""
                    LEFT JOIN ""{m_tempblocktable}"" ""B""
                        ON  ""B"".""FileID"" = ""F"".""ID""
                    WHERE ""B"".""Metadata"" IS NOT 1
                    GROUP BY ""F"".""ID""
                ", token)
                    .ConfigureAwait(false);

                // Will result in a single line (no support to also track metadata)
                await cmd.ExecuteNonQueryAsync($@"
                    INSERT INTO ""{m_totalprogtable}"" (
                        ""TotalFiles"",
                        ""TotalBlocks"",
                        ""TotalSize"",
                        ""FilesFullyRestored"",
                        ""FilesPartiallyRestored"",
                        ""BlocksRestored"",
                        ""SizeRestored""
                    )
                    SELECT
                        IFNULL(COUNT(""P"".""FileId""), 0),
                        IFNULL(SUM(""P"".""TotalBlocks""), 0),
                        IFNULL(SUM(""P"".""TotalSize""), 0),
                        IFNULL(
                            COUNT(
                                CASE
                                    WHEN ""P"".""BlocksRestored"" = ""P"".""TotalBlocks""
                                    THEN 1
                                    ELSE NULL
                                END
                            ),
                            0
                        ),
                        IFNULL(
                            COUNT(
                                CASE
                                    WHEN
                                        ""P"".""BlocksRestored"" BETWEEN 1
                                        AND ""P"".""TotalBlocks"" - 1
                                    THEN 1
                                    ELSE NULL
                                END
                            ),
                            0
                        ),
                        IFNULL(SUM(""P"".""BlocksRestored""), 0),
                        IFNULL(SUM(""P"".""SizeRestored""), 0)
                    FROM ""{m_fileprogtable}"" ""P""
                ", token)
                    .ConfigureAwait(false);

                // Finally we create TRIGGERs to keep all our statistics up to date.
                // This is lightning fast, as SQLite uses internal hooks and our indices to do the update magic.
                // Note: We do assume that neither files nor blocks will be added or deleted during restore process
                //       and that the size of each block stays constant so there is no need to track that information
                //       with additional INSERT and DELETE triggers.

                // A trigger to update the file-stat entry each time a block changes restoration state.
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TRIGGER ""TrackRestoredBlocks_{m_tempblocktable}""
                    AFTER UPDATE OF ""Restored""
                    ON ""{m_tempblocktable}""
                    WHEN OLD.""Restored"" != NEW.""Restored""
                    AND NEW.""Metadata"" = 0
                    BEGIN UPDATE ""{m_fileprogtable}""
                    SET
                        ""BlocksRestored"" =
                            ""{m_fileprogtable}"".""BlocksRestored""
                            + (NEW.""Restored"" - OLD.""Restored""),
                        ""SizeRestored"" =
                            ""{m_fileprogtable}"".""SizeRestored""
                            + ((NEW.""Restored"" - OLD.""Restored"") * NEW.Size)
                    WHERE ""{m_fileprogtable}"".""FileId"" = NEW.""FileID""
                    ; END
                ", token)
                    .ConfigureAwait(false);

                // A trigger to update total stats each time a file stat changed (nested triggering by file-stats)
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TRIGGER ""UpdateTotalStats_{m_fileprogtable}""
                    AFTER UPDATE ON ""{m_fileprogtable}""
                    BEGIN UPDATE ""{m_totalprogtable}""
                    SET
                        ""FilesFullyRestored"" =
                            ""{m_totalprogtable}"".""FilesFullyRestored""
                            + (CASE
                                WHEN NEW.""BlocksRestored"" = NEW.""TotalBlocks""
                                THEN 1
                                ELSE 0
                            END)
                            - (CASE
                                WHEN OLD.""BlocksRestored"" = OLD.""TotalBlocks""
                                THEN 1
                                ELSE 0
                            END),
                        ""FilesPartiallyRestored"" =
                            ""{m_totalprogtable}"".""FilesPartiallyRestored""
                            + (CASE
                                WHEN
                                    NEW.""BlocksRestored"" BETWEEN 1
                                    AND NEW.""TotalBlocks"" - 1
                                THEN 1
                                ELSE 0
                            END)
                            - (CASE
                                WHEN
                                    OLD.""BlocksRestored"" BETWEEN 1
                                    AND OLD.""TotalBlocks"" - 1
                                THEN 1
                                ELSE 0
                            END),
                        ""BlocksRestored"" =
                            ""{m_totalprogtable}"".""BlocksRestored""
                            + NEW.""BlocksRestored""
                            - OLD.""BlocksRestored"",
                        ""SizeRestored"" =
                            ""{m_totalprogtable}"".""SizeRestored""
                            + NEW.""SizeRestored""
                            - OLD.""SizeRestored""
                    ; END
                ", token)
                    .ConfigureAwait(false);

                if (createFilesNewlyDoneTracker)
                {
                    // A trigger checking if a file is done (all blocks restored in file-stat) (nested triggering by file-stats)
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TRIGGER ""UpdateFilesNewlyDone_{m_fileprogtable}""
                        AFTER UPDATE OF
                            ""BlocksRestored"",
                            ""TotalBlocks""
                        ON ""{m_fileprogtable}""
                        WHEN NEW.""BlocksRestored"" = NEW.""TotalBlocks""
                        BEGIN
                            INSERT OR IGNORE INTO ""{m_filesnewlydonetable}"" (""ID"")
                            VALUES (NEW.""FileId"");
                        END
                    ", token)
                        .ConfigureAwait(false);
                }

            }
            catch (Exception ex)
            {
                m_fileprogtable = null;
                m_totalprogtable = null;
                Logging.Log.WriteWarningMessage(LOGTAG, "ProgressTrackerSetupError", ex, "Failed to set up progress tracking tables");
                throw;
            }
            finally
            {
                await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Prepares the restore file list by querying the database for filesets and their associated files.
        /// </summary>
        /// <param name="restoretime">The time at which the restore operation is being performed.</param>
        /// <param name="versions">An array of version identifiers to filter the filesets.</param>
        /// <param name="filter">An optional filter to apply to the files being restored.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a tuple containing the count of files to restore and the total size of those files.</returns>
        public async Task<Tuple<long, long>> PrepareRestoreFilelist(DateTime restoretime, long[] versions, IFilter filter, CancellationToken token)
        {
            m_tempfiletable = "Fileset-" + m_temptabsetguid;
            m_tempblocktable = "Blocks-" + m_temptabsetguid;

            await using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                var filesetIds =
                    await GetFilesetIDs(Library.Utility.Utility.NormalizeDateTime(restoretime), versions, false, token)
                    .ToListAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                while (filesetIds.Count > 0)
                {
                    var filesetId = filesetIds[0];
                    filesetIds.RemoveAt(0);

                    cmd.SetCommandAndParameters(@"
                        SELECT ""Timestamp""
                        FROM ""Fileset""
                        WHERE ""ID"" = @FilesetId
                    ")
                        .SetParameterValue("@FilesetId", filesetId);
                    m_restoreTime = ParseFromEpochSeconds(
                        await cmd.ExecuteScalarInt64Async(0, token)
                            .ConfigureAwait(false)
                    );

                    var ix = await FilesetTimes(token)
                            .Select((value, index) => new { value.Key, index })
                            .Where(n => n.Key == filesetId)
                            .Select(pair => pair.index + 1)
                            .FirstOrDefaultAsync(cancellationToken: token)
                            .ConfigureAwait(false) - 1;

                    Logging.Log.WriteInformationMessage(LOGTAG, "SearchingBackup", "Searching backup {0} ({1}) ...", ix, m_restoreTime);

                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempfiletable}"" ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempblocktable}"" ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{m_tempfiletable}"" (
                            ""ID"" INTEGER PRIMARY KEY,
                            ""Path"" TEXT NOT NULL,
                            ""BlocksetID"" INTEGER NOT NULL,
                            ""MetadataID"" INTEGER NOT NULL,
                            ""TargetPath"" TEXT NULL,
                            ""DataVerified"" BOOLEAN NOT NULL,
                            ""LatestBlocksetId"" INTEGER,
                            ""LocalSourceExists"" BOOLEAN
                        )
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{m_tempblocktable}"" (
                            ""ID"" INTEGER PRIMARY KEY,
                            ""FileID"" INTEGER NOT NULL,
                            ""Index"" INTEGER NOT NULL,
                            ""Hash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Restored"" BOOLEAN NOT NULL,
                            ""Metadata"" BOOLEAN NOT NULL,
                            ""VolumeID"" INTEGER NOT NULL,
                            ""BlockID"" INTEGER NOT NULL
                        )
                    ", token)
                        .ConfigureAwait(false);

                    // TODO: Optimize to use the path prefix

                    if (filter == null || filter.Empty)
                    {
                        // Simple case, restore everything
                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tempfiletable}"" (
                                ""ID"",
                                ""Path"",
                                ""BlocksetID"",
                                ""MetadataID"",
                                ""DataVerified""
                            )
                            SELECT
                                ""File"".""ID"",
                                ""File"".""Path"",
                                ""File"".""BlocksetID"",
                                ""File"".""MetadataID"",
                                0
                            FROM
                                ""File"",
                                ""FilesetEntry""
                            WHERE
                                ""File"".""ID"" = ""FilesetEntry"".""FileID""
                                AND ""FilesetEntry"".""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQueryAsync(token)
                            .ConfigureAwait(false);
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == FilterType.Simple)
                    {
                        using (new Logging.Timer(LOGTAG, "CommitBeforePrepareFileset", "CommitBeforePrepareFileset"))
                            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);

                        cmd.SetTransaction(m_rtr);
                        // If we get a list of filenames, the lookup table is faster
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        var p = expression.GetSimpleList();
                        var m_filenamestable = "Filenames-" + m_temptabsetguid;
                        await cmd.ExecuteNonQueryAsync($@"
                            CREATE TEMPORARY TABLE ""{m_filenamestable}"" (
                                ""Path"" TEXT NOT NULL
                            )
                        ", token)
                            .ConfigureAwait(false);

                        cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_filenamestable}"" (""Path"")
                            VALUES (@Path)
                        ")
                            .ConfigureAwait(false);

                        foreach (var s in p)
                        {
                            await cmd
                                .SetParameterValue("@Path", s)
                                .ExecuteNonQueryAsync(token)
                                .ConfigureAwait(false);
                        }

                        var c = await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tempfiletable}"" (
                                ""ID"",
                                ""Path"",
                                ""BlocksetID"",
                                ""MetadataID"",
                                ""DataVerified""
                            )
                            SELECT
                                ""File"".""ID"",
                                ""File"".""Path"",
                                ""File"".""BlocksetID"",
                                ""File"".""MetadataID"",
                                0
                            FROM
                                ""File"",
                                ""FilesetEntry""
                            WHERE
                                ""File"".""ID"" = ""FilesetEntry"".""FileID""
                                AND ""FilesetEntry"".""FilesetID"" = @FilesetId
                                AND ""Path"" IN (
                                    SELECT DISTINCT ""Path""
                                    FROM ""{m_filenamestable}""
                                )
                        ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQueryAsync(token)
                            .ConfigureAwait(false);

                        if (c != p.Length && c != 0)
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine();

                            cmd.SetCommandAndParameters($@"
                                SELECT ""Path""
                                FROM ""{m_filenamestable}""
                                WHERE ""Path"" NOT IN (
                                    SELECT ""Path""
                                    FROM ""{m_tempfiletable}""
                                )
                            ");
                            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                                while (await rd.ReadAsync(token).ConfigureAwait(false))
                                    sb.AppendLine(rd.ConvertValueToString(0));

                            cmd.SetCommandAndParameters(@"
                                SELECT ""Timestamp""
                                FROM ""Fileset""
                                WHERE ""ID"" = @FilesetId
                            ")
                                .SetParameterValue("@FilesetId", filesetId);
                            var actualrestoretime = ParseFromEpochSeconds(
                                await cmd.ExecuteScalarInt64Async(0, token)
                                    .ConfigureAwait(false)
                            );

                            Logging.Log.WriteWarningMessage(LOGTAG, "FilesNotFoundInBackupList", null, "{0} File(s) were not found in list of files for backup at {1}, will not be restored: {2}", p.Length - c, actualrestoretime.ToLocalTime(), sb);
                        }

                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filenamestable}"" ", token)
                            .ConfigureAwait(false);

                        using (new Logging.Timer(LOGTAG, "CommitAfterPrepareFileset", "CommitAfterPrepareFileset"))
                            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
                        cmd.SetTransaction(m_rtr);
                    }
                    else
                    {
                        // Restore but filter elements based on the filter expression
                        // If this is too slow, we could add a special handler for wildcard searches too
                        cmd.SetCommandAndParameters(@"
                            SELECT
                                ""File"".""ID"",
                                ""File"".""Path"",
                                ""File"".""BlocksetID"",
                                ""File"".""MetadataID""
                            FROM
                                ""File"",
                                ""FilesetEntry""
                            WHERE
                                ""File"".""ID"" = ""FilesetEntry"".""FileID""
                                AND ""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetId);

                        object[] values = new object[4];
                        await using var cmd2 = m_connection.CreateCommand($@"
                            INSERT INTO ""{m_tempfiletable}"" (
                                ""ID"",
                                ""Path"",
                                ""BlocksetID"",
                                ""MetadataID"",
                                ""DataVerified""
                            )
                            VALUES (
                                @ID,
                                @Path,
                                @BlocksetID,
                                @MetadataID,
                                0
                            )
                        ");
                        await using var rd = await cmd.ExecuteReaderAsync(token)
                            .ConfigureAwait(false);
                        while (await rd.ReadAsync(token).ConfigureAwait(false))
                        {
                            rd.GetValues(values);
                            var path = values[1] as string;
                            if (path != null && FilterExpression.Matches(filter, path.ToString()!))
                            {
                                await cmd2
                                    .SetParameterValue("@ID", values[0])
                                    .SetParameterValue("@Path", values[1])
                                    .SetParameterValue("@BlocksetID", values[2])
                                    .SetParameterValue("@MetadataID", values[3])
                                    .ExecuteNonQueryAsync(token)
                                    .ConfigureAwait(false);
                            }
                        }
                    }

                    //creating indexes after insertion is much faster
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_ID""
                        ON ""{m_tempfiletable}"" (""ID"")
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_TargetPath""
                        ON ""{m_tempfiletable}"" (""TargetPath"")
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_Path""
                        ON ""{m_tempfiletable}"" (""Path"")
                    ", token)
                        .ConfigureAwait(false);

                    cmd.SetCommandAndParameters($@"
                        SELECT
                            COUNT(DISTINCT ""{m_tempfiletable}"".""Path""),
                            SUM(""Blockset"".""Length"")
                        FROM
                            ""{m_tempfiletable}"",
                            ""Blockset""
                        WHERE ""{m_tempfiletable}"".""BlocksetID"" = ""Blockset"".""ID""
                    ");
                    await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        var filecount = 0L;
                        var filesize = 0L;

                        if (await rd.ReadAsync(token).ConfigureAwait(false))
                        {
                            filecount = rd.ConvertValueToInt64(0, 0);
                            filesize = rd.ConvertValueToInt64(1, 0);
                        }

                        if (filecount > 0)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "RestoreTargetFileCount", "Needs to restore {0} files ({1})", filecount, Library.Utility.Utility.FormatSizeString(filesize));
                            return new Tuple<long, long>(filecount, filesize);
                        }
                    }
                }

                await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
            }

            return new Tuple<long, long>(0, 0);
        }

        /// <summary>
        /// Retrieves the first path from the temporary file table.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the first path found in the temporary file table, or null if no paths are found.</returns>
        public async Task<string?> GetFirstPath(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                SELECT ""Path""
                FROM ""{m_tempfiletable}""
                ORDER BY LENGTH(""Path"") DESC
                LIMIT 1
            ")
                .SetTransaction(m_rtr);

            var v0 = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            if (v0 == null || v0 == DBNull.Value)
                return null;

            return v0.ToString();
        }

        /// <summary>
        /// Retrieves the largest prefix path from the temporary file table.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the largest prefix path found in the temporary file table.</returns>
        public async Task<string> GetLargestPrefix(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                SELECT ""Path""
                FROM ""{m_tempfiletable}""
                ORDER BY LENGTH(""Path"") DESC
                LIMIT 1
            ")
                .SetTransaction(m_rtr);

            var v0 = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            var maxpath = "";
            if (v0 != null && v0 != DBNull.Value)
                maxpath = v0.ToString()!;

            var dirsep = Util.GuessDirSeparator(maxpath);

            var filecount = await cmd.ExecuteScalarInt64Async($@"
                SELECT COUNT(*)
                FROM ""{m_tempfiletable}""
            ", -1, token)
                .ConfigureAwait(false);

            var foundfiles = -1L;

            //TODO: Handle FS case-sensitive?
            cmd.SetCommandAndParameters($@"
                SELECT COUNT(*)
                FROM ""{m_tempfiletable}""
                WHERE SUBSTR(""Path"", 1, @PrefixLength) = @Prefix
            ");

            while (filecount != foundfiles && maxpath.Length > 0)
            {
                var mp = Util.AppendDirSeparator(maxpath, dirsep);
                foundfiles = await cmd
                    .SetParameterValue("@PrefixLength", mp.Length)
                    .SetParameterValue("@Prefix", mp)
                    .ExecuteScalarInt64Async(-1, token)
                    .ConfigureAwait(false);

                if (filecount != foundfiles)
                {
                    var oldlen = maxpath.Length;

                    var lix = maxpath.LastIndexOf(dirsep, maxpath.Length - 2, StringComparison.Ordinal);
                    maxpath = maxpath.Substring(0, lix + 1);
                    if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen)
                        maxpath = "";
                }
            }

            return maxpath == "" ? "" : Util.AppendDirSeparator(maxpath, dirsep);
        }

        /// <summary>
        /// Sets the target paths for files in the temporary file table.
        /// This method adjusts the target paths based on the largest prefix and destination provided.
        /// </summary>
        /// <param name="largest_prefix">The largest prefix path to use for adjusting target paths.</param>
        /// <param name="destination">The destination path to prepend to the target paths.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the target paths have been set.</returns>
        public async Task SetTargetPaths(string largest_prefix, string destination, CancellationToken token)
        {
            var dirsep = Util.GuessDirSeparator(string.IsNullOrWhiteSpace(largest_prefix) ?
                await GetFirstPath(token).ConfigureAwait(false)
                :
                largest_prefix);

            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);
            if (string.IsNullOrEmpty(destination))
            {
                //The string fixing here is meant to provide some non-random
                // defaults when restoring cross OS, e.g. backup on Linux, restore on Windows
                //This is mostly meaningless, and the user really should use --restore-path

                if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                {
                    // For Win -> Linux, we remove the colon from the drive letter, and use the drive letter as root folder
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" =
                        CASE
                            WHEN SUBSTR(""Path"", 2, 1) == ':'
                            THEN '\\' || SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3)
                            ELSE ""Path""
                        END
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" =
                        CASE
                            WHEN SUBSTR(""Path"", 1, 2) == '\\'
                            THEN '\\' || SUBSTR(""Path"", 2)
                            ELSE ""Path""
                        END
                    ", token)
                        .ConfigureAwait(false);

                }
                else if (OperatingSystem.IsWindows() && dirsep == "/")
                {
                    // For Linux -> Win, we use the temporary folder's drive as the root path
                    await cmd.SetCommandAndParameters($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" =
                            CASE
                                WHEN SUBSTR(""Path"", 1, 1) == '/'
                                THEN @Path || SUBSTR(""Path"", 2)
                                ELSE ""Path""
                            END
                    ")
                        .SetParameterValue("@Path", Util.AppendDirSeparator(System.IO.Path.GetPathRoot(Library.Utility.TempFolder.SystemTempPath)).Replace("\\", "/"))
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Same OS, just use the path directly
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" = ""Path""
                    ", token)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(largest_prefix))
                {
                    //Special case, restoring to new folder, but files are from different drives (no shared root on Windows)

                    // We use the format <restore path> / <drive letter> / <source path>
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""TargetPath"" =
                            CASE
                                WHEN SUBSTR(""Path"", 2, 1) == ':'
                                THEN SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3)
                                ELSE ""Path""
                            END
                    ", token)
                        .ConfigureAwait(false);

                    // For UNC paths, we use \\server\folder -> <restore path> / <servername> / <source path>
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""TargetPath"" =
                        CASE
                            WHEN SUBSTR(""Path"", 1, 2) == '\\'
                            THEN SUBSTR(""Path"", 2)
                            ELSE ""TargetPath""
                        END
                    ", token)
                        .ConfigureAwait(false);
                }
                else
                {
                    largest_prefix = Util.AppendDirSeparator(largest_prefix, dirsep);
                    await cmd.SetCommandAndParameters($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""TargetPath"" = SUBSTR(""Path"", @PrefixLength)
                    ")
                        .SetParameterValue("@PrefixLength", largest_prefix.Length + 1)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);
                }
            }

            // Cross-os path remapping support
            if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                // For Win paths on Linux
                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = REPLACE(""TargetPath"", '\', '/')
                ", token)
                    .ConfigureAwait(false);
            else if (OperatingSystem.IsWindows() && dirsep == "/")
                // For Linux paths on Windows
                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = REPLACE(REPLACE(""TargetPath"", '\', '_'), '/', '\')
                ", token)
                    .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(destination))
            {
                // Paths are now relative with target-os naming system
                // so we prefix them with the target path
                await cmd.SetCommandAndParameters($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = @Destination || ""TargetPath""
                ")
                    .SetParameterValue("@Destination", Util.AppendDirSeparator(destination))
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
            }

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds missing blocks in the temporary block table.
        /// </summary>
        /// <param name="skipMetadata">If true, skips metadata blocks when searching for missing blocks.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the missing blocks have been found and inserted into the temporary block table.</returns>
        public async Task FindMissingBlocks(bool skipMetadata, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);
            var p1 = await cmd.ExecuteNonQueryAsync($@"
                INSERT INTO ""{m_tempblocktable}"" (
                    ""FileID"",
                    ""Index"",
                    ""Hash"",
                    ""Size"",
                    ""Restored"",
                    ""Metadata"",
                    ""VolumeId"",
                    ""BlockId""
                )
                SELECT DISTINCT
                    ""{m_tempfiletable}"".""ID"",
                    ""BlocksetEntry"".""Index"",
                    ""Block"".""Hash"",
                    ""Block"".""Size"",
                    0,
                    0,
                    ""Block"".""VolumeID"",
                    ""Block"".""ID""
                FROM
                    ""{m_tempfiletable}"",
                    ""BlocksetEntry"",
                    ""Block""
                WHERE
                    ""{m_tempfiletable}"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                    AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
            ", token)
                .ConfigureAwait(false);

            var p2 = 0;
            if (!skipMetadata)
                p2 = await cmd.ExecuteNonQueryAsync($@"
                    INSERT INTO ""{m_tempblocktable}"" (
                        ""FileID"",
                        ""Index"",
                        ""Hash"",
                        ""Size"",
                        ""Restored"",
                        ""Metadata"",
                        ""VolumeId"",
                        ""BlockId""
                    )
                    SELECT DISTINCT
                        ""{m_tempfiletable}"".""ID"",
                        ""BlocksetEntry"".""Index"",
                        ""Block"".""Hash"",
                        ""Block"".""Size"",
                        0,
                        1,
                        ""Block"".""VolumeID"",
                        ""Block"".""ID""
                    FROM
                        ""{m_tempfiletable}"",
                        ""BlocksetEntry"",
                        ""Block"",
                        ""Metadataset""
                    WHERE
                        ""{m_tempfiletable}"".""MetadataID"" = ""Metadataset"".""ID""
                        AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                        AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                ", token)
                    .ConfigureAwait(false);

            // Creating indexes after insertion is much faster
            await cmd.ExecuteNonQueryAsync($@"
                CREATE INDEX ""{m_tempblocktable}_HashSizeIndex""
                ON ""{m_tempblocktable}"" (""Hash"", ""Size"")
            ", token)
                .ConfigureAwait(false);

            // Better suited to speed up commit on UpdateBlocks
            await cmd.ExecuteNonQueryAsync($@"
                CREATE INDEX ""{m_tempblocktable}_FileIdIndexIndex""
                ON ""{m_tempblocktable}"" (""FileId"", ""Index"")
            ", token)
                .ConfigureAwait(false);

            var size = await cmd.ExecuteScalarInt64Async($@"
                SELECT SUM(""Size"")
                FROM ""{m_tempblocktable}""
            ", 0, token)
                .ConfigureAwait(false);

            Logging.Log.WriteVerboseMessage(LOGTAG, "RestoreSourceSize", "Restore list contains {0} blocks with a total size of {1}", p1 + p2, Library.Utility.Utility.FormatSizeString(size));

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the target path of a file in the temporary file table.
        /// </summary>
        /// <param name="ID">The ID of the file in the temporary file table.</param>
        /// <param name="newname">The new target path to set for the file.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the target path has been updated.</returns>
        public async Task UpdateTargetPath(long ID, string newname, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                UPDATE ""{m_tempfiletable}""
                SET ""TargetPath"" = @TargetPath
                WHERE ""ID"" = @ID
            ");
            await cmd
                .SetTransaction(m_rtr)
                .SetParameterValue("@TargetPath", newname)
                .SetParameterValue("@ID", ID)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Interface for an object describing a block in an existing file.
        /// </summary>
        public interface IExistingFileBlock
        {
            /// <summary>
            /// Gets the hash of the block.
            /// </summary>
            string Hash { get; }
            /// <summary>
            /// Gets the index of the block within the file.
            /// </summary>
            long Index { get; }
            /// <summary>
            /// Gets the size of the block in bytes.
            /// </summary>
            long Size { get; }
        }

        /// <summary>
        /// Interface for an existing file with its blocks.
        /// </summary>
        public interface IExistingFile
        {
            /// <summary>
            /// Gets the target path of the file.
            /// </summary>
            string TargetPath { get; }
            /// <summary>
            /// Gets the hash of the file.
            /// </summary>
            string TargetHash { get; }
            /// <summary>
            /// Gets the ID of the file in the database.
            /// </summary>
            long TargetFileID { get; }
            /// <summary>
            /// Gets the length of the file in bytes.
            /// </summary>
            long Length { get; }
            /// <summary>
            /// An asynchronous enumerable of the blocks in the file.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            IAsyncEnumerable<IExistingFileBlock> Blocks(CancellationToken token);
        }

        /// <summary>
        /// Interface for a block source, which can be a file or a metadata source.
        /// </summary>
        public interface IBlockSource
        {
            /// <summary>
            /// Gets the path of the block source.
            /// </summary>
            string Path { get; }
            /// <summary>
            /// Gets the offset of the block within the source.
            /// </summary>
            long Offset { get; }
            /// <summary>
            /// Indicates whether the block source is metadata.
            /// </summary>
            bool IsMetadata { get; }
        }

        /// <summary>
        /// Interface for a block descriptor, which describes a block in a file.
        /// </summary>
        public interface IBlockDescriptor
        {
            /// <summary>
            /// Gets the hash of the block.
            /// </summary>
            string Hash { get; }
            /// <summary>
            /// Gets the size of the block in bytes.
            /// </summary>
            long Size { get; }
            /// <summary>
            /// Gets the offset of the block within the file.
            /// </summary>
            long Offset { get; }
            /// <summary>
            /// Gets the index of the block within the file.
            /// </summary>
            long Index { get; }
            /// <summary>
            /// Indicates whether the block is metadata.
            /// </summary>
            bool IsMetadata { get; }
            /// <summary>
            /// An asynchronous enumerable of block sources for this block descriptor.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            IAsyncEnumerable<IBlockSource> BlockSources(CancellationToken token);
        }

        /// <summary>
        /// Interface for a local block source, which provides information about a file and its blocks.
        /// </summary>
        public interface ILocalBlockSource
        {
            /// <summary>
            /// Gets the target path of the file.
            /// </summary>
            string TargetPath { get; }
            /// <summary>
            /// Gets the ID of the target file in the database.
            /// </summary>
            long TargetFileID { get; }
            /// <summary>
            /// An asynchronous enumerable of block descriptors for the file.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            IAsyncEnumerable<IBlockDescriptor> Blocks(CancellationToken token);
        }

        /// <summary>
        /// Interface for an object describing a file to restore.
        /// </summary>
        public interface IFileToRestore
        {
            /// <summary>
            /// Gets the target path of the file.
            /// </summary>
            string Path { get; }
            /// <summary>
            /// Gets the hash of the file.
            /// </summary>
            string Hash { get; }
            /// <summary>
            /// Gets the ID of the file in the database.
            /// </summary>
            long Length { get; }
        }

        /// <summary>
        /// Interface for a remote volume, which contains blocks to be restored.
        /// </summary>
        public interface IPatchBlock
        {
            /// <summary>
            /// Gets the hash of the block.
            /// </summary>
            long Offset { get; }
            /// <summary>
            /// Gets the size of the block in bytes.
            /// </summary>
            long Size { get; }
            /// <summary>
            /// Gets the path of the block source.
            /// </summary>
            string Key { get; }
        }

        /// <summary>
        /// Interface for a remote volume that contains blocks to be restored.
        /// </summary>
        public interface IVolumePatch
        {
            /// <summary>
            /// Gets the name of the remote volume.
            /// </summary>
            string Path { get; }
            /// <summary>
            /// Gets the hash of the remote volume.
            /// </summary>
            long FileID { get; }
            /// <summary>
            /// Gets the size of the remote volume in bytes.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            IAsyncEnumerable<IPatchBlock> Blocks(CancellationToken token);
        }

        /// <summary>
        /// Implementation of the IExistingFile interface that retrieves existing files and their blocks from the database.
        /// </summary>
        private class ExistingFile : IExistingFile
        {
            /// <summary>
            /// The data reader used to read existing files from the database.
            /// </summary>
            private readonly SqliteDataReader m_reader;

            /// <summary>
            /// Initializes a new instance of the ExistingFile class.
            /// </summary>
            /// <param name="rd">The SqliteDataReader used to read existing files.</param>
            public ExistingFile(SqliteDataReader rd)
            {
                m_reader = rd;
                HasMore = true;
            }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public string TargetHash { get { return m_reader.ConvertValueToString(1) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(2); } }
            public long Length { get { return m_reader.ConvertValueToInt64(3); } }

            /// <summary>
            /// Indicates whether there are more blocks to read for the current file.
            /// </summary>
            public bool HasMore { get; private set; }

            /// <summary>
            /// Represents a block in an existing file.
            /// </summary>
            private class ExistingFileBlock : IExistingFileBlock
            {
                /// <summary>
                /// The data reader used to read block information for existing files.
                /// </summary>
                private readonly SqliteDataReader m_reader;

                /// <summary>
                /// Initializes a new instance of the ExistingFileBlock class.
                /// </summary>
                public ExistingFileBlock(SqliteDataReader rd)
                {
                    m_reader = rd;
                }

                public string Hash { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                public long Index { get { return m_reader.ConvertValueToInt64(5); } }
                public long Size { get { return m_reader.ConvertValueToInt64(6); } }
            }

            /// <summary>
            /// Gets the blocks in the existing file.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IExistingFileBlock objects representing the blocks in the file.</returns>
            public async IAsyncEnumerable<IExistingFileBlock> Blocks([EnumeratorCancellation] CancellationToken token)
            {
                string p = TargetPath;
                while (HasMore && p == TargetPath)
                {
                    yield return new ExistingFileBlock(m_reader);
                    HasMore = await m_reader.ReadAsync(token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Retrieves existing files with their blocks from the database.
            /// </summary>
            /// <param name="db">The LocalDatabase instance to query.</param>
            /// <param name="tablename">The name of the table containing existing files.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IExistingFile objects representing the existing files with their blocks.</returns>
            public static async IAsyncEnumerable<IExistingFile> GetExistingFilesWithBlocks(LocalDatabase db, string tablename, [EnumeratorCancellation] CancellationToken token)
            {
                await using var cmd = db.Connection.CreateCommand($@"
                    SELECT
                        ""{tablename}"".""TargetPath"",
                        ""Blockset"".""FullHash"",
                        ""{tablename}"".""ID"",
                        ""Blockset"".""Length"",
                        ""Block"".""Hash"",
                        ""BlocksetEntry"".""Index"",
                        ""Block"".""Size""
                    FROM
                        ""{tablename}"",
                        ""Blockset"",
                        ""BlocksetEntry"",
                        ""Block""
                    WHERE
                        ""{tablename}"".""BlocksetID"" = ""Blockset"".""ID""
                        AND ""BlocksetEntry"".""BlocksetID"" = ""{tablename}"".""BlocksetID""
                        AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    ORDER BY
                        ""{tablename}"".""TargetPath"",
                        ""BlocksetEntry"".""Index""
                ")
                    .SetTransaction(db.Transaction);
                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var more = true;
                    while (more)
                    {
                        var f = new ExistingFile(rd);
                        string current = f.TargetPath;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.TargetPath)
                            more = await rd.ReadAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves existing files with their blocks from the temporary file table.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of IExistingFile objects representing the existing files with their blocks.</returns>
        public IAsyncEnumerable<IExistingFile> GetExistingFilesWithBlocks(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return ExistingFile.GetExistingFilesWithBlocks(this, m_tempfiletable, token);
        }

        /// <summary>
        /// Implements the ILocalBlockSource interface to provide access to local block sources.
        /// </summary>
        private class LocalBlockSource : ILocalBlockSource
        {
            /// <summary>
            /// Implements the IBlockDescriptor interface to provide access to block descriptors.
            /// </summary>
            private class BlockDescriptor : IBlockDescriptor
            {
                /// <summary>
                /// Implements the IBlockSource interface to provide access to block sources.
                /// </summary>
                private class BlockSource : IBlockSource
                {
                    /// <summary>
                    /// The data reader used to read block source information.
                    /// </summary>
                    private readonly SqliteDataReader m_reader;

                    /// <summary>
                    /// Initializes a new instance of the <see cref="BlockSource"/> class.
                    /// </summary>
                    /// <param name="rd">The SqliteDataReader used to read block source information.</param>
                    public BlockSource(SqliteDataReader rd)
                    {
                        m_reader = rd;
                    }

                    public string Path { get { return m_reader.ConvertValueToString(6) ?? ""; } }
                    public long Offset { get { return m_reader.ConvertValueToInt64(7); } }
                    public bool IsMetadata { get { return false; } }
                }

                /// <summary>
                /// The data reader used to read block descriptor information.
                /// </summary>
                private readonly SqliteDataReader m_reader;

                /// <summary>
                /// Initializes a new instance of the <see cref="BlockDescriptor"/> class.
                /// </summary>
                /// <param name="rd">The SqliteDataReader used to read block descriptor information.</param>
                public BlockDescriptor(SqliteDataReader rd)
                {
                    m_reader = rd;
                    HasMore = true;
                }

                /// <summary>
                /// Gets the target path of the block source.
                /// </summary>
                private string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }

                public string Hash { get { return m_reader.ConvertValueToString(2) ?? ""; } }
                public long Offset { get { return m_reader.ConvertValueToInt64(3); } }
                public long Index { get { return m_reader.ConvertValueToInt64(4); } }
                public long Size { get { return m_reader.ConvertValueToInt64(5); } }
                public bool IsMetadata { get { return !(m_reader.ConvertValueToInt64(9) == 0); } }

                /// <summary>
                /// Indicates whether there are more block sources to read for the current block descriptor.
                /// </summary>
                public bool HasMore { get; private set; }

                /// <inheritdoc/>
                public async IAsyncEnumerable<IBlockSource> BlockSources([EnumeratorCancellation] CancellationToken token)
                {
                    var p = TargetPath;
                    var h = Hash;
                    var s = Size;

                    while (HasMore && p == TargetPath && h == Hash && s == Size)
                    {
                        yield return new BlockSource(m_reader);
                        HasMore = await m_reader.ReadAsync(token).ConfigureAwait(false);
                    }
                }

            }

            /// <summary>
            /// The data reader used to read local block source information.
            /// </summary>
            private readonly SqliteDataReader m_reader;

            /// <summary>
            /// Initializes a new instance of the <see cref="LocalBlockSource"/> class.
            /// </summary>
            /// <param name="rd">The SqliteDataReader used to read local block source information.</param>
            public LocalBlockSource(SqliteDataReader rd)
            {
                m_reader = rd;
                HasMore = true;
            }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(1); } }

            /// <summary>
            /// Indicates whether there are more blocks to read for the current file.
            /// </summary>
            public bool HasMore { get; private set; }

            /// <inheritdoc/>
            public async IAsyncEnumerable<IBlockDescriptor> Blocks([EnumeratorCancellation] CancellationToken token)
            {
                var p = TargetPath;
                while (HasMore && p == TargetPath)
                {
                    var c = new BlockDescriptor(m_reader);
                    var h = c.Hash;
                    var s = c.Size;

                    yield return c;

                    HasMore = c.HasMore;
                    while (HasMore && c.Hash == h && c.Size == s && TargetPath == p)
                        HasMore = await m_reader.ReadAsync(token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Retrieves files and their source blocks from the database.
            /// </summary>
            /// <param name="db">The LocalDatabase instance to query.</param>
            /// <param name="filetablename">The name of the file table to query.</param>
            /// <param name="blocktablename">The name of the block table to query.</param>
            /// <param name="blocksize">The size of each block in bytes.</param>
            /// <param name="skipMetadata">If true, skips metadata blocks when retrieving files and source blocks.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of ILocalBlockSource objects representing the files and their source blocks.</returns>
            public static async IAsyncEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(LocalDatabase db, string filetablename, string blocktablename, long blocksize, bool skipMetadata, [EnumeratorCancellation] CancellationToken token)
            {
                // TODO: Skip metadata as required
                // Have to order by target path and hash, to ensure BlockDescriptor and BlockSource match adjacent rows
                await using var cmd = db.Connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""A"".""TargetPath"",
                        ""A"".""ID"",
                        ""B"".""Hash"",
                        (""B"".""Index"" * {blocksize}),
                        ""B"".""Index"",
                        ""B"".""Size"",
                        ""C"".""Path"",
                        (""D"".""Index"" * {blocksize}),
                        ""E"".""Size"",
                        ""B"".""Metadata""
                    FROM
                        ""{filetablename}"" ""A"",
                        ""{blocktablename}"" ""B"",
                        ""File"" ""C"",
                        ""BlocksetEntry"" ""D"",
                        ""Block"" ""E""
                    WHERE
                        ""A"".""ID"" = ""B"".""FileID""
                        AND ""C"".""BlocksetID"" = ""D"".""BlocksetID""
                        AND ""D"".""BlockID"" = ""E"".""ID""
                        AND ""B"".""Hash"" = ""E"".""Hash""
                        AND ""B"".""Size"" = ""E"".""Size""
                        AND ""B"".""Restored"" = 0
                    ORDER BY
                        ""A"".""TargetPath"",
                        ""B"".""Index""
                ")
                    .SetTransaction(db.Transaction);
                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                var more = await rd.ReadAsync(token).ConfigureAwait(false);

                while (more)
                {
                    var f = new LocalBlockSource(rd);
                    string current = f.TargetPath;
                    yield return f;

                    more = f.HasMore;
                    while (more && current == f.TargetPath)
                        more = await rd.ReadAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Retrieves files and their source blocks from the temporary file and block tables.
        /// </summary>
        /// <param name="skipMetadata">If true, skips metadata blocks when retrieving files and source blocks.</param>
        /// <param name="blocksize">The size of each block in bytes.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of ILocalBlockSource objects representing the files and their source blocks.</returns>
        public IAsyncEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(bool skipMetadata, long blocksize, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return LocalBlockSource.GetFilesAndSourceBlocks(this, m_tempfiletable, m_tempblocktable, blocksize, skipMetadata, token);
        }

        /// <summary>
        /// Retrieves the missing remote volumes that need to be restored.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of IRemoteVolume objects representing the missing remote volumes.</returns>
        public async IAsyncEnumerable<IRemoteVolume> GetMissingVolumes([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                SELECT
                    ""RV"".""Name"",
                    ""RV"".""Hash"",
                    ""RV"".""Size"",
                    ""BB"".""MaxIndex""
                FROM ""RemoteVolume"" ""RV""
                INNER JOIN (
                    SELECT
                        ""TB"".""VolumeID"",
                        MAX(""TB"".""Index"") as ""MaxIndex""
                    FROM ""{m_tempblocktable}"" ""TB""
                    WHERE ""TB"".""Restored"" = 0
                    GROUP BY  ""TB"".""VolumeID""
                ) as ""BB""
                    ON ""RV"".""ID"" = ""BB"".""VolumeID""
                ORDER BY ""BB"".""MaxIndex""
            ")
                .SetTransaction(m_rtr);

            // Return order from SQLite-DISTINCT is likely to be sorted by Name, which is bad for restore.
            // If the end of very large files (e.g. iso's) is restored before the beginning, most OS write out zeros to fill the file.
            // If we manage to get the volumes in an order restoring front blocks first, this can save time.
            // An optimal algorithm would build a dependency net with cycle resolution to find the best near topological
            // order of volumes, but this is a bit too fancy here.
            // We will just put a very simple heuristic to work, that will try to prefer volumes containing lower block indexes:
            // We just order all volumes by the maximum block index they contain. This query is slow, but should be worth the effort.
            // Now it is likely to restore all files from front to back. Large files will always be done last.
            // One could also use like the average block number in a volume, that needs to be measured.

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToInt64(2, -1)
                );
            }
        }

        /// <summary>
        /// Interface for retrieving files and metadata with missing blocks.
        /// </summary>
        public interface IFilesAndMetadata : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Retrieves files with missing blocks.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IVolumePatch objects representing the files with missing blocks.</returns>
            IAsyncEnumerable<IVolumePatch> FilesWithMissingBlocks(CancellationToken token);
            /// <summary>
            /// Retrieves metadata with missing blocks.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IVolumePatch objects representing the metadata with missing blocks.</returns>
            IAsyncEnumerable<IVolumePatch> MetadataWithMissingBlocks(CancellationToken token);
        }

        private class FilesAndMetadata : IFilesAndMetadata
        {
            private string m_tmptable = null!;
            private string m_filetablename = null!;
            private string m_blocktablename = null!;
            private long m_blocksize;

            private LocalDatabase m_db = null!;

            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public FilesAndMetadata(SqliteConnection connection, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
            {
                throw new NotImplementedException("Use CreateAsync instead of the constructor");
            }

            private FilesAndMetadata() { }

            /// <summary>
            /// Creates a new instance of FilesAndMetadata, initializing the temporary table and inserting blocks from the current volume.
            /// </summary>
            /// <param name="db">The LocalDatabase instance to use for database operations.</param>
            /// <param name="filetablename">The name of the file table in the database.</param>
            /// <param name="blocktablename">The name of the block table in the database.</param>
            /// <param name="blocksize">The size of each block in bytes.</param>
            /// <param name="curvolume">The BlockVolumeReader containing the current volume's blocks.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that when awaited returns an instance of <see cref="FilesAndMetadata"/>.</returns>
            public static async Task<FilesAndMetadata> CreateAsync(LocalDatabase db, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume, CancellationToken token)
            {
                var fam = new FilesAndMetadata()
                {
                    m_db = db,
                    m_filetablename = filetablename,
                    m_blocktablename = blocktablename,
                    m_blocksize = blocksize,
                };

                await using var c = db.Connection.CreateCommand()
                    .SetTransaction(db.Transaction);
                fam.m_tmptable = "VolumeFiles-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                await c.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{fam.m_tmptable}"" (
                        ""Hash"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL
                    )
                ", token)
                    .ConfigureAwait(false);

                c.SetCommandAndParameters($@"
                    INSERT INTO ""{fam.m_tmptable}"" (
                        ""Hash"",
                        ""Size""
                    )
                    VALUES (
                        @Hash,
                        @Size
                    )
                ");
                foreach (var s in curvolume.Blocks)
                {
                    await c.SetParameterValue("@Hash", s.Key)
                        .SetParameterValue("@Size", s.Value)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);
                }

                // The index _HashSizeIndex is not needed anymore. Index on "Blocks-..." is used on Join in GetMissingBlocks

                await db.Transaction.CommitAsync(token: token).ConfigureAwait(false);

                return fam;
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                if (m_tmptable != null)
                {
                    await using var c = m_db.Connection.CreateCommand(@$"DROP TABLE IF EXISTS ""{m_tmptable}""")
                        .SetTransaction(m_db.Transaction);
                    await c.ExecuteNonQueryAsync().ConfigureAwait(false);
                    await m_db.Transaction.CommitAsync().ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Implements the IVolumePatch interface to provide access to files with missing blocks.
            /// </summary>
            private class VolumePatch : IVolumePatch
            {
                /// <summary>
                /// Implements the IPatchBlock interface to provide access to blocks in a volume.
                /// </summary>
                private class PatchBlock : IPatchBlock
                {
                    /// <summary>
                    /// The data reader used to read patch block information.
                    /// </summary>
                    private readonly SqliteDataReader m_reader;
                    /// <summary>
                    /// Initializes a new instance of the <see cref="PatchBlock"/> class.
                    /// </summary>
                    public PatchBlock(SqliteDataReader rd)
                    {
                        m_reader = rd;
                    }

                    public long Offset { get { return m_reader.ConvertValueToInt64(2); } }
                    public long Size { get { return m_reader.ConvertValueToInt64(3); } }
                    public string Key { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                }

                /// <summary>
                /// The data reader used to read volume patch information.
                /// </summary>
                private readonly SqliteDataReader m_reader;
                /// <summary>
                /// Initializes a new instance of the <see cref="VolumePatch"/> class.
                /// </summary>
                public VolumePatch(SqliteDataReader rd)
                {
                    m_reader = rd;
                    HasMore = true;
                }

                public string Path { get { return m_reader.ConvertValueToString(0) ?? ""; } }
                public long FileID { get { return m_reader.ConvertValueToInt64(1); } }
                public bool HasMore { get; private set; }

                /// <inheritdoc/>
                public async IAsyncEnumerable<IPatchBlock> Blocks([EnumeratorCancellation] CancellationToken token)
                {
                    string p = Path;
                    while (HasMore && p == Path)
                    {
                        yield return new PatchBlock(m_reader);
                        HasMore = await m_reader.ReadAsync(token).ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Retrieves the files with missing blocks.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IVolumePatch objects representing the files with missing blocks.</returns>
            public async IAsyncEnumerable<IVolumePatch> FilesWithMissingBlocks([EnumeratorCancellation] CancellationToken token)
            {
                // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                await using var cmd = m_db.Connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""A"".""TargetPath"",
                        ""BB"".""FileID"",
                        (""BB"".""Index"" * {m_blocksize}),
                        ""BB"".""Size"",
                        ""BB"".""Hash""
                    FROM
                        ""{m_filetablename}"" ""A"",
                        ""{m_blocktablename}"" ""BB""
                    WHERE
                        ""A"".""ID"" = ""BB"".""FileID""
                        AND ""BB"".""Restored"" = 0
                        AND ""BB"".""Metadata"" = {"0"}
                        AND ""BB"".""ID"" IN (
                            SELECT ""B"".""ID""
                            FROM
                                ""{m_blocktablename}"" ""B"",
                                ""{m_tmptable}"" ""C""
                            WHERE
                                ""B"".""Hash"" = ""C"".""Hash""
                                AND ""B"".""Size"" = ""C"".""Size""
                        )
                    ORDER BY
                        ""A"".""TargetPath"",
                        ""BB"".""Index""
                ")
                    .SetTransaction(m_db.Transaction);

                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var more = true;
                    while (more)
                    {
                        var f = new VolumePatch(rd);
                        var current = f.Path;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.Path)
                            more = await rd.ReadAsync(token).ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Retrieves the metadata with missing blocks.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of IVolumePatch objects representing the metadata with missing blocks.</returns>
            public async IAsyncEnumerable<IVolumePatch> MetadataWithMissingBlocks([EnumeratorCancellation] CancellationToken token)
            {
                // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                await using var cmd = m_db.Connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""A"".""TargetPath"",
                        ""BB"".""FileID"",
                        (""BB"".""Index"" * {m_blocksize}),
                        ""BB"".""Size"",
                        ""BB"".""Hash""
                    FROM
                        ""{m_filetablename}"" ""A"",
                        ""{m_blocktablename}"" ""BB""
                    WHERE
                        ""A"".""ID"" = ""BB"".""FileID""
                        AND ""BB"".""Restored"" = 0
                        AND ""BB"".""Metadata"" = {"1"}
                        AND ""BB"".""ID"" IN (
                            SELECT ""B"".""ID""
                            FROM
                                ""{m_blocktablename}"" ""B"",
                                ""{m_tmptable}"" ""C""
                            WHERE
                                ""B"".""Hash"" = ""C"".""Hash""
                                AND ""B"".""Size"" = ""C"".""Size""
                        )
                    ORDER BY
                        ""A"".""TargetPath"",
                        ""BB"".""Index""
                ")
                    .SetTransaction(m_db.Transaction);

                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var more = true;
                    while (more)
                    {
                        var f = new VolumePatch(rd);
                        string current = f.Path;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.Path)
                            more = await rd.ReadAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves files and metadata with missing blocks for the given volume.
        /// </summary>
        /// <param name="blocksize"> The size of each block in bytes.</param>
        /// <param name="curvolume">The current volume reader containing the blocks to restore.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns an IFilesAndMetadata instance containing the files and metadata with missing blocks.</returns>
        public async Task<IFilesAndMetadata> GetMissingBlockData(BlockVolumeReader curvolume, long blocksize, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return await FilesAndMetadata.CreateAsync(this, m_tempfiletable, m_tempblocktable, blocksize, curvolume, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a connection and transaction from the connection pool.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns a tuple containing the SqliteConnection and ReusableTransaction.</returns>
        public async Task<(SqliteConnection, ReusableTransaction)> GetConnectionFromPool(CancellationToken token)
        {
            if (!m_connection_pool.TryTake(out var entry))
            {
                var connection = await SQLiteLoader.LoadConnectionAsync()
                    .ConfigureAwait(false);

                connection.ConnectionString = m_connection.ConnectionString + ";Cache=Shared";
                await connection.OpenAsync(token).ConfigureAwait(false);
                await SQLiteLoader.ApplyCustomPragmasAsync(connection)
                    .ConfigureAwait(false);

                var transaction = new ReusableTransaction(connection);

                return (connection, transaction);
            }

            return entry;
        }

        /// <summary>
        /// Represents a file to restore, containing its path, hash, and length.
        /// </summary>
        private class FileToRestore : IFileToRestore
        {
            public string Path { get; private set; }
            public string Hash { get; private set; }
            public long Length { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="FileToRestore"/> class.
            /// </summary>
            /// <param name="id">The ID of the file to restore.</param>
            /// <param name="path">The path of the file to restore.</param>
            /// <param name="hash">The hash of the file to restore.</param>
            /// <param name="length">The length of the file to restore in bytes.</param>
            public FileToRestore(long id, string path, string hash, long length)
            {
                Path = path;
                Hash = hash;
                Length = length;
            }
        }

        /// <summary>
        /// Returns a list of files to restore, including their target paths, hashes, and lengths.
        /// </summary>
        /// <param name="onlyNonVerified">If true, only returns files that have not been verified.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of IFileToRestore objects representing the files to restore.</returns>
        public async IAsyncEnumerable<IFileToRestore> GetFilesToRestore(bool onlyNonVerified, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                SELECT
                    ""{m_tempfiletable}"".""ID"",
                    ""{m_tempfiletable}"".""TargetPath"",
                    ""Blockset"".""FullHash"",
                    ""Blockset"".""Length""
                FROM
                    ""{m_tempfiletable}"",
                    ""Blockset""
                WHERE
                    ""{m_tempfiletable}"".""BlocksetID"" = ""Blockset"".""ID""
                    AND ""{m_tempfiletable}"".""DataVerified"" <= @Verified
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@Verified", !onlyNonVerified);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new FileToRestore(
                    rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "", rd.ConvertValueToString(2) ?? "", rd.ConvertValueToInt64(3));
        }

        /// <summary>
        /// Returns a list of files and symlinks to restore.
        /// </summary>
        /// <remarks>At its current state, this method is designed to be called by Duplicati.Library.Main.Operation.Restore.FileLister. It locks the database to ensure that calls from Duplicati.Library.Main.Operation.Restore.BlockManager do not interfere with each other.</remarks>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of FileRequest objects representing the files and symlinks to restore.</returns>
        public async IAsyncEnumerable<FileRequest> GetFilesAndSymlinksToRestore([EnumeratorCancellation] CancellationToken token)
        {
            await m_dbLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Order by length descending, so that larger files are restored first.
                await using var cmd = m_connection.CreateCommand($@"
                    SELECT
                        ""F"".""ID"",
                        ""F"".""Path"",
                        ""F"".""TargetPath"",
                        IFNULL(""B"".""FullHash"", ''),
                        IFNULL(""B"".""Length"", 0) AS ""Length"",
                        ""F"".""BlocksetID""
                    FROM ""{m_tempfiletable}"" ""F""
                    LEFT JOIN ""Blockset"" ""B""
                        ON ""F"".""BlocksetID"" = ""B"".""ID""
                    WHERE ""F"".""BlocksetID"" != {FOLDER_BLOCKSET_ID}
                    ORDER BY ""Length"" DESC
                ")
                    .SetTransaction(m_rtr);

                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                    yield return new FileRequest(
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1),
                        rd.ConvertValueToString(2),
                        rd.ConvertValueToString(3),
                        rd.ConvertValueToInt64(4),
                        rd.ConvertValueToInt64(5)
                    );
            }
            finally
            {
                m_dbLock.Release();
            }
        }

        /// <summary>
        /// Returns a list of folders to restore. Used to restore folder metadata.
        /// </summary>
        /// <remarks>At its current state, this method is designed to be called by Duplicati.Library.Main.Operation.Restore.FileLister. It locks the database to ensure that calls from Duplicati.Library.Main.Operation.Restore.BlockManager do not interfere with each other.</remarks>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of FileRequest objects representing the folders to restore.</returns>
        public async IAsyncEnumerable<FileRequest> GetFolderMetadataToRestore([EnumeratorCancellation] CancellationToken token)
        {
            await m_dbLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await using var cmd = m_connection.CreateCommand();
                cmd.SetTransaction(m_rtr);
                await using var rd = await cmd.ExecuteReaderAsync($@"
                SELECT
                    ""F"".""ID"",
                    '',
                    ""F"".""TargetPath"",
                    '',
                    0,
                    {FOLDER_BLOCKSET_ID}
                FROM ""{m_tempfiletable}"" ""F""
                WHERE
                    ""F"".""BlocksetID"" = {FOLDER_BLOCKSET_ID}
                    AND ""F"".""MetadataID"" IS NOT NULL
                    AND ""F"".""MetadataID"" >= 0
            ", token)
                    .ConfigureAwait(false);

                while (await rd.ReadAsync(token).ConfigureAwait(false))
                    yield return new FileRequest(
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1),
                        rd.ConvertValueToString(2),
                        rd.ConvertValueToString(3),
                        rd.ConvertValueToInt64(4),
                        rd.ConvertValueToInt64(5)
                    );
            }
            finally
            {
                m_dbLock.Release();
            }
        }

        /// <summary>
        /// Returns a list of blocks and their volume IDs. Used by the <see cref="BlockManager"/> to keep track of blocks and volumes to automatically evict them from the respective caches.
        /// </summary>
        /// <remarks>At its current state, this method is designed to be called by Duplicati.Library.Main.Operation.Restore.BlockManager. It locks the database to ensure that calls from Duplicati.Library.Main.Operation.Restore.FileLister do not interfere with each other.</remarks>
        /// <param name="skipMetadata">Flag indicating whether the returned blocks should exclude the metadata blocks.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of tuples containing the block ID and volume ID.</returns>
        public async IAsyncEnumerable<(long, long)> GetBlocksAndVolumeIDs(bool skipMetadata, [EnumeratorCancellation] CancellationToken token)
        {
            await m_dbLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                var metadata_query = skipMetadata ? "" : $@"
                    UNION ALL
                    SELECT
                        ""Block"".""ID"",
                        ""Block"".""VolumeID""
                    FROM ""{m_tempfiletable}""
                    INNER JOIN ""Metadataset""
                        ON ""{m_tempfiletable}"".""MetadataID"" = ""Metadataset"".""ID""
                    INNER JOIN ""BlocksetEntry""
                        ON ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                    INNER JOIN ""Block""
                        ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                ";

                await using var cmd = m_connection.CreateCommand($@"
                    SELECT
                        ""Block"".""ID"",
                        ""Block"".""VolumeID""
                    FROM ""BlocksetEntry""
                    INNER JOIN ""{m_tempfiletable}""
                        ON ""BlocksetEntry"".""BlocksetID"" = ""{m_tempfiletable}"".""BlocksetID""
                    INNER JOIN ""Block""
                        ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    {metadata_query}
                ")
                    .SetTransaction(m_rtr);

                await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    yield return (
                        reader.ConvertValueToInt64(0),
                        reader.ConvertValueToInt64(1)
                    );
            }
            finally
            {
                m_dbLock.Release();
            }
        }

        /// <summary>
        /// Returns a list of <see cref="BlockRequest"/> for the given blockset ID. It is used by the <see cref="FileProcessor"/> to restore the blocks of a file.
        /// </summary>
        /// <param name="blocksetID">The BlocksetID of the file.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of <see cref="BlockRequest"/> representing the blocks of the file.</returns>
        public async IAsyncEnumerable<BlockRequest> GetBlocksFromFile(long blocksetID, [EnumeratorCancellation] CancellationToken token)
        {
            var (connection, transaction) = await GetConnectionFromPool(token)
                .ConfigureAwait(false);

            try
            {
                await using var cmd = connection.CreateCommand(@$"
                    SELECT
                        ""Block"".""ID"",
                        ""Block"".""Hash"",
                        ""Block"".""Size"",
                        ""Block"".""VolumeID""
                    FROM ""BlocksetEntry""
                    INNER JOIN ""Block""
                        ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    WHERE ""BlocksetEntry"".""BlocksetID"" = @BlocksetID
                    ORDER BY ""BlocksetEntry"".""Index""
                ")
                    .SetTransaction(transaction)
                    .SetParameterValue("@BlocksetID", blocksetID);

                await using var reader = await cmd.ExecuteReaderAsync(token)
                    .ConfigureAwait(false);
                for (long i = 0; await reader.ReadAsync(token).ConfigureAwait(false); i++)
                    yield return new BlockRequest(
                        reader.ConvertValueToInt64(0),
                        i,
                        reader.ConvertValueToString(1),
                        reader.ConvertValueToInt64(2),
                        reader.ConvertValueToInt64(3),
                        false
                    );
            }
            finally
            {
                // Return the connection to the pool
                m_connection_pool.Add((connection, transaction));
            }
        }

        /// <summary>
        /// Returns a list of <see cref="BlockRequest"/> for the metadata blocks of the given file. It is used by the <see cref="FileProcessor"/> to restore the metadata of a file.
        /// </summary>
        /// <param name="fileID">The ID of the file.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of <see cref="BlockRequest"/> representing the metadata blocks of the file.</returns>
        public async IAsyncEnumerable<BlockRequest> GetMetadataBlocksFromFile(long fileID, [EnumeratorCancellation] CancellationToken token)
        {
            var (connection, transaction) = await GetConnectionFromPool(token)
                .ConfigureAwait(false);

            try
            {
                await using var cmd = connection.CreateCommand($@"
                    SELECT
                        ""Block"".""ID"",
                        ""Block"".""Hash"",
                        ""Block"".""Size"",
                        ""Block"".""VolumeID""
                    FROM ""File""
                    INNER JOIN ""Metadataset""
                        ON ""File"".""MetadataID"" = ""Metadataset"".""ID""
                    INNER JOIN ""BlocksetEntry""
                        ON ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                    INNER JOIN ""Block""
                        ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    WHERE ""File"".""ID"" = @FileID
                ")
                    .SetTransaction(transaction)
                    .SetParameterValue("@FileID", fileID);

                await using var reader = await cmd.ExecuteReaderAsync(token)
                    .ConfigureAwait(false);
                for (long i = 0; await reader.ReadAsync(token).ConfigureAwait(false); i++)
                {
                    yield return new BlockRequest(
                        reader.ConvertValueToInt64(0),
                        i,
                        reader.ConvertValueToString(1),
                        reader.ConvertValueToInt64(2),
                        reader.ConvertValueToInt64(3),
                        false
                    );
                }
            }
            finally
            {
                // Return the connection to the pool
                m_connection_pool.Add((connection, transaction));
            }
        }

        /// <summary>
        /// Returns the volume information for the given volume ID. It is used by the <see cref="VolumeManager"/> to get the volume information for the given volume ID.
        /// </summary>
        /// <param name="VolumeID">The ID of the volume.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of tuples containing the volume name, size, and hash.</returns>
        public async IAsyncEnumerable<(string, long, string)> GetVolumeInfo(long VolumeID, [EnumeratorCancellation] CancellationToken token)
        {
            var (connection, transaction) = await GetConnectionFromPool(token)
                .ConfigureAwait(false);

            try
            {
                await using var cmd = connection.CreateCommand(@"
                    SELECT
                        ""Name"",
                        ""Size"",
                        ""Hash""
                    FROM ""RemoteVolume""
                    WHERE ""ID"" = @VolumeID
                ")
                    .SetTransaction(transaction)
                    .SetParameterValue("@VolumeID", VolumeID);

                await using var reader = await cmd.ExecuteReaderAsync(token)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    yield return (
                        reader.ConvertValueToString(0) ?? "",
                        reader.ConvertValueToInt64(1),
                        reader.ConvertValueToString(2) ?? ""
                    );
            }
            finally
            {
                m_connection_pool.Add((connection, transaction));
            }
        }

        /// <summary>
        /// Drops all temporary tables used for the restore process and commits the transaction.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the temporary tables are dropped and the transaction is committed.</returns>
        public async Task DropRestoreTable(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            if (m_tempfiletable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempfiletable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_tempfiletable = null; }

            if (m_tempblocktable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempblocktable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_tempblocktable = null; }

            if (m_latestblocktable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_latestblocktable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_latestblocktable = null; }

            if (m_fileprogtable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_fileprogtable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_fileprogtable = null; }

            if (m_totalprogtable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_totalprogtable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_totalprogtable = null; }

            if (m_filesnewlydonetable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}""", token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_filesnewlydonetable = null; }

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Interface for marking blocks as restored, missing, or verified.
        /// </summary>
        public interface IBlockMarker : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Marks a specific block as restored.
            /// </summary>
            /// <param name="targetfileid">The ID of the target file.</param>
            /// <param name="index">The index of the block.</param>
            /// <param name="hash">The hash of the block.</param>
            /// <param name="blocksize">The size of the block.</param>
            /// <param name="metadata">Indicates whether the block is metadata.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when the block is marked as restored.</returns>
            Task SetBlockRestored(long targetfileid, long index, string hash, long blocksize, bool metadata, CancellationToken token);

            /// <summary>
            /// Marks all blocks of a specific file as missing.
            /// </summary>
            /// <param name="targetfileid">The ID of the target file.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when all blocks of the file are marked as missing.</returns>
            Task SetAllBlocksMissing(long targetfileid, CancellationToken token);

            /// <summary>
            /// Marks all blocks of a specific file as restored.
            /// </summary>
            /// <param name="targetfileid">The ID of the target file.</param>
            /// <param name="includeMetadata">Indicates whether to include metadata blocks.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when all blocks of the file are marked as restored.</returns>
            Task SetAllBlocksRestored(long targetfileid, bool includeMetadata, CancellationToken token);

            /// <summary>
            /// Marks the data of a specific file as verified.
            /// </summary>
            /// <param name="targetfileid">The ID of the target file.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when the file data is marked as verified.</returns>
            Task SetFileDataVerified(long targetfileid, CancellationToken token);

            /// <summary>
            /// Commits the changes made by the block marker.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when the changes are committed.</returns>
            Task CommitAsync(CancellationToken token);

            /// <summary>
            /// Updates the processed statistics for the blocks and files.
            /// </summary>
            /// <param name="writer">An operation progress updater to report the processed statistics.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that completes when the processed statistics are updated.</returns>
            Task UpdateProcessed(IOperationProgressUpdater writer, CancellationToken token);
        }

        /// <summary>
        /// A new implementation of IBlockMarker, marking the blocks directly in the blocks table as restored
        /// and reading statistics about progress from DB (kept up-to-date by triggers).
        /// There is no negative influence on performance, esp. since the block table is temporary anyway.
        /// </summary>
        private class DirectBlockMarker : IBlockMarker
        {
            /// <summary>
            /// Updates the "Restored" status to 1 for a specific block in the block table, matching by file ID, index, hash, size, and metadata, but only if it is currently not restored.
            /// </summary>
            private SqliteCommand m_insertblockCommand = null!;

            /// <summary>
            /// Resets the "Restored" status to 0 for all blocks belonging to a specific file in the block table.
            /// </summary>
            private SqliteCommand m_resetfileCommand = null!;

            /// <summary>
            /// Updates the "Restored" status to 1 for all blocks of a file in the block table, optionally including metadata blocks depending on the parameter.
            /// </summary>
            private SqliteCommand m_updateAsRestoredCommand = null!;

            /// <summary>
            /// Marks a file as data verified by setting the "DataVerified" field to 1 in the file table for a specific file ID.
            /// </summary>
            private SqliteCommand m_updateFileAsDataVerifiedCommand = null!;

            /// <summary>
            /// Retrieves the number of fully restored files and the total restored size from the progress/statistics table, or, if unavailable, counts restored blocks and their size directly from the block table.
            /// </summary>
            private SqliteCommand m_statUpdateCommand = null!;

            /// <summary>
            /// The database instance used for executing commands.
            /// </summary>
            private LocalDatabase m_db = null!;

            /// <summary>
            /// Indicates whether there are updates to be processed.
            /// </summary>
            private bool m_hasUpdates = false;

            /// <summary>
            /// The name of the temporary block table used for marking blocks as restored.
            /// </summary>
            private string m_blocktablename = null!;
            /// <summary>
            /// The name of the temporary file table used for marking files as data verified.
            /// </summary>
            private string m_filetablename = null!;

            /// <summary>
            /// Calling this constructor will throw an exception. Use CreateAsync instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public DirectBlockMarker(SqliteConnection connection, string blocktablename, string filetablename, string statstablename)
            {
                throw new NotImplementedException("Use CreateAsync instead of the constructor");
            }

            /// <summary>
            /// Private constructor to prevent direct instantiation. Use CreateAsync to create an instance.
            /// </summary>
            private DirectBlockMarker() { }

            /// <summary>
            /// Creates a new instance of the <see cref="DirectBlockMarker"/> class asynchronously.
            /// </summary>
            /// <param name="db">The local database instance to use for executing commands.</param>
            /// <param name="blocktablename">The name of the temporary block table.</param>
            /// <param name="filetablename">The name of the temporary file table.</param>
            /// <param name="statstablename">The name of the statistics table, or null if not available.</param>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that when awaited returns a new instance of the <see cref="DirectBlockMarker"/> class.</returns>
            public static async Task<DirectBlockMarker> CreateAsync(LocalDatabase db, string blocktablename, string filetablename, string statstablename, CancellationToken token)
            {
                var dbm = new DirectBlockMarker()
                {
                    m_db = db,
                    m_blocktablename = blocktablename,
                    m_filetablename = filetablename,

                    m_insertblockCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{blocktablename}"" SET ""Restored"" = 1
                        WHERE
                            ""FileID"" = @TargetFileId
                            AND ""Index"" = @Index
                            AND ""Hash"" = @Hash
                            AND ""Size"" = @Size
                            AND ""Metadata"" = @Metadata
                            AND ""Restored"" = 0
                    ", token)
                        .ConfigureAwait(false),

                    m_resetfileCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{blocktablename}""
                        SET ""Restored"" = 0
                        WHERE ""FileID"" = @TargetFileId
                    ", token)
                        .ConfigureAwait(false),

                    m_updateAsRestoredCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{blocktablename}""
                        SET ""Restored"" = 1
                        WHERE
                            ""FileID"" = @TargetFileId
                            AND ""Metadata"" <= @Metadata
                    ", token)
                        .ConfigureAwait(false),

                    m_updateFileAsDataVerifiedCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{filetablename}""
                        SET ""DataVerified"" = 1
                        WHERE ""ID"" = @TargetFileId
                    ", token)
                        .ConfigureAwait(false),

                    m_statUpdateCommand = statstablename == null ?
                        // very slow fallback if stats tables were not created
                        await db.Connection.CreateCommandAsync($@"
                            SELECT
                                COUNT(DISTINCT ""FileID""),
                                SUM(""Size"")
                            FROM ""{blocktablename}""
                            WHERE ""Restored"" = 1
                        ", token)
                            .ConfigureAwait(false)
                        :
                        // Fields in Stats: TotalFiles, TotalBlocks, TotalSize
                        //                  FilesFullyRestored, FilesPartiallyRestored, BlocksRestored, SizeRestored
                        await db.Connection.CreateCommandAsync($@"
                            SELECT
                                SUM(""FilesFullyRestored""),
                                SUM(""SizeRestored"")
                            FROM ""{statstablename}""
                        ", token)
                            .ConfigureAwait(false)
                };

                return dbm;
            }

            /// <inheritdoc/>
            public async Task UpdateProcessed(IOperationProgressUpdater updater, CancellationToken token)
            {
                if (!m_hasUpdates)
                    return;

                m_hasUpdates = false;
                await using var rd = await m_statUpdateCommand
                    .SetTransaction(m_db.Transaction)
                    .ExecuteReaderAsync(token)
                    .ConfigureAwait(false);

                var filesprocessed = 0L;
                var processedsize = 0L;

                if (rd.Read())
                {
                    filesprocessed += rd.ConvertValueToInt64(0, 0);
                    processedsize += rd.ConvertValueToInt64(1, 0);
                }

                updater.UpdatefilesProcessed(filesprocessed, processedsize);
            }

            // <inheritdoc/>
            public async Task SetAllBlocksMissing(long targetfileid, CancellationToken token)
            {
                m_hasUpdates = true;
                m_resetfileCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid);

                var r = await m_resetfileCommand.ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            /// <inheritdoc/>
            public async Task SetAllBlocksRestored(long targetfileid, bool includeMetadata, CancellationToken token)
            {
                m_hasUpdates = true;
                m_updateAsRestoredCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Metadata", includeMetadata ? 1 : 0);

                var r = await m_updateAsRestoredCommand
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            /// <inheritdoc/>
            public async Task SetFileDataVerified(long targetfileid, CancellationToken token)
            {
                m_hasUpdates = true;
                m_updateFileAsDataVerifiedCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid);

                var r = await m_updateFileAsDataVerifiedCommand
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
                if (r != 1)
                    throw new Exception("Unexpected result when marking file as verified.");
            }

            /// <inheritdoc/>
            public async Task SetBlockRestored(long targetfileid, long index, string hash, long size, bool metadata, CancellationToken token)
            {
                m_hasUpdates = true;
                m_insertblockCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Index", index)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@Metadata", metadata);

                var r = await m_insertblockCommand
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                if (r != 1)
                    throw new Exception("Unexpected result when marking block.");
            }

            /// <inheritdoc/>
            public async Task CommitAsync(CancellationToken token)
            {
                using (new Logging.Timer(LOGTAG, "CommitBlockMarker", "CommitBlockMarker"))
                    await m_db.Transaction.CommitAsync(token: token).ConfigureAwait(false);
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                await m_insertblockCommand.DisposeAsync().ConfigureAwait(false);
                await m_resetfileCommand.DisposeAsync().ConfigureAwait(false);
                await m_updateAsRestoredCommand.DisposeAsync().ConfigureAwait(false);
                await m_updateFileAsDataVerifiedCommand.DisposeAsync().ConfigureAwait(false);
                await m_statUpdateCommand.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a new block marker for marking blocks as restored, missing, or verified.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns an instance of <see cref="IBlockMarker"/>.</returns>
        public async Task<IBlockMarker> CreateBlockMarkerAsync(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            if (string.IsNullOrWhiteSpace(m_totalprogtable))
                throw new InvalidOperationException("No progress table set up for this restore.");
            return await DirectBlockMarker.CreateAsync(this, m_tempblocktable, m_tempfiletable, m_totalprogtable, token)
                .ConfigureAwait(false);
        }

        public override void Dispose()
        {
            DisposeAsync().AsTask().Await();
        }

        public override async ValueTask DisposeAsync()
        {
            await DisposePoolAsync().ConfigureAwait(false);
            await DropRestoreTable(default).ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes all connections and their transactions in the connection pool.
        /// </summary>
        /// <returns>A task that completes when all connections and transactions are disposed.</returns>
        public async Task DisposePoolAsync()
        {
            foreach (var (connection, transaction) in m_connection_pool)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                await connection.CloseAsync().ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            m_connection_pool.Clear();
        }

        /// <summary>
        /// Returns a list of target folders for the folder blockset.
        /// </summary>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of strings representing the target folders.</returns>
        public async IAsyncEnumerable<string> GetTargetFolders([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand($@"
                SELECT ""TargetPath""
                FROM ""{m_tempfiletable}""
                WHERE ""BlocksetID"" == @BlocksetID
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetID", FOLDER_BLOCKSET_ID);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return rd.ConvertValueToString(0) ?? "";
        }

        /// <summary>
        /// Returns a list of files and their source blocks for fast restoration.
        /// </summary>
        public interface IFastSource
        {
            /// <summary>
            /// The target path of the file to restore.
            /// </summary>
            string TargetPath { get; }
            /// <summary>
            /// The ID of the target file.
            /// </summary>
            long TargetFileID { get; }
            /// <summary>
            /// The source path of the file to restore.
            /// </summary>
            string SourcePath { get; }
            /// <summary>
            /// Gets an asynchronous enumerable of block entries for the file.
            /// </summary>
            /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
            /// <returns>An asynchronous enumerable of <see cref="IBlockEntry"/> representing the blocks of the file.</returns>
            IAsyncEnumerable<IBlockEntry> Blocks(CancellationToken token);
        }

        /// <summary>
        /// Interface representing a block entry in the fast source.
        /// </summary>
        public interface IBlockEntry
        {
            /// <summary>
            /// The offset of the block in the source file.
            /// </summary>
            long Offset { get; }
            /// <summary>
            /// The size of the block.
            /// </summary>
            long Size { get; }
            /// <summary>
            /// The index of the block in the source file.
            /// </summary>
            long Index { get; }
            /// <summary>
            /// The hash of the block.
            /// </summary>
            string Hash { get; }
        }

        /// <summary>
        /// Implementation of <see cref="IFastSource"/> that provides access to file and block information for fast restoration.
        /// </summary>
        private class FastSource : IFastSource
        {
            /// <summary>
            /// Implementation of <see cref="IBlockEntry"/> that provides access to block information.
            /// </summary>
            private class BlockEntry : IBlockEntry
            {
                /// <summary>
                /// The SqliteDataReader used to read block data.
                /// </summary>
                private readonly SqliteDataReader m_rd;
                /// <summary>
                /// The block size used to calculate the offset.
                /// </summary>
                private readonly long m_blocksize;

                /// <summary>
                /// Initializes a new instance of the <see cref="BlockEntry"/> class with the specified SqliteDataReader and block size.
                /// </summary>
                /// <param name="rd">The SqliteDataReader used to read block data.</param>
                /// <param name="blocksize">The block size used to calculate the offset.</param>
                public BlockEntry(SqliteDataReader rd, long blocksize)
                {
                    m_rd = rd;
                    m_blocksize = blocksize;
                }

                public long Offset { get { return m_rd.ConvertValueToInt64(3) * m_blocksize; } }
                public long Index { get { return m_rd.ConvertValueToInt64(3); } }
                public long Size { get { return m_rd.ConvertValueToInt64(5); } }
                public string Hash { get { return m_rd.ConvertValueToString(4) ?? ""; } }
            }

            /// <summary>
            /// The SqliteDataReader used to read file and block data.
            /// </summary>
            private readonly SqliteDataReader m_rd;
            /// <summary>
            /// The block size used to calculate the offset of blocks.
            /// </summary>
            private readonly long m_blocksize;

            /// <summary>
            /// Initializes a new instance of the <see cref="FastSource"/> class with the specified SqliteDataReader and block size.
            /// </summary>
            /// <param name="rd">The SqliteDataReader used to read file and block data.</param>
            /// <param name="blocksize">The block size used to calculate the offset of blocks.</param>
            public FastSource(SqliteDataReader rd, long blocksize)
            {
                m_rd = rd;
                m_blocksize = blocksize;
                MoreData = true;
            }

            /// <summary>
            /// Indicates whether there is more data to read from the SqliteDataReader.
            /// </summary>
            public bool MoreData { get; private set; }

            public string TargetPath { get { return m_rd.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_rd.ConvertValueToInt64(2); } }
            public string SourcePath { get { return m_rd.ConvertValueToString(1) ?? ""; } }

            /// <inheritdoc/>
            public async IAsyncEnumerable<IBlockEntry> Blocks([EnumeratorCancellation] CancellationToken token)
            {
                var tid = TargetFileID;

                do
                {
                    yield return new BlockEntry(m_rd, m_blocksize);
                } while ((MoreData = await m_rd.ReadAsync(token).ConfigureAwait(false)) && tid == TargetFileID);
            }
        }

        /// <summary>
        /// Returns a list of files and their source blocks for fast restoration.
        /// </summary>
        /// <param name="blocksize">The size of the blocks to be used for restoration.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of <see cref="IFastSource"/> representing the files and their source blocks.</returns>
        public async IAsyncEnumerable<IFastSource> GetFilesAndSourceBlocksFast(long blocksize, [EnumeratorCancellation] CancellationToken token)
        {
            await using (var cmdReader = m_connection.CreateCommand())
            await using (var cmd = m_connection.CreateCommand())
            {
                cmdReader.SetTransaction(m_rtr);
                cmd.SetTransaction(m_rtr);
                cmd.SetCommandAndParameters($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""LocalSourceExists"" = 1
                    WHERE ""Path"" = @Path
                ");
                cmdReader.SetCommandAndParameters($@"
                    SELECT DISTINCT ""{m_tempfiletable}"".""Path""
                    FROM ""{m_tempfiletable}""
                ");
                await using (var rd = await cmdReader.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await rd.ReadAsync(token).ConfigureAwait(false))
                    {
                        var sourcepath = rd.ConvertValueToString(0);
                        if (SystemIO.IO_OS.FileExists(sourcepath))
                        {
                            await cmd
                                .SetParameterValue("@Path", sourcepath)
                                .ExecuteNonQueryAsync(token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "LocalSourceMissing", "Local source file not found: {0}", sourcepath);
                        }
                    }
                }

                //This localSourceExists index will make the query engine to start by searching FileSet table. As the result is ordered by FileSet.ID, we will get the cursor "instantly"
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE INDEX ""{m_tempfiletable}_LocalSourceExists""
                    ON ""{m_tempfiletable}"" (""LocalSourceExists"")
                ", token)
                    .ConfigureAwait(false);

                await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
            }

            m_latestblocktable = "LatestBlocksetIds-" + m_temptabsetguid;

            var whereclause = $@"
                ""{m_tempfiletable}"".""LocalSourceExists"" = 1
                AND ""{m_tempblocktable}"".""Restored"" = 0
                AND ""{m_tempblocktable}"".""Metadata"" = 0
                AND ""{m_tempfiletable}"".""TargetPath"" != ""{m_tempfiletable}"".""Path""
            ";

            var latestBlocksetIds = $@"
                SELECT
                    ""File"".""Path"" AS ""PATH"",
                    ""File"".""BlocksetID"" AS ""BlocksetID"",
                    MAX(""Fileset"".""Timestamp"") AS ""Timestamp""
                FROM
                    ""File"",
                    ""FilesetEntry"",
                    ""Fileset""
                WHERE
                    ""File"".""ID"" = ""FilesetEntry"".""FileID""
                    AND ""FilesetEntry"".""FilesetID"" = ""Fileset"".""ID""
                    AND ""File"".""Path"" IN (
                        SELECT DISTINCT ""{m_tempfiletable}"".""Path""
                        FROM
                            ""{m_tempfiletable}"",
                            ""{m_tempblocktable}""
                        WHERE
                            ""{m_tempfiletable}"".""ID"" = ""{m_tempblocktable}"".""FileID""
                            AND {whereclause}
                    )
                GROUP BY ""File"".""Path""
            ";

            await using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_latestblocktable}"" ", token)
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync($@"CREATE TEMPORARY TABLE ""{m_latestblocktable}"" AS {latestBlocksetIds}", token)
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync($@"
                    CREATE INDEX ""{m_latestblocktable}_path""
                    ON ""{m_latestblocktable}"" (""Path"")
                ", token)
                    .ConfigureAwait(false);

                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""LatestBlocksetId"" = (
                        SELECT ""BlocksetId""
                        FROM ""{m_latestblocktable}""
                        WHERE ""Path"" = ""{m_tempfiletable}"".""Path""
                    )
                ", token)
                    .ConfigureAwait(false);
            }

            var sources = $@"
                SELECT DISTINCT
                    ""{m_tempfiletable}"".""TargetPath"",
                    ""{m_tempfiletable}"".""Path"",
                    ""{m_tempfiletable}"".""ID"",
                    ""{m_tempblocktable}"".""Index"",
                    ""{m_tempblocktable}"".""Hash"",
                    ""{m_tempblocktable}"".""Size""
                FROM
                    ""{m_tempfiletable}"",
                    ""{m_tempblocktable}"",
                    ""BlocksetEntry""
                WHERE
                    ""{m_tempfiletable}"".""ID"" = ""{m_tempblocktable}"".""FileID""
                    AND ""BlocksetEntry"".""BlocksetID"" = ""{m_tempfiletable}"".""LatestBlocksetID""
                    AND ""BlocksetEntry"".""BlockID"" = ""{m_tempblocktable}"".""BlockID""
                    AND ""BlocksetEntry"".""Index"" = ""{m_tempblocktable}"".""Index""
                    AND {whereclause}
                ORDER BY
                    ""{m_tempfiletable}"".""ID"",
                    ""{m_tempblocktable}"".""Index""
                ";

            await using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                await using var rd = await cmd.ExecuteReaderAsync(sources, token)
                    .ConfigureAwait(false);
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    bool more;
                    do
                    {
                        var n = new FastSource(rd, blocksize);
                        var tid = n.TargetFileID;
                        yield return n;

                        more = n.MoreData;
                        while (more && n.TargetFileID == tid)
                            more = await rd.ReadAsync(token).ConfigureAwait(false);

                    } while (more);
                }
            }

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

    }
}
