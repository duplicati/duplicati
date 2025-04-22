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
using System.Data;
using System.Linq;
using System.Text;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Operation.Restore;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

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
        protected ConcurrentBag<IDbConnection> m_connection_pool = [];
        protected string? m_latestblocktable;
        protected string? m_fileprogtable;
        protected string? m_totalprogtable;
        protected string? m_filesnewlydonetable;

        protected DateTime m_restoreTime;

        public DateTime RestoreTime { get { return m_restoreTime; } }

        public LocalRestoreDatabase(string path, long pagecachesize)
            : this(new LocalDatabase(path, "Restore", false, pagecachesize))
        {
            ShouldCloseConnection = true;
        }

        public LocalRestoreDatabase(LocalDatabase dbparent)
            : base(dbparent)
        {
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
        public void CreateProgressTracker(bool createFilesNewlyDoneTracker)
        {
            m_fileprogtable = "FileProgress-" + m_temptabsetguid;
            m_totalprogtable = "TotalProgress-" + m_temptabsetguid;
            m_filesnewlydonetable = createFilesNewlyDoneTracker ? "FilesNewlyDone-" + m_temptabsetguid : null;

            using (var cmd = m_connection.CreateCommand())
            {
                // How to handle METADATA?
                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_fileprogtable}"" "));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_fileprogtable}"" (
""FileId"" INTEGER PRIMARY KEY,
""TotalBlocks"" INTEGER NOT NULL, ""TotalSize"" INTEGER NOT NULL,
""BlocksRestored"" INTEGER NOT NULL, ""SizeRestored"" INTEGER NOT NULL
)"));

                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_totalprogtable}"" "));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_totalprogtable}"" (
""TotalFiles"" INTEGER NOT NULL, ""TotalBlocks"" INTEGER NOT NULL, ""TotalSize"" INTEGER NOT NULL,
""FilesFullyRestored"" INTEGER NOT NULL, ""FilesPartiallyRestored"" INTEGER NOT NULL,
""BlocksRestored"" INTEGER NOT NULL, ""SizeRestored"" INTEGER NOT NULL
)"));

                if (createFilesNewlyDoneTracker)
                {
                    cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}"" "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_filesnewlydonetable}"" (
""ID"" INTEGER PRIMARY KEY
)"));
                }

                try
                {
                    // Initialize statistics with File- and Block-Data (it is valid to already have restored blocks in files)
                    // A rebuild with this function should be valid anytime.
                    // Note: FilesNewlyDone is NOT initialized, as in initialization nothing is really new.
                    string sql;

                    // We use a LEFT JOIN to allow for empty files (no data Blocks)
                    sql = FormatInvariant($@" INSERT INTO ""{m_fileprogtable}"" (""FileId"", ""TotalBlocks"", ""TotalSize"", ""BlocksRestored"", ""SizeRestored"")
SELECT   ""F"".""ID"", IFNULL(COUNT(""B"".""ID""), 0), IFNULL(SUM(""B"".""Size""), 0)
       , IFNULL(COUNT(CASE ""B"".""Restored"" WHEN 1 THEN ""B"".""ID"" ELSE NULL END), 0)
       , IFNULL(SUM(CASE ""B"".""Restored"" WHEN 1 THEN ""B"".""Size"" ELSE 0 END), 0)
  FROM ""{m_tempfiletable}"" ""F"" LEFT JOIN ""{m_tempblocktable}"" ""B""
       ON  ""B"".""FileID"" = ""F"".""ID""
 WHERE ""B"".""Metadata"" IS NOT 1
 GROUP BY ""F"".""ID"" ");

                    // Will be one row per file.
                    cmd.ExecuteNonQuery(sql);

                    sql = FormatInvariant($@"INSERT INTO ""{m_totalprogtable}"" (
  ""TotalFiles"", ""TotalBlocks"", ""TotalSize""
, ""FilesFullyRestored"", ""FilesPartiallyRestored"", ""BlocksRestored"", ""SizeRestored""
 )
 SELECT   IFNULL(COUNT(""P"".""FileId""), 0), IFNULL(SUM(""P"".""TotalBlocks""), 0), IFNULL(SUM(""P"".""TotalSize""), 0)
        , IFNULL(COUNT(CASE WHEN ""P"".""BlocksRestored"" = ""P"".""TotalBlocks"" THEN 1 ELSE NULL END), 0)
        , IFNULL(COUNT(CASE WHEN ""P"".""BlocksRestored"" BETWEEN 1 AND ""P"".""TotalBlocks"" - 1 THEN 1 ELSE NULL END), 0)
        , IFNULL(SUM(""P"".""BlocksRestored""), 0), IFNULL(SUM(""P"".""SizeRestored""), 0)
   FROM ""{m_fileprogtable}"" ""P"" ");

                    // Will result in a single line (no support to also track metadata)
                    cmd.ExecuteNonQuery(sql);

                    // Finally we create TRIGGERs to keep all our statistics up to date.
                    // This is lightning fast, as SQLite uses internal hooks and our indices to do the update magic.
                    // Note: We do assume that neither files nor blocks will be added or deleted during restore process
                    //       and that the size of each block stays constant so there is no need to track that information
                    //       with additional INSERT and DELETE triggers.

                    // A trigger to update the file-stat entry each time a block changes restoration state.
                    sql = FormatInvariant($@"CREATE TEMPORARY TRIGGER ""TrackRestoredBlocks_{m_tempblocktable}"" AFTER UPDATE OF ""Restored"" ON ""{m_tempblocktable}""
WHEN OLD.""Restored"" != NEW.""Restored"" AND NEW.""Metadata"" = 0
BEGIN UPDATE ""{m_fileprogtable}""
   SET ""BlocksRestored"" = ""{m_fileprogtable}"".""BlocksRestored"" + (NEW.""Restored"" - OLD.""Restored"")
     , ""SizeRestored"" = ""{m_fileprogtable}"".""SizeRestored"" + ((NEW.""Restored"" - OLD.""Restored"") * NEW.Size)
 WHERE ""{m_fileprogtable}"".""FileId"" = NEW.""FileID""
; END ");
                    cmd.ExecuteNonQuery(sql);

                    // A trigger to update total stats each time a file stat changed (nested triggering by file-stats)
                    sql = FormatInvariant($@"CREATE TEMPORARY TRIGGER ""UpdateTotalStats_{m_fileprogtable}"" AFTER UPDATE ON ""{m_fileprogtable}""
BEGIN UPDATE ""{m_totalprogtable}""
   SET ""FilesFullyRestored"" = ""{m_totalprogtable}"".""FilesFullyRestored""
               + (CASE WHEN NEW.""BlocksRestored"" = NEW.""TotalBlocks"" THEN 1 ELSE 0 END)
               - (CASE WHEN OLD.""BlocksRestored"" = OLD.""TotalBlocks"" THEN 1 ELSE 0 END)
     , ""FilesPartiallyRestored"" = ""{m_totalprogtable}"".""FilesPartiallyRestored""
               + (CASE WHEN NEW.""BlocksRestored"" BETWEEN 1 AND NEW.""TotalBlocks"" - 1 THEN 1 ELSE 0 END)
               - (CASE WHEN OLD.""BlocksRestored"" BETWEEN 1 AND OLD.""TotalBlocks"" - 1 THEN 1 ELSE 0 END)
     , ""BlocksRestored"" = ""{m_totalprogtable}"".""BlocksRestored"" + NEW.""BlocksRestored"" - OLD.""BlocksRestored""
     , ""SizeRestored"" = ""{m_totalprogtable}"".""SizeRestored"" + NEW.""SizeRestored"" - OLD.""SizeRestored""
; END");
                    cmd.ExecuteNonQuery(sql);


                    if (createFilesNewlyDoneTracker)
                    {
                        // A trigger checking if a file is done (all blocks restored in file-stat) (nested triggering by file-stats)
                        sql = FormatInvariant($@"CREATE TEMPORARY TRIGGER ""UpdateFilesNewlyDone_{m_fileprogtable}"" AFTER UPDATE OF ""BlocksRestored"", ""TotalBlocks"" ON ""{m_fileprogtable}""
WHEN NEW.""BlocksRestored"" = NEW.""TotalBlocks""
BEGIN
   INSERT OR IGNORE INTO ""{m_filesnewlydonetable}"" (""ID"") VALUES (NEW.""FileId"");
END ");
                        cmd.ExecuteNonQuery(sql);
                    }

                }
                catch (Exception ex)
                {
                    m_fileprogtable = null;
                    m_totalprogtable = null;
                    Logging.Log.WriteWarningMessage(LOGTAG, "ProgressTrackerSetupError", ex, "Failed to set up progress tracking tables");
                    throw;
                }
            }
        }

        public Tuple<long, long> PrepareRestoreFilelist(DateTime restoretime, long[] versions, IFilter filter)
        {
            m_tempfiletable = "Fileset-" + m_temptabsetguid;
            m_tempblocktable = "Blocks-" + m_temptabsetguid;

            using (var cmd = m_connection.CreateCommand())
            {
                var filesetIds = GetFilesetIDs(Library.Utility.Utility.NormalizeDateTime(restoretime), versions).ToList();
                while (filesetIds.Count > 0)
                {
                    var filesetId = filesetIds[0];
                    filesetIds.RemoveAt(0);

                    m_restoreTime = ParseFromEpochSeconds(cmd
                        .SetCommandAndParameters(@"SELECT ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" = @FilesetId")
                        .SetParameterValue("@FilesetId", filesetId)
                        .ExecuteScalarInt64(0));

                    var ix = FilesetTimes.Select((value, index) => new { value.Key, index })
                            .Where(n => n.Key == filesetId)
                            .Select(pair => pair.index + 1)
                            .FirstOrDefault() - 1;

                    Logging.Log.WriteInformationMessage(LOGTAG, "SearchingBackup", "Searching backup {0} ({1}) ...", ix, m_restoreTime);

                    cmd.Parameters.Clear();

                    cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tempfiletable}"" "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tempblocktable}"" "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tempfiletable}"" (""ID"" INTEGER PRIMARY KEY, ""Path"" TEXT NOT NULL, ""BlocksetID"" INTEGER NOT NULL, ""MetadataID"" INTEGER NOT NULL, ""TargetPath"" TEXT NULL, ""DataVerified"" BOOLEAN NOT NULL, ""LatestBlocksetId"" INTEGER, ""LocalSourceExists"" BOOLEAN) "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tempblocktable}"" (""ID"" INTEGER PRIMARY KEY, ""FileID"" INTEGER NOT NULL, ""Index"" INTEGER NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Restored"" BOOLEAN NOT NULL, ""Metadata"" BOOLEAN NOT NULL, ""VolumeID"" INTEGER NOT NULL, ""BlockID"" INTEGER NOT NULL)"));

                    // TODO: Optimize to use the path prefix

                    if (filter == null || filter.Empty)
                    {
                        // Simple case, restore everything
                        cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tempfiletable}"" (""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""DataVerified"") SELECT ""File"".""ID"", ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"", 0 FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = @FilesetId"))
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQuery();
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == FilterType.Simple)
                    {
                        // If we get a list of filenames, the lookup table is faster
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        using (var tr = m_connection.BeginTransactionSafe())
                        {
                            var p = expression.GetSimpleList();
                            var m_filenamestable = "Filenames-" + m_temptabsetguid;
                            cmd.Transaction = tr;
                            cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_filenamestable}"" (""Path"" TEXT NOT NULL) "));
                            cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_filenamestable}"" (""Path"") VALUES (@Path)"));

                            foreach (var s in p)
                            {
                                cmd.SetParameterValue("@Path", s)
                                    .ExecuteNonQuery();
                            }

                            var c = cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tempfiletable}"" (""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""DataVerified"") SELECT ""File"".""ID"", ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"", 0 FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = @FilesetId AND ""Path"" IN (SELECT DISTINCT ""Path"" FROM ""{m_filenamestable}"") "))
                                .SetParameterValue("@FilesetId", filesetId)
                                .ExecuteNonQuery();

                            cmd.Parameters.Clear();

                            if (c != p.Length && c != 0)
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine();

                                using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT ""Path"" FROM ""{m_filenamestable}"" WHERE ""Path"" NOT IN (SELECT ""Path"" FROM ""{m_tempfiletable}"")")))
                                    while (rd.Read())
                                        sb.AppendLine(rd.ConvertValueToString(0));

                                var actualrestoretime = ParseFromEpochSeconds(
                                    cmd.SetCommandAndParameters(@"SELECT ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" = @FilesetId")
                                        .SetParameterValue("@FilesetId", filesetId)
                                        .ExecuteScalarInt64(0));

                                Logging.Log.WriteWarningMessage(LOGTAG, "FilesNotFoundInBackupList", null, "{0} File(s) were not found in list of files for backup at {1}, will not be restored: {2}", p.Length - c, actualrestoretime.ToLocalTime(), sb);
                                cmd.Parameters.Clear();
                            }

                            cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_filenamestable}"" "));

                            using (new Logging.Timer(LOGTAG, "CommitPrepareFileset", "CommitPrepareFileset"))
                                tr.Commit();
                        }
                    }
                    else
                    {
                        // Restore but filter elements based on the filter expression
                        // If this is too slow, we could add a special handler for wildcard searches too
                        cmd.SetCommandAndParameters(@"SELECT ""File"".""ID"", ""File"".""Path"", ""File"".""BlocksetID"", ""File"".""MetadataID"" FROM ""File"", ""FilesetEntry"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FilesetID"" = @FilesetId")
                            .SetParameterValue("@FilesetId", filesetId);

                        object[] values = new object[4];
                        using (var cmd2 = m_connection.CreateCommand(FormatInvariant($@"INSERT INTO ""{m_tempfiletable}"" (""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""DataVerified"") VALUES (@ID, @Path, @BlocksetID, @MetadataID, 0)")))
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                            {
                                rd.GetValues(values);
                                if (values[1] != null && values[1] != DBNull.Value && FilterExpression.Matches(filter, values[1].ToString()))
                                {
                                    cmd2.SetParameterValue("@ID", values[0])
                                        .SetParameterValue("@Path", values[1])
                                        .SetParameterValue("@BlocksetID", values[2])
                                        .SetParameterValue("@MetadataID", values[3])
                                        .ExecuteNonQuery();
                                }
                            }
                    }

                    //creating indexes after insertion is much faster
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempfiletable}_ID"" ON ""{m_tempfiletable}"" (""ID"")"));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempfiletable}_TargetPath"" ON ""{m_tempfiletable}"" (""TargetPath"")"));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempfiletable}_Path"" ON ""{m_tempfiletable}"" (""Path"")"));

                    using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT COUNT(DISTINCT ""{m_tempfiletable}"".""Path""), SUM(""Blockset"".""Length"") FROM ""{m_tempfiletable}"", ""Blockset"" WHERE ""{m_tempfiletable}"".""BlocksetID"" = ""Blockset"".""ID"" ")))
                    {
                        var filecount = 0L;
                        var filesize = 0L;

                        if (rd.Read())
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
            }

            return new Tuple<long, long>(0, 0);
        }

        public string? GetFirstPath()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var v0 = cmd.ExecuteScalar(FormatInvariant($@"SELECT ""Path"" FROM ""{m_tempfiletable}"" ORDER BY LENGTH(""Path"") DESC LIMIT 1"));
                if (v0 == null || v0 == DBNull.Value)
                    return null;

                return v0.ToString();
            }
        }

        public string GetLargestPrefix()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var v0 = cmd.ExecuteScalar(FormatInvariant($@"SELECT ""Path"" FROM ""{m_tempfiletable}"" ORDER BY LENGTH(""Path"") DESC LIMIT 1"));
                var maxpath = "";
                if (v0 != null && v0 != DBNull.Value)
                    maxpath = v0.ToString()!;

                var dirsep = Util.GuessDirSeparator(maxpath);

                var filecount = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM ""{m_tempfiletable}"""), -1);
                var foundfiles = -1L;

                //TODO: Handle FS case-sensitive?
                cmd.SetCommandAndParameters(FormatInvariant($@"SELECT COUNT(*) FROM ""{m_tempfiletable}"" WHERE SUBSTR(""Path"", 1, @PrefixLength) = @Prefix"));

                while (filecount != foundfiles && maxpath.Length > 0)
                {
                    var mp = Util.AppendDirSeparator(maxpath, dirsep);
                    foundfiles = cmd.SetParameterValue("@PrefixLength", mp.Length)
                        .SetParameterValue("@Prefix", mp)
                        .ExecuteScalarInt64(-1);

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
        }

        public void SetTargetPaths(string largest_prefix, string destination)
        {
            var dirsep = Util.GuessDirSeparator(string.IsNullOrWhiteSpace(largest_prefix) ? GetFirstPath() : largest_prefix);

            using (var cmd = m_connection.CreateCommand())
            {
                if (string.IsNullOrEmpty(destination))
                {
                    //The string fixing here is meant to provide some non-random
                    // defaults when restoring cross OS, e.g. backup on Linux, restore on Windows
                    //This is mostly meaningless, and the user really should use --restore-path

                    if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                    {
                        // For Win -> Linux, we remove the colon from the drive letter, and use the drive letter as root folder
                        cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""Targetpath"" = CASE WHEN SUBSTR(""Path"", 2, 1) == ':' THEN '\\' || SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3) ELSE ""Path"" END"));
                        cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""Targetpath"" = CASE WHEN SUBSTR(""Path"", 1, 2) == '\\' THEN '\\' || SUBSTR(""Path"", 2) ELSE ""Path"" END"));

                    }
                    else if (OperatingSystem.IsWindows() && dirsep == "/")
                    {
                        // For Linux -> Win, we use the temporary folder's drive as the root path
                        cmd.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""Targetpath"" = CASE WHEN SUBSTR(""Path"", 1, 1) == '/' THEN @Path || SUBSTR(""Path"", 2) ELSE ""Path"" END"))
                            .SetParameterValue("@Path", Util.AppendDirSeparator(System.IO.Path.GetPathRoot(Library.Utility.TempFolder.SystemTempPath)).Replace("\\", "/"))
                            .ExecuteNonQuery();
                    }
                    else
                    {
                        // Same OS, just use the path directly
                        cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""Targetpath"" = ""Path"" "));
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(largest_prefix))
                    {
                        //Special case, restoring to new folder, but files are from different drives (no shared root on Windows)

                        // We use the format <restore path> / <drive letter> / <source path>
                        cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = CASE WHEN SUBSTR(""Path"", 2, 1) == ':' THEN SUBSTR(""Path"", 1, 1) || SUBSTR(""Path"", 3) ELSE ""Path"" END"));

                        // For UNC paths, we use \\server\folder -> <restore path> / <servername> / <source path>
                        cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = CASE WHEN SUBSTR(""Path"", 1, 2) == '\\' THEN SUBSTR(""Path"", 2) ELSE ""TargetPath"" END"));
                    }
                    else
                    {
                        largest_prefix = Util.AppendDirSeparator(largest_prefix, dirsep);
                        cmd.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = SUBSTR(""Path"", @PrefixLength)"))
                            .SetParameterValue("@PrefixLength", largest_prefix.Length + 1)
                            .ExecuteNonQuery();
                    }
                }

                // Cross-os path remapping support
                if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                    // For Win paths on Linux
                    cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = REPLACE(""TargetPath"", '\', '/')"));
                else if (OperatingSystem.IsWindows() && dirsep == "/")
                    // For Linux paths on Windows
                    cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = REPLACE(REPLACE(""TargetPath"", '\', '_'), '/', '\')"));

                if (!string.IsNullOrEmpty(destination))
                {
                    // Paths are now relative with target-os naming system
                    // so we prefix them with the target path
                    cmd.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = @Destination || ""TargetPath"" "))
                        .SetParameterValue("@Destination", Util.AppendDirSeparator(destination))
                        .ExecuteNonQuery();
                }
            }
        }

        public void FindMissingBlocks(bool skipMetadata)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var p1 = cmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""{m_tempblocktable}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Restored"", ""Metadata"", ""VolumeId"", ""BlockId"") SELECT DISTINCT ""{m_tempfiletable}"".""ID"", ""BlocksetEntry"".""Index"", ""Block"".""Hash"", ""Block"".""Size"", 0, 0, ""Block"".""VolumeID"", ""Block"".""ID"" FROM ""{m_tempfiletable}"", ""BlocksetEntry"", ""Block"" WHERE ""{m_tempfiletable}"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" "));

                var p2 = 0;
                if (!skipMetadata)
                    p2 = cmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""{m_tempblocktable}"" (""FileID"", ""Index"", ""Hash"", ""Size"", ""Restored"", ""Metadata"", ""VolumeId"", ""BlockId"") SELECT DISTINCT ""{m_tempfiletable}"".""ID"", ""BlocksetEntry"".""Index"", ""Block"".""Hash"", ""Block"".""Size"", 0, 1, ""Block"".""VolumeID"", ""Block"".""ID""   FROM ""{m_tempfiletable}"", ""BlocksetEntry"", ""Block"", ""Metadataset"" WHERE ""{m_tempfiletable}"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" "));

                //creating indexes after insertion is much faster
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempblocktable}_HashSizeIndex"" ON ""{m_tempblocktable}"" (""Hash"", ""Size"")"));
                // better suited to speed up commit on UpdateBlocks
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempblocktable}_FileIdIndexIndex"" ON ""{m_tempblocktable}"" (""FileId"", ""Index"")"));

                var size = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT SUM(""Size"") FROM ""{m_tempblocktable}"" "), 0);
                Logging.Log.WriteVerboseMessage(LOGTAG, "RestoreSourceSize", "Restore list contains {0} blocks with a total size of {1}", p1 + p2, Library.Utility.Utility.FormatSizeString(size));
            }
        }

        public void UpdateTargetPath(long ID, string newname)
        {
            using var cmd = m_connection.CreateCommand(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""TargetPath"" = @TargetPath WHERE ""ID"" = @ID"))
                .SetParameterValue("@TargetPath", newname)
                .SetParameterValue("@ID", ID);

            cmd.ExecuteNonQuery();
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
            IEnumerable<IExistingFileBlock> Blocks { get; }
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
            IEnumerable<IBlockSource> Blocksources { get; }
        }

        public interface ILocalBlockSource
        {
            string TargetPath { get; }
            long TargetFileID { get; }
            IEnumerable<IBlockDescriptor> Blocks { get; }
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
            IEnumerable<IPatchBlock> Blocks { get; }
        }

        private class ExistingFile : IExistingFile
        {
            private readonly IDataReader m_reader;

            public ExistingFile(IDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public string TargetHash { get { return m_reader.ConvertValueToString(1) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(2); } }
            public long Length { get { return m_reader.ConvertValueToInt64(3); } }

            public bool HasMore { get; private set; }

            private class ExistingFileBlock : IExistingFileBlock
            {
                private readonly IDataReader m_reader;

                public ExistingFileBlock(IDataReader rd) { m_reader = rd; }

                public string Hash { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                public long Index { get { return m_reader.ConvertValueToInt64(5); } }
                public long Size { get { return m_reader.ConvertValueToInt64(6); } }
            }

            public IEnumerable<IExistingFileBlock> Blocks
            {
                get
                {
                    string p = TargetPath;
                    while (HasMore && p == TargetPath)
                    {
                        yield return new ExistingFileBlock(m_reader);
                        HasMore = m_reader.Read();
                    }
                }
            }

            public static IEnumerable<IExistingFile> GetExistingFilesWithBlocks(IDbConnection connection, string tablename)
            {
                using (var cmd = connection.CreateCommand(FormatInvariant($@"SELECT ""{tablename}"".""TargetPath"", ""Blockset"".""FullHash"", ""{tablename}"".""ID"", ""Blockset"".""Length"", ""Block"".""Hash"", ""BlocksetEntry"".""Index"", ""Block"".""Size"" FROM ""{tablename}"", ""Blockset"", ""BlocksetEntry"", ""Block"" WHERE ""{tablename}"".""BlocksetID"" = ""Blockset"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""{tablename}"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" ORDER BY ""{tablename}"".""TargetPath"", ""BlocksetEntry"".""Index""")))
                using (var rd = cmd.ExecuteReader())
                    if (rd.Read())
                    {
                        var more = true;
                        while (more)
                        {
                            var f = new ExistingFile(rd);
                            string current = f.TargetPath;
                            yield return f;

                            more = f.HasMore;
                            while (more && current == f.TargetPath)
                                more = rd.Read();
                        }
                    }
            }
        }

        public IEnumerable<IExistingFile> GetExistingFilesWithBlocks()
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return ExistingFile.GetExistingFilesWithBlocks(m_connection, m_tempfiletable);
        }

        private class LocalBlockSource : ILocalBlockSource
        {
            private class BlockDescriptor : IBlockDescriptor
            {
                private class BlockSource : IBlockSource
                {
                    private readonly IDataReader m_reader;
                    public BlockSource(IDataReader rd) { m_reader = rd; }

                    public string Path { get { return m_reader.ConvertValueToString(6) ?? ""; } }
                    public long Offset { get { return m_reader.ConvertValueToInt64(7); } }
                    public bool IsMetadata { get { return false; } }
                }

                private readonly IDataReader m_reader;
                public BlockDescriptor(IDataReader rd) { m_reader = rd; HasMore = true; }

                private string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }

                public string Hash { get { return m_reader.ConvertValueToString(2) ?? ""; } }
                public long Offset { get { return m_reader.ConvertValueToInt64(3); } }
                public long Index { get { return m_reader.ConvertValueToInt64(4); } }
                public long Size { get { return m_reader.ConvertValueToInt64(5); } }
                public bool IsMetadata { get { return !(m_reader.ConvertValueToInt64(9) == 0); } }

                public bool HasMore { get; private set; }

                public IEnumerable<IBlockSource> Blocksources
                {
                    get
                    {
                        var p = TargetPath;
                        var h = Hash;
                        var s = Size;

                        while (HasMore && p == TargetPath && h == Hash && s == Size)
                        {
                            yield return new BlockSource(m_reader);
                            HasMore = m_reader.Read();
                        }
                    }
                }
            }

            private readonly IDataReader m_reader;
            public LocalBlockSource(IDataReader rd) { m_reader = rd; HasMore = true; }

            public string TargetPath { get { return m_reader.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_reader.ConvertValueToInt64(1); } }

            public bool HasMore { get; private set; }

            public IEnumerable<IBlockDescriptor> Blocks
            {
                get
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
                            HasMore = m_reader.Read();
                    }
                }
            }

            public static IEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(IDbConnection connection, string filetablename, string blocktablename, long blocksize, bool skipMetadata)
            {
                // TODO: Skip metadata as required
                using (var cmd = connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""A"".""TargetPath"", ""A"".""ID"", ""B"".""Hash"", (""B"".""Index"" * {blocksize}), ""B"".""Index"", ""B"".""Size"", ""C"".""Path"", (""D"".""Index"" * {blocksize}), ""E"".""Size"", ""B"".""Metadata"" FROM ""{filetablename}"" ""A"", ""{blocktablename}"" ""B"", ""File"" ""C"", ""BlocksetEntry"" ""D"", ""Block"" E WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""C"".""BlocksetID"" = ""D"".""BlocksetID"" AND ""D"".""BlockID"" = ""E"".""ID"" AND ""B"".""Hash"" = ""E"".""Hash"" AND ""B"".""Size"" = ""E"".""Size"" AND ""B"".""Restored"" = 0")))
                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                    {
                        var more = true;
                        while (more)
                        {
                            var f = new LocalBlockSource(rd);
                            string current = f.TargetPath;
                            yield return f;

                            more = f.HasMore;
                            while (more && current == f.TargetPath)
                                more = rd.Read();
                        }
                    }
                }
            }
        }

        public IEnumerable<ILocalBlockSource> GetFilesAndSourceBlocks(bool skipMetadata, long blocksize)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return LocalBlockSource.GetFilesAndSourceBlocks(m_connection, m_tempfiletable, m_tempblocktable, blocksize, skipMetadata);
        }

        public IEnumerable<IRemoteVolume> GetMissingVolumes()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                // Return order from SQLite-DISTINCT is likely to be sorted by Name, which is bad for restore.
                // If the end of very large files (e.g. iso's) is restored before the beginning, most OS write out zeros to fill the file.
                // If we manage to get the volumes in an order restoring front blocks first, this can save time.
                // An optimal algorithm would build a dependency net with cycle resolution to find the best near topological
                // order of volumes, but this is a bit too fancy here.
                // We will just put a very simple heuristic to work, that will try to prefer volumes containing lower block indexes:
                // We just order all volumes by the maximum block index they contain. This query is slow, but should be worth the effort.
                // Now it is likely to restore all files from front to back. Large files will always be done last.
                // One could also use like the average block number in a volume, that needs to be measured.

                cmd.SetCommandAndParameters(FormatInvariant($@"SELECT ""RV"".""Name"", ""RV"".""Hash"", ""RV"".""Size"", ""BB"".""MaxIndex""
FROM ""RemoteVolume"" ""RV"" INNER JOIN
      (SELECT ""TB"".""VolumeID"", MAX(""TB"".""Index"") as ""MaxIndex""
         FROM ""{m_tempblocktable}"" ""TB""
        WHERE ""TB"".""Restored"" = 0
        GROUP BY  ""TB"".""VolumeID""
      ) as ""BB"" ON ""RV"".""ID"" = ""BB"".""VolumeID""
ORDER BY ""BB"".""MaxIndex"" "));

                using (var rd = cmd.ExecuteReader())
                {
                    object[] r = new object[3];
                    while (rd.Read())
                    {
                        rd.GetValues(r);
                        yield return new RemoteVolume(
                            rd.ConvertValueToString(0),
                            rd.ConvertValueToString(1),
                            rd.ConvertValueToInt64(2, -1)
                        );
                    }
                }
            }
        }

        public interface IFilesAndMetadata : IDisposable
        {
            IEnumerable<IVolumePatch> FilesWithMissingBlocks { get; }
            IEnumerable<IVolumePatch> MetadataWithMissingBlocks { get; }
        }

        private class FilesAndMetadata : IFilesAndMetadata
        {
            private readonly string m_tmptable;
            private readonly string m_filetablename;
            private readonly string m_blocktablename;
            private readonly long m_blocksize;

            private readonly IDbConnection m_connection;

            public FilesAndMetadata(IDbConnection connection, string filetablename, string blocktablename, long blocksize, BlockVolumeReader curvolume)
            {
                m_filetablename = filetablename;
                m_blocktablename = blocktablename;
                m_blocksize = blocksize;
                m_connection = connection;

                using (var c = m_connection.CreateCommand())
                {
                    m_tmptable = "VolumeFiles-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    c.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tmptable}"" ( ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL )"));

                    c.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tmptable}"" (""Hash"", ""Size"") VALUES (@Hash, @Size)"));
                    foreach (var s in curvolume.Blocks)
                    {
                        c.SetParameterValue("@Hash", s.Key);
                        c.SetParameterValue("@Size", s.Value);
                        c.ExecuteNonQuery();
                    }

                    // The index _HashSizeIndex is not needed anymore. Index on "Blocks-..." is used on Join in GetMissingBlocks
                }
            }

            public void Dispose()
            {
                if (m_tmptable != null)
                    using (var c = m_connection.CreateCommand())
                        c.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tmptable}"""));
            }

            private class VolumePatch : IVolumePatch
            {
                private class PatchBlock : IPatchBlock
                {
                    private readonly IDataReader m_reader;
                    public PatchBlock(IDataReader rd) { m_reader = rd; }

                    public long Offset { get { return m_reader.ConvertValueToInt64(2); } }
                    public long Size { get { return m_reader.ConvertValueToInt64(3); } }
                    public string Key { get { return m_reader.ConvertValueToString(4) ?? ""; } }
                }

                private readonly IDataReader m_reader;
                public VolumePatch(IDataReader rd) { m_reader = rd; HasMore = true; }

                public string Path { get { return m_reader.ConvertValueToString(0) ?? ""; } }
                public long FileID { get { return m_reader.ConvertValueToInt64(1); } }
                public bool HasMore { get; private set; }

                public IEnumerable<IPatchBlock> Blocks
                {
                    get
                    {
                        string p = Path;
                        while (HasMore && p == Path)
                        {
                            yield return new PatchBlock(m_reader);
                            HasMore = m_reader.Read();
                        }
                    }
                }
            }

            public IEnumerable<IVolumePatch> FilesWithMissingBlocks
            {
                get
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                        cmd.SetCommandAndParameters(FormatInvariant($@"  SELECT DISTINCT ""A"".""TargetPath"", ""BB"".""FileID"", (""BB"".""Index"" * {m_blocksize}), ""BB"".""Size"", ""BB"".""Hash""
FROM ""{m_filetablename}"" ""A"", ""{m_blocktablename}"" ""BB""
WHERE ""A"".""ID"" = ""BB"".""FileID"" AND ""BB"".""Restored"" = 0 AND ""BB"".""Metadata"" = {"0"}
AND ""BB"".""ID"" IN  (SELECT ""B"".""ID"" FROM ""{m_blocktablename}"" ""B"", ""{m_tmptable}"" ""C"" WHERE ""B"".""Hash"" = ""C"".""Hash"" AND ""B"".""Size"" = ""C"".""Size"")
ORDER BY ""A"".""TargetPath"", ""BB"".""Index"""));
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                var more = true;
                                while (more)
                                {
                                    var f = new VolumePatch(rd);
                                    var current = f.Path;
                                    yield return f;

                                    more = f.HasMore;
                                    while (more && current == f.Path)
                                        more = rd.Read();
                                }
                            }
                        }
                    }
                }
            }

            public IEnumerable<IVolumePatch> MetadataWithMissingBlocks
            {
                get
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        // The IN-clause with subquery enables SQLite to use indexes better. Three way join (A,B,C) is slow here!
                        cmd.SetCommandAndParameters(FormatInvariant($@"  SELECT DISTINCT ""A"".""TargetPath"", ""BB"".""FileID"", (""BB"".""Index"" * {m_blocksize}), ""BB"".""Size"", ""BB"".""Hash""
 FROM ""{m_filetablename}"" ""A"", ""{m_blocktablename}"" ""BB""
WHERE ""A"".""ID"" = ""BB"".""FileID"" AND ""BB"".""Restored"" = 0 AND ""BB"".""Metadata"" = {"1"}
  AND ""BB"".""ID"" IN  (SELECT ""B"".""ID"" FROM ""{m_blocktablename}"" ""B"", ""{m_tmptable}"" ""C"" WHERE ""B"".""Hash"" = ""C"".""Hash"" AND ""B"".""Size"" = ""C"".""Size"")
ORDER BY ""A"".""TargetPath"", ""BB"".""Index"""));
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                var more = true;
                                while (more)
                                {
                                    var f = new VolumePatch(rd);
                                    string current = f.Path;
                                    yield return f;

                                    more = f.HasMore;
                                    while (more && current == f.Path)
                                        more = rd.Read();
                                }
                            }
                        }
                    }
                }
            }
        }

        public IFilesAndMetadata GetMissingBlockData(BlockVolumeReader curvolume, long blocksize)
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            return new FilesAndMetadata(m_connection, m_tempfiletable, m_tempblocktable, blocksize, curvolume);
        }

        /// <summary>
        /// Returns a connection from the connection pool.
        /// </summary>
        /// <returns>A connection from the connection pool.</returns>
        public IDbConnection GetConnectionFromPool()
        {
            if (!m_connection_pool.TryTake(out var connection))
            {
                connection = SQLiteHelper.SQLiteLoader.LoadConnection();
                connection.ConnectionString = m_connection.ConnectionString + ";Cache=Shared;";
                connection.Open();

                SQLiteHelper.SQLiteLoader.ApplyCustomPragmas(connection, m_pagecachesize);

                using var cmd = connection.CreateCommand();
                cmd.ExecuteNonQuery("PRAGMA journal_mode = WAL;");
                cmd.ExecuteNonQuery("PRAGMA read_uncommitted = true;");
            }

            return connection;
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

        public IEnumerable<IFileToRestore> GetFilesToRestore(bool onlyNonVerified)
        {
            using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT ""{m_tempfiletable}"".""ID"", ""{m_tempfiletable}"".""TargetPath"", ""Blockset"".""FullHash"", ""Blockset"".""Length"" FROM ""{m_tempfiletable}"",""Blockset"" WHERE ""{m_tempfiletable}"".""BlocksetID"" = ""Blockset"".""ID"" AND ""{m_tempfiletable}"".""DataVerified"" <= @Verified"))
                .SetParameterValue("@Verified", !onlyNonVerified);

            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    yield return new FileToRestore(
                        rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "", rd.ConvertValueToString(2) ?? "", rd.ConvertValueToInt64(3));
        }

        /// <summary>
        /// Returns a list of files and symlinks to restore.
        /// </summary>
        /// <returns>A list of files and symlinks to restore.</returns>
        public IEnumerable<FileRequest> GetFilesAndSymlinksToRestore()
        {
            using var cmd = m_connection.CreateCommand(FormatInvariant($@"
                SELECT F.ID, F.Path, F.TargetPath, IFNULL(B.FullHash, ''), IFNULL(B.Length, 0), F.BlocksetID
                FROM ""{m_tempfiletable}"" F
                LEFT JOIN Blockset B ON F.BlocksetID = B.ID
                WHERE F.BlocksetID != {FOLDER_BLOCKSET_ID}"));
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                yield return new FileRequest(rd.ConvertValueToInt64(0), rd.ConvertValueToString(1), rd.ConvertValueToString(2), rd.ConvertValueToString(3), rd.ConvertValueToInt64(4), rd.ConvertValueToInt64(5));
        }

        /// <summary>
        /// Returns a list of folders to restore. Used to restore folder metadata.
        /// </summary>
        /// <returns>A list of folders to restore.</returns>
        public IEnumerable<FileRequest> GetFolderMetadataToRestore()
        {
            using var cmd = m_connection.CreateCommand();
            using var rd = cmd.ExecuteReader(FormatInvariant($@"
                SELECT F.ID, '', F.TargetPath, '', 0, {FOLDER_BLOCKSET_ID}
                FROM ""{m_tempfiletable}"" F
                WHERE F.BlocksetID = {FOLDER_BLOCKSET_ID} AND F.MetadataID IS NOT NULL AND F.MetadataID >= 0"));

            while (rd.Read())
                yield return new FileRequest(rd.ConvertValueToInt64(0), rd.ConvertValueToString(1), rd.ConvertValueToString(2), rd.ConvertValueToString(3), rd.ConvertValueToInt64(4), rd.ConvertValueToInt64(5));
        }

        /// <summary>
        /// Returns a list of blocks and their volume IDs. Used by the <see cref="BlockManager"/> to keep track of blocks and volumes to automatically evict them from the respective caches.
        /// </summary>
        /// <param name="skipMetadata">Flag indicating whether the returned blocks should exclude the metadata blocks.</param>
        /// <returns>A list of tuples containing the block ID and the volume ID of the block.</returns>
        public IEnumerable<(long, long)> GetBlocksAndVolumeIDs(bool skipMetadata)
        {
            using var cmd = Connection.CreateCommand();
            using var reader = cmd.ExecuteReader(FormatInvariant($@"
                SELECT ""Block"".""ID"", ""Block"".""VolumeID""
                FROM ""BlocksetEntry""
                INNER JOIN ""{m_tempfiletable}"" ON ""BlocksetEntry"".""BlocksetID"" = ""{m_tempfiletable}"".""BlocksetID""
                INNER JOIN ""Block"" ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                ")
                + (skipMetadata ? "" : FormatInvariant($@"
                UNION ALL
                SELECT ""Block"".""ID"", ""Block"".""VolumeID""
                FROM ""{m_tempfiletable}""
                INNER JOIN ""Metadataset"" ON ""{m_tempfiletable}"".""MetadataID"" = ""Metadataset"".""ID""
                INNER JOIN ""BlocksetEntry"" ON ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                INNER JOIN ""Block"" ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
            ")));
            while (reader.Read())
                yield return (reader.ConvertValueToInt64(0), reader.ConvertValueToInt64(1));
        }

        /// <summary>
        /// Returns a list of <see cref="BlockRequest"/> for the given blockset ID. It is used by the <see cref="FileProcessor"/> to restore the blocks of a file.
        /// </summary>
        /// <param name="blocksetID">The BlocksetID of the file.</param>
        /// <returns>A list of <see cref="BlockRequest"/> needed to restore the given file.</returns>
        public IEnumerable<BlockRequest> GetBlocksFromFile(long blocksetID)
        {
            var connection = GetConnectionFromPool();
            using var cmd = connection.CreateCommand(FormatInvariant(@$"
                SELECT ""Block"".""ID"", ""Block"".""Hash"", ""Block"".""Size"", ""Block"".""VolumeID""
                FROM ""BlocksetEntry"" INNER JOIN ""Block""
                ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                WHERE ""BlocksetEntry"".""BlocksetID"" = @BlocksetID"))
                .SetParameterValue("@BlocksetID", blocksetID);

            using var reader = cmd.ExecuteReader();
            for (long i = 0; reader.Read(); i++)
                yield return new BlockRequest(reader.ConvertValueToInt64(0), i, reader.ConvertValueToString(1), reader.ConvertValueToInt64(2), reader.ConvertValueToInt64(3), false);

            // Return the connection to the pool
            m_connection_pool.Add(connection);
        }

        /// <summary>
        /// Returns a list of <see cref="BlockRequest"/> for the metadata blocks of the given file. It is used by the <see cref="FileProcessor"/> to restore the metadata of a file.
        /// </summary>
        /// <param name="fileID">The ID of the file.</param>
        /// <returns>A list of <see cref="BlockRequest"/> needed to restore the metadata of the given file.</returns>
        public IEnumerable<BlockRequest> GetMetadataBlocksFromFile(long fileID)
        {
            var connection = GetConnectionFromPool();
            using var cmd = connection.CreateCommand(FormatInvariant($@"
                SELECT ""Block"".""ID"", ""Block"".""Hash"", ""Block"".""Size"", ""Block"".""VolumeID""
                FROM ""File""
                INNER JOIN ""Metadataset"" ON ""File"".""MetadataID"" = ""Metadataset"".""ID""
                INNER JOIN ""BlocksetEntry"" ON ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                INNER JOIN ""Block"" ON ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                WHERE ""File"".""ID"" = @FileID
            "))
            .SetParameterValue("@FileID", fileID);

            using var reader = cmd.ExecuteReader();
            for (long i = 0; reader.Read(); i++)
            {
                yield return new BlockRequest(reader.ConvertValueToInt64(0), i, reader.ConvertValueToString(1), reader.ConvertValueToInt64(2), reader.ConvertValueToInt64(3), false);
            }

            // Return the connection to the pool
            m_connection_pool.Add(connection);
        }

        /// <summary>
        /// Returns the volume information for the given volume ID. It is used by the <see cref="VolumeManager"/> to get the volume information for the given volume ID.
        /// </summary>
        /// <param name="VolumeID">The ID of the volume.</param>
        /// <returns>A tuple containing the name, size, and hash of the volume.</returns>
        public IEnumerable<(string, long, string)> GetVolumeInfo(long VolumeID)
        {
            using var cmd = m_connection.CreateCommand("SELECT Name, Size, Hash FROM RemoteVolume WHERE ID = @VolumeID")
                .SetParameterValue("@VolumeID", VolumeID);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                yield return (reader.ConvertValueToString(0) ?? "", reader.ConvertValueToInt64(1), reader.ConvertValueToString(2) ?? "");
        }

        public void DropRestoreTable()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_tempfiletable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tempfiletable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_tempfiletable = null; }

                if (m_tempblocktable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tempblocktable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_tempblocktable = null; }

                if (m_latestblocktable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_latestblocktable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_latestblocktable = null; }

                if (m_fileprogtable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_fileprogtable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_fileprogtable = null; }

                if (m_totalprogtable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_totalprogtable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_totalprogtable = null; }

                if (m_filesnewlydonetable != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_filesnewlydonetable}"""));
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "CleanupError", ex, "Cleanup error: {0}", ex.Message); }
                    finally { m_filesnewlydonetable = null; }

            }
        }

        public interface IBlockMarker : IDisposable
        {
            void SetBlockRestored(long targetfileid, long index, string hash, long blocksize, bool metadata);
            void SetAllBlocksMissing(long targetfileid);
            void SetAllBlocksRestored(long targetfileid, bool includeMetadata);
            void SetFileDataVerified(long targetfileid);
            void Commit();
            void UpdateProcessed(IOperationProgressUpdater writer);
        }

        /// <summary>
        /// A new implementation of IBlockMarker, marking the blocks directly in the blocks table as restored
        /// and reading statistics about progress from DB (kept up-to-date by triggers).
        /// There is no negative influence on performance, esp. since the block table is temporary anyway.
        /// </summary>
        private class DirectBlockMarker : IBlockMarker
        {
            private IDbCommand m_insertblockCommand;
            private IDbCommand m_resetfileCommand;
            private IDbCommand m_updateAsRestoredCommand;
            private IDbCommand m_updateFileAsDataVerifiedCommand;
            private IDbCommand m_statUpdateCommand;
            private IDbTransaction m_transaction;
            private bool m_hasUpdates = false;

            private readonly string m_blocktablename;
            private readonly string m_filetablename;

            public DirectBlockMarker(IDbConnection connection, string blocktablename, string filetablename, string statstablename)
            {
                m_transaction = connection.BeginTransactionSafe();
                m_blocktablename = blocktablename;
                m_filetablename = filetablename;

                m_insertblockCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"UPDATE ""{m_blocktablename}"" SET ""Restored"" = 1
WHERE ""FileID"" = @TargetFileId AND ""Index"" = @Index AND ""Hash"" = @Hash AND ""Size"" = @Size AND ""Metadata"" = @Metadata AND ""Restored"" = 0 "));

                m_resetfileCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"UPDATE ""{m_blocktablename}"" SET ""Restored"" = 0 WHERE ""FileID"" = @TargetFileId "));
                m_updateAsRestoredCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"UPDATE ""{m_blocktablename}"" SET ""Restored"" = 1 WHERE ""FileID"" = @TargetFileId AND ""Metadata"" <= @Metadata "));
                m_updateFileAsDataVerifiedCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"UPDATE ""{m_filetablename}"" SET ""DataVerified"" = 1 WHERE ""ID"" = @TargetFileId"));

                if (statstablename != null)
                {
                    // Fields in Stats: TotalFiles, TotalBlocks, TotalSize
                    //                  FilesFullyRestored, FilesPartiallyRestored, BlocksRestored, SizeRestored
                    m_statUpdateCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"SELECT SUM(""FilesFullyRestored""), SUM(""SizeRestored"") FROM ""{statstablename}"" "));
                }
                else // very slow fallback if stats tables were not created
                    m_statUpdateCommand = connection.CreateCommand(m_transaction, FormatInvariant($@"SELECT COUNT(DISTINCT ""FileID""), SUM(""Size"") FROM ""{m_blocktablename}"" WHERE ""Restored"" = 1 "));

            }

            public void UpdateProcessed(IOperationProgressUpdater updater)
            {
                if (!m_hasUpdates)
                    return;

                m_hasUpdates = false;
                using (var rd = m_statUpdateCommand.ExecuteReader())
                {
                    var filesprocessed = 0L;
                    var processedsize = 0L;

                    if (rd.Read())
                    {
                        filesprocessed += rd.ConvertValueToInt64(0, 0);
                        processedsize += rd.ConvertValueToInt64(1, 0);
                    }

                    updater.UpdatefilesProcessed(filesprocessed, processedsize);
                }
            }

            public void SetAllBlocksMissing(long targetfileid)
            {
                m_hasUpdates = true;
                var r = m_resetfileCommand.SetParameterValue("@TargetFileId", targetfileid)
                    .ExecuteNonQuery();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            public void SetAllBlocksRestored(long targetfileid, bool includeMetadata)
            {
                m_hasUpdates = true;
                var r = m_updateAsRestoredCommand.SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Metadata", includeMetadata ? 1 : 0)
                    .ExecuteNonQuery();
                if (r <= 0)
                    throw new Exception("Unexpected reset result");
            }

            public void SetFileDataVerified(long targetfileid)
            {
                m_hasUpdates = true;
                var r = m_updateFileAsDataVerifiedCommand.SetParameterValue("@TargetFileId", targetfileid)
                    .ExecuteNonQuery();
                if (r != 1)
                    throw new Exception("Unexpected result when marking file as verified.");
            }

            public void SetBlockRestored(long targetfileid, long index, string hash, long size, bool metadata)
            {
                m_hasUpdates = true;
                var r = m_insertblockCommand.SetParameterValue("@TargetFileId", targetfileid)
                    .SetParameterValue("@Index", index)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@Metadata", metadata)
                    .ExecuteNonQuery();
                if (r != 1)
                    throw new Exception("Unexpected result when marking block.");
            }

            public void Commit()
            {
                m_insertblockCommand.Dispose();
                m_insertblockCommand = null!;
                using (new Logging.Timer(LOGTAG, "CommitBlockMarker", "CommitBlockMarker"))
                    m_transaction.Commit();
                m_transaction.Dispose();
                m_transaction = null!;
            }

            public void Dispose()
            {
                m_insertblockCommand?.Dispose();
                m_resetfileCommand?.Dispose();
                m_updateAsRestoredCommand?.Dispose();
                m_updateFileAsDataVerifiedCommand?.Dispose();
                m_statUpdateCommand?.Dispose();
                m_transaction?.Dispose();
            }
        }

        public IBlockMarker CreateBlockMarker()
        {
            if (string.IsNullOrWhiteSpace(m_tempfiletable) || string.IsNullOrWhiteSpace(m_tempblocktable))
                throw new InvalidOperationException("No temporary file table set up for this restore.");
            if (string.IsNullOrWhiteSpace(m_totalprogtable))
                throw new InvalidOperationException("No progress table set up for this restore.");
            return new DirectBlockMarker(m_connection, m_tempblocktable, m_tempfiletable, m_totalprogtable);
        }

        public override void Dispose()
        {
            foreach (var connection in m_connection_pool)
            {
                connection.Close();
                connection.Dispose();
            }
            m_connection_pool.Clear();
            DropRestoreTable();
            base.Dispose();
        }

        public IEnumerable<string> GetTargetFolders()
        {
            using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT ""TargetPath"" FROM ""{m_tempfiletable}"" WHERE ""BlocksetID"" == @BlocksetID"))
                .SetParameterValue("@BlocksetID", FOLDER_BLOCKSET_ID);
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    yield return rd.ConvertValueToString(0) ?? "";
        }

        public interface IFastSource
        {
            string TargetPath { get; }
            long TargetFileID { get; }
            string SourcePath { get; }
            IEnumerable<IBlockEntry> Blocks { get; }
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
                private readonly IDataReader m_rd;
                private readonly long m_blocksize;
                public BlockEntry(IDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; }
                public long Offset { get { return m_rd.ConvertValueToInt64(3) * m_blocksize; } }
                public long Index { get { return m_rd.ConvertValueToInt64(3); } }
                public long Size { get { return m_rd.ConvertValueToInt64(5); } }
                public string Hash { get { return m_rd.ConvertValueToString(4) ?? ""; } }
            }

            private readonly IDataReader m_rd;
            private readonly long m_blocksize;
            public FastSource(IDataReader rd, long blocksize) { m_rd = rd; m_blocksize = blocksize; MoreData = true; }
            public bool MoreData { get; private set; }
            public string TargetPath { get { return m_rd.ConvertValueToString(0) ?? ""; } }
            public long TargetFileID { get { return m_rd.ConvertValueToInt64(2); } }
            public string SourcePath { get { return m_rd.ConvertValueToString(1) ?? ""; } }

            public IEnumerable<IBlockEntry> Blocks
            {
                get
                {
                    var tid = TargetFileID;

                    do
                    {
                        yield return new BlockEntry(m_rd, m_blocksize);
                    } while ((MoreData = m_rd.Read()) && tid == TargetFileID);

                }
            }
        }

        public IEnumerable<IFastSource> GetFilesAndSourceBlocksFast(long blocksize)
        {
            using (var transaction = m_connection.BeginTransactionSafe())
            using (var cmdReader = m_connection.CreateCommand())
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                cmd.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET ""LocalSourceExists"" = 1 WHERE Path = @Path"));
                using (var rd = cmdReader.ExecuteReader(FormatInvariant($@"SELECT DISTINCT ""{m_tempfiletable}"".""Path"" FROM ""{m_tempfiletable}""")))
                {
                    while (rd.Read())
                    {
                        var sourcepath = rd.ConvertValueToString(0);
                        if (SystemIO.IO_OS.FileExists(sourcepath))
                        {
                            cmd.SetParameterValue("@Path", sourcepath);
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "LocalSourceMissing", "Local source file not found: {0}", sourcepath);
                        }
                    }
                }

                //This localSourceExists index will make the query engine to start by searching FileSet table. As the result is ordered by FileSet.ID, we will get the cursor "instantly"
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tempfiletable}_LocalSourceExists"" ON ""{m_tempfiletable}"" (""LocalSourceExists"")"));
                transaction.Commit();
            }

            m_latestblocktable = "LatestBlocksetIds-" + m_temptabsetguid;

            var whereclause = FormatInvariant($@"
                ""{m_tempfiletable}"".""LocalSourceExists"" = 1 AND
                ""{m_tempblocktable}"".""Restored"" = 0 AND ""{m_tempblocktable}"".""Metadata"" = 0 AND
                ""{m_tempfiletable}"".""TargetPath"" != ""{m_tempfiletable}"".""Path""");

            var latestBlocksetIds = FormatInvariant($@"
                SELECT
                    ""File"".""Path"" AS ""PATH"",
                    ""File"".""BlocksetID"" AS ""BlocksetID"",
                    MAX(""Fileset"".""Timestamp"") AS ""Timestamp""
                FROM
                    ""File"",
                    ""FilesetEntry"",
                    ""Fileset""
                WHERE
                    ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND
                    ""FilesetEntry"".""FilesetID"" = ""Fileset"".""ID"" AND
                    ""File"".""Path"" IN
                        (SELECT DISTINCT
                            ""{m_tempfiletable}"".""Path""
                        FROM
                            ""{m_tempfiletable}"",
                            ""{m_tempblocktable}""
                        WHERE
                            ""{m_tempfiletable}"".""ID"" = ""{m_tempblocktable}"".""FileID"" AND
                            {whereclause})
                GROUP BY ""File"".""Path""");

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_latestblocktable}"" "));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_latestblocktable}"" AS {latestBlocksetIds}"));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_latestblocktable}_path"" ON ""{m_latestblocktable}"" (""Path"")"));

                cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""{m_tempfiletable}"" SET LatestBlocksetId = (SELECT BlocksetId FROM ""{m_latestblocktable}"" WHERE Path = ""{m_tempfiletable}"".Path)"));
            }

            var sources = FormatInvariant($@"
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
    ""{m_tempfiletable}"".""ID"" = ""{m_tempblocktable}"".""FileID"" AND
    ""BlocksetEntry"".""BlocksetID"" = ""{m_tempfiletable}"".""LatestBlocksetID"" AND
    ""BlocksetEntry"".""BlockID"" = ""{m_tempblocktable}"".""BlockID"" AND
    ""BlocksetEntry"".""Index"" = ""{m_tempblocktable}"".""Index"" AND
    {whereclause}
ORDER BY ""{m_tempfiletable}"".""ID"", ""{m_tempblocktable}"".""Index"" ");

            using (var cmd = m_connection.CreateCommand())
            using (var rd = cmd.ExecuteReader(sources))
            {
                if (rd.Read())
                {
                    bool more;
                    do
                    {
                        var n = new FastSource(rd, blocksize);
                        var tid = n.TargetFileID;
                        yield return n;

                        more = n.MoreData;
                        while (more && n.TargetFileID == tid)
                            more = rd.Read();

                    } while (more);
                }
            }
        }

    }
}
