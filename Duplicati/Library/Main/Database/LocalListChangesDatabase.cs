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
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListChangesDatabase : LocalDatabase
    {

        public static async Task<LocalListChangesDatabase> CreateAsync(string path, long pagecachesize, LocalListChangesDatabase? dbnew = null)
        {
            dbnew ??= new LocalListChangesDatabase();

            dbnew = (LocalListChangesDatabase)await CreateLocalDatabaseAsync(path, "ListChanges", false, pagecachesize, dbnew);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        public interface IStorageHelper : IDisposable
        {
            Task AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew);

            Task AddFromDb(long filesetId, bool asNew, IFilter filter);

            Task<IChangeCountReport> CreateChangeCountReport();
            Task<IChangeSizeReport> CreateChangeSizeReport();
            IAsyncEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport();
        }

        public interface IChangeCountReport
        {
            long AddedFolders { get; }
            long AddedSymlinks { get; }
            long AddedFiles { get; }

            long DeletedFolders { get; }
            long DeletedSymlinks { get; }
            long DeletedFiles { get; }

            long ModifiedFolders { get; }
            long ModifiedSymlinks { get; }
            long ModifiedFiles { get; }
        }

        public interface IChangeSizeReport
        {
            long AddedSize { get; }
            long DeletedSize { get; }

            long PreviousSize { get; }
            long CurrentSize { get; }
        }

        internal class ChangeCountReport : IChangeCountReport
        {
            public long AddedFolders { get; internal set; }
            public long AddedSymlinks { get; internal set; }
            public long AddedFiles { get; internal set; }

            public long DeletedFolders { get; internal set; }
            public long DeletedSymlinks { get; internal set; }
            public long DeletedFiles { get; internal set; }

            public long ModifiedFolders { get; internal set; }
            public long ModifiedSymlinks { get; internal set; }
            public long ModifiedFiles { get; internal set; }
        }

        internal class ChangeSizeReport : IChangeSizeReport
        {
            public long AddedSize { get; internal set; }
            public long DeletedSize { get; internal set; }

            public long PreviousSize { get; internal set; }
            public long CurrentSize { get; internal set; }
        }

        private class StorageHelper : IStorageHelper
        {
            private LocalDatabase m_db = null!;

            private SqliteCommand m_insertPreviousElementCommand = null!;
            private SqliteCommand m_insertCurrentElementCommand = null!;

            private string m_previousTable = null!;
            private string m_currentTable = null!;

            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public StorageHelper(SqliteConnection con)
            {
            }

            private StorageHelper() { }

            public static async Task<StorageHelper> CreateAsync(LocalDatabase db)
            {
                var sh = new StorageHelper
                {
                    m_db = db,
                    m_previousTable = "Previous-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray()),
                    m_currentTable = "Current-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())
                };

                using (var cmd = sh.m_db.Connection.CreateCommand(db.Transaction))
                {
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{sh.m_previousTable}"" (
                            ""Path"" TEXT NOT NULL,
                            ""FileHash"" TEXT NULL,
                            ""MetaHash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Type"" INTEGER NOT NULL
                        )
                    ");

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{sh.m_currentTable}"" (
                            ""Path"" TEXT NOT NULL,
                            ""FileHash"" TEXT NULL,
                            ""MetaHash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Type"" INTEGER NOT NULL
                        )
                    ");
                }

                sh.m_insertPreviousElementCommand = await sh.m_db.Connection.CreateCommandAsync($@"
                    INSERT INTO ""{sh.m_previousTable}"" (
                        ""Path"",
                        ""FileHash"",
                        ""MetaHash"",
                        ""Size"",
                        ""Type""
                    )
                    VALUES (
                        @Path,
                        @FileHash,
                        @MetaHash,
                        @Size,
                        @Type
                    )
                ");

                sh.m_insertCurrentElementCommand = await sh.m_db.Connection.CreateCommandAsync($@"
                    INSERT INTO ""{sh.m_currentTable}"" (
                        ""Path"",
                        ""FileHash"",
                        ""MetaHash"",
                        ""Size"",
                        ""Type""
                    )
                    VALUES (
                        @Path,
                        @FileHash,
                        @MetaHash,
                        @Size,
                        @Type
                    )
                ");

                return sh;
            }

            public async Task AddFromDb(long filesetId, bool asNew, IFilter filter)
            {
                var tablename = asNew ? m_currentTable : m_previousTable;

                var folders = $@"
                    SELECT
                        ""File"".""Path"" AS ""Path"",
                        NULL AS ""FileHash"",
                        ""Blockset"".""Fullhash"" AS ""MetaHash"",
                        -1 AS ""Size"",
                        {(int)Interface.ListChangesElementType.Folder} AS ""Type"",
                        ""FilesetEntry"".""FilesetID"" AS ""FilesetID""
                    FROM
                        ""File"",
                        ""FilesetEntry"",
                        ""Metadataset"",
                        ""Blockset""
                    WHERE
                        ""File"".""ID"" = ""FilesetEntry"".""FileID""
                        AND ""File"".""BlocksetID"" = -100
                        AND ""Metadataset"".""ID""=""File"".""MetadataID""
                        AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
                ";

                var symlinks = $@"
                    SELECT
                        ""File"".""Path"" AS ""Path"",
                        NULL AS ""FileHash"",
                        ""Blockset"".""Fullhash"" AS ""MetaHash"",
                        -1 AS ""Size"",
                        {(int)Interface.ListChangesElementType.Symlink} AS ""Type"",
                        ""FilesetEntry"".""FilesetID"" AS ""FilesetID""
                    FROM
                        ""File"",
                        ""FilesetEntry"",
                        ""Metadataset"",
                        ""Blockset""
                    WHERE
                        ""File"".""ID"" = ""FilesetEntry"".""FileID""
                        AND ""File"".""BlocksetID"" = -200
                        AND ""Metadataset"".""ID""=""File"".""MetadataID""
                        AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
                ";

                var files = $@"
                    SELECT
                        ""File"".""Path"" AS ""Path"",
                        ""FB"".""FullHash"" AS ""FileHash"",
                        ""MB"".""Fullhash"" AS ""MetaHash"",
                        ""FB"".""Length"" AS ""Size"",
                        {(int)Interface.ListChangesElementType.File} AS ""Type"",
                        ""FilesetEntry"".""FilesetID"" AS ""FilesetID""
                    FROM
                        ""File"",
                        ""FilesetEntry"",
                        ""Metadataset"",
                        ""Blockset"" MB,
                        ""Blockset"" FB
                    WHERE
                        ""File"".""ID"" = ""FilesetEntry"".""FileID""
                        AND ""File"".""BlocksetID"" >= 0
                        AND ""Metadataset"".""ID""=""File"".""MetadataID""
                        AND ""Metadataset"".""BlocksetID"" = ""MB"".""ID""
                        AND ""File"".""BlocksetID"" = ""FB"".""ID"" ";

                var combined = $"({folders} UNION {symlinks} UNION {files})";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    if (filter == null || filter.Empty)
                    {
                        // Simple case, select everything
                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{tablename}"" (
                                ""Path"",
                                ""FileHash"",
                                ""MetaHash"",
                                ""Size"",
                                ""Type""
                            )
                            SELECT
                                ""Path"",
                                ""FileHash"",
                                ""MetaHash"",
                                ""Size"",
                                ""Type""
                            FROM {combined} A
                            WHERE ""A"".""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQueryAsync();
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == Duplicati.Library.Utility.FilterType.Simple)
                    {
                        // File list based
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        var p = expression.GetSimpleList();
                        var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        await cmd.ExecuteNonQueryAsync($@"
                            CREATE TEMPORARY TABLE ""{filenamestable}"" (
                                ""Path"" TEXT NOT NULL
                            )
                        ");

                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{filenamestable}"" (""Path"")
                            VALUES (@Path)
                        ")
                            .PrepareAsync();

                        foreach (var s in p)
                            await cmd.SetParameterValue("@Path", s)
                                .ExecuteNonQueryAsync();

                        string whereClause;
                        if (expression.Result)
                        {
                            // Include filter
                            whereClause = $@"
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""Path"" IN (
                                    SELECT DISTINCT ""Path""
                                    FROM ""{filenamestable}""
                                )
                            ";
                        }
                        else
                        {
                            // Exclude filter
                            whereClause = $@"
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""Path"" NOT IN (
                                    SELECT DISTINCT ""Path""
                                    FROM ""{filenamestable}""
                                )
                            ";
                        }
                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{tablename}"" (
                                ""Path"",
                                ""FileHash"",
                                ""MetaHash"",
                                ""Size"",
                                ""Type""
                            )
                            SELECT
                                ""Path"",
                                ""FileHash"",
                                ""MetaHash"",
                                ""Size"",
                                ""Type""
                            FROM {combined} A
                            WHERE {whereClause}
                        ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQueryAsync();

                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filenamestable}"" ");
                    }
                    else
                    {
                        // Do row-wise iteration
                        var values = new object[5];
                        await cmd.SetCommandAndParameters($@"
                            SELECT
                                ""A"".""Path"",
                                ""A"".""FileHash"",
                                ""A"".""MetaHash"",
                                ""A"".""Size"",
                                ""A"".""Type""
                            FROM {combined} A
                            WHERE ""A"".""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .PrepareAsync();

                        using var cmd2 = m_db.Connection.CreateCommand(m_db.Transaction)
                            .SetCommandAndParameters($@"
                                INSERT INTO ""{tablename}"" (
                                    ""Path"",
                                    ""FileHash"",
                                    ""MetaHash"",
                                    ""Size"",
                                    ""Type""
                                )
                                VALUES (
                                    @Path,
                                    @FileHash,
                                    @MetaHash,
                                    @Size,
                                    @Type
                                )
                            ");
                        await cmd2.PrepareAsync();

                        using var rd = await cmd.ExecuteReaderAsync();
                        while (await rd.ReadAsync())
                        {
                            rd.GetValues(values);
                            var path = values[0] as string;
                            if (path != null && FilterExpression.Matches(filter, path.ToString()))
                            {
                                await cmd2
                                    .SetParameterValue("@Path", values[0])
                                    .SetParameterValue("@FileHash", values[1])
                                    .SetParameterValue("@MetaHash", values[2])
                                    .SetParameterValue("@Size", values[3])
                                    .SetParameterValue("@Type", values[4])
                                    .ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }

            public async Task AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew)
            {
                var cmd = asNew ? m_insertCurrentElementCommand : m_insertPreviousElementCommand;
                await cmd
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FileHash", filehash)
                    .SetParameterValue("@MetaHash", metahash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@Type", (int)type)
                    .ExecuteNonQueryAsync();
            }

            private static async IAsyncEnumerable<string?> ReaderToStringList(SqliteDataReader rd)
            {
                using (rd)
                    while (await rd.ReadAsync())
                    {
                        var v = rd.GetValue(0);
                        if (v == null || v == DBNull.Value)
                            yield return null;
                        else
                            yield return v.ToString();
                    }
            }

            private (string Added, string Deleted, string Modified) GetSqls(bool allTypes)
            {
                return (
                    $@"
                        SELECT ""Path""
                        FROM ""{m_currentTable}""
                        WHERE
                            {(allTypes ? "" : @$" ""{m_currentTable}"".""Type"" = @Type AND ")}
                            ""{m_currentTable}"".""Path"" NOT IN (
                                SELECT ""Path""
                                FROM ""{m_previousTable}""
                            )
                    ",

                    $@"
                        SELECT ""Path""
                        FROM ""{m_previousTable}""
                        WHERE
                            {(allTypes ? "" : @$" ""{m_previousTable}"".""Type"" = @Type AND ")}
                            ""{m_previousTable}"".""Path"" NOT IN (
                                SELECT ""Path""
                                FROM ""{m_currentTable}""
                            )
                    ",

                    $@"
                        SELECT ""{m_currentTable}"".""Path""
                        FROM ""{m_currentTable}"",""{m_previousTable}""
                        WHERE
                            {(allTypes ? "" : $@" ""{m_currentTable}"".""Type"" = @Type AND ")}
                            ""{m_currentTable}"".""Path"" = ""{m_previousTable}"".""Path""
                            AND (
                                ""{m_currentTable}"".""FileHash"" != ""{m_previousTable}"".""FileHash""
                                OR ""{m_currentTable}"".""MetaHash"" != ""{m_previousTable}"".""MetaHash""
                                OR ""{m_currentTable}"".""Type"" != ""{m_previousTable}"".""Type""
                            )
                    "
                );
            }

            public async Task<IChangeSizeReport> CreateChangeSizeReport()
            {
                var (Added, Deleted, Modified) = GetSqls(true);

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    var result = new ChangeSizeReport
                    {
                        PreviousSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_previousTable}""
                        ", 0),

                        CurrentSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_currentTable}""
                        ", 0),

                        AddedSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_currentTable}""
                            WHERE ""{m_currentTable}"".""Path"" IN ({Added})
                        ", 0),

                        DeletedSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_previousTable}""
                            WHERE ""{m_previousTable}"".""Path"" IN ({Deleted})
                        ", 0)
                    };

                    return result;
                }
            }

            public async Task<IChangeCountReport> CreateChangeCountReport()
            {
                var (Added, Deleted, Modified) = GetSqls(false);

                var added = @$"
                    SELECT COUNT(*)
                    FROM ({Added})
                ";

                var deleted = @$"
                    SELECT COUNT(*)
                    FROM ({Deleted})
                ";

                var modified = @$"
                    SELECT COUNT(*)
                    FROM ({Modified})
                ";

                using var cmd = m_db.Connection.CreateCommand(m_db.Transaction);

                var result = new ChangeCountReport
                {
                    AddedFolders = await cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0),
                    AddedSymlinks = await cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0),
                    AddedFiles = await cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0),

                    DeletedFolders = await cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0),
                    DeletedSymlinks = await cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0),
                    DeletedFiles = await cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0),

                    ModifiedFolders = await cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0),
                    ModifiedSymlinks = await cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0),
                    ModifiedFiles = await cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0)
                };

                return result;
            }

            public async IAsyncEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport()
            {
                var (Added, Deleted, Modified) = GetSqls(false);

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    var elTypes = new[] {
                        Interface.ListChangesElementType.Folder,
                        Interface.ListChangesElementType.Symlink,
                        Interface.ListChangesElementType.File
                    };

                    async IAsyncEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> BuildResult(SqliteCommand cmd, string sql, Interface.ListChangesChangeType changeType)
                    {
                        cmd.SetCommandAndParameters(sql);
                        foreach (var type in elTypes)
                            await foreach (var s in ReaderToStringList(await cmd.SetParameterValue("@Type", (int)type).ExecuteReaderAsync()))
                                yield return new Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>(changeType, type, s ?? "");
                    }

                    await foreach (var r in BuildResult(cmd, Added, Interface.ListChangesChangeType.Added))
                        yield return r;
                    await foreach (var r in BuildResult(cmd, Deleted, Interface.ListChangesChangeType.Deleted))
                        yield return r;
                    await foreach (var r in BuildResult(cmd, Modified, Interface.ListChangesChangeType.Modified))
                        yield return r;
                }
            }

            public void Dispose()
            {
                DisposeAsync().Await();
            }

            public async Task DisposeAsync()
            {
                if (m_insertPreviousElementCommand != null)
                {
                    try { await m_insertPreviousElementCommand.DisposeAsync(); }
                    catch { }
                    finally { m_insertPreviousElementCommand = null!; }
                }

                if (m_insertCurrentElementCommand != null)
                {
                    try { await m_insertCurrentElementCommand.DisposeAsync(); }
                    catch { }
                    finally { m_insertCurrentElementCommand = null!; }
                }

                try { await m_db.Transaction.RollBackAsync(); }
                catch { }
                finally
                {
                    m_previousTable = null!;
                    m_currentTable = null!;
                }
            }
        }

        public async Task<IStorageHelper> CreateStorageHelper()
        {
            return await StorageHelper.CreateAsync(this);
        }
    }
}

