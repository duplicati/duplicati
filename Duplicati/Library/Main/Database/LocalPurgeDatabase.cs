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
    /// A local database for purging files from a fileset.
    /// </summary>
    internal class LocalPurgeDatabase : LocalDeleteDatabase
    {
        /// <summary>
        /// Creates a new instance of the <see cref="LocalPurgeDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="pagecachesize">The size of the page cache.</param>
        /// <param name="dbnew">The optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalPurgeDatabase"/>.</returns>
        public static async Task<LocalPurgeDatabase> CreateAsync(string path, long pagecachesize, LocalPurgeDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalPurgeDatabase();

            dbnew = (LocalPurgeDatabase)
                await LocalDeleteDatabase.CreateAsync(path, "Purge", pagecachesize, dbnew, token)
                    .ConfigureAwait(false);

            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LocalPurgeDatabase"/> class.
        /// </summary>
        /// <param name="dbparent">The parent database to use.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalPurgeDatabase"/>.</returns>
        public static async Task<LocalPurgeDatabase> CreateAsync(LocalDatabase dbparent, LocalPurgeDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalPurgeDatabase();

            dbnew = (LocalPurgeDatabase)
                await LocalDeleteDatabase.CreateAsync(dbparent, dbnew, token)
                    .ConfigureAwait(false);

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ITemporaryFileset"/> class.
        /// </summary>
        /// <param name="parentid">The ID of the parent fileset.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="ITemporaryFileset"/>.</returns>
        public async Task<ITemporaryFileset> CreateTemporaryFileset(long parentid, CancellationToken token)
        {
            return await TemporaryFileset.CreateAsync(parentid, this, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the remote volume name for a fileset with the specified ID.
        /// </summary>
        /// <param name="id">The ID of the fileset.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the name of the remote volume.</returns>
        public async Task<string> GetRemoteVolumeNameForFileset(long id, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""B"".""Name""
                FROM
                    ""Fileset"" ""A"",
                    ""RemoteVolume"" ""B""
                WHERE
                    ""A"".""VolumeID"" = ""B"".""ID""
                    AND ""A"".""ID"" = @FilesetId
                ")
                    .SetParameterValue("@FilesetId", id);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await rd.ReadAsync(token).ConfigureAwait(false))
                throw new Exception($"No remote volume found for fileset with id {id}");
            else
                return rd.ConvertValueToString(0) ?? throw new Exception($"Remote volume name for fileset with id {id} is null");
        }

        /// <summary>
        /// Counts the number of orphan files in the database.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the count of orphan files.</returns>
        internal async Task<long> CountOrphanFiles(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            cmd.SetCommandAndParameters(@"
                SELECT COUNT(*)
                FROM ""FileLookup""
                WHERE ""ID"" NOT IN (
                    SELECT DISTINCT ""FileID""
                    FROM ""FilesetEntry""
                )
            ");
            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await rd.ReadAsync(token).ConfigureAwait(false))
                return rd.ConvertValueToInt64(0, 0);
            else
                return 0;
        }

        /// <summary>
        /// Interface for a temporary fileset used for purging files from a fileset.
        /// </summary>
        public interface ITemporaryFileset : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Gets the ID of the parent fileset.
            /// </summary>
            long ParentID { get; }
            /// <summary>
            /// Gets the count of removed files.
            /// </summary>
            long RemovedFileCount { get; }
            /// <summary>
            /// Gets the total size of removed files.
            /// </summary>
            long RemovedFileSize { get; }
            /// <summary>
            /// Gets the count of updated files.
            /// </summary>
            long UpdatedFileCount { get; }

            /// <summary>
            /// Applies a filter to the temporary fileset.
            /// </summary>
            /// <param name="filter">The filter to apply.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the filter has been applied.</returns>
            Task ApplyFilter(Library.Utility.IFilter filter, CancellationToken token);
            /// <summary>
            /// Applies a filter using a custom command.
            /// </summary>
            /// <param name="filtercommand">The command to execute for filtering.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the filter has been applied.</returns>
            Task ApplyFilter(Func<SqliteCommand, long, string, Task<int>> filtercommand, CancellationToken token);
            /// <summary>
            /// Converts the temporary fileset to a permanent fileset.
            /// </summary>
            /// <param name="name">The name of the new fileset.</param>
            /// <param name="timestamp">The timestamp for the new fileset.</param>
            /// <param name="isFullBackup">Indicates if this is a full backup.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that when awaited contains a tuple with the remote volume ID and the new fileset ID.</returns>
            Task<Tuple<long, long>> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup, CancellationToken token);
            /// <summary>
            /// Lists all deleted files in the temporary fileset.
            /// </summary>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>An asynchronous enumerable of key-value pairs where the key is the file path and the value is the file size.</returns>
            IAsyncEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles(CancellationToken token);
        }

        /// <summary>
        /// A temporary fileset implementation for purging files from a fileset.
        /// </summary>
        private class TemporaryFileset : ITemporaryFileset
        {
            /// <summary>
            /// The name of the temporary table used for storing deleted files.
            /// </summary>
            private string m_tablename = null!;
            /// <summary>
            /// The database instance used for queries.
            /// </summary>
            private LocalDatabase m_db = null!;

            public long ParentID { get; private set; }
            public long RemovedFileCount { get; private set; }
            public long RemovedFileSize { get; private set; }
            public long UpdatedFileCount { get; private set; }

            /// <summary>
            /// Calling this constructor will throw an exception. Use CreateAsync instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public TemporaryFileset(long parentid, LocalPurgeDatabase parentdb, SqliteConnection connection, SqliteTransaction transaction)
            {
                throw new NotImplementedException("Use CreateAsync instead");
            }

            /// <summary>
            /// Private constructor to prevent direct instantiation.
            /// </summary>
            private TemporaryFileset() { }

            /// <summary>
            /// Creates a new instance of the <see cref="TemporaryFileset"/> class asynchronously.
            /// </summary>
            /// <param name="parentid">The ID of the parent fileset.</param>
            /// <param name="db">The database instance to use.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that when awaited contains a new instance of <see cref="TemporaryFileset"/>.</returns>
            public static async Task<TemporaryFileset> CreateAsync(long parentid, LocalDatabase db, CancellationToken token)
            {
                var tempf = new TemporaryFileset()
                {
                    ParentID = parentid,
                    m_db = db,
                    m_tablename = "TempDeletedFilesTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())
                };

                await using (var cmd = db.Connection.CreateCommand())
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{tempf.m_tablename}"" (
                            ""FileID"" INTEGER PRIMARY KEY
                        )
                    ", token)
                        .ConfigureAwait(false);

                return tempf;
            }

            /// <summary>
            /// Applies a filter to the temporary fileset using a custom command.
            /// </summary>
            /// <param name="filtercommand">The command to execute for filtering.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the filter has been applied.</returns>
            public async Task ApplyFilter(Func<SqliteCommand, long, string, Task<int>> filtercommand, CancellationToken token)
            {
                int updated;
                await using (var cmd = m_db.Connection.CreateCommand())
                    updated = await filtercommand(cmd, ParentID, m_tablename)
                        .ConfigureAwait(false);

                await PostFilterChecks(updated, token).ConfigureAwait(false);
            }

            /// <summary>
            /// Applies a filter to the temporary fileset.
            /// </summary>
            /// <param name="filter">The filter to apply.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the filter has been applied.</returns>
            public async Task ApplyFilter(Library.Utility.IFilter filter, CancellationToken token)
            {
                if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == Duplicati.Library.Utility.FilterType.Simple)
                {
                    // File list based
                    // unfortunately we cannot do this if the filesystem is not case-sensitive as
                    // SQLite only supports ASCII compares
                    var p = expression.GetSimpleList();
                    var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    await using var cmd = m_db.Connection.CreateCommand();
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
                        await cmd.SetParameterValue("@Path", s)
                            .ExecuteNonQueryAsync(token)
                            .ConfigureAwait(false);

                    await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tablename}"" (""FileID"")
                            SELECT DISTINCT ""A"".""FileID""
                            FROM
                                ""FilesetEntry"" ""A"",
                                ""File"" ""B""
                            WHERE
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""FileID"" = ""B"".""ID""
                                AND ""B"".""Path"" IN ""{filenamestable}""
                        ")
                        .SetParameterValue("@FilesetId", ParentID)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);

                    await cmd
                        .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filenamestable}"" ", token)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Do row-wise iteration
                    var values = new object[2];
                    await using var cmd = m_db.Connection.CreateCommand();
                    await using var cmd2 = m_db.Connection.CreateCommand();
                    await cmd2.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tablename}"" (""FileID"")
                            VALUES (@FileId)
                        ")
                        .PrepareAsync(token)
                        .ConfigureAwait(false);

                    cmd.SetCommandAndParameters(@"
                            SELECT
                                ""B"".""Path"",
                                ""A"".""FileID""
                            FROM
                                ""FilesetEntry"" ""A"",
                                ""File"" ""B""
                            WHERE
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""FileID"" = ""B"".""ID""
                        ")
                        .SetParameterValue("@FilesetId", ParentID);

                    await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await rd.ReadAsync(token).ConfigureAwait(false))
                    {
                        rd.GetValues(values);
                        var path = values[0] as string;
                        if (path != null && Library.Utility.FilterExpression.Matches(filter, path.ToString()))
                        {
                            await cmd2
                                .SetParameterValue("@FileId", values[1])
                                .ExecuteNonQueryAsync(token)
                                .ConfigureAwait(false);
                        }
                    }
                }

                await PostFilterChecks(0, token).ConfigureAwait(false);
            }

            /// <summary>
            /// Applies the filter checks after filtering.
            /// </summary>
            /// <param name="updated">The number of files updated by the filter.</param>
            /// <param name="token">A cancellation token to cancel the operation.</param>
            /// <returns>A task that completes when the checks have been applied.</returns>
            private async Task PostFilterChecks(int updated, CancellationToken token)
            {
                await using var cmd = m_db.Connection.CreateCommand();
                UpdatedFileCount = updated;

                RemovedFileCount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""{m_tablename}""
                    ", 0, token)
                    .ConfigureAwait(false);

                RemovedFileSize = await cmd.ExecuteScalarInt64Async($@"
                        SELECT SUM(""C"".""Length"")
                        FROM
                            ""{m_tablename}"" ""A"",
                            ""FileLookup"" ""B"",
                            ""Blockset"" ""C"",
                            ""Metadataset"" ""D""
                        WHERE
                            ""A"".""FileID"" = ""B"".""ID""
                            AND (
                                ""B"".""BlocksetID"" = ""C"".""ID""
                                OR (
                                    ""B"".""MetadataID"" = ""D"".""ID""
                                    AND ""D"".""BlocksetID"" = ""C"".""ID""
                                )
                            )
                    ", 0, token)
                    .ConfigureAwait(false);

                var filesetcount = await cmd.SetCommandAndParameters($@"
                        SELECT COUNT(*)
                        FROM ""FilesetEntry""
                        WHERE ""FilesetID"" = @ParentID
                    ")
                    .SetParameterValue("@ParentID", ParentID)
                    .ExecuteScalarInt64Async(0, token)
                    .ConfigureAwait(false);

                if (filesetcount == RemovedFileCount)
                    throw new Interface.UserInformationException($"Refusing to purge {RemovedFileCount} files from fileset with ID {ParentID}, as that would remove the entire fileset.\nTo delete a fileset, use the \"delete\" command.", "PurgeWouldRemoveEntireFileset");
            }

            /// <inheritdoc/>
            public async Task<Tuple<long, long>> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup, CancellationToken token)
            {
                var remotevolid =
                    await m_db
                        .RegisterRemoteVolume(name, RemoteVolumeType.Files, RemoteVolumeState.Temporary, token)
                        .ConfigureAwait(false);

                var filesetid = await m_db
                    .CreateFileset(remotevolid, timestamp, token)
                    .ConfigureAwait(false);

                await m_db
                    .UpdateFullBackupStateInFileset(filesetid, isFullBackup, token)
                    .ConfigureAwait(false);

                await using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                    await cmd.SetCommandAndParameters($@"
                        INSERT INTO ""FilesetEntry"" (
                            ""FilesetID"",
                            ""FileID"",
                            ""Lastmodified""
                        )
                        SELECT
                            @TargetFilesetId,
                            ""FileID"",
                            ""LastModified""
                        FROM ""FilesetEntry""
                        WHERE
                            ""FilesetID"" = @SourceFilesetId
                            AND ""FileID"" NOT IN ""{m_tablename}""
                    ")
                        .SetParameterValue("@TargetFilesetId", filesetid)
                        .SetParameterValue("@SourceFilesetId", ParentID)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);

                return new Tuple<long, long>(remotevolid, filesetid);
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles([EnumeratorCancellation] CancellationToken token)
            {
                await using var cmd = m_db.Connection.CreateCommand();
                await using var rd = await cmd.ExecuteReaderAsync($@"
                    SELECT
                        ""B"".""Path"",
                        ""C"".""Length""
                    FROM
                        ""{m_tablename}"" ""A"",
                        ""File"" ""B"",
                        ""Blockset"" ""C""
                    WHERE
                        ""A"".""FileID"" = ""B"".""ID""
                        AND ""B"".""BlocksetID"" = ""C"".""ID""
                ", token)
                    .ConfigureAwait(false);

                while (await rd.ReadAsync(token).ConfigureAwait(false))
                    yield return new KeyValuePair<string, long>(
                        rd.ConvertValueToString(0) ?? "",
                        rd.ConvertValueToInt64(1)
                    );
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using var cmd = m_db.Connection.CreateCommand();
                    await cmd
                        .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tablename}""", default)
                        .ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
