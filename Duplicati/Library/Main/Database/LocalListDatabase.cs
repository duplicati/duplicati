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
using System.Data;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListDatabase : LocalDatabase
    {
        public LocalListDatabase(string path, long pagecachesize)
            : base(path, "List", false, pagecachesize)
        {
            ShouldCloseConnection = true;
        }

        public interface IFileversion
        {
            string? Path { get; }
            IEnumerable<long> Sizes { get; }
        }

        public interface IFileset
        {
            long Version { get; }
            int IsFullBackup { get; set; }
            DateTime Time { get; }
            long FileCount { get; }
            long FileSizes { get; }
        }

        public interface IFileSets : IDisposable
        {
            IEnumerable<IFileset> Sets { get; }
            IEnumerable<IFileset> QuickSets { get; }
            IEnumerable<IFileversion> SelectFiles(IFilter filter);
            IEnumerable<IFileversion> GetLargestPrefix(IFilter filter);
            IEnumerable<IFileversion> SelectFolderContents(IFilter filter);
            void TakeFirst();
        }

        private class FileSets : IFileSets
        {
            private readonly IDbConnection m_connection;
            private string m_tablename;
            private readonly KeyValuePair<long, DateTime>[] m_filesets;

            public FileSets(LocalListDatabase owner, DateTime time, long[] versions)
            {
                m_connection = owner.m_connection;
                m_filesets = owner.FilesetTimes.ToArray();
                m_tablename = "Filesets-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var tmp = owner.GetFilelistWhereClause(time, versions, m_filesets);
                var query = tmp.Item1;
                var args = tmp.Item2;

                using (var cmd = m_connection.CreateCommand())
                {
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tablename}"" AS SELECT DISTINCT ""ID"" AS ""FilesetID"", ""IsFullBackup"" AS ""IsFullBackup"" , ""Timestamp"" AS ""Timestamp"" FROM ""Fileset"" {query}"), args);
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{m_tablename}_FilesetIDTimestampIndex"" ON ""{m_tablename}"" (""FilesetID"", ""Timestamp"" DESC)"));
                }
            }

            private class Fileset : IFileset
            {
                public long Version { get; private set; }
                public int IsFullBackup { get; set; }
                public DateTime Time { get; private set; }
                public long FileCount { get; private set; }
                public long FileSizes { get; private set; }

                public Fileset(long version, int isFullBackup, DateTime time, long filecount, long filesizes)
                {
                    Version = version;
                    IsFullBackup = isFullBackup;
                    Time = time;
                    FileCount = filecount;
                    FileSizes = filesizes;
                }
            }

            private class Fileversion : IFileversion
            {
                private readonly IDataReader m_reader;
                public string? Path { get; private set; }
                public bool More { get; private set; }

                public Fileversion(IDataReader reader)
                {
                    m_reader = reader;
                    Path = reader.ConvertValueToString(0);
                    More = true;
                }

                public IEnumerable<long> Sizes
                {
                    get
                    {
                        while (More && Path == m_reader.ConvertValueToString(0))
                        {
                            yield return m_reader.ConvertValueToInt64(1, -1);
                            More = m_reader.Read();
                        }
                    }
                }
            }

            private class FileversionFixed : IFileversion
            {
                public string? Path { get; internal set; }
                public IEnumerable<long> Sizes { get { return new long[0]; } }
            }

            public IEnumerable<IFileversion> GetLargestPrefix(IFilter filter)
            {
                return GetLargestPrefix(filter, null);
            }

            private IEnumerable<IFileversion> GetLargestPrefix(IFilter filter, string? prefixrule)
            {
                using (var tmpnames = new FilteredFilenameTable(m_connection, filter, null))
                using (var cmd = m_connection.CreateCommand())
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{tmpnames.Tablename}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{m_tablename}"") ) "));

                    //If we have a prefix rule, apply it
                    if (!string.IsNullOrWhiteSpace(prefixrule))
                        cmd.SetCommandAndParameters(FormatInvariant($@"DELETE FROM ""{tmpnames.Tablename}"" WHERE SUBSTR(""Path"", 1, {prefixrule.Length}) != @Rule"))
                            .SetParameterValue("@Rule", prefixrule)
                            .ExecuteNonQuery();

                    // Then we recursively find the largest prefix
                    var v0 = cmd.ExecuteScalar(FormatInvariant($@"SELECT ""Path"" FROM ""{tmpnames.Tablename}"" ORDER BY LENGTH(""Path"") DESC LIMIT 1"));
                    var maxpath = "";
                    if (v0 != null)
                        maxpath = v0.ToString() ?? "";

                    var dirsep = Util.GuessDirSeparator(maxpath);

                    var filecount = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM ""{tmpnames.Tablename}"""), 0);
                    var foundfiles = -1L;

                    //TODO: Handle FS case-sensitive?
                    cmd.SetCommandAndParameters(FormatInvariant($@"SELECT COUNT(*) FROM ""{tmpnames.Tablename}"" WHERE SUBSTR(""Path"", 1, @PrefixLength) = @Prefix"));

                    while (filecount != foundfiles && maxpath.Length > 0)
                    {
                        var mp = Util.AppendDirSeparator(maxpath, dirsep);
                        foundfiles = cmd.SetParameterValue("@PrefixLength", mp.Length)
                            .SetParameterValue("@Prefix", mp)
                            .ExecuteScalarInt64(0);

                        if (filecount != foundfiles)
                        {
                            var oldlen = maxpath.Length;
                            var lix = maxpath.LastIndexOf(dirsep, maxpath.Length - 2, StringComparison.Ordinal);

                            maxpath = maxpath.Substring(0, lix + 1);
                            if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen || maxpath == "\\\\")
                                maxpath = "";
                        }
                    }

                    // Special handling for Windows and multi-drive/UNC backups as they do not have a single common root
                    if (string.IsNullOrWhiteSpace(maxpath) && string.IsNullOrWhiteSpace(prefixrule))
                    {
                        var paths = cmd.ExecuteReaderEnumerable(FormatInvariant($@"SELECT Path FROM ""{tmpnames.Tablename}""")).Select(x => x.ConvertValueToString(0) ?? "").ToArray();
                        var roots = paths.Select(x => x.Substring(0, 1)).Distinct().Where(x => x != "\\").ToArray();

                        //unc path like \\server.domain\
                        var regexUNCPrefix = new System.Text.RegularExpressions.Regex(@"^\\\\.*?\\");
                        var rootsUNC = paths.Select(x => regexUNCPrefix.Match(x)).Where(x => x.Success).Select(x => x.Value).Distinct().ToArray();

                        return roots.Concat(rootsUNC).Select(x => GetLargestPrefix(filter, x).First()).Distinct().ToArray();
                    }

                    return [new FileversionFixed { Path = maxpath == "" ? "" : Util.AppendDirSeparator(maxpath, dirsep) }];
                }
            }

            private IEnumerable<string> SelectFolderEntries(IDbCommand cmd, string prefix, string table)
            {
                if (!string.IsNullOrEmpty(prefix))
                    prefix = Util.AppendDirSeparator(prefix, Util.GuessDirSeparator(prefix));

                var ppl = prefix.Length;
                using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT DISTINCT ""Path"" FROM ""{table}"" ")))
                    while (rd.Read())
                    {
                        var s = rd.ConvertValueToString(0) ?? "";
                        if (!s.StartsWith(prefix, StringComparison.Ordinal))
                            continue;

                        var dirsep = Util.GuessDirSeparator(s);

                        s = s.Substring(ppl);
                        var ix = s.IndexOf(dirsep, StringComparison.Ordinal);
                        if (ix > 0 && ix != s.Length - 1)
                            s = s.Substring(0, ix + 1);
                        yield return prefix + s;
                    }
            }

            public IEnumerable<IFileversion> SelectFolderContents(IFilter filter)
            {
                var tbname = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    string pathprefix;
                    if (filter == null || filter.Empty)
                        pathprefix = "";
                    else if (filter as FilterExpression == null || ((FilterExpression)filter).Type != FilterType.Simple || ((FilterExpression)filter).GetSimpleList().Length != 1)
                        throw new ArgumentException("Filter for list-folder-contents must be a path prefix with no wildcards", nameof(filter));
                    else
                        pathprefix = ((FilterExpression)filter).GetSimpleList().First();

                    var dirsep = Util.GuessDirSeparator(pathprefix);

                    if (pathprefix.Length > 0 || dirsep == "/")
                        pathprefix = Util.AppendDirSeparator(pathprefix, dirsep);

                    using (var tmpnames = new FilteredFilenameTable(m_connection, new FilterExpression(new string[] { pathprefix + "*" }, true), null))
                    using (var cmd = m_connection.CreateCommand())
                    {
                        //First we trim the filelist to exclude filenames not found in any of the filesets
                        cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{tmpnames.Tablename}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{m_tablename}"") ) "));

                        // If we had instr support this would work:
                        /*var distinctPaths = @"SELECT DISTINCT :1 || " +
                            @"CASE(INSTR(SUBSTR(""Path"", :2), '/')) " +
                            @"WHEN 0 THEN SUBSTR(""Path"", :2) " +
                            @"ELSE SUBSTR(""Path"", :2,  INSTR(SUBSTR(path, :2), '/')) " +
                            @"END AS ""Path"", ""FilesetID"" " +
                            @" FROM (" + cartesianPathFileset + @")";*/

                        // Instead we manually iterate the paths
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tbname}"" (""Path"" TEXT NOT NULL)"));

                        using (var c2 = m_connection.CreateCommand())
                        {
                            c2.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{tbname}"" (""Path"") VALUES (@Path)"));
                            foreach (var n in SelectFolderEntries(cmd, pathprefix, tmpnames.Tablename).Distinct())
                                c2.SetParameterValue("@Path", n)
                                    .ExecuteNonQuery();

                            c2.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{tbname}_PathIndex"" ON ""{tbname}"" (""Path"")"));
                        }

                        //Then we select the matching results
                        var filesets = FormatInvariant($@"SELECT ""FilesetID"", ""Timestamp"" FROM ""{m_tablename}"" ORDER BY ""Timestamp"" DESC");
                        var cartesianPathFileset = FormatInvariant($@"SELECT ""A"".""Path"", ""B"".""FilesetID"" FROM ""{tbname}"" A, ({filesets}) B ORDER BY ""A"".""Path"" ASC, ""B"".""Timestamp"" DESC");

                        var filesWithSizes = FormatInvariant($@"SELECT ""Length"", ""FilesetEntry"".""FilesetID"", ""File"".""Path"" FROM ""Blockset"", ""FilesetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND FilesetEntry.""FilesetID"" IN (SELECT DISTINCT ""FilesetID"" FROM ""{m_tablename}"") ");
                        var query = @"SELECT ""C"".""Path"", ""D"".""Length"", ""C"".""FilesetID"" FROM (" + cartesianPathFileset + @") C LEFT OUTER JOIN (" + filesWithSizes + @") D ON ""C"".""FilesetID"" = ""D"".""FilesetID"" AND ""C"".""Path"" = ""D"".""Path""";

                        using (var rd = cmd.ExecuteReader(query))
                            if (rd.Read())
                            {
                                bool more;
                                do
                                {
                                    var f = new Fileversion(rd);
                                    if (!(string.IsNullOrWhiteSpace(f.Path) || f.Path == pathprefix))
                                    {
                                        yield return f;
                                        more = f.More;
                                    }
                                    else
                                    {
                                        more = rd.Read();
                                    }

                                } while (more);
                            }
                    }
                }
                finally
                {
                    try
                    {
                        using (var c = m_connection.CreateCommand())
                            c.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{tbname}"""));
                    }
                    catch
                    {
                    }
                }
            }

            public IEnumerable<IFileversion> SelectFiles(IFilter filter)
            {
                using (var tmpnames = new FilteredFilenameTable(m_connection, filter, null))
                using (var cmd = m_connection.CreateCommand())
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{tmpnames.Tablename}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{m_tablename}"") ) "));

                    //Then we select the matching results
                    var filesets = FormatInvariant($@"SELECT ""FilesetID"", ""Timestamp"" FROM ""{m_tablename}"" ORDER BY ""Timestamp"" DESC");
                    var cartesianPathFileset = FormatInvariant($@"SELECT ""A"".""Path"", ""B"".""FilesetID"" FROM ""{tmpnames.Tablename}"" A, ({filesets}) B ORDER BY ""A"".""Path"" ASC, ""B"".""Timestamp"" DESC");
                    var filesWithSizes = FormatInvariant($@"SELECT ""Length"", ""FilesetEntry"".""FilesetID"", ""File"".""Path"" FROM ""Blockset"", ""FilesetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FileID"" = ""File"".""ID""  AND FilesetEntry.""FilesetID"" IN (SELECT DISTINCT ""FilesetID"" FROM ""{m_tablename}"") ");
                    var query = @"SELECT ""C"".""Path"", ""D"".""Length"", ""C"".""FilesetID"" FROM (" + cartesianPathFileset + @") C LEFT OUTER JOIN (" + filesWithSizes + @") D ON ""C"".""FilesetID"" = ""D"".""FilesetID"" AND ""C"".""Path"" = ""D"".""Path""";
                    using (var rd = cmd.ExecuteReader(query))
                        if (rd.Read())
                        {
                            bool more;
                            do
                            {
                                var f = new Fileversion(rd);
                                yield return f;
                                more = f.More;
                            } while (more);
                        }
                }
            }

            public void TakeFirst()
            {
                using (var cmd = m_connection.CreateCommand())
                    cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""{m_tablename}"" WHERE ""FilesetID"" NOT IN (SELECT ""FilesetID"" FROM ""{m_tablename}"" ORDER BY ""Timestamp"" DESC LIMIT 1 )"));
            }

            public IEnumerable<IFileset> QuickSets
            {
                get
                {
                    var dict = new Dictionary<long, long>();
                    for (var i = 0; i < m_filesets.Length; i++)
                        dict[m_filesets[i].Key] = i;

                    using (var cmd = m_connection.CreateCommand())
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""ID"", ""IsFullBackup"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC "))
                        while (rd.Read())
                        {
                            var id = rd.ConvertValueToInt64(0);
                            var backupType = rd.GetInt32(1);
                            var e = dict[id];
                            yield return new Fileset(e, backupType, m_filesets[e].Value, -1L, -1L);
                        }
                }
            }

            public IEnumerable<IFileset> Sets
            {
                get
                {
                    var dict = new Dictionary<long, long>();
                    for (var i = 0; i < m_filesets.Length; i++)
                        dict[m_filesets[i].Key] = i;

                    var summation = FormatInvariant(
                        $@"SELECT ""A"".""FilesetID"" AS ""FilesetID"", COUNT(*) AS ""FileCount"", SUM(""C"".""Length"") AS ""FileSizes"" FROM ""FilesetEntry"" A, ""File"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" AND ""A"".""FilesetID"" IN (SELECT DISTINCT ""FilesetID"" FROM ""{m_tablename}"") GROUP BY ""A"".""FilesetID"" ");

                    using (var cmd = m_connection.CreateCommand())
                    using (var rd = cmd.ExecuteReader(FormatInvariant(
                        $@"SELECT DISTINCT ""A"".""FilesetID"", ""A"".""IsFullBackup"", ""B"".""FileCount"", ""B"".""FileSizes"" FROM ""{m_tablename}"" A LEFT OUTER JOIN ( {summation} ) B ON ""A"".""FilesetID"" = ""B"".""FilesetID"" ORDER BY ""A"".""Timestamp"" DESC "))
                    )
                        while (rd.Read())
                        {
                            var id = rd.ConvertValueToInt64(0);
                            var isFullBackup = rd.GetInt32(1);
                            var e = dict[id];
                            var filecount = rd.ConvertValueToInt64(2, -1L);
                            var filesizes = rd.ConvertValueToInt64(3, -1L);

                            yield return new Fileset(e, isFullBackup, m_filesets[e].Value, filecount, filesizes);
                        }
                }
            }

            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using (var cmd = m_connection.CreateCommand())
                            cmd.ExecuteNonQuery(FormatInvariant(@$"DROP TABLE IF EXISTS ""{m_tablename}"" "));
                    }
                    catch { }
                    finally { m_tablename = null!; }
                }

            }
        }

        public IFileSets SelectFileSets(DateTime time, long[] versions)
        {
            return new FileSets(this, time, versions);
        }

        /// <summary>
        /// Represents a fileset entry in the list fileset results.
        /// </summary>
        /// <param name="Version">The fileset version
        /// <param name="ID">The fileset ID</param>
        /// <param name="IsFullBackup">Flag indicating if the backup is a full backup or synthetic backup</param>
        /// <param name="Time">The timestamp of the fileset</param>
        /// <param name="FileCount">The number of files in the fileset</param>
        /// <param name="FileSizes">>The total size of files in the fileset</param>
        private sealed record FilesetEntry(long Version, long ID, bool? IsFullBackup, DateTime Time, long? FileCount, long? FileSizes) : IListFilesetResultFileset;

        /// <summary>
        /// Lists all filesets with summary data
        /// </summary>
        /// <returns>A collection of fileset entries</returns>
        public IEnumerable<IListFilesetResultFileset> ListFilesetsExtended()
        {
            const string query = @"
SELECT 
    f.""ID"" AS ""FilesetID"",
    f.""Timestamp"",
    f.""IsFullBackup"",
    COUNT(fe.""FileID"") AS ""NumberOfFiles"",
    COALESCE(SUM(b.""Length""), 0) AS ""TotalFileSize""
FROM 
    ""Fileset"" f
LEFT JOIN 
    ""FilesetEntry"" fe ON f.""ID"" = fe.""FilesetID""
LEFT JOIN 
    ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
LEFT JOIN 
    ""Blockset"" b ON fl.""BlocksetID"" = b.""ID""
GROUP BY 
    f.""ID""
ORDER BY 
    f.""Timestamp"" DESC;
";

            var version = 0L;
            using (var cmd = m_connection.CreateCommand(query))
                foreach (var rd in cmd.ExecuteReaderEnumerable())
                {
                    var id = rd.ConvertValueToInt64(0);
                    var time = ParseFromEpochSeconds(rd.ConvertValueToInt64(1));
                    var isFullBackup = rd.GetInt32(2) == BackupType.FULL_BACKUP;
                    var filecount = rd.ConvertValueToInt64(3, -1L);
                    var filesizes = rd.ConvertValueToInt64(4, -1L);
                    yield return new FilesetEntry(version++, id, isFullBackup, time, filecount, filesizes);
                }
        }

        /// <summary>
        /// Represents a folder entry in the list folder results.
        /// </summary>
        /// <param name="Path">The path of the folder entry.</param>
        /// <param name="Size">The size of the folder entry.</param>
        /// <param name="IsDirectory">Indicates if the entry is a directory.</param>
        /// <param name="IsSymlink">Indicates if the entry is a symbolic link.</param>
        /// <param name="LastModified">The last modified date of the folder entry.</param>
        private sealed record FolderEntry(string Path, long Size, bool IsDirectory, bool IsSymlink, DateTime LastModified) : IListFolderEntry;

        /// <summary>
        /// Lists the folder entries for a given fileset and prefix IDs.
        /// </summary>
        /// <param name="prefixIds">The prefix IDs to filter the folder entries.</param>
        /// <param name="filesetid">The fileset ID to filter the folder entries.</param>
        /// <param name="offset">The offset for pagination.</param>
        /// <param name="limit">>The limit for pagination.</param>
        /// <returns>A paginated result set of folder entries.</returns>
        public IPaginatedResults<IListFolderEntry> ListFolder(IEnumerable<long> prefixIds, long filesetid, long offset, long limit)
        {
            if (offset != 0 && limit <= 0)
                throw new ArgumentException("Cannot use offset without limit specified.", nameof(offset));
            if (limit <= 0)
                limit = long.MaxValue;

            using var cmd = m_connection.CreateCommand();

            if (prefixIds.Count() == 0)
                return new PaginatedResults<IListFolderEntry>(0, (int)limit, 0, 0, Enumerable.Empty<IListFolderEntry>());

            // Then query the matching files
            cmd.SetCommandAndParameters($@"
        SELECT 
            pp.""Prefix"" || fl.""Path"" AS ""FullPath"",
            b.""Length"",
            (CASE WHEN fl.""BlocksetID"" = @FolderBlocksetId THEN 1 ELSE 0 END) AS ""IsDirectory"",
            (CASE WHEN fl.""BlocksetID"" = @SymlinkBlocksetId THEN 1 ELSE 0 END) AS ""IsSymlink"",
            fe.""Lastmodified""
        FROM ""FilesetEntry"" fe
        INNER JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
        INNER JOIN ""PathPrefix"" pp ON fl.""PrefixID"" = pp.""ID""
        LEFT JOIN ""Blockset"" b ON fl.""BlocksetID"" = b.""ID""
        WHERE fe.""FilesetID"" = @filesetid AND fl.""PrefixID"" IN (@PrefixIds)
        ORDER BY pp.""Prefix"" ASC, fl.""Path"" ASC
        LIMIT @limit OFFSET @offset
    ")
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                .ExpandInClauseParameter("@PrefixIds", prefixIds)
                .SetParameterValue("@filesetid", filesetid)
                .SetParameterValue("@limit", limit)
                .SetParameterValue("@offset", offset);

            var results = new List<IListFolderEntry>();
            foreach (var rd in cmd.ExecuteReaderEnumerable())
            {
                var path = rd.ConvertValueToString(0) ?? string.Empty;
                var size = rd.ConvertValueToInt64(-1);
                var isDir = rd.GetInt32(2) != 0;
                var isSymlink = rd.GetInt32(3) != 0;
                var lastModified = new DateTime(rd.ConvertValueToInt64(4, 0), DateTimeKind.Utc);

                results.Add(new FolderEntry(path, size, isDir, isSymlink, lastModified));
            }

            long totalCount;
            if (offset != 0)
            {
                // Only calculate the total for the first query
                totalCount = -1;
            }
            else if (results.Count <= limit)
            {
                // If we have all results, we already know the total
                totalCount = results.Count;
            }
            else
            {
                // Get the total count of files for the given fileset and prefix IDs
                totalCount = cmd.SetCommandAndParameters($@"
                    SELECT COUNT(*)
                    FROM FilesetEntry fe
                    INNER JOIN FileLookup fl ON fe.FileID = fl.ID
                    WHERE fe.FilesetID = @filesetid
                    AND fl.PrefixID IN (@PrefixIds)
                ")
                .ExpandInClauseParameter("@PrefixIds", prefixIds)
                .SetParameterValue("@filesetid", filesetid)
                .ExecuteScalarInt64(0);
            }

            // Return paginated results
            var intLimit = limit > int.MaxValue ? int.MaxValue : (int)limit;
            return new PaginatedResults<IListFolderEntry>((int)(offset / limit), (int)intLimit, (int)((totalCount + limit - 1) / limit), totalCount, results);
        }


        /// <summary>
        /// Gets the prefix IDs for a given set of folder-prefixes.
        /// </summary>
        /// <param name="prefixes">The prefixes to get the IDs for.</param>
        /// <returns>>A list of prefix IDs.</returns>
        public IEnumerable<long> GetPrefixIds(IEnumerable<string> prefixes)
        {
            using var cmd = m_connection.CreateCommand();

            // Map folders to prefix IDs
            cmd.SetCommandAndParameters($@"SELECT ""ID"" FROM ""PathPrefix"" WHERE ""Prefix"" IN (@Prefixes)")
                .ExpandInClauseParameter("@Prefixes", prefixes);

            return cmd.ExecuteReaderEnumerable()
                .Select(x => x.ConvertValueToInt64(0))
                .ToList();
        }

        /// <summary>
        /// Gets the minimal unique prefix entries for a given fileset ID.
        /// This returns the roots of all unique paths in the fileset.
        /// </summary>
        /// <param name="filesetId">The fileset id</param>
        /// <returns>The entries that are unique roots</returns>
        public IList<IListFolderEntry> GetMinimalUniquePrefixEntries(long filesetId)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters(@"
                SELECT
                    pp.""Prefix"" || fl.""Path"" AS ""FullPath"",
                    b.""Length"",
                    CASE WHEN fl.""BlocksetID"" = -100 THEN 1 ELSE 0 END AS ""IsDirectory"",
                    CASE WHEN fl.""BlocksetID"" = -200 THEN 1 ELSE 0 END AS ""IsSymlink"",
                    fe.""Lastmodified""
                FROM ""FilesetEntry"" fe
                JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
                JOIN ""PathPrefix"" pp ON fl.""PrefixID"" = pp.""ID""
                LEFT JOIN ""Blockset"" b ON fl.""BlocksetID"" = b.""ID""
                WHERE fe.""FilesetID"" = @FilesetId
                ORDER BY ""FullPath"";
            ")
                .SetParameterValue("@FilesetId", filesetId);

            string? lastRoot = null;
            var results = new List<IListFolderEntry>();

            foreach (var rd in cmd.ExecuteReaderEnumerable())
            {
                var path = rd.ConvertValueToString(0) ?? string.Empty;
                if (lastRoot == null || !path.StartsWith(lastRoot, StringComparison.Ordinal))
                {
                    lastRoot = path;
                    var size = rd.ConvertValueToInt64(1, -1);
                    var isDir = rd.GetInt32(2) != 0;
                    var isSymlink = rd.GetInt32(3) != 0;
                    var lastModified = new DateTime(rd.ConvertValueToInt64(4, 0), DateTimeKind.Utc);

                    results.Add(new FolderEntry(path, size, isDir, isSymlink, lastModified));
                }
            }

            return results;
        }

        /// <summary>
        /// Lists all versions of specific file paths, optionally filtered by fileset IDs.
        /// </summary>
        /// <param name="paths">Paths to match exactly.</param>
        /// <param name="filesetIds">Optional fileset IDs to restrict the search to.</param>
        /// <param name="offset">Pagination offset.</param>
        /// <param name="limit">Pagination limit.</param>
        /// <returns>All matching file versions ordered by path and then version time.</returns>
        public IPaginatedResults<IListFileVersion> ListFileVersions(IEnumerable<string> paths, long[]? filesetIds, long offset, long limit)
        {
            if (paths == null || !paths.Any())
                return new PaginatedResults<IListFileVersion>(0, (int)(limit > 0 ? limit : long.MaxValue), 0, 0, Enumerable.Empty<IListFileVersion>());

            if (offset != 0 && limit <= 0)
                throw new ArgumentException("Cannot use offset without limit specified.", nameof(offset));
            if (limit <= 0)
                limit = long.MaxValue;

            using var cmd = m_connection.CreateCommand();

            var countWhere = "WHERE fl.\"Path\" IN (@Paths)";
            if (filesetIds != null && filesetIds.Length > 0)
                countWhere += " AND fe.\"FilesetID\" IN (@FilesetIds)";

            cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC");

            var versionMap = cmd.ExecuteReaderEnumerable()
                .Select((x, i) => (Version: i, FilesetId: x.ConvertValueToInt64(0)))
                .ToDictionary(x => x.FilesetId, x => x.Version);

            // Then fetch the actual data
            var fetchSql = @"
        SELECT 
            fe.""FilesetID"",
            f.""Timestamp"",
            fl.""Path"",
            COALESCE(b.""Length"", 0) AS ""Size"",
            CASE WHEN fl.""BlocksetID"" = @FolderBlocksetId THEN 1 ELSE 0 END AS ""IsDirectory"",
            CASE WHEN fl.""BlocksetID"" = @SymlinkBlocksetId THEN 1 ELSE 0 END AS ""IsSymlink"",
            fe.""Lastmodified""
        FROM ""FilesetEntry"" fe
        INNER JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
        INNER JOIN ""Fileset"" f ON fe.""FilesetID"" = f.""ID""
        LEFT JOIN ""Blockset"" b ON fl.""BlocksetID"" = b.""ID""
    " + countWhere + @"
        ORDER BY fl.""Path"" ASC, f.""Timestamp"" ASC
        LIMIT @limit OFFSET @offset";

            cmd.SetCommandAndParameters(fetchSql)
               .ExpandInClauseParameter("@Paths", paths)
               .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
               .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
               .SetParameterValue("@limit", limit)
               .SetParameterValue("@offset", offset);

            if (filesetIds != null && filesetIds.Length > 0)
                cmd.ExpandInClauseParameter("@FilesetIds", filesetIds);

            var results = new List<IListFileVersion>();
            foreach (var rd in cmd.ExecuteReaderEnumerable())
            {
                var version = versionMap[rd.ConvertValueToInt64(0, -1)];
                var time = new DateTime(rd.ConvertValueToInt64(1, 0), DateTimeKind.Utc);
                var path = rd.ConvertValueToString(2) ?? string.Empty;
                var size = rd.ConvertValueToInt64(3, 0);
                var isDirectory = rd.GetInt32(4) != 0;
                var isSymlink = rd.GetInt32(5) != 0;
                var lastModified = new DateTime(rd.ConvertValueToInt64(6, 0), DateTimeKind.Utc);

                results.Add(new ListFileVersion(version, time, path, size, isDirectory, isSymlink, lastModified));
            }

            long totalCount;
            if (offset != 0)
            {
                // Only calculate the total for the first query
                totalCount = -1;
            }
            else if (results.Count <= limit)
            {
                // If we have all results, we already know the total
                totalCount = results.Count;
            }
            else
            {
                // Get total count
                var countSql = @"
                    SELECT COUNT(*)
                    FROM ""FilesetEntry"" fe
                    INNER JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
                ";

                cmd.SetCommandAndParameters(countSql + "\n" + countWhere)
                   .ExpandInClauseParameter("@Paths", paths);

                if (filesetIds != null && filesetIds.Length > 0)
                    cmd.ExpandInClauseParameter("@FilesetIds", filesetIds);

                totalCount = cmd.ExecuteScalarInt64(0);
            }

            var intLimit = limit > int.MaxValue ? int.MaxValue : (int)limit;
            var totalPages = (int)((totalCount + limit - 1) / limit);

            return new PaginatedResults<IListFileVersion>(
                (int)(offset / limit),
                intLimit,
                totalPages,
                totalCount,
                results
            );
        }

        /// <summary>
        /// Searches for file versions matching a filter in the specified path prefixes.
        /// </summary>
        /// <param name="pathprefixes">The path prefixes to search in, or search in all if empty.</param>
        /// <param name="filter">The filter to match against file paths.</param>
        /// <param name="filesetIds">Optional fileset IDs to restrict the search to.</param>
        /// <param name="offset">The offset for pagination.</param>
        /// <param name="limit">The limit for pagination.</param>
        /// <returns>All matching file versions ordered by path and then version time.</returns>
        public IPaginatedResults<ISearchFileVersion> SearchEntries(IEnumerable<string>? pathprefixes, IFilter filter, long[]? filesetIds, long offset, long limit)
        {
            if (offset != 0 && limit <= 0)
                throw new ArgumentException("Cannot use offset without limit specified.", nameof(offset));
            if (limit <= 0)
                limit = long.MaxValue;

            using var cmd = m_connection.CreateCommand();

            // Analyze the filter to determine the default behavior
            FilterExpression.AnalyzeFilters(filter, out var includes, out var excludes);
            var defaultExclude = includes && !excludes; // true = exclude unmatched, false = include unmatched
            var defaultBehavior = defaultExclude ? 1 : 0;
            var caseSensitive = false; //TODO: Should we expose this? Or use: Library.Utility.Utility.IsFSCaseSensitive?

            var filterProps = new Dictionary<string, object?>();
            var caseWhenParts = new List<string>();

            var filterExpressions = new List<FilterExpression>();
            if (filter is FilterExpression fe)
            {
                filterExpressions.Add(fe);
            }
            else if (filter is JoinedFilterExpression jfe)
            {
                var work = new Queue<IFilter>([jfe.First, jfe.Second]);
                while (work.Count > 0)
                {
                    var current = work.Dequeue();
                    if (current is FilterExpression fe1)
                    {
                        filterExpressions.Add(fe1);
                    }
                    else if (current is JoinedFilterExpression jfe1)
                    {
                        work.Enqueue(jfe1.First);
                        work.Enqueue(jfe1.Second);
                    }
                    else
                    {
                        throw new UserInformationException($"Filter type {current.GetType()} is not supported.", "FilterTypeNotSupported");
                    }
                }
            }

            foreach (var filterExpression in filterExpressions)
            {
                if (filterExpression.Type == FilterType.Empty)
                {
                    // No additional CASE needed
                }
                else if (filterExpression.Type == FilterType.Simple)
                {
                    var parts = filterExpression.GetSimpleList();
                    foreach (var part in parts)
                    {
                        var propName = $"@Part{filterProps.Count}";
                        filterProps[propName] = part;
                        if (caseSensitive)
                            caseWhenParts.Add($@"WHEN instr(LOWER(pp.""Prefix"" || fl.""Path""), LOWER({propName})) > 0 THEN {(filterExpression.Result ? 0 : 1)}");
                        else
                            caseWhenParts.Add($@"WHEN instr(pp.""Prefix"" || fl.""Path"", {propName}) > 0 THEN {(filterExpression.Result ? 0 : 1)}");
                    }
                }
                else if (filterExpression.Type == FilterType.Wildcard)
                {
                    var parts = filterExpression.GetSimpleList();
                    foreach (var part in parts)
                    {
                        var propName = $"@Part{filterProps.Count}";
                        filterProps[propName] = part;
                        if (caseSensitive)
                            caseWhenParts.Add($@"WHEN LOWER(pp.""Prefix"" || fl.""Path"") GLOB LOWER({propName}) THEN {(filterExpression.Result ? 0 : 1)}");
                        else
                            caseWhenParts.Add($@"WHEN pp.""Prefix"" || fl.""Path"" GLOB {propName} THEN {(filterExpression.Result ? 0 : 1)}");
                    }
                }
                else
                {
                    throw new UserInformationException($"Filter type {filterExpression.Type} is not supported.", "FilterTypeNotSupported");
                }
            }

            // Build WHERE clauses
            var whereClauses = new List<string>();

            if (pathprefixes != null && pathprefixes.Any())
                whereClauses.Add(@"pp.""Prefix"" IN (@PathPrefixes)");
            if (filesetIds != null && filesetIds.Length > 0)
                whereClauses.Add(@"fe.""FilesetID"" IN (@FilesetIds)");

            if (caseWhenParts.Any())
            {
                whereClauses.Add($@"COALESCE(
            CASE
                {string.Join("\n                ", caseWhenParts)}
                ELSE NULL
            END,
            @DefaultBehavior
        ) = 0");
            }
            else
            {
                // No CASE parts, fallback to default behavior directly
                if (defaultBehavior == 1)
                {
                    // Default is EXCLUDE -> exclude everything
                    whereClauses.Add("0 = 1");
                }
            }

            var whereClause = string.Join("\n  AND ", whereClauses);

            cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC");

            var versionMap = cmd.ExecuteReaderEnumerable()
                .Select((x, i) => (Version: i, FilesetId: x.ConvertValueToInt64(0)))
                .ToDictionary(x => x.FilesetId, x => x.Version);

            // Fetch results
            var fetchSql = $@"
        SELECT 
            fe.""FilesetID"",
            f.""Timestamp"",
            pp.""Prefix"" || fl.""Path"" AS ""FullPath"",
            COALESCE(b.""Length"", 0) AS ""Size"",
            CASE WHEN fl.""BlocksetID"" = @FolderBlocksetId THEN 1 ELSE 0 END AS ""IsDirectory"",
            CASE WHEN fl.""BlocksetID"" = @SymlinkBlocksetId THEN 1 ELSE 0 END AS ""IsSymlink"",
            fe.""Lastmodified""
        FROM ""FilesetEntry"" fe
        INNER JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
        INNER JOIN ""Fileset"" f ON fe.""FilesetID"" = f.""ID""
        LEFT JOIN ""Blockset"" b ON fl.""BlocksetID"" = b.""ID""
        INNER JOIN ""PathPrefix"" pp ON fl.""PrefixID"" = pp.""ID""
        WHERE {whereClause}
        ORDER BY pp.""Prefix"" ASC, fl.""Path"" ASC, f.""Timestamp"" ASC
        LIMIT @limit OFFSET @offset
    ";

            cmd.SetCommandAndParameters(fetchSql)
               .SetParameterValues(filterProps)
               .SetParameterValue("@DefaultBehavior", defaultBehavior)
               .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
               .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
               .SetParameterValue("@limit", limit)
               .SetParameterValue("@offset", offset);

            if (pathprefixes != null && pathprefixes.Any())
                cmd.ExpandInClauseParameter("@PathPrefixes", pathprefixes);

            if (filesetIds != null && filesetIds.Length > 0)
                cmd.ExpandInClauseParameter("@FilesetIds", filesetIds);

            var results = new List<ISearchFileVersion>();
            foreach (var rd in cmd.ExecuteReaderEnumerable())
            {
                var version = versionMap[rd.ConvertValueToInt64(0, -1)];
                var time = new DateTime(rd.ConvertValueToInt64(1, 0), DateTimeKind.Utc);
                var path = rd.ConvertValueToString(2) ?? string.Empty;
                var size = rd.ConvertValueToInt64(3, 0);
                var isDirectory = rd.GetInt32(4) != 0;
                var isSymlink = rd.GetInt32(5) != 0;
                var lastModified = new DateTime(rd.ConvertValueToInt64(6, 0), DateTimeKind.Utc);

                // We cannot know exactly where the match occurred here
                // (unless we do another pass), so just use Range(0,0) for now
                results.Add(new SearchFileVersion(version, time, path, size, isDirectory, isSymlink, lastModified, new Range(0, 0)));
            }

            long totalCount;
            if (offset != 0)
            {
                // Only calculate the total for the first query
                totalCount = -1;
            }
            else if (results.Count <= limit)
            {
                // If we have all results, we already know the total
                totalCount = results.Count;
            }
            else
            {
                // Calculate the total
                var countSql = $@"
                    SELECT COUNT(*)
                    FROM ""FilesetEntry"" fe
                    INNER JOIN ""FileLookup"" fl ON fe.""FileID"" = fl.""ID""
                    INNER JOIN ""PathPrefix"" pp ON fl.""PrefixID"" = pp.""ID""
                    WHERE {whereClause}
                ";

                cmd.SetCommandAndParameters(countSql)
                   .SetParameterValues(filterProps)
                   .SetParameterValue("@DefaultBehavior", defaultBehavior);

                if (pathprefixes != null && pathprefixes.Any())
                    cmd.ExpandInClauseParameter("@PathPrefixes", pathprefixes);

                if (filesetIds != null && filesetIds.Length > 0)
                    cmd.ExpandInClauseParameter("@FilesetIds", filesetIds);

                totalCount = cmd.ExecuteScalarInt64(0);
            }

            var intLimit = limit > int.MaxValue ? int.MaxValue : (int)limit;
            var totalPages = (int)((totalCount + limit - 1) / limit);

            return new PaginatedResults<ISearchFileVersion>(
                (int)(offset / limit),
                intLimit,
                totalPages,
                totalCount,
                results
            );
        }
    }
}

