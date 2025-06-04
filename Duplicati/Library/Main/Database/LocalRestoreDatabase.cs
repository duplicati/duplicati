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
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Operation.Restore;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    internal class LocalRestoreDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRestoreDatabase));

        protected readonly string m_temptabsetguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
        /// <summary>
        /// The name of the temporary table in the database, which is used to store the list of files to restore.
        /// </summary>
        protected string? m_tempfiletable;
        protected string? m_tempblocktable;
        protected ConcurrentBag<(SqliteConnection, ReusableTransaction)> m_connection_pool = [];
        protected string? m_latestblocktable;
        protected string? m_fileprogtable;
        protected string? m_totalprogtable;
        protected string? m_filesnewlydonetable;

        protected DateTime m_restoreTime;

        public DateTime RestoreTime { get { return m_restoreTime; } }

        public static async Task<LocalRestoreDatabase> CreateAsync(string path, long pagecachesize, LocalRestoreDatabase? dbnew = null)
        {
            dbnew ??= new LocalRestoreDatabase();

            dbnew = (LocalRestoreDatabase)await CreateLocalDatabaseAsync(path, "Restore", false, pagecachesize, dbnew);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        public static async Task<LocalRestoreDatabase> CreateAsync(LocalDatabase dbparent, LocalRestoreDatabase? dbnew = null)
        {
            dbnew ??= new LocalRestoreDatabase();

            return (LocalRestoreDatabase)await CreateLocalDatabaseAsync(dbparent, dbnew);
        }

        /// <summary>
        /// Create tables and triggers for automatic tracking of progress during a restore operation.
        /// This replaces continuous requerying of block progress by iterating over blocks table.
        /// SQLite is much faster keeping information up to date with internal triggers.
        /// </summary>
        /// <param name="createFilesNewlyDoneTracker"> This allows to create another table that keeps track
        /// of all files that are done (all data blocks restored). </param>
        /// <remarks>
        /// The method is prepared to create a table that keeps track of all files being done completely.
        /// That means, it fires for files where the number of restored blocks equals the number of all blocks.
        /// It is intended to be used for fast identification of fully restored files to trigger their verification.
        /// It should be read after a commit and truncated after putting the files to a verification queue.
        /// Note: If a file is done once and then set back to a none restored state, the file is not automatically removed.
        ///       But if it reaches a restored state later, it will be re-added (trigger will fire)
        /// </remarks>
        public async Task CreateProgressTracker(bool createFilesNewlyDoneTracker)
        {
            m_fileprogtable = "FileProgress-" + m_temptabsetguid;
            m_totalprogtable = "TotalProgress-" + m_temptabsetguid;
            m_filesnewlydonetable = createFilesNewlyDoneTracker ? "FilesNewlyDone-" + m_temptabsetguid : null;

            using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            // How to handle METADATA?
            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_fileprogtable}"" ");
            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{m_fileprogtable}"" (
                    ""FileId"" INTEGER PRIMARY KEY,
                    ""TotalBlocks"" INTEGER NOT NULL,
                    ""TotalSize"" INTEGER NOT NULL,
                    ""BlocksRestored"" INTEGER NOT NULL,
                    ""SizeRestored"" INTEGER NOT NULL
                )
            ");

            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_totalprogtable}"" ");
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
            ");

            if (createFilesNewlyDoneTracker)
            {
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}"" ");
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{m_filesnewlydonetable}"" (
                        ""ID"" INTEGER PRIMARY KEY
                    )
                ");
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
                ");

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
                ");

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
                ");

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
                ");


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
                    ");
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
                await m_rtr.CommitAsync();
            }
        }

        public async Task<Tuple<long, long>> PrepareRestoreFilelist(DateTime restoretime, long[] versions, IFilter filter)
        {
            m_tempfiletable = "Fileset-" + m_temptabsetguid;
            m_tempblocktable = "Blocks-" + m_temptabsetguid;

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                var filesetIds = await GetFilesetIDs(Library.Utility.Utility.NormalizeDateTime(restoretime), versions).ToListAsync();
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
                    m_restoreTime = ParseFromEpochSeconds(await cmd.ExecuteScalarInt64Async(0));

                    var ix = await FilesetTimes()
                            .Select((value, index) => new { value.Key, index })
                            .Where(n => n.Key == filesetId)
                            .Select(pair => pair.index + 1)
                            .FirstOrDefaultAsync() - 1;

                    Logging.Log.WriteInformationMessage(LOGTAG, "SearchingBackup", "Searching backup {0} ({1}) ...", ix, m_restoreTime);

                    cmd.Parameters.Clear();

                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempfiletable}"" ");
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempblocktable}"" ");
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
                    ");
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
                    ");

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
                            .ExecuteNonQueryAsync();
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == FilterType.Simple)
                    {
                        using (new Logging.Timer(LOGTAG, "CommitBeforePrepareFileset", "CommitBeforePrepareFileset"))
                            await m_rtr.CommitAsync();
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
                        ");
                        cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_filenamestable}"" (""Path"")
                            VALUES (@Path)
                        ");

                        foreach (var s in p)
                        {
                            await cmd.SetParameterValue("@Path", s)
                                .ExecuteNonQueryAsync();
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
                            .ExecuteNonQueryAsync();

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
                            using (var rd = await cmd.ExecuteReaderAsync())
                                while (await rd.ReadAsync())
                                    sb.AppendLine(rd.ConvertValueToString(0));

                            cmd.SetCommandAndParameters(@"
                                SELECT ""Timestamp""
                                FROM ""Fileset""
                                WHERE ""ID"" = @FilesetId
                            ")
                                .SetParameterValue("@FilesetId", filesetId);
                            var actualrestoretime = ParseFromEpochSeconds(await cmd.ExecuteScalarInt64Async(0));

                            Logging.Log.WriteWarningMessage(LOGTAG, "FilesNotFoundInBackupList", null, "{0} File(s) were not found in list of files for backup at {1}, will not be restored: {2}", p.Length - c, actualrestoretime.ToLocalTime(), sb);
                            cmd.Parameters.Clear();
                        }

                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filenamestable}"" ");

                        using (new Logging.Timer(LOGTAG, "CommitAfterPrepareFileset", "CommitAfterPrepareFileset"))
                            await m_rtr.CommitAsync();
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
                        using var cmd2 = m_connection.CreateCommand($@"
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
                        using var rd = await cmd.ExecuteReaderAsync();
                        while (await rd.ReadAsync())
                        {
                            rd.GetValues(values);
                            var path = values[1] as string;
                            if (path != null && FilterExpression.Matches(filter, path.ToString()!))
                            {
                                await cmd2.SetParameterValue("@ID", values[0])
                                    .SetParameterValue("@Path", values[1])
                                    .SetParameterValue("@BlocksetID", values[2])
                                    .SetParameterValue("@MetadataID", values[3])
                                    .ExecuteNonQueryAsync();
                            }
                        }
                    }

                    //creating indexes after insertion is much faster
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_ID""
                        ON ""{m_tempfiletable}"" (""ID"")
                    ");
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_TargetPath""
                        ON ""{m_tempfiletable}"" (""TargetPath"")
                    ");
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{m_tempfiletable}_Path""
                        ON ""{m_tempfiletable}"" (""Path"")
                    ");

                    cmd.SetCommandAndParameters($@"
                        SELECT
                            COUNT(DISTINCT ""{m_tempfiletable}"".""Path""),
                            SUM(""Blockset"".""Length"")
                        FROM
                            ""{m_tempfiletable}"",
                            ""Blockset""
                        WHERE ""{m_tempfiletable}"".""BlocksetID"" = ""Blockset"".""ID""
                    ");
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        var filecount = 0L;
                        var filesize = 0L;

                        if (await rd.ReadAsync())
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

                await m_rtr.CommitAsync();
            }

            return new Tuple<long, long>(0, 0);
        }

        public async Task<string?> GetFirstPath()
        {
            using var cmd = m_connection.CreateCommand($@"
                SELECT ""Path""
                FROM ""{m_tempfiletable}""
                ORDER BY LENGTH(""Path"") DESC
                LIMIT 1
            ")
                .SetTransaction(m_rtr);

            var v0 = await cmd.ExecuteScalarAsync();
            if (v0 == null || v0 == DBNull.Value)
                return null;

            await m_rtr.CommitAsync();

            return v0.ToString();
        }

        public async Task<string> GetLargestPrefix()
        {
            using var cmd = m_connection.CreateCommand($@"
                SELECT ""Path""
                FROM ""{m_tempfiletable}""
                ORDER BY LENGTH(""Path"") DESC
                LIMIT 1
            ")
                .SetTransaction(m_rtr);

            var v0 = await cmd.ExecuteScalarAsync();
            var maxpath = "";
            if (v0 != null && v0 != DBNull.Value)
                maxpath = v0.ToString()!;

            var dirsep = Util.GuessDirSeparator(maxpath);

            var filecount = await cmd.ExecuteScalarInt64Async($@"
                SELECT COUNT(*)
                FROM ""{m_tempfiletable}""
            ", -1);
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
                foundfiles = await cmd.SetParameterValue("@PrefixLength", mp.Length)
                    .SetParameterValue("@Prefix", mp)
                    .ExecuteScalarInt64Async(-1);

                if (filecount != foundfiles)
                {
                    var oldlen = maxpath.Length;

                    var lix = maxpath.LastIndexOf(dirsep, maxpath.Length - 2, StringComparison.Ordinal);
                    maxpath = maxpath.Substring(0, lix + 1);
                    if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen)
                        maxpath = "";
                }
            }

            await m_rtr.CommitAsync();

            return maxpath == "" ? "" : Util.AppendDirSeparator(maxpath, dirsep);
        }

        public async Task SetTargetPaths(string largest_prefix, string destination)
        {
            var dirsep = Util.GuessDirSeparator(string.IsNullOrWhiteSpace(largest_prefix) ? await GetFirstPath() : largest_prefix);

            using var cmd = m_connection.CreateCommand()
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
                    ");
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" =
                        CASE
                            WHEN SUBSTR(""Path"", 1, 2) == '\\'
                            THEN '\\' || SUBSTR(""Path"", 2)
                            ELSE ""Path""
                        END
                    ");

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
                        .ExecuteNonQueryAsync();
                }
                else
                {
                    // Same OS, just use the path directly
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""Targetpath"" = ""Path""
                    ");
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
                    ");

                    // For UNC paths, we use \\server\folder -> <restore path> / <servername> / <source path>
                    await cmd.ExecuteNonQueryAsync($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""TargetPath"" =
                        CASE
                            WHEN SUBSTR(""Path"", 1, 2) == '\\'
                            THEN SUBSTR(""Path"", 2)
                            ELSE ""TargetPath""
                        END
                    ");
                }
                else
                {
                    largest_prefix = Util.AppendDirSeparator(largest_prefix, dirsep);
                    await cmd.SetCommandAndParameters($@"
                        UPDATE ""{m_tempfiletable}""
                        SET ""TargetPath"" = SUBSTR(""Path"", @PrefixLength)
                    ")
                        .SetParameterValue("@PrefixLength", largest_prefix.Length + 1)
                        .ExecuteNonQueryAsync();
                }
            }

            // Cross-os path remapping support
            if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                // For Win paths on Linux
                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = REPLACE(""TargetPath"", '\', '/')
                ");
            else if (OperatingSystem.IsWindows() && dirsep == "/")
                // For Linux paths on Windows
                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = REPLACE(REPLACE(""TargetPath"", '\', '_'), '/', '\')
                ");

            if (!string.IsNullOrEmpty(destination))
            {
                // Paths are now relative with target-os naming system
                // so we prefix them with the target path
                await cmd.SetCommandAndParameters($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""TargetPath"" = @Destination || ""TargetPath""
                ")
                    .SetParameterValue("@Destination", Util.AppendDirSeparator(destination))
                    .ExecuteNonQueryAsync();
            }

            await m_rtr.CommitAsync();
        }

        public async Task FindMissingBlocks(bool skipMetadata)
        {
            using var cmd = m_connection.CreateCommand()
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
            ");

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
                ");

            //creating indexes after insertion is much faster
            await cmd.ExecuteNonQueryAsync($@"
                CREATE INDEX ""{m_tempblocktable}_HashSizeIndex""
                ON ""{m_tempblocktable}"" (""Hash"", ""Size"")
            ");
            // better suited to speed up commit on UpdateBlocks
            await cmd.ExecuteNonQueryAsync($@"
                CREATE INDEX ""{m_tempblocktable}_FileIdIndexIndex""
                ON ""{m_tempblocktable}"" (""FileId"", ""Index"")
            ");

            var size = await cmd.ExecuteScalarInt64Async($@"
                SELECT SUM(""Size"")
                FROM ""{m_tempblocktable}""
            ", 0);
            Logging.Log.WriteVerboseMessage(LOGTAG, "RestoreSourceSize", "Restore list contains {0} blocks with a total size of {1}", p1 + p2, Library.Utility.Utility.FormatSizeString(size));

            await m_rtr.CommitAsync();
        }

        public async Task UpdateTargetPath(long ID, string newname)
        {
            using var cmd = m_connection.CreateCommand($@"
                UPDATE ""{m_tempfiletable}""
                SET ""TargetPath"" = @TargetPath
                WHERE ""ID"" = @ID
            ");
            await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@TargetPath", newname)
                .SetParameterValue("@ID", ID)
                .ExecuteNonQueryAsync();

            await m_rtr.CommitAsync();
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
            IAsyncEnumerable<IExistingFileBlock> Blocks();
        }

        public interface IBlockSource
        {
            string Path { get; }
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
            IAsyncEnumerable<IBlockSource> BlockSources();
        }

        public interface ILocalBlockSource
        {
            string TargetPath { get; }
            long TargetFileID { get; }
            IAsyncEnumerable<IBlockDescriptor> Blocks();
        }

        /// <summary>
        /// Interface for an object describing a file to restore.
        /// </summary>
        public interface IFileToRestore
        {
            string Path { get; }
            string Hash { get; }
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
            IAsyncEnumerable<IPatchBlock> Blocks();
        }

        private class ExistingFile : IExistingFile
        {
            private readonly SqliteDataReader m_reader;

            public ExistingFile(SqliteDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public string TargetHash { get { return m_reader.ConvertValueToString(1) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(2); } }
            public long Length { get { return m_reader.ConvertValueToInt64(3); } }

            public bool HasMore { get; private set; }

            private class ExistingFileBlock : IExistingFileBlock
            {
                private readonly SqliteDataReader m_reader;

                public ExistingFileBlock(SqliteDataReader rd) { m_reader = rd; }

                public string Hash { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                public long Index { get { return m_reader.ConvertValueToInt64(5); } }
                public long Size { get { return m_reader.ConvertValueToInt64(6); } }
            }

            public async IAsyncEnumerable<IExistingFileBlock> Blocks()
            {
                string p = TargetPath;
                while (HasMore && p == TargetPath)
                {
                    yield return new ExistingFileBlock(m_reader);
                    HasMore = await m_reader.ReadAsync();
                }
            }

            public static async IAsyncEnumerable<IExistingFile> GetExistingFilesWithBlocks(LocalDatabase db, string tablename)
            {
                using var cmd = db.Connection.CreateCommand($@"
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
                using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    var more = true;
                    while (more)
                    {
                        var f = new ExistingFile(rd);
                        string current = f.TargetPath;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.TargetPath)
                            more = await rd.ReadAsync();
                    }
                }
                await db.Transaction.CommitAsync();
            }
        }

        public IAsyncEnumerable<IExistingFile> GetExistingFilesWithBlocks()
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return ExistingFile.GetExistingFilesWithBlocks(this, m_tempfiletable);
        }

        private class LocalBlockSource : ILocalBlockSource
        {
            private class BlockDescriptor : IBlockDescriptor
            {
                private class BlockSource : IBlockSource
                {
                    private readonly SqliteDataReader m_reader;
                    public BlockSource(SqliteDataReader rd) { m_reader = rd; }

                    public string Path { get { return m_reader.ConvertValueToString(6) ?? ""; } }
                    public long Offset { get { return m_reader.ConvertValueToInt64(7); } }
                    public bool IsMetadata { get { return false; } }
                }

                private readonly SqliteDataReader m_reader;
                public BlockDescriptor(SqliteDataReader rd) { m_reader = rd; HasMore = true; }

                private string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }

                public string Hash { get { return m_reader.ConvertValueToString(2) ?? ""; } }
                public long Offset { get { return m_reader.ConvertValueToInt64(3); } }
                public long Index { get { return m_reader.ConvertValueToInt64(4); } }
                public long Size { get { return m_reader.ConvertValueToInt64(5); } }
                public bool IsMetadata { get { return !(m_reader.ConvertValueToInt64(9) == 0); } }

                public bool HasMore { get; private set; }

                public async IAsyncEnumerable<IBlockSource> BlockSources()
                {
                    var p = TargetPath;
                    var h = Hash;
                    var s = Size;

                    while (HasMore && p == TargetPath && h == Hash && s == Size)
                    {
                        yield return new BlockSource(m_reader);
                        HasMore = await m_reader.ReadAsync();
                    }
                }

            }

            private readonly SqliteDataReader m_reader;
            public LocalBlockSource(SqliteDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(1); } }

            public bool HasMore { get; private set; }

            public async IAsyncEnumerable<IBlockDescriptor> Blocks()
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
                        HasMore = await m_reader.ReadAsync();
                }
            }

            public static async IAsyncEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(LocalDatabase db, string filetablename, string blocktablename, long blocksize, bool skipMetadata)
            {
                // TODO: Skip metadata as required
                // Have to order by target path and hash, to ensure BlockDescriptor and BlockSource match adjacent rows
                using var cmd = db.Connection.CreateCommand($@"
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
                        ""Block"" E
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
                using var rd = await cmd.ExecuteReaderAsync();
                var more = await rd.ReadAsync();

                while (more)
                {
                    var f = new LocalBlockSource(rd);
                    string current = f.TargetPath;
                    yield return f;

                    more = f.HasMore;
                    while (more && current == f.TargetPath)
                        more = await rd.ReadAsync();
                }

                await db.Transaction.CommitAsync();
            }
        }

        public IAsyncEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(bool skipMetadata, long blocksize)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return LocalBlockSource.GetFilesAndSourceBlocks(this, m_tempfiletable, m_tempblocktable, blocksize, skipMetadata);
        }

        public async IAsyncEnumerable<IRemoteVolume> GetMissingVolumes()
        {
            using var cmd = m_connection.CreateCommand($@"
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

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToInt64(2, -1)
                );
            }

            await m_rtr.CommitAsync();
        }

        public interface IFilesAndMetadata : IDisposable
        {
            IAsyncEnumerable<IVolumePatch> FilesWithMissingBlocks();
            IAsyncEnumerable<IVolumePatch> MetadataWithMissingBlocks();
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

            public static async Task<FilesAndMetadata> CreateAsync(LocalDatabase db, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
            {
                var fam = new FilesAndMetadata()
                {
                    m_db = db,
                    m_filetablename = filetablename,
                    m_blocktablename = blocktablename,
                    m_blocksize = blocksize,
                };

                using var c = db.Connection.CreateCommand()
                    .SetTransaction(db.Transaction);
                fam.m_tmptable = "VolumeFiles-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                await c.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{fam.m_tmptable}"" (
                        ""Hash"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL
                    )
                ");

                c.SetCommandAndParameters($@"
                    INSERT INTO ""{fam.m_tmptable}"" (
                        ""Hash"",
                        ""Size""
                    )
                    VALUES (
                        @Hash,
                        @Size)
                ");
                foreach (var s in curvolume.Blocks)
                {
                    await c.SetParameterValue("@Hash", s.Key)
                        .SetParameterValue("@Size", s.Value)
                        .ExecuteNonQueryAsync();
                }

                // The index _HashSizeIndex is not needed anymore. Index on "Blocks-..." is used on Join in GetMissingBlocks

                await db.Transaction.CommitAsync();

                return fam;
            }

            public void Dispose()
            {
                DisposeAsync().Await();
            }

            public async Task DisposeAsync()
            {
                if (m_tmptable != null)
                {
                    using var c = m_db.Connection.CreateCommand(@$"DROP TABLE IF EXISTS ""{m_tmptable}""")
                        .SetTransaction(m_db.Transaction);
                    await c.ExecuteNonQueryAsync();
                    await m_db.Transaction.CommitAsync();
                }
            }

            private class VolumePatch : IVolumePatch
            {
                private class PatchBlock : IPatchBlock
                {
                    private readonly SqliteDataReader m_reader;
                    public PatchBlock(SqliteDataReader rd) { m_reader = rd; }

                    public long Offset { get { return m_reader.ConvertValueToInt64(2); } }
                    public long Size { get { return m_reader.ConvertValueToInt64(3); } }
                    public string Key { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                }

                private readonly SqliteDataReader m_reader;
                public VolumePatch(SqliteDataReader rd) { m_reader = rd; HasMore = true; }

                public string Path { get { return m_reader.ConvertValueToString(0) ?? ""; } }
                public long FileID { get { return m_reader.ConvertValueToInt64(1); } }
                public bool HasMore { get; private set; }

                public async IAsyncEnumerable<IPatchBlock> Blocks()
                {
                    string p = Path;
                    while (HasMore && p == Path)
                    {
                        yield return new PatchBlock(m_reader);
                        HasMore = await m_reader.ReadAsync();
                    }
                }
            }

            public async IAsyncEnumerable<IVolumePatch> FilesWithMissingBlocks()
            {
                // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                using var cmd = m_db.Connection.CreateCommand($@"
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

                using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    var more = true;
                    while (more)
                    {
                        var f = new VolumePatch(rd);
                        var current = f.Path;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.Path)
                            more = await rd.ReadAsync();
                    }
                }
                await m_db.Transaction.CommitAsync();
            }

            public async IAsyncEnumerable<IVolumePatch> MetadataWithMissingBlocks()
            {
                // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                using var cmd = m_db.Connection.CreateCommand($@"
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

                using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    var more = true;
                    while (more)
                    {
                        var f = new VolumePatch(rd);
                        string current = f.Path;
                        yield return f;

                        more = f.HasMore;
                        while (more && current == f.Path)
                            more = await rd.ReadAsync();
                    }
                }
            }
        }

        public async Task<IFilesAndMetadata> GetMissingBlockData(BlockVolumeReader curvolume, long blocksize)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return await FilesAndMetadata.CreateAsync(this, m_tempfiletable, m_tempblocktable, blocksize, curvolume);
        }

        /// <summary>
        /// Returns a connection from the connection pool.
        /// </summary>
        /// <returns>A connection from the connection pool.</returns>
        public async Task<(SqliteConnection, ReusableTransaction)> GetConnectionFromPool()
        {
            if (!m_connection_pool.TryTake(out var entry))
            {
                var connection = await SQLiteLoader.LoadConnectionAsync();
                connection.ConnectionString = m_connection.ConnectionString + ";Cache=Shared";
                await connection.OpenAsync();
                await SQLiteLoader.ApplyCustomPragmasAsync(connection, m_pagecachesize);
                var transaction = new ReusableTransaction(connection);

                return (connection, transaction);
            }

            return entry;
        }

        private class FileToRestore : IFileToRestore
        {
            public string Path { get; private set; }
            public string Hash { get; private set; }
            public long Length { get; private set; }

            public FileToRestore(long id, string path, string hash, long length)
            {
                Path = path;
                Hash = hash;
                Length = length;
            }
        }

        public async IAsyncEnumerable<IFileToRestore> GetFilesToRestore(bool onlyNonVerified)
        {
            using var cmd = m_connection.CreateCommand($@"
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

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new FileToRestore(
                    rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "", rd.ConvertValueToString(2) ?? "", rd.ConvertValueToInt64(3));

            await m_rtr.CommitAsync();
        }

        /// <summary>
        /// Returns a list of files and symlinks to restore.
        /// </summary>
        /// <returns>A list of files and symlinks to restore.</returns>
        public async IAsyncEnumerable<FileRequest> GetFilesAndSymlinksToRestore()
        {
            // Order by length descending, so that larger files are restored first.
            using var cmd = m_connection.CreateCommand($@"
                SELECT
                    F.ID,
                    F.Path,
                    F.TargetPath,
                    IFNULL(B.FullHash, ''),
                    IFNULL(B.Length, 0) AS ""Length"",
                    F.BlocksetID
                FROM ""{m_tempfiletable}"" F
                LEFT JOIN Blockset B
                    ON F.BlocksetID = B.ID
                WHERE F.BlocksetID != {FOLDER_BLOCKSET_ID}
                ORDER BY ""Length"" DESC
            ")
                .SetTransaction(m_rtr);
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new FileRequest(rd.ConvertValueToInt64(0), rd.ConvertValueToString(1), rd.ConvertValueToString(2), rd.ConvertValueToString(3), rd.ConvertValueToInt64(4), rd.ConvertValueToInt64(5));

            await m_rtr.CommitAsync();
        }

        /// <summary>
        /// Returns a list of folders to restore. Used to restore folder metadata.
        /// </summary>
        /// <returns>A list of folders to restore.</returns>
        public async IAsyncEnumerable<FileRequest> GetFolderMetadataToRestore()
        {
            using var cmd = m_connection.CreateCommand();
            cmd.SetTransaction(m_rtr);
            using var rd = await cmd.ExecuteReaderAsync($@"
                SELECT
                    F.ID,
                    '',
                    F.TargetPath,
                    '',
                    0,
                    {FOLDER_BLOCKSET_ID}
                FROM ""{m_tempfiletable}"" F
                WHERE
                    F.BlocksetID = {FOLDER_BLOCKSET_ID}
                    AND F.MetadataID IS NOT NULL
                    AND F.MetadataID >= 0
            ");

            while (await rd.ReadAsync())
                yield return new FileRequest(
                    rd.ConvertValueToInt64(0),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToString(2),
                    rd.ConvertValueToString(3),
                    rd.ConvertValueToInt64(4),
                    rd.ConvertValueToInt64(5)
                );
        }

        /// <summary>
        /// Returns a list of blocks and their volume IDs. Used by the <see cref="BlockManager"/> to keep track of blocks and volumes to automatically evict them from the respective caches.
        /// </summary>
        /// <param name="skipMetadata">Flag indicating whether the returned blocks should exclude the metadata blocks.</param>
        /// <returns>A list of tuples containing the block ID and the volume ID of the block.</returns>
        public async IAsyncEnumerable<(long, long)> GetBlocksAndVolumeIDs(bool skipMetadata)
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
            using var cmd = Connection.CreateCommand($@"
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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                yield return (
                    reader.ConvertValueToInt64(0),
                    reader.ConvertValueToInt64(1)
                );

            await m_rtr.CommitAsync();
        }

        /// <summary>
        /// Returns a list of <see cref="BlockRequest"/> for the given blockset ID. It is used by the <see cref="FileProcessor"/> to restore the blocks of a file.
        /// </summary>
        /// <param name="blocksetID">The BlocksetID of the file.</param>
        /// <returns>A list of <see cref="BlockRequest"/> needed to restore the given file.</returns>
        public async IAsyncEnumerable<BlockRequest> GetBlocksFromFile(long blocksetID)
        {
            var (connection, transaction) = await GetConnectionFromPool();
            try
            {

                using var cmd = connection.CreateCommand(@$"
                SELECT
                    ""Block"".""ID"",
                    ""Block"".""Hash"",
                    ""Block"".""Size"",
                    ""Block"".""VolumeID""
                FROM ""BlocksetEntry""
                INNER JOIN ""Block""
                    ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                WHERE ""BlocksetEntry"".""BlocksetID"" = @BlocksetID
            ")
                    .SetTransaction(transaction)
                    .SetParameterValue("@BlocksetID", blocksetID);

                using var reader = await cmd.ExecuteReaderAsync();
                for (long i = 0; await reader.ReadAsync(); i++)
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
        /// <returns>A list of <see cref="BlockRequest"/> needed to restore the metadata of the given file.</returns>
        public async IAsyncEnumerable<BlockRequest> GetMetadataBlocksFromFile(long fileID)
        {
            var (connection, transaction) = await GetConnectionFromPool();
            try
            {
                using var cmd = connection.CreateCommand($@"
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

                using var reader = await cmd.ExecuteReaderAsync();
                for (long i = 0; await reader.ReadAsync(); i++)
                {
                    yield return new BlockRequest(reader.ConvertValueToInt64(0), i, reader.ConvertValueToString(1), reader.ConvertValueToInt64(2), reader.ConvertValueToInt64(3), false);
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
        /// <returns>A tuple containing the name, size, and hash of the volume.</returns>
        public async IAsyncEnumerable<(string, long, string)> GetVolumeInfo(long VolumeID)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    Name,
                    Size,
                    Hash
                FROM RemoteVolume
                WHERE ID = @VolumeID
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@VolumeID", VolumeID);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                yield return (reader.ConvertValueToString(0) ?? "", reader.ConvertValueToInt64(1), reader.ConvertValueToString(2) ?? "");

            await m_rtr.CommitAsync();
        }

        public async Task DropRestoreTable()
        {
            using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            if (m_tempfiletable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempfiletable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_tempfiletable = null; }

            if (m_tempblocktable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tempblocktable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_tempblocktable = null; }

            if (m_latestblocktable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_latestblocktable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_latestblocktable = null; }

            if (m_fileprogtable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_fileprogtable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_fileprogtable = null; }

            if (m_totalprogtable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_totalprogtable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_totalprogtable = null; }

            if (m_filesnewlydonetable != null)
                try
                {
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}""");
                }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                finally { m_filesnewlydonetable = null; }

            await m_rtr.CommitAsync();
        }

        public interface IBlockMarker : IDisposable
        {
            Task SetBlockRestored(long targetfileid, long index, string hash, long blocksize, bool metadata);
            Task SetAllBlocksMissing(long targetfileid);
            Task SetAllBlocksRestored(long targetfileid, bool includeMetadata);
            Task SetFileDataVerified(long targetfileid);
            Task CommitAsync();
            Task UpdateProcessed(IOperationProgressUpdater writer);
        }

        /// <summary>
        /// A new implementation of IBlockMarker, marking the blocks directly in the blocks table as restored
        /// and reading statistics about progress from DB (kept up-to-date by triggers).
        /// There is no negative influence on performance, esp. since the block table is temporary anyway.
        /// </summary>
        private class DirectBlockMarker : IBlockMarker
        {
            private SqliteCommand m_insertblockCommand = null!;
            private SqliteCommand m_resetfileCommand = null!;
            private SqliteCommand m_updateAsRestoredCommand = null!;
            private SqliteCommand m_updateFileAsDataVerifiedCommand = null!;
            private SqliteCommand m_statUpdateCommand = null!;
            private LocalDatabase m_db = null!;
            private bool m_hasUpdates = false;

            private string m_blocktablename = null!;
            private string m_filetablename = null!;

            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public DirectBlockMarker(SqliteConnection connection, string blocktablename, string filetablename, string statstablename)
            {
                throw new NotImplementedException("Use CreateAsync instead of the constructor");
            }

            private DirectBlockMarker() { }

            public static async Task<DirectBlockMarker> CreateAsync(LocalDatabase db, string blocktablename, string filetablename, string statstablename)
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
                    "),

                    m_resetfileCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{blocktablename}""
                        SET ""Restored"" = 0
                        WHERE ""FileID"" = @TargetFileId
                    "),

                    m_updateAsRestoredCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{blocktablename}""
                        SET ""Restored"" = 1
                        WHERE
                            ""FileID"" = @TargetFileId
                            AND ""Metadata"" <= @Metadata
                    "),

                    m_updateFileAsDataVerifiedCommand = await db.Connection.CreateCommandAsync($@"
                        UPDATE ""{filetablename}""
                        SET ""DataVerified"" = 1
                        WHERE ""ID"" = @TargetFileId
                    "),

                    m_statUpdateCommand = statstablename == null ?
                        // very slow fallback if stats tables were not created
                        await db.Connection.CreateCommandAsync($@"
                            SELECT
                                COUNT(DISTINCT ""FileID""),
                                SUM(""Size"")
                            FROM ""{blocktablename}""
                            WHERE ""Restored"" = 1
                        ")
                        :
                        // Fields in Stats: TotalFiles, TotalBlocks, TotalSize
                        //                  FilesFullyRestored, FilesPartiallyRestored, BlocksRestored, SizeRestored
                        await db.Connection.CreateCommandAsync($@"
                            SELECT
                                SUM(""FilesFullyRestored""),
                                SUM(""SizeRestored"")
                            FROM ""{statstablename}""
                        ")
                };

                return dbm;
            }

            public async Task UpdateProcessed(IOperationProgressUpdater updater)
            {
                if (!m_hasUpdates)
                    return;

                m_hasUpdates = false;
                using var rd = await m_statUpdateCommand
                    .SetTransaction(m_db.Transaction)
                    .ExecuteReaderAsync();
                var filesprocessed = 0L;
                var processedsize = 0L;

                if (rd.Read())
                {
                    filesprocessed += rd.ConvertValueToInt64(0, 0);
                    processedsize += rd.ConvertValueToInt64(1, 0);
                }

                updater.UpdatefilesProcessed(filesprocessed, processedsize);
            }

            public async Task SetAllBlocksMissing(long targetfileid)
            {
                m_hasUpdates = true;
                m_resetfileCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid);

                var r = await m_resetfileCommand.ExecuteNonQueryAsync();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            public async Task SetAllBlocksRestored(long targetfileid, bool includeMetadata)
            {
                m_hasUpdates = true;
                m_updateAsRestoredCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Metadata", includeMetadata ? 1 : 0);

                var r = await m_updateAsRestoredCommand.ExecuteNonQueryAsync();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            public async Task SetFileDataVerified(long targetfileid)
            {
                m_hasUpdates = true;
                m_updateFileAsDataVerifiedCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid);

                var r = await m_updateFileAsDataVerifiedCommand.ExecuteNonQueryAsync();
                if (r != 1)
                    throw new Exception("Unexpected result when marking file as verified.");
            }

            public async Task SetBlockRestored(long targetfileid, long index, string hash, long size, bool metadata)
            {
                m_hasUpdates = true;
                m_insertblockCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Index", index)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@Metadata", metadata);

                var r = await m_insertblockCommand.ExecuteNonQueryAsync();
                if (r != 1)
                    throw new Exception("Unexpected result when marking block.");
            }

            public async Task CommitAsync()
            {
                using (new Logging.Timer(LOGTAG, "CommitBlockMarker", "CommitBlockMarker"))
                    await m_db.Transaction.CommitAsync();
            }

            public void Dispose()
            {
                m_insertblockCommand?.Dispose();
                m_resetfileCommand?.Dispose();
                m_updateAsRestoredCommand?.Dispose();
                m_updateFileAsDataVerifiedCommand?.Dispose();
                m_statUpdateCommand?.Dispose();
            }
        }

        [Obsolete("Calling this constructor will throw an exception. Use CreateBlockMarkerAsync instead.")]
        public IBlockMarker CreateBlockMarker()
        {
            throw new NotImplementedException("Use CreateBlockMarkerAsync instead of the constructor");
        }

        public async Task<IBlockMarker> CreateBlockMarkerAsync()
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            if (string.IsNullOrWhiteSpace(m_totalprogtable))
                throw new InvalidOperationException("No progress table set up for this restore.");
            return await DirectBlockMarker.CreateAsync(this, m_tempblocktable, m_tempfiletable, m_totalprogtable);
        }

        public override void Dispose()
        {
            DisposeAsync().Await();
        }

        public override async Task DisposeAsync()
        {
            await DisposePoolAsync();
            await DropRestoreTable();
            await base.DisposeAsync();
        }

        public async Task DisposePoolAsync()
        {
            foreach (var (connection, transaction) in m_connection_pool)
            {
                await transaction.DisposeAsync();
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
            m_connection_pool.Clear();
        }

        public async IAsyncEnumerable<string> GetTargetFolders()
        {
            using var cmd = m_connection.CreateCommand($@"
                SELECT ""TargetPath""
                FROM ""{m_tempfiletable}""
                WHERE ""BlocksetID"" == @BlocksetID
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetID", FOLDER_BLOCKSET_ID);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return rd.ConvertValueToString(0) ?? "";

            await m_rtr.CommitAsync();
        }

        public interface IFastSource
        {
            string TargetPath { get; }
            long TargetFileID { get; }
            string SourcePath { get; }
            IAsyncEnumerable<IBlockEntry> Blocks();
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
                private readonly SqliteDataReader m_rd;
                private readonly long m_blocksize;
                public BlockEntry(SqliteDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; }
                public long Offset { get { return m_rd.ConvertValueToInt64(3) * m_blocksize; } }
                public long Index { get { return m_rd.ConvertValueToInt64(3); } }
                public long Size { get { return m_rd.ConvertValueToInt64(5); } }
                public string Hash { get { return m_rd.ConvertValueToString(4) ?? ""; } }
            }

            private readonly SqliteDataReader m_rd;
            private readonly long m_blocksize;
            public FastSource(SqliteDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; MoreData = true; }
            public bool MoreData { get; private set; }
            public string TargetPath { get { return m_rd.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_rd.ConvertValueToInt64(2); } }
            public string SourcePath { get { return m_rd.ConvertValueToString(1) ?? ""; } }

            public async IAsyncEnumerable<IBlockEntry> Blocks()
            {
                var tid = TargetFileID;

                do
                {
                    yield return new BlockEntry(m_rd, m_blocksize);
                } while ((MoreData = await m_rd.ReadAsync()) && tid == TargetFileID);
            }
        }

        public async IAsyncEnumerable<IFastSource> GetFilesAndSourceBlocksFast(long blocksize)
        {
            using (var cmdReader = m_connection.CreateCommand())
            using (var cmd = m_connection.CreateCommand())
            {
                cmdReader.SetTransaction(m_rtr);
                cmd.SetTransaction(m_rtr);
                cmd.SetCommandAndParameters($@"
                    UPDATE ""{m_tempfiletable}""
                    SET ""LocalSourceExists"" = 1
                    WHERE Path = @Path
                ");
                cmdReader.SetCommandAndParameters($@"
                    SELECT DISTINCT ""{m_tempfiletable}"".""Path""
                    FROM ""{m_tempfiletable}""
                ");
                using (var rd = await cmdReader.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var sourcepath = rd.ConvertValueToString(0);
                        if (SystemIO.IO_OS.FileExists(sourcepath))
                        {
                            await cmd.SetParameterValue("@Path", sourcepath)
                                .ExecuteNonQueryAsync();
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
                ");

                await m_rtr.CommitAsync();
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

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_latestblocktable}"" ");
                await cmd.ExecuteNonQueryAsync($@"CREATE TEMPORARY TABLE ""{m_latestblocktable}"" AS {latestBlocksetIds}");
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE INDEX ""{m_latestblocktable}_path""
                    ON ""{m_latestblocktable}"" (""Path"")
                ");

                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""{m_tempfiletable}""
                    SET LatestBlocksetId = (
                        SELECT BlocksetId
                        FROM ""{m_latestblocktable}""
                        WHERE Path = ""{m_tempfiletable}"".Path
                    )
                ");
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

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.SetTransaction(m_rtr);
                using var rd = await cmd.ExecuteReaderAsync(sources);
                if (await rd.ReadAsync())
                {
                    bool more;
                    do
                    {
                        var n = new FastSource(rd, blocksize);
                        var tid = n.TargetFileID;
                        yield return n;

                        more = n.MoreData;
                        while (more && n.TargetFileID == tid)
                            more = await rd.ReadAsync();

                    } while (more);
                }
            }

            await m_rtr.CommitAsync();
        }

    }
}
