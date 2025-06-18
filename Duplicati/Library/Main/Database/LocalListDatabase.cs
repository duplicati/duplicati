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
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using Duplicati.Library.Interface;
using Microsoft.Data.Sqlite;
using Google.Protobuf.WellKnownTypes;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// A local database that stores file lists and filesets.
    /// </summary>
    internal class LocalListDatabase : LocalDatabase
    {
        /// <summary>
        /// Creates a new instance of the LocalListDatabase.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="pagecachesize">The size of the page cache.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <returns>A task that when awaited contains a new instance of LocalListDatabase.</returns>
        public static async Task<LocalListDatabase> CreateAsync(string path, long pagecachesize, LocalListDatabase? dbnew = null)
        {
            dbnew ??= new LocalListDatabase();

            dbnew = (LocalListDatabase)
                await CreateLocalDatabaseAsync(path, "List", false, pagecachesize, dbnew)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Interface for file versions.
        /// </summary>
        public interface IFileversion
        {
            /// <summary>
            /// Gets the path of the file, or null if not available.
            /// </summary>
            string? Path { get; }
            /// <summary>
            /// Asynchronously retrieves the sizes of the file versions.
            /// </summary>
            IAsyncEnumerable<long> Sizes();
        }

        /// <summary>
        /// Interface for filesets.
        /// </summary>
        public interface IFileset
        {
            /// <summary>
            /// Gets the version of the fileset.
            /// </summary>
            long Version { get; }
            /// <summary>
            /// Gets or sets a value indicating whether the fileset is a full backup.
            /// </summary>
            int IsFullBackup { get; set; }
            /// <summary>
            /// Gets the timestamp of the fileset.
            /// </summary>
            DateTime Time { get; }
            /// <summary>
            /// Gets the count of files in the fileset.
            /// </summary>
            long FileCount { get; }
            /// <summary>
            /// Gets the total sizes of files in the fileset.
            /// </summary>
            long FileSizes { get; }
        }

        /// <summary>
        /// Interface for managing file sets in the local database.
        /// </summary>
        public interface IFileSets : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Gets all of the filesets in the database as an asynchronous enumerable, including file count and total size.
            /// </summary>
            IAsyncEnumerable<IFileset> Sets();
            /// <summary>
            /// Gets all of the filesets in the database as an asynchronous enumerable, omitting file count and total size.
            /// </summary>
            IAsyncEnumerable<IFileset> QuickSets();
            /// <summary>
            /// Gets the files in the filesets that match the provided filter as an asynchronous enumerable.
            /// </summary>
            IAsyncEnumerable<IFileversion> SelectFiles(IFilter filter);
            /// <summary>
            /// Gets the largest prefix of files in the filesets that match the provided filter as an asynchronous enumerable.
            /// </summary>
            IAsyncEnumerable<IFileversion> GetLargestPrefix(IFilter filter);
            /// <summary>
            /// Selects the contents of a folder in the filesets that match the provided filter as an asynchronous enumerable.
            /// </summary>
            IAsyncEnumerable<IFileversion> SelectFolderContents(IFilter filter);
            Task TakeFirst();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="FileSets"/> class implementing the <see cref="IFileSets"> interface.
        /// </summary>
        private class FileSets : IFileSets
        {
            /// <summary>
            /// The database instance to execute commands against.
            /// </summary>
            private LocalDatabase m_db = null!;
            /// <summary>
            /// The name of the temporary table used to store filesets.
            /// </summary>
            private string m_tablename = null!;
            /// <summary>
            /// An array of key-value pairs representing filesets, where the key is the fileset ID and the value is the timestamp.
            /// </summary>
            private KeyValuePair<long, DateTime>[] m_filesets = [];

            /// <summary>
            /// Private constructor to prevent instantiation without using CreateAsync.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public FileSets(LocalListDatabase owner, DateTime time, long[] versions)
            {
                throw new NotSupportedException("Use CreateAsync instead.");
            }

            /// <summary>
            /// Private constructor to prevent instantiation without using CreateAsync.
            /// </summary>
            private FileSets() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="FileSets"/> class.
            /// </summary>
            /// <param name="owner">The owner database instance.</param>
            /// <param name="time">The time to filter filesets by.</param>
            /// <param name="versions">An array of versions to filter filesets by.</param>
            /// <returns>A task that when awaited contains a new instance of <see cref="FileSets"/>.</returns>
            public static async Task<FileSets> CreateAsync(LocalListDatabase owner, DateTime time, long[] versions)
            {
                var fs = new FileSets
                {
                    m_db = owner,
                    m_filesets = await owner
                        .FilesetTimes()
                        .ToArrayAsync()
                        .ConfigureAwait(false),
                    m_tablename = "Filesets-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())
                };
                var tmp = await owner.GetFilelistWhereClause(time, versions, fs.m_filesets)
                    .ConfigureAwait(false);
                var query = tmp.Item1;
                var args = tmp.Item2;

                using (var cmd = fs.m_db.Connection.CreateCommand())
                {
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{fs.m_tablename}"" AS
                        SELECT DISTINCT
                            ""ID"" AS ""FilesetID"",
                            ""IsFullBackup"" AS ""IsFullBackup"",
                            ""Timestamp"" AS ""Timestamp""
                        FROM ""Fileset"" {query}
                    ", args)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE INDEX ""{fs.m_tablename}_FilesetIDTimestampIndex""
                        ON ""{fs.m_tablename}"" (
                            ""FilesetID"",
                            ""Timestamp"" DESC
                        )
                    ")
                        .ConfigureAwait(false);
                }

                return fs;
            }

            /// <summary>
            /// Internal class representing a fileset.
            /// </summary>
            private class Fileset : IFileset
            {
                /// <summary>
                /// The backup version this fileset is part of.
                /// </summary>
                public long Version { get; private set; }
                /// <summary>
                /// Indicates whether this fileset is a full backup (1) or a partial backup (0).
                /// </summary>
                public int IsFullBackup { get; set; }
                /// <summary>
                /// The timestamp of the fileset, indicating when it was created.
                /// </summary>
                public DateTime Time { get; private set; }
                /// <summary>
                /// The count of files in this fileset.
                /// </summary>
                public long FileCount { get; private set; }
                /// <summary>
                /// The total sizes of files in this fileset.
                /// </summary>
                public long FileSizes { get; private set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="Fileset"/> class.
                /// </summary>
                /// <param name="version">The backup version of the fileset.</param>
                /// <param name="isFullBackup">Indicates whether this fileset is a full backup (1) or a partial backup (0).</param>
                /// <param name="time">The timestamp of the fileset, indicating when it was created.</param>
                /// <param name="filecount">The count of files in this fileset.</param>
                /// <param name="filesizes">The total sizes of files in this fileset.</param>
                public Fileset(long version, int isFullBackup, DateTime time, long filecount, long filesizes)
                {
                    Version = version;
                    IsFullBackup = isFullBackup;
                    Time = time;
                    FileCount = filecount;
                    FileSizes = filesizes;
                }
            }

            /// <summary>
            /// Implementation of the <see cref="IFileversion"/>  interface.
            /// </summary>
            private class Fileversion : IFileversion
            {
                /// <summary>
                /// The SqliteDataReader used to read file version data.
                /// </summary>
                private readonly SqliteDataReader m_reader;
                /// <summary>
                /// The path of the file, or null if not available.
                /// </summary>
                public string? Path { get; private set; }
                /// <summary>
                /// Indicates whether there are more file versions to read.
                /// </summary>
                public bool More { get; private set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="Fileversion"/> class.
                /// </summary>
                /// <param name="reader">The SqliteDataReader used to read file version data.</param>
                public Fileversion(SqliteDataReader reader)
                {
                    m_reader = reader;
                    Path = reader.ConvertValueToString(0);
                    More = true;
                }

                public async IAsyncEnumerable<long> Sizes()
                {
                    while (More && Path == m_reader.ConvertValueToString(0))
                    {
                        yield return m_reader.ConvertValueToInt64(1, -1);
                        More = await m_reader.ReadAsync().ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Internal class representing a fixed file version with no sizes.
            /// </summary>
            private class FileversionFixed : IFileversion
            {
                public string? Path { get; internal set; }
                public IAsyncEnumerable<long> Sizes() => AsyncEnumerable.Empty<long>();
            }

            public IAsyncEnumerable<IFileversion> GetLargestPrefix(IFilter filter)
            {
                return GetLargestPrefix(filter, null);
            }

            /// <summary>
            /// Recursively finds the largest prefix of files in the filesets that match the provided filter.
            /// </summary>
            /// <param name="filter"> The filter to apply to the files.</param>
            /// <param name="prefixrule">The prefix rule to apply, or null if no prefix rule is specified.</param>
            /// <returns>An asynchronous enumerable of file versions representing the largest prefix.</returns>
            private async IAsyncEnumerable<IFileversion> GetLargestPrefix(IFilter filter, string? prefixrule)
            {
                using (var tmpnames = await FilteredFilenameTable.CreateFilteredFilenameTableAsync(m_db, filter).ConfigureAwait(false))
                using (var cmd = m_db.Connection.CreateCommand())
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    await cmd.ExecuteNonQueryAsync($@"
                        DELETE FROM ""{tmpnames.Tablename}""
                        WHERE ""Path"" NOT IN (
                            SELECT DISTINCT ""Path""
                            FROM
                                ""File"",
                                ""FilesetEntry""
                            WHERE
                                ""FilesetEntry"".""FileID"" = ""File"".""ID""
                                AND ""FilesetEntry"".""FilesetID"" IN (
                                    SELECT ""FilesetID""
                                    FROM ""{m_tablename}""
                                )
                        )
                    ")
                        .ConfigureAwait(false);

                    //If we have a prefix rule, apply it
                    if (!string.IsNullOrWhiteSpace(prefixrule))
                        await cmd.SetCommandAndParameters($@"
                            DELETE FROM ""{tmpnames.Tablename}""
                            WHERE SUBSTR(""Path"", 1, {prefixrule.Length}) != @Rule
                        ")
                            .SetParameterValue("@Rule", prefixrule)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                    // Then we recursively find the largest prefix
                    var v0 = await cmd.ExecuteScalarAsync($@"
                        SELECT ""Path""
                        FROM ""{tmpnames.Tablename}""
                        ORDER BY LENGTH(""Path"") DESC
                        LIMIT 1
                    ")
                        .ConfigureAwait(false);

                    var maxpath = "";
                    if (v0 != null)
                        maxpath = v0.ToString() ?? "";

                    var dirsep = Util.GuessDirSeparator(maxpath);

                    var filecount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""{tmpnames.Tablename}""
                    ", 0)
                        .ConfigureAwait(false);

                    var foundfiles = -1L;

                    //TODO: Handle FS case-sensitive?
                    await cmd.SetCommandAndParameters($@"
                        SELECT COUNT(*)
                        FROM ""{tmpnames.Tablename}""
                        WHERE SUBSTR(""Path"", 1, @PrefixLength) = @Prefix
                    ")
                        .PrepareAsync()
                        .ConfigureAwait(false);

                    while (filecount != foundfiles && maxpath.Length > 0)
                    {
                        var mp = Util.AppendDirSeparator(maxpath, dirsep);
                        foundfiles = await cmd
                            .SetParameterValue("@PrefixLength", mp.Length)
                            .SetParameterValue("@Prefix", mp)
                            .ExecuteScalarInt64Async(0)
                            .ConfigureAwait(false);

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
                        var paths = cmd.ExecuteReaderEnumerableAsync($@"
                            SELECT ""Path""
                            FROM ""{tmpnames.Tablename}""
                        ")
                            .Select(x => x.ConvertValueToString(0) ?? "");

                        var roots = paths
                            .Select(x => x.Substring(0, 1))
                            .Distinct()
                            .Where(x => x != "\\");

                        //unc path like \\server.domain\
                        var regexUNCPrefix = new System.Text.RegularExpressions.Regex(@"^\\\\.*?\\");
                        var rootsUNC = paths
                            .Select(x => regexUNCPrefix.Match(x))
                            .Where(x => x.Success)
                            .Select(x => x.Value)
                            .Distinct();

                        var result = roots
                            .Concat(rootsUNC)
                            .Select(x => GetLargestPrefix(filter, x)
                                .FirstAsync())
                            .Distinct();

                        await foreach (var el in result.ConfigureAwait(false))
                            yield return await el;
                    }

                    yield return new FileversionFixed
                    {
                        Path = maxpath == "" ? "" : Util.AppendDirSeparator(maxpath, dirsep)
                    };
                }
            }

            /// <summary>
            /// Asynchronously retrieves all folder entries from the specified table that match the given prefix.
            /// </summary>
            /// <param name="cmd">The SqliteCommand to execute.</param>
            /// <param name="prefix">The prefix to filter folder entries by.</param>
            /// <param name="table">The name of the table to query.</param>
            /// <returns>An asynchronous enumerable of folder entries.</returns>
            private async IAsyncEnumerable<string> SelectFolderEntries(SqliteCommand cmd, string prefix, string table)
            {
                if (!string.IsNullOrEmpty(prefix))
                    prefix = Util.AppendDirSeparator(prefix, Util.GuessDirSeparator(prefix));

                var ppl = prefix.Length;
                cmd.SetCommandAndParameters($@"
                    SELECT DISTINCT ""Path""
                    FROM ""{table}""
                ");
                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
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

            public async IAsyncEnumerable<IFileversion> SelectFolderContents(IFilter filter)
            {
                var tbname = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    string pathprefix;
                    if (filter == null || filter.Empty)
                        pathprefix = "";
                    else if (filter as FilterExpression == null
                            || ((FilterExpression)filter).Type != FilterType.Simple
                            || ((FilterExpression)filter).GetSimpleList().Length != 1)
                        throw new ArgumentException("Filter for list-folder-contents must be a path prefix with no wildcards", nameof(filter));
                    else
                        pathprefix = ((FilterExpression)filter).GetSimpleList().First();

                    var dirsep = Util.GuessDirSeparator(pathprefix);

                    if (pathprefix.Length > 0 || dirsep == "/")
                        pathprefix = Util.AppendDirSeparator(pathprefix, dirsep);

                    using (var tmpnames = await FilteredFilenameTable.CreateFilteredFilenameTableAsync(m_db, new FilterExpression(new string[] { pathprefix + "*" }, true)).ConfigureAwait(false))
                    using (var cmd = m_db.Connection.CreateCommand())
                    {
                        //First we trim the filelist to exclude filenames not found in any of the filesets
                        await cmd.ExecuteNonQueryAsync($@"
                            DELETE FROM ""{tmpnames.Tablename}""
                            WHERE ""Path"" NOT IN (
                                SELECT DISTINCT ""Path""
                                FROM
                                    ""File"",
                                    ""FilesetEntry""
                                WHERE
                                    ""FilesetEntry"".""FileID"" = ""File"".""ID""
                                    AND ""FilesetEntry"".""FilesetID"" IN (
                                        SELECT ""FilesetID""
                                        FROM ""{m_tablename}""
                                    )
                            )
                        ")
                            .ConfigureAwait(false);

                        // If we had instr support this would work:
                        /*var distinctPaths = @"SELECT DISTINCT :1 || " +
                            @"CASE(INSTR(SUBSTR(""Path"", :2), '/')) " +
                            @"WHEN 0 THEN SUBSTR(""Path"", :2) " +
                            @"ELSE SUBSTR(""Path"", :2,  INSTR(SUBSTR(path, :2), '/')) " +
                            @"END AS ""Path"", ""FilesetID"" " +
                            @" FROM (" + cartesianPathFileset + @")";*/

                        // Instead we manually iterate the paths
                        await cmd.ExecuteNonQueryAsync($@"
                            CREATE TEMPORARY TABLE ""{tbname}"" (
                                ""Path"" TEXT NOT NULL
                            )
                        ")
                            .ConfigureAwait(false);

                        using (var c2 = m_db.Connection.CreateCommand())
                        {
                            c2.SetCommandAndParameters($@"
                                INSERT INTO ""{tbname}"" (""Path"")
                                VALUES (@Path)
                            ");

                            await foreach (var n in SelectFolderEntries(cmd, pathprefix, tmpnames.Tablename).Distinct().ConfigureAwait(false))
                                await c2
                                    .SetParameterValue("@Path", n)
                                    .ExecuteNonQueryAsync()
                                    .ConfigureAwait(false);

                            await c2.ExecuteNonQueryAsync($@"
                                CREATE INDEX ""{tbname}_PathIndex""
                                ON ""{tbname}"" (""Path"")
                            ")
                                .ConfigureAwait(false);
                        }

                        //Then we select the matching results
                        var filesets = $@"
                            SELECT
                                ""FilesetID"",
                                ""Timestamp""
                            FROM ""{m_tablename}""
                            ORDER BY ""Timestamp"" DESC
                        ";

                        var cartesianPathFileset = $@"
                            SELECT
                                ""A"".""Path"",
                                ""B"".""FilesetID""
                            FROM
                                ""{tbname}"" ""A"",
                                ({filesets}) ""B""
                            ORDER BY
                                ""A"".""Path"" ASC,
                                ""B"".""Timestamp"" DESC
                        ";

                        var filesWithSizes = $@"
                            SELECT
                                ""Length"",
                                ""FilesetEntry"".""FilesetID"",
                                ""File"".""Path""
                            FROM
                                ""Blockset"",
                                ""FilesetEntry"",
                                ""File""
                            WHERE
                                ""File"".""BlocksetID"" = ""Blockset"".""ID""
                                AND ""FilesetEntry"".""FileID"" = ""File"".""ID""
                                AND FilesetEntry.""FilesetID"" IN (
                                    SELECT DISTINCT ""FilesetID""
                                    FROM ""{m_tablename}""
                                )
                        ";

                        var query = @$"
                            SELECT
                                ""C"".""Path"",
                                ""D"".""Length"",
                                ""C"".""FilesetID""
                            FROM ({cartesianPathFileset}) ""C""
                            LEFT OUTER JOIN ({filesWithSizes}) ""D""
                                ON ""C"".""FilesetID"" = ""D"".""FilesetID""
                                AND ""C"".""Path"" = ""D"".""Path""
                        ";

                        using var rd = await cmd
                            .ExecuteReaderAsync(query)
                            .ConfigureAwait(false);

                        if (await rd.ReadAsync().ConfigureAwait(false))
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
                                    more = await rd.ReadAsync()
                                        .ConfigureAwait(false);
                                }

                            } while (more);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        using (var c = m_db.Connection.CreateCommand())
                            await c.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tbname}""")
                                .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }

            public async IAsyncEnumerable<IFileversion> SelectFiles(IFilter filter)
            {
                using (var tmpnames = await FilteredFilenameTable.CreateFilteredFilenameTableAsync(m_db, filter).ConfigureAwait(false))
                using (var cmd = m_db.Connection.CreateCommand())
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    await cmd.ExecuteNonQueryAsync($@"
                        DELETE FROM ""{tmpnames.Tablename}""
                        WHERE ""Path"" NOT IN (
                            SELECT DISTINCT ""Path""
                            FROM
                                ""File"",
                                ""FilesetEntry""
                            WHERE
                                ""FilesetEntry"".""FileID"" = ""File"".""ID""
                                AND ""FilesetEntry"".""FilesetID"" IN (
                                    SELECT ""FilesetID""
                                    FROM ""{m_tablename}""
                                )
                        )
                    ")
                        .ConfigureAwait(false);

                    //Then we select the matching results
                    var filesets = $@"
                        SELECT
                            ""FilesetID"",
                            ""Timestamp""
                        FROM ""{m_tablename}""
                        ORDER BY ""Timestamp"" DESC
                    ";

                    var cartesianPathFileset = $@"
                        SELECT
                            ""A"".""Path"",
                            ""B"".""FilesetID""
                        FROM
                            ""{tmpnames.Tablename}"" ""A"",
                            ({filesets}) ""B""
                        ORDER BY
                            ""A"".""Path"" ASC,
                            ""B"".""Timestamp"" DESC
                    ";

                    var filesWithSizes = $@"
                        SELECT
                            ""Length"",
                            ""FilesetEntry"".""FilesetID"",
                            ""File"".""Path""
                        FROM
                            ""Blockset"",
                            ""FilesetEntry"",
                            ""File""
                        WHERE
                            ""File"".""BlocksetID"" = ""Blockset"".""ID""
                            AND ""FilesetEntry"".""FileID"" = ""File"".""ID""
                            AND FilesetEntry.""FilesetID"" IN (
                                SELECT DISTINCT ""FilesetID""
                                FROM ""{m_tablename}""
                            )
                    ";

                    var query = @$"
                        SELECT
                            ""C"".""Path"",
                            ""D"".""Length"",
                            ""C"".""FilesetID""
                        FROM ({cartesianPathFileset}) ""C""
                        LEFT OUTER JOIN ({filesWithSizes}) ""D""
                            ON ""C"".""FilesetID"" = ""D"".""FilesetID""
                            AND ""C"".""Path"" = ""D"".""Path""
                    ";

                    using var rd = await cmd.ExecuteReaderAsync(query)
                        .ConfigureAwait(false);
                    if (await rd.ReadAsync().ConfigureAwait(false))
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

            /// <summary>
            /// Deletes all filesets except the most recent one.
            /// </summary>
            public async Task TakeFirst()
            {
                using var cmd = m_db.Connection.CreateCommand();
                await cmd.ExecuteNonQueryAsync($@"
                    DELETE FROM ""{m_tablename}""
                    WHERE ""FilesetID"" NOT IN (
                        SELECT ""FilesetID""
                        FROM ""{m_tablename}""
                        ORDER BY ""Timestamp"" DESC
                        LIMIT 1
                    )
                ")
                    .ConfigureAwait(false);
            }

            public async IAsyncEnumerable<IFileset> QuickSets()
            {
                var dict = new Dictionary<long, long>();
                for (var i = 0; i < m_filesets.Length; i++)
                    dict[m_filesets[i].Key] = i;

                using (var cmd = m_db.Connection.CreateCommand())
                using (var rd = await cmd.ExecuteReaderAsync(@"
                    SELECT DISTINCT
                        ""ID"",
                        ""IsFullBackup""
                    FROM ""Fileset""
                    ORDER BY ""Timestamp"" DESC
                ").ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                    {
                        var id = rd.ConvertValueToInt64(0);
                        var backupType = rd.GetInt32(1);
                        var e = dict[id];

                        yield return new Fileset(
                            e,
                            backupType,
                            m_filesets[e].Value,
                            -1L,
                            -1L
                        );
                    }
            }

            public async IAsyncEnumerable<IFileset> Sets()
            {
                var dict = new Dictionary<long, long>();
                for (var i = 0; i < m_filesets.Length; i++)
                    dict[m_filesets[i].Key] = i;

                var summation = $@"
                    SELECT
                        ""A"".""FilesetID"" AS ""FilesetID"",
                        COUNT(*) AS ""FileCount"",
                        SUM(""C"".""Length"") AS ""FileSizes""
                    FROM
                        ""FilesetEntry"" ""A"",
                        ""File"" ""B"",
                        ""Blockset"" ""C""
                    WHERE
                        ""A"".""FileID"" = ""B"".""ID""
                        AND ""B"".""BlocksetID"" = ""C"".""ID""
                        AND ""A"".""FilesetID"" IN (
                            SELECT DISTINCT ""FilesetID""
                            FROM ""{m_tablename}"")
                            GROUP BY ""A"".""FilesetID""
                ";

                using var cmd = m_db.Connection.CreateCommand(m_db.Transaction);
                using var rd = await cmd.ExecuteReaderAsync($@"
                    SELECT DISTINCT
                        ""A"".""FilesetID"",
                        ""A"".""IsFullBackup"",
                        ""B"".""FileCount"",
                        ""B"".""FileSizes""
                    FROM ""{m_tablename}"" ""A""
                    LEFT OUTER JOIN ( {summation} ) ""B""
                        ON ""A"".""FilesetID"" = ""B"".""FilesetID""
                    ORDER BY ""A"".""Timestamp"" DESC
                ")
                    .ConfigureAwait(false);

                while (await rd.ReadAsync().ConfigureAwait(false))
                {
                    var id = rd.ConvertValueToInt64(0);
                    var isFullBackup = rd.GetInt32(1);
                    var e = dict[id];
                    var filecount = rd.ConvertValueToInt64(2, -1L);
                    var filesizes = rd.ConvertValueToInt64(3, -1L);

                    yield return new Fileset(
                        e,
                        isFullBackup,
                        m_filesets[e].Value,
                        filecount,
                        filesizes
                    );
                }
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using (var cmd = m_db.Connection.CreateCommand())
                            await cmd.ExecuteNonQueryAsync(@$"DROP TABLE IF EXISTS ""{m_tablename}"" ")
                                .ConfigureAwait(false);
                    }
                    catch { }
                    finally { m_tablename = null!; }
                }
            }
        }

        /// <summary>
        /// Creates a new instance of <see cref="FileSets"/> with the specified time and versions.
        /// </summary>
        /// <param name="time">The time to filter filesets by.</param>
        /// <param name="versions">An array of versions to filter filesets by.</param>
        /// <returns>A task that when awaited returns a new instance of <see cref="FileSets"/>.</returns>
        public async Task<IFileSets> SelectFileSets(DateTime time, long[] versions)
        {
            return await FileSets.CreateAsync(this, time, versions)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Represents a fileset entry in the list fileset results.
        /// </summary>
        /// <param name="Version">The fileset version.</param>
        /// <param name="IsFullBackup">Flag indicating if the backup is a full backup or synthetic backup.</param>
        /// <param name="Time">The timestamp of the fileset.</param>
        /// <param name="FileCount">The number of files in the fileset.</param>
        /// <param name="FileSizes">>The total size of files in the fileset.</param>
        private sealed record FilesetEntry(long Version, bool? IsFullBackup, DateTime Time, long? FileCount, long? FileSizes) : IListFilesetResultFileset;

        /// <summary>
        /// Lists all filesets with summary data.
        /// </summary>
        /// <returns>An asynchronous enumerable of fileset entries.</returns>
        public async IAsyncEnumerable<IListFilesetResultFileset> ListFilesetsExtended()
        {
            const string query = @"
                SELECT
                    ""f"".""ID"" AS ""FilesetID"",
                    ""f"".""Timestamp"",
                    ""f"".""IsFullBackup"",
                    COUNT(""fe"".""FileID"") AS ""NumberOfFiles"",
                    COALESCE(SUM(""b"".""Length""), 0) AS ""TotalFileSize""
                FROM ""Fileset"" ""f""
                LEFT JOIN ""FilesetEntry"" ""fe""
                    ON ""f"".""ID"" = ""fe"".""FilesetID""
                LEFT JOIN ""FileLookup"" ""fl""
                    ON ""fe"".""FileID"" = ""fl"".""ID""
                LEFT JOIN ""Blockset"" ""b""
                    ON ""fl"".""BlocksetID"" = ""b"".""ID""
                GROUP BY ""f"".""ID""
                ORDER BY ""f"".""Timestamp"" DESC;
            ";

            using (var cmd = m_connection.CreateCommand(query))
                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
                {
                    var id = rd.ConvertValueToInt64(0);
                    var time = ParseFromEpochSeconds(rd.ConvertValueToInt64(1));
                    var isFullBackup = rd.GetInt32(2) == BackupType.FULL_BACKUP;
                    var filecount = rd.ConvertValueToInt64(3, -1L);
                    var filesizes = rd.ConvertValueToInt64(4, -1L);

                    yield return new FilesetEntry(
                        id,
                        isFullBackup,
                        time,
                        filecount,
                        filesizes
                    );
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
        /// <returns>A task that when awaited returns a paginated result set of folder entries.</returns>
        public async Task<IPaginatedResults<IListFolderEntry>> ListFolder(IEnumerable<long> prefixIds, long filesetid, long offset, long limit)
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
                    ""pp"".""Prefix"" || ""fl"".""Path"" AS ""FullPath"",
                    ""b"".""Length"",
                    (CASE
                        WHEN ""fl"".""BlocksetID"" = @FolderBlocksetId
                        THEN 1
                        ELSE 0
                    END) AS ""IsDirectory"",
                    (CASE
                        WHEN ""fl"".""BlocksetID"" = @SymlinkBlocksetId
                        THEN 1
                        ELSE 0
                    END) AS ""IsSymlink"",
                    ""fe"".""Lastmodified""
                FROM ""FilesetEntry"" ""fe""
                INNER JOIN ""FileLookup"" ""fl""
                    ON ""fe"".""FileID"" = ""fl"".""ID""
                INNER JOIN ""PathPrefix"" ""pp""
                    ON ""fl"".""PrefixID"" = ""pp"".""ID""
                LEFT JOIN ""Blockset"" ""b""
                    ON ""fl"".""BlocksetID"" = ""b"".""ID""
                WHERE
                    ""fe"".""FilesetID"" = @filesetid
                    AND ""fl"".""PrefixID"" IN (@PrefixIds)
                ORDER BY
                    ""pp"".""Prefix"" ASC,
                    ""fl"".""Path"" ASC
                LIMIT @limit
                OFFSET @offset
            ")
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                .ExpandInClauseParameterMssqlite("@PrefixIds", prefixIds)
                .SetParameterValue("@filesetid", filesetid)
                .SetParameterValue("@limit", limit)
                .SetParameterValue("@offset", offset);

            var results = new List<IListFolderEntry>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
            {
                var path = rd.ConvertValueToString(0) ?? string.Empty;
                var size = rd.ConvertValueToInt64(-1);
                var isDir = rd.GetInt32(2) != 0;
                var isSymlink = rd.GetInt32(3) != 0;
                var lastModified = new DateTime(rd.ConvertValueToInt64(4, 0), DateTimeKind.Utc);

                results.Add(new FolderEntry(
                    path,
                    size,
                    isDir,
                    isSymlink,
                    lastModified
                ));
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
                totalCount = await cmd.SetCommandAndParameters($@"
                    SELECT COUNT(*)
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                    WHERE
                        ""fe"".""FilesetID"" = @filesetid
                        AND ""fl"".""PrefixID"" IN (@PrefixIds)
                ")
                    .ExpandInClauseParameterMssqlite("@PrefixIds", prefixIds)
                    .SetParameterValue("@filesetid", filesetid)
                    .ExecuteScalarInt64Async(0)
                    .ConfigureAwait(false);
            }

            // Return paginated results
            var intLimit = limit > int.MaxValue ? int.MaxValue : (int)limit;
            return new PaginatedResults<IListFolderEntry>((int)(offset / limit), (int)intLimit, (int)((totalCount + limit - 1) / limit), totalCount, results);
        }

        /// <summary>
        /// Lists the folder entries for a given fileset and prefix IDs.
        /// </summary>
        /// <param name="prefixIds">The prefix IDs to return entries for.</param>
        /// <param name="filesetid">The fileset ID to filter the folder entries.</param>
        /// <param name="offset">The offset for pagination.</param>
        /// <param name="limit">The limit for pagination.</param>
        /// <returns>A task that when awaited returns a paginated result set of folder entries.</returns>
        public async Task<IPaginatedResults<IListFolderEntry>> ListFilesetEntries(IEnumerable<long> prefixIds, long filesetid, long offset, long limit)
        {
            if (offset != 0 && limit <= 0)
                throw new ArgumentException("Cannot use offset without limit specified.", nameof(offset));
            if (limit <= 0)
                limit = long.MaxValue;

            using var cmd = m_connection.CreateCommand();

            if (!prefixIds.Any())
                return new PaginatedResults<IListFolderEntry>(0, (int)limit, 0, 0, Enumerable.Empty<IListFolderEntry>());

            // Fetch entries
            cmd.SetCommandAndParameters(@"
                SELECT
                    ""pp"".""Prefix"" || ""fl"".""Path"" AS ""FullPath"",
                    ""b"".""Length"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @FolderBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsDirectory"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @SymlinkBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsSymlink"",
                    ""fe"".""Lastmodified""
                FROM ""FilesetEntry"" ""fe""
                INNER JOIN ""FileLookup"" ""fl""
                    ON ""fe"".""FileID"" = ""fl"".""ID""
                INNER JOIN ""PathPrefix"" ""pp""
                    ON ""fl"".""PrefixID"" = ""pp"".""ID""
                LEFT JOIN ""Blockset"" b
                    ON ""fl"".""BlocksetID"" = ""b"".""ID""
                WHERE
                    ""fe"".""FilesetId"" = @FilesetId
                    AND ""fl"".""PrefixId"" IN (@PrefixIds)
                ORDER BY
                    ""pp"".""Prefix"" ASC,
                    ""fl"".""Path"" ASC
                LIMIT @limit
                OFFSET @offset
            ")
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                .SetParameterValue("@FilesetId", filesetid)
                .ExpandInClauseParameterMssqlite("@PrefixIds", prefixIds)
                .SetParameterValue("@limit", limit)
                .SetParameterValue("@offset", offset);

            var results = new List<IListFolderEntry>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
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
                totalCount = await cmd.SetCommandAndParameters(@"
                    SELECT COUNT(*)
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                    WHERE
                        ""fe"".""FilesetId"" = @FilesetId
                        AND ""fl"".""PrefixId"" IN (@PrefixIds)
                ")
                    .SetParameterValue("@FilesetId", filesetid)
                    .ExpandInClauseParameterMssqlite("@PrefixIds", prefixIds)
                    .ExecuteScalarInt64Async(0)
                    .ConfigureAwait(false);
            }

            var intLimit = limit > int.MaxValue ? int.MaxValue : (int)limit;
            return new PaginatedResults<IListFolderEntry>((int)(offset / limit), intLimit, (int)((totalCount + limit - 1) / limit), totalCount, results);
        }

        /// <summary>
        /// Gets the prefix IDs for a given set of folder-prefixes.
        /// </summary>
        /// <param name="prefixes">The prefixes to get the IDs for.</param>
        /// <returns>An asynchronous enumerable of prefix IDs.</returns>
        public IAsyncEnumerable<long> GetPrefixIds(IEnumerable<string> prefixes)
        {
            using var cmd = m_connection.CreateCommand();

            // Map folders to prefix IDs
            cmd.SetCommandAndParameters($@"
                SELECT ""ID""
                FROM ""PathPrefix""
                WHERE ""Prefix"" IN (@Prefixes)
            ")
                .ExpandInClauseParameter("@Prefixes", prefixes);

            return cmd.ExecuteReaderEnumerableAsync()
                .Select(x => x.ConvertValueToInt64(0));
        }

        /// <summary>
        /// Gathers root prefixes from the fileset entries.
        /// </summary>
        /// <param name="filesetid">The filesetId to get the prefixes for.</param>
        /// <returns>An asynchronous enumerable of root prefixes.</returns>
        public IAsyncEnumerable<long> GetRootPrefixes(long filesetid)
        {
            using var cmd = m_connection.CreateCommand();

            cmd.SetCommandAndParameters(@"
                WITH Prefixes AS (
                    SELECT DISTINCT
                        ""fl"".""PrefixID"",
                        ""pp"".""Prefix""
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                    INNER JOIN ""PathPrefix"" ""pp""
                        ON ""fl"".""PrefixID"" = ""pp"".""ID""
                    WHERE ""fe"".""FilesetID"" = @filesetid
                )
                SELECT ""p1"".""PrefixID""
                FROM Prefixes ""p1""
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM Prefixes ""p2""
                    WHERE
                        ""p2"".""PrefixID"" != ""p1"".""PrefixID""
                        AND ""p1"".""Prefix"" LIKE ""p2"".""Prefix"" || '%'
                        AND LENGTH(""p1"".""Prefix"") > LENGTH(""p2"".""Prefix"")
                )
                ORDER BY ""p1"".""Prefix"" ASC
            ")
                .SetParameterValue("@filesetid", filesetid);

            return cmd.ExecuteReaderEnumerableAsync()
                .Select(x => x.ConvertValueToInt64(0, -1))
                .Where(x => x != -1);
        }

        /// <summary>
        /// Gathers all prefixes and their parent prefix IDs from the fileset entries.
        /// </summary>
        /// <param name="filesetid">The filesetId to get the prefixes for</param>
        /// <returns>A task that when awaited returns an asynchronous enumerable of (PrefixID, ParentPrefixID) pairs.</returns>
        public async Task<IAsyncEnumerable<(long PrefixId, long ParentPrefixId)>> GetRootPrefixesWithParents(long filesetid)
        {
            using var cmd = m_connection.CreateCommand();

            var dirSeparator = Util.GuessDirSeparator((
                await cmd.SetCommandAndParameters(@"
                    SELECT ""Prefix""
                    FROM ""PathPrefix""
                    LIMIT 1
                ")
                    .ExecuteScalarAsync()
                    .ConfigureAwait(false)
            )?.ToString());

            cmd.SetCommandAndParameters(@"
                WITH ""Prefixes"" AS (
                    SELECT DISTINCT
                        ""fl"".""PrefixID"",
                        ""pp"".""Prefix""
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                    INNER JOIN ""PathPrefix"" ""pp""
                        ON ""fl"".""PrefixID"" = ""pp"".""ID""
                    WHERE ""fe"".""FilesetID"" = @filesetid
                ),
                ""PrefixParents"" AS (
                    SELECT
                        ""child"".""PrefixID"" AS ""ChildPrefixID"",
                        ""parent"".""PrefixID"" AS ""ParentPrefixID"",
                        LENGTH(""parent"".""Prefix"") AS ""ParentLength""
                    FROM ""Prefixes"" ""child""
                    JOIN ""Prefixes"" ""parent""
                        ON LENGTH(""child"".""Prefix"") > LENGTH(""parent"".""Prefix"")
                        AND SUBSTR(""child"".""Prefix"", 1, LENGTH(""parent"".""Prefix"")) = ""parent"".""Prefix""
                        AND (SUBSTR(""child"".""Prefix"", LENGTH(""parent"".""Prefix"") + 1, 1) = @dirSeparator)
                )
                SELECT
                    ""child"".""PrefixID"",
                    (
                        SELECT ""ParentPrefixID""
                        FROM PrefixParents ""pp""
                        WHERE ""pp"".""ChildPrefixID"" = ""child"".""PrefixID""
                        ORDER BY ""pp"".""ParentLength"" DESC
                        LIMIT 1
                    ) AS ""ParentPrefixID""
                FROM ""Prefixes"" ""child""
                ORDER BY ""child"".""Prefix"" ASC
            ")
                .SetParameterValue("@dirSeparator", dirSeparator)
                .SetParameterValue("@filesetid", filesetid);

            return cmd.ExecuteReaderEnumerableAsync()
                .Select(x => (
                    PrefixId: x.ConvertValueToInt64(0, -1),
                    ParentPrefixId: x.ConvertValueToInt64(1, -1)
                ));
        }

        /// <summary>
        /// Lists all versions of specific file paths, optionally filtered by fileset IDs.
        /// </summary>
        /// <param name="paths">Paths to match exactly.</param>
        /// <param name="filesetIds">Optional fileset IDs to restrict the search to.</param>
        /// <param name="offset">Pagination offset.</param>
        /// <param name="limit">Pagination limit.</param>
        /// <returns>A task that when awaited returns all matching file versions ordered by path and then version time.</returns>
        public async Task<IPaginatedResults<IListFileVersion>> ListFileVersions(IEnumerable<string> paths, long[]? filesetIds, long offset, long limit)
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

            cmd.SetCommandAndParameters(@"
                SELECT ""ID""
                FROM ""Fileset""
                ORDER BY ""Timestamp"" DESC
            ");

            var versionMap = await cmd
                .ExecuteReaderEnumerableAsync()
                .Select((x, i) => (Version: i, FilesetId: x.ConvertValueToInt64(0)))
                .ToDictionaryAsync(x => x.FilesetId, x => x.Version)
                .ConfigureAwait(false);

            // Then fetch the actual data
            var fetchSql = @$"
                SELECT
                    ""fe"".""FilesetID"",
                    ""f"".""Timestamp"",
                    ""fl"".""Path"",
                    COALESCE(""b"".""Length"", 0) AS ""Size"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @FolderBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsDirectory"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @SymlinkBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsSymlink"",
                    ""fe"".""Lastmodified""
                FROM ""FilesetEntry"" ""fe""
                INNER JOIN ""FileLookup"" ""fl""
                    ON ""fe"".""FileID"" = ""fl"".""ID""
                INNER JOIN ""Fileset"" ""f""
                    ON ""fe"".""FilesetID"" = ""f"".""ID""
                LEFT JOIN ""Blockset"" ""b""
                    ON ""fl"".""BlocksetID"" = ""b"".""ID""
                {countWhere}
                ORDER BY
                    ""fl"".""Path"" ASC,
                    ""f"".""Timestamp"" ASC
                LIMIT @limit
                OFFSET @offset
            ";

            cmd.SetCommandAndParameters(fetchSql)
               .ExpandInClauseParameter("@Paths", paths)
               .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
               .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
               .SetParameterValue("@limit", limit)
               .SetParameterValue("@offset", offset);

            if (filesetIds != null && filesetIds.Length > 0)
                cmd.ExpandInClauseParameterMssqlite("@FilesetIds", filesetIds);

            var results = new List<IListFileVersion>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
            {
                var version = versionMap[rd.ConvertValueToInt64(0, -1)];
                var time = new DateTime(rd.ConvertValueToInt64(1, 0), DateTimeKind.Utc);
                var path = rd.ConvertValueToString(2) ?? string.Empty;
                var size = rd.ConvertValueToInt64(3, 0);
                var isDirectory = rd.GetInt32(4) != 0;
                var isSymlink = rd.GetInt32(5) != 0;
                var lastModified = new DateTime(rd.ConvertValueToInt64(6, 0), DateTimeKind.Utc);

                results.Add(new ListFileVersion(
                    version,
                    time,
                    path,
                    size,
                    isDirectory,
                    isSymlink,
                    lastModified
                ));
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
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                ";

                cmd.SetCommandAndParameters(countSql + "\n" + countWhere)
                   .ExpandInClauseParameter("@Paths", paths);

                if (filesetIds != null && filesetIds.Length > 0)
                    cmd.ExpandInClauseParameterMssqlite("@FilesetIds", filesetIds);

                totalCount = await cmd.ExecuteScalarInt64Async(0)
                    .ConfigureAwait(false);
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
        /// <returns>A task that when awaited returns all matching file versions ordered by path and then version time.</returns>
        public async Task<IPaginatedResults<ISearchFileVersion>> SearchEntries(IEnumerable<string>? pathprefixes, IFilter filter, long[]? filesetIds, long offset, long limit)
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
                            caseWhenParts.Add($@"
                                WHEN instr(LOWER(pp.""Prefix"" || fl.""Path""), LOWER({propName})) > 0
                                THEN {(filterExpression.Result ? 0 : 1)}
                            ");
                        else
                            caseWhenParts.Add($@"
                                WHEN instr(pp.""Prefix"" || fl.""Path"", {propName}) > 0
                                THEN {(filterExpression.Result ? 0 : 1)}
                            ");
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
                            caseWhenParts.Add($@"
                                WHEN LOWER(pp.""Prefix"" || fl.""Path"") GLOB LOWER({propName})
                                THEN {(filterExpression.Result ? 0 : 1)}
                            ");
                        else
                            caseWhenParts.Add($@"
                                WHEN pp.""Prefix"" || fl.""Path"" GLOB {propName}
                                THEN {(filterExpression.Result ? 0 : 1)}
                            ");
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
                whereClauses.Add($@"
                    COALESCE(
                        CASE
                            {string.Join("\n                ", caseWhenParts)}
                            ELSE NULL
                        END,
                        @DefaultBehavior
                    ) = 0
                ");
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

            cmd.SetCommandAndParameters(@"
                SELECT ""ID""
                FROM ""Fileset""
                ORDER BY ""Timestamp"" DESC
            ");

            var versionMap = await cmd
                .ExecuteReaderEnumerableAsync()
                .Select((x, i) => (Version: i, FilesetId: x.ConvertValueToInt64(0)))
                .ToDictionaryAsync(x => x.FilesetId, x => x.Version)
                .ConfigureAwait(false);

            // Fetch results
            var fetchSql = $@"
                SELECT
                    ""fe"".""FilesetID"",
                    ""f"".""Timestamp"",
                    ""pp"".""Prefix"" || ""fl"".""Path"" AS ""FullPath"",
                    COALESCE(""b"".""Length"", 0) AS ""Size"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @FolderBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsDirectory"",
                    CASE
                        WHEN ""fl"".""BlocksetID"" = @SymlinkBlocksetId
                        THEN 1
                        ELSE 0
                    END AS ""IsSymlink"",
                    ""fe"".""Lastmodified""
                FROM ""FilesetEntry"" ""fe""
                INNER JOIN ""FileLookup"" ""fl""
                    ON ""fe"".""FileID"" = ""fl"".""ID""
                INNER JOIN ""Fileset"" ""f""
                    ON ""fe"".""FilesetID"" = ""f"".""ID""
                LEFT JOIN ""Blockset"" ""b""
                    ON ""fl"".""BlocksetID"" = ""b"".""ID""
                INNER JOIN ""PathPrefix"" ""pp""
                    ON ""fl"".""PrefixID"" = ""pp"".""ID""
                WHERE {whereClause}
                ORDER BY
                    ""pp"".""Prefix"" ASC,
                    ""fl"".""Path"" ASC,
                    ""f"".""Timestamp"" ASC
                LIMIT @limit
                OFFSET @offset
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
                cmd.ExpandInClauseParameterMssqlite("@FilesetIds", filesetIds);

            var results = new List<ISearchFileVersion>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
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
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""fl""
                        ON ""fe"".""FileID"" = ""fl"".""ID""
                    INNER JOIN ""PathPrefix"" ""pp""
                        ON ""fl"".""PrefixID"" = ""pp"".""ID""
                    WHERE {whereClause}
                ";

                cmd.SetCommandAndParameters(countSql)
                   .SetParameterValues(filterProps)
                   .SetParameterValue("@DefaultBehavior", defaultBehavior);

                if (pathprefixes != null && pathprefixes.Any())
                    cmd.ExpandInClauseParameter("@PathPrefixes", pathprefixes);

                if (filesetIds != null && filesetIds.Length > 0)
                    cmd.ExpandInClauseParameterMssqlite("@FilesetIds", filesetIds);

                totalCount = await cmd.ExecuteScalarInt64Async(0)
                    .ConfigureAwait(false);
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
