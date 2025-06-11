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
    internal class LocalPurgeDatabase : LocalDeleteDatabase
    {
        public static async Task<LocalPurgeDatabase> CreateAsync(string path, long pagecachesize, LocalPurgeDatabase? dbnew = null)
        {
            dbnew ??= new LocalPurgeDatabase();

            dbnew = (LocalPurgeDatabase)await LocalDeleteDatabase.CreateAsync(path, "Purge", pagecachesize, dbnew);

            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        public static async Task<LocalPurgeDatabase> CreateAsync(LocalDatabase dbparent, LocalPurgeDatabase? dbnew = null)
        {
            dbnew ??= new LocalPurgeDatabase();

            dbnew = (LocalPurgeDatabase)await LocalDeleteDatabase.CreateAsync(dbparent, dbnew);

            return dbnew;
        }

        public async Task<ITemporaryFileset> CreateTemporaryFileset(long parentid)
        {
            return await TemporaryFileset.CreateAsync(parentid, this);
        }

        public async Task<string> GetRemoteVolumeNameForFileset(long id)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""B"".""Name""
                FROM
                    ""Fileset"" A,
                    ""RemoteVolume"" B
                WHERE
                    ""A"".""VolumeID"" = ""B"".""ID""
                    AND ""A"".""ID"" = @FilesetId
                ")
                    .SetParameterValue("@FilesetId", id);

            using (var rd = await cmd.ExecuteReaderAsync())
                if (!await rd.ReadAsync())
                    throw new Exception($"No remote volume found for fileset with id {id}");
                else
                    return rd.ConvertValueToString(0) ?? throw new Exception($"Remote volume name for fileset with id {id} is null");
        }

        internal async Task<long> CountOrphanFiles()
        {
            using var cmd = m_connection.CreateCommand(m_rtr);
            cmd.SetCommandAndParameters(@"
                SELECT COUNT(*)
                FROM ""FileLookup""
                WHERE ""ID"" NOT IN (
                    SELECT DISTINCT ""FileID""
                    FROM ""FilesetEntry""
                )
            ");
            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
                return rd.ConvertValueToInt64(0, 0);
            else
                return 0;
        }

        public interface ITemporaryFileset : IDisposable
        {
            long ParentID { get; }
            long RemovedFileCount { get; }
            long RemovedFileSize { get; }
            long UpdatedFileCount { get; }

            Task ApplyFilter(Library.Utility.IFilter filter);
            Task ApplyFilter(Func<SqliteCommand, long, string, Task<int>> filtercommand);
            Task<Tuple<long, long>> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup);
            IAsyncEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles();
        }

        private class TemporaryFileset : ITemporaryFileset
        {
            private string m_tablename = null!;
            private LocalDatabase m_db = null!;

            public long ParentID { get; private set; }
            public long RemovedFileCount { get; private set; }
            public long RemovedFileSize { get; private set; }
            public long UpdatedFileCount { get; private set; }

            [Obsolete("Calling this constructor will throw an exception. Use CreateAsync instead.")]
            public TemporaryFileset(long parentid, LocalPurgeDatabase parentdb, SqliteConnection connection, SqliteTransaction transaction)
            {
                throw new NotImplementedException("Use CreateAsync instead");
            }

            private TemporaryFileset() { }

            public static async Task<TemporaryFileset> CreateAsync(long parentid, LocalDatabase db)
            {
                var tempf = new TemporaryFileset()
                {
                    ParentID = parentid,
                    m_db = db,
                    m_tablename = "TempDeletedFilesTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())
                };

                using (var cmd = db.Connection.CreateCommand())
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{tempf.m_tablename}"" (
                            ""FileID"" INTEGER PRIMARY KEY
                        )
                    ");

                return tempf;
            }

            public async Task ApplyFilter(Func<SqliteCommand, long, string, Task<int>> filtercommand)
            {
                int updated;
                using (var cmd = m_db.Connection.CreateCommand())
                    updated = await filtercommand(cmd, ParentID, m_tablename);

                await PostFilterChecks(updated);
            }

            public async Task ApplyFilter(Library.Utility.IFilter filter)
            {
                if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == Duplicati.Library.Utility.FilterType.Simple)
                {
                    // File list based
                    // unfortunately we cannot do this if the filesystem is not case-sensitive as
                    // SQLite only supports ASCII compares
                    var p = expression.GetSimpleList();
                    var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    using (var cmd = m_db.Connection.CreateCommand())
                    {
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

                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tablename}"" (""FileID"")
                            SELECT DISTINCT ""A"".""FileID""
                            FROM
                                ""FilesetEntry"" A,
                                ""File"" B
                            WHERE
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""FileID"" = ""B"".""ID""
                                AND ""B"".""Path"" IN ""{filenamestable}""
                        ")
                            .SetParameterValue("@FilesetId", ParentID)
                            .ExecuteNonQueryAsync();

                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filenamestable}"" ");
                    }
                }
                else
                {
                    // Do row-wise iteration
                    var values = new object[2];
                    using (var cmd = m_db.Connection.CreateCommand())
                    using (var cmd2 = m_db.Connection.CreateCommand())
                    {
                        await cmd2.SetCommandAndParameters($@"
                            INSERT INTO ""{m_tablename}"" (""FileID"")
                            VALUES (@FileId)
                        ")
                            .PrepareAsync();

                        cmd.SetCommandAndParameters(@"
                            SELECT
                                ""B"".""Path"",
                                ""A"".""FileID""
                            FROM
                                ""FilesetEntry"" A,
                                ""File"" B
                            WHERE
                                ""A"".""FilesetID"" = @FilesetId
                                AND ""A"".""FileID"" = ""B"".""ID""
                        ")
                            .SetParameterValue("@FilesetId", ParentID);

                        using (var rd = await cmd.ExecuteReaderAsync())
                            while (await rd.ReadAsync())
                            {
                                rd.GetValues(values);
                                var path = values[0] as string;
                                if (path != null && Library.Utility.FilterExpression.Matches(filter, path.ToString()))
                                {
                                    await cmd2
                                        .SetParameterValue("@FileId", values[1])
                                        .ExecuteNonQueryAsync();
                                }
                            }
                    }
                }

                await PostFilterChecks(0);
            }

            private async Task PostFilterChecks(int updated)
            {
                using (var cmd = m_db.Connection.CreateCommand())
                {
                    UpdatedFileCount = updated;

                    RemovedFileCount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""{m_tablename}""
                    ", 0);

                    RemovedFileSize = await cmd.ExecuteScalarInt64Async($@"
                        SELECT SUM(""C"".""Length"")
                        FROM
                            ""{m_tablename}"" A,
                            ""FileLookup"" B,
                            ""Blockset"" C,
                            ""Metadataset"" D
                        WHERE
                            ""A"".""FileID"" = ""B"".""ID""
                            AND (
                                ""B"".""BlocksetID"" = ""C"".""ID""
                                OR (
                                    ""B"".""MetadataID"" = ""D"".""ID""
                                    AND ""D"".""BlocksetID"" = ""C"".""ID""
                                )
                            )
                        ", 0);

                    var filesetcount = await cmd.ExecuteScalarInt64Async($@"
                        SELECT COUNT(*)
                        FROM ""FilesetEntry""
                        WHERE ""FilesetID"" = {ParentID}
                    ", 0);

                    if (filesetcount == RemovedFileCount)
                        throw new Interface.UserInformationException($"Refusing to purge {RemovedFileCount} files from fileset with ID {ParentID}, as that would remove the entire fileset.\nTo delete a fileset, use the \"delete\" command.", "PurgeWouldRemoveEntireFileset");
                }
            }

            public async Task<Tuple<long, long>> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup)
            {
                var remotevolid = await m_db.RegisterRemoteVolume(name, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                var filesetid = await m_db.CreateFileset(remotevolid, timestamp);
                await m_db.UpdateFullBackupStateInFileset(filesetid, isFullBackup);

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
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
                        .ExecuteNonQueryAsync();

                return new Tuple<long, long>(remotevolid, filesetid);
            }

            public async IAsyncEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles()
            {
                using (var cmd = m_db.Connection.CreateCommand())
                using (var rd = await cmd.ExecuteReaderAsync($@"
                    SELECT
                        ""B"".""Path"",
                        ""C"".""Length""
                    FROM
                        ""{m_tablename}"" A,
                        ""File"" B,
                        ""Blockset"" C
                    WHERE
                        ""A"".""FileID"" = ""B"".""ID""
                        AND ""B"".""BlocksetID"" = ""C"".""ID""
                "))
                    while (await rd.ReadAsync())
                        yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
            }

            public void Dispose()
            {
                DisposeAsync().Await();
            }

            public async Task DisposeAsync()
            {
                try
                {
                    using (var cmd = m_db.Connection.CreateCommand())
                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tablename}""");
                }
                catch { }
            }
        }
    }
}
