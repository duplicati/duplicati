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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// A local database for tracking changes in file lists, such as added, deleted, or modified files.
    /// </summary>
    internal class LocalListChangesDatabase : LocalDatabase
    {

        /// <summary>
        /// Creates a new instance of the <see cref="LocalListChangesDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalListChangesDatabase"/>.</returns>
        public static async Task<LocalListChangesDatabase> CreateAsync(string path, LocalListChangesDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalListChangesDatabase();

            dbnew = (LocalListChangesDatabase)
                await CreateLocalDatabaseAsync(path, "ListChanges", false, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Interface for storage helper that manages temporary storage of file changes.
        /// </summary>
        public interface IStorageHelper : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Adds an element to the temporary storage.
            /// </summary>
            /// <param name="path">The path of the file or folder.</param>
            /// <param name="filehash">The file hash, if applicable.</param>
            /// <param name="metahash">The metadata hash.</param>
            /// <param name="size">The size of the file or folder.</param>
            /// <param name="type">The type of the element (file, folder, symlink).</param>
            /// <param name="asNew">If true, adds to the current table; otherwise, adds to the previous table.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the element is added.</returns>
            Task AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew, CancellationToken token);

            /// <summary>
            /// Adds elements from the database to the temporary storage.
            /// </summary>
            /// <param name="filesetId">The ID of the fileset to add.</param>
            /// <param name="asNew">If true, adds to the current table; otherwise, adds to the previous table.</param>
            /// <param name="filter">An optional filter to apply when adding elements.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the elements are added.</returns>
            Task AddFromDb(long filesetId, bool asNew, IFilter filter, CancellationToken token);

            /// <summary>
            /// Creates a report containing the count of added, deleted, and modified elements.
            /// </summary>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that, when awaited, returns an <see cref="IChangeCountReport"/> with the change counts.</returns>
            Task<IChangeCountReport> CreateChangeCountReport(CancellationToken token);

            /// <summary>
            /// Creates a report containing the size information for added, deleted, previous, and current elements.
            /// </summary>
            /// <param name="token"> A cancellation token to cancel the operation.</param>
            /// <returns>A task that, when awaited, returns an <see cref="IChangeSizeReport"/> with the size details.</returns>
            Task<IChangeSizeReport> CreateChangeSizeReport(CancellationToken token);

            /// <summary>
            /// Asynchronously generates a report of changed files, yielding tuples that describe the change type, element type, and file path.
            /// </summary>
            /// <param name="token"> A cancellation token to cancel the operation.</param>
            /// <returns>An asynchronous enumerable of tuples containing the change type, element type, and file path.</returns>
            IAsyncEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport(CancellationToken token);
        }

        /// <summary>
        /// Interface for reporting changes in file counts and sizes.
        /// </summary>
        public interface IChangeCountReport
        {
            /// <summary>
            /// Gets the count of added folders.
            /// </summary>
            long AddedFolders { get; }
            /// <summary>
            /// Gets the count of added symlinks.
            /// </summary>
            long AddedSymlinks { get; }
            /// <summary>
            /// Gets the count of added files.
            /// </summary>
            long AddedFiles { get; }

            /// <summary>
            /// Gets the count of deleted folders.
            /// </summary>
            long DeletedFolders { get; }
            /// <summary>
            /// Gets the count of deleted symlinks.
            /// </summary>
            long DeletedSymlinks { get; }
            /// <summary>
            /// Gets the count of deleted files.
            /// </summary>
            long DeletedFiles { get; }

            /// <summary>
            /// Gets the count of modified folders.
            /// </summary>
            long ModifiedFolders { get; }
            /// <summary>
            /// Gets the count of modified symlinks.
            /// </summary>
            long ModifiedSymlinks { get; }
            /// <summary>
            /// Gets the count of modified files.
            /// </summary>
            long ModifiedFiles { get; }
        }

        /// <summary>
        /// Interface for a report describing changes in file sizes.
        /// </summary>
        public interface IChangeSizeReport
        {
            /// <summary>
            /// Gets the total size of added files.
            /// </summary>
            long AddedSize { get; }
            /// <summary>
            /// Gets the total size of deleted files.
            /// </summary>
            long DeletedSize { get; }

            /// <summary>
            /// Gets the size of files in the previous state.
            /// </summary>
            long PreviousSize { get; }
            /// <summary>
            /// Gets the size of files in the current state.
            /// </summary>
            long CurrentSize { get; }
        }

        /// <summary>
        /// Internal class that implements the <see cref="IChangeCountReport"/> interface to report changes in file counts.
        /// </summary>
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

        /// <summary>
        /// Internal class that implements the <see cref="IChangeSizeReport"/> interface to report changes in file sizes.
        /// </summary>
        internal class ChangeSizeReport : IChangeSizeReport
        {
            public long AddedSize { get; internal set; }
            public long DeletedSize { get; internal set; }

            public long PreviousSize { get; internal set; }
            public long CurrentSize { get; internal set; }
        }

        /// <summary>
        /// Helper class for managing temporary storage of file changes.
        /// Implements the <see cref="IStorageHelper"/> interface.
        /// </summary>
        private class StorageHelper : IStorageHelper
        {
            /// <summary>
            /// The database instance used for storage operations.
            /// </summary>
            private LocalDatabase m_db = null!;

            /// <summary>
            /// Command for inserting elements into the previous table.
            /// </summary>
            private SqliteCommand m_insertPreviousElementCommand = null!;
            /// <summary>
            /// Command for inserting elements into the current table.
            /// </summary>
            private SqliteCommand m_insertCurrentElementCommand = null!;

            /// <summary>
            /// The name of the temporary table for previous elements.
            /// </summary>
            private string m_previousTable = null!;
            /// <summary>
            /// The name of the temporary table for current elements.
            /// </summary>
            private string m_currentTable = null!;

            /// <summary>
            /// Private constructor to prevent direct instantiation.
            /// This constructor is obsolete and will throw an exception if called.
            /// Use the <see cref="CreateAsync(LocalDatabase)"/> method to create an instance instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public StorageHelper(SqliteConnection con) { }

            /// <summary>
            /// Private constructor to prevent direct instantiation.
            /// This class should be created using the CreateAsync method.
            /// </summary>
            private StorageHelper() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="StorageHelper"/> class.
            /// </summary>
            /// <param name="db">The local database instance to use.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that, when awaited, returns a new instance of <see cref="StorageHelper"/>.</returns>
            public static async Task<StorageHelper> CreateAsync(LocalDatabase db, CancellationToken token)
            {
                var sh = new StorageHelper
                {
                    m_db = db,
                    m_previousTable = "Previous-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray()),
                    m_currentTable = "Current-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())
                };

                await using (var cmd = sh.m_db.Connection.CreateCommand(db.Transaction))
                {
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{sh.m_previousTable}"" (
                            ""Path"" TEXT NOT NULL,
                            ""FileHash"" TEXT NULL,
                            ""MetaHash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Type"" INTEGER NOT NULL
                        )
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{sh.m_currentTable}"" (
                            ""Path"" TEXT NOT NULL,
                            ""FileHash"" TEXT NULL,
                            ""MetaHash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Type"" INTEGER NOT NULL
                        )
                    ", token)
                        .ConfigureAwait(false);
                }

                sh.m_insertPreviousElementCommand = await sh.m_db.Connection
                    .CreateCommandAsync($@"
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
                    ", token)
                    .ConfigureAwait(false);

                sh.m_insertCurrentElementCommand = await sh.m_db.Connection
                    .CreateCommandAsync($@"
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
                    ", token)
                    .ConfigureAwait(false);

                return sh;
            }

            /// <inheritdoc/>
            public async Task AddFromDb(long filesetId, bool asNew, IFilter filter, CancellationToken token)
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

                await using var cmd = m_db.Connection.CreateCommand(m_db.Transaction);
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
                            FROM {combined} ""A""
                            WHERE ""A"".""FilesetID"" = @FilesetId
                        ")
                        .SetParameterValue("@FilesetId", filesetId)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);
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
                        ", token)
                        .ConfigureAwait(false);

                    await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{filenamestable}"" (""Path"")
                            VALUES (@Path)
                        ")
                        .PrepareAsync(token)
                        .ConfigureAwait(false);

                    foreach (var s in p)
                        await cmd
                            .SetParameterValue("@Path", s)
                            .ExecuteNonQueryAsync(token)
                            .ConfigureAwait(false);

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
                            FROM {combined} ""A""
                            WHERE {whereClause}
                        ")
                        .SetParameterValue("@FilesetId", filesetId)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);

                    await cmd
                        .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filenamestable}"" ", token)
                        .ConfigureAwait(false);
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
                            FROM {combined} ""A""
                            WHERE ""A"".""FilesetID"" = @FilesetId
                        ")
                        .SetParameterValue("@FilesetId", filesetId)
                        .PrepareAsync(token)
                        .ConfigureAwait(false);

                    await using var cmd2 = m_db.Connection.CreateCommand(m_db.Transaction)
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
                    await cmd2.PrepareAsync(token).ConfigureAwait(false);

                    await using var rd = await cmd
                        .ExecuteReaderAsync(token)
                        .ConfigureAwait(false);

                    while (await rd.ReadAsync(token).ConfigureAwait(false))
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
                                .ExecuteNonQueryAsync(token)
                                .ConfigureAwait(false);
                        }
                    }
                }
            }

            /// <inheritdoc/>
            public async Task AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew, CancellationToken token)
            {
                var cmd = asNew ? m_insertCurrentElementCommand : m_insertPreviousElementCommand;
                await cmd
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FileHash", filehash)
                    .SetParameterValue("@MetaHash", metahash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@Type", (int)type)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Converts a SqliteDataReader to an asynchronous enumerable of strings.
            /// </summary>
            /// <param name="rd">The SqliteDataReader to read from.</param>
            /// <param name="token"> A cancellation token to cancel the operation.</param>
            /// <returns>An asynchronous enumerable of strings, where each string is a value from the first column of the reader.</returns>
            private static async IAsyncEnumerable<string?> ReaderToStringList(SqliteDataReader rd, [EnumeratorCancellation] CancellationToken token)
            {
                await using (rd)
                    while (await rd.ReadAsync(token).ConfigureAwait(false))
                    {
                        var v = rd.GetValue(0);
                        if (v == null || v == DBNull.Value)
                            yield return null;
                        else
                            yield return v.ToString();
                    }
            }

            /// <summary>
            /// Retrieves SQL queries for added, deleted, and modified files based on the current and previous tables.
            /// </summary>
            /// <param name="allTypes">If true, retrieves all types of changes; otherwise, filters by type.</param>
            /// <returns>A tuple containing SQL queries for added, deleted, and modified files.</returns>
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

            /// <summary>
            /// Creates a report of changes in file sizes, including added, deleted, previous, and current sizes.
            /// </summary>
            /// <param name="token"> A cancellation token to cancel the operation.</param>
            /// <returns>A task that, when awaited, returns an <see cref="IChangeSizeReport"/> with the size details.</returns>
            public async Task<IChangeSizeReport> CreateChangeSizeReport(CancellationToken token)
            {
                var (Added, Deleted, Modified) = GetSqls(true);

                await using var cmd = m_db.Connection.CreateCommand(m_db.Transaction);
                var result = new ChangeSizeReport
                {
                    PreviousSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_previousTable}""
                        ", 0, token)
                        .ConfigureAwait(false),

                    CurrentSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_currentTable}""
                        ", 0, token)
                        .ConfigureAwait(false),

                    AddedSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_currentTable}""
                            WHERE ""{m_currentTable}"".""Path"" IN ({Added})
                        ", 0, token)
                        .ConfigureAwait(false),

                    DeletedSize = await cmd.ExecuteScalarInt64Async($@"
                            SELECT SUM(""Size"")
                            FROM ""{m_previousTable}""
                            WHERE ""{m_previousTable}"".""Path"" IN ({Deleted})
                        ", 0, token)
                        .ConfigureAwait(false)
                };

                return result;
            }

            /// <summary>
            /// Creates a report containing the count of added, deleted, and modified elements.
            /// </summary>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that, when awaited, returns an <see cref="IChangeCountReport"/> with the change counts.</returns>
            public async Task<IChangeCountReport> CreateChangeCountReport(CancellationToken token)
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

                await using var cmd = m_db.Connection.CreateCommand(m_db.Transaction);

                var result = new ChangeCountReport
                {
                    AddedFolders = await cmd
                        .SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    AddedSymlinks = await cmd
                        .SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    AddedFiles = await cmd
                        .SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    DeletedFolders = await cmd
                        .SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    DeletedSymlinks = await cmd
                        .SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    DeletedFiles = await cmd
                        .SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    ModifiedFolders = await cmd
                        .SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    ModifiedSymlinks = await cmd
                        .SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false),
                    ModifiedFiles = await cmd
                        .SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false)
                };

                return result;
            }

            /// <summary>
            /// Asynchronously creates a report of changed files, yielding tuples that describe the change type, element type, and file path.
            /// </summary>
            /// <param name="token"> A cancellation token to cancel the operation.</param>
            /// <returns>An asynchronous enumerable of tuples containing the change type, element type, and file path.</returns>
            public async IAsyncEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport([EnumeratorCancellation] CancellationToken token)
            {
                var (Added, Deleted, Modified) = GetSqls(false);

                await using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
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
                            await foreach (var s in ReaderToStringList(await cmd.SetParameterValue("@Type", (int)type).ExecuteReaderAsync(token).ConfigureAwait(false), token).ConfigureAwait(false))
                                yield return new Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>(changeType, type, s ?? "");
                    }

                    await foreach (var r in
                            BuildResult(cmd, Added, Interface.ListChangesChangeType.Added)
                                .ConfigureAwait(false)
                    )
                        yield return r;

                    await foreach (var r in
                        BuildResult(cmd, Deleted, Interface.ListChangesChangeType.Deleted)
                            .ConfigureAwait(false)
                    )
                        yield return r;

                    await foreach (var r in
                        BuildResult(cmd, Modified, Interface.ListChangesChangeType.Modified)
                            .ConfigureAwait(false)
                    )
                        yield return r;
                }
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                if (m_insertPreviousElementCommand != null)
                {
                    try
                    {
                        await m_insertPreviousElementCommand
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch { }
                    finally { m_insertPreviousElementCommand = null!; }
                }

                if (m_insertCurrentElementCommand != null)
                {
                    try
                    {
                        await m_insertCurrentElementCommand
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch { }
                    finally { m_insertCurrentElementCommand = null!; }
                }

                try
                {
                    await m_db.Transaction
                        .RollBackAsync()
                        .ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    m_previousTable = null!;
                    m_currentTable = null!;
                }
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IStorageHelper"/> for managing temporary storage of file changes.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that, when awaited, returns an instance of <see cref="IStorageHelper"/>.</returns>
        public async Task<IStorageHelper> CreateStorageHelper(CancellationToken token)
        {
            return await StorageHelper.CreateAsync(this, token).ConfigureAwait(false);
        }
    }
}

