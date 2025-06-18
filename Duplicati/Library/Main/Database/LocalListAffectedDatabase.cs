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

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// LocalListAffectedDatabase is a specialized database class for listing
    /// affected filesets, files, remote logs, and remote volumes.
    /// </summary>
    internal class LocalListAffectedDatabase : LocalDatabase
    {
        /// <summary>
        /// Creates a new instance of <see cref="LocalListAffectedDatabase"/>.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="pagecachesize">The size of the page cache in bytes.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <returns>A task that when awaited returns a new instance of <see cref="LocalListAffectedDatabase"/>.</returns>
        public static async Task<LocalListAffectedDatabase> CreateAsync(string path, long pagecachesize, LocalListAffectedDatabase? dbnew = null)
        {
            dbnew ??= new LocalListAffectedDatabase();

            dbnew = (LocalListAffectedDatabase)
                await CreateLocalDatabaseAsync(path, "ListAffected", false, pagecachesize, dbnew)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Represents a fileset result for listing affected filesets.
        /// </summary>
        private class ListResultFileset : Interface.IListResultFileset
        {
            /// <summary>
            /// Gets or sets the version of the fileset.
            /// </summary>
            public long Version { get; set; }
            /// <summary>
            /// Gets or sets whether the backup related to this fileset is a full backup.
            /// </summary>
            public int IsFullBackup { get; set; }
            /// <summary>
            /// Gets or sets the time when the fileset was created.
            /// </summary>
            public DateTime Time { get; set; }
            /// <summary>
            /// Gets or sets the number of files in the fileset.
            /// </summary>
            public long FileCount { get; set; }
            /// <summary>
            /// Gets or sets the total size of files in the fileset.
            /// </summary>
            public long FileSizes { get; set; }
        }

        /// <summary>
        /// Represents a file result for listing affected files.
        /// </summary>
        private class ListResultFile : Interface.IListResultFile
        {
            /// <summary>
            /// Gets or sets the path of the file reperesented by this result entry.
            /// </summary>
            public required string Path { get; set; }
            /// <summary>
            /// Gets or sets the sizes of the file blocks, if available.
            /// This can be null if the sizes are not available or not applicable.
            /// </summary>
            public required IEnumerable<long>? Sizes { get; set; }
        }

        /// <summary>
        /// Represents a remote log entry for listing affected remote logs.
        /// </summary>
        private class ListResultRemoteLog : Interface.IListResultRemoteLog
        {
            /// <summary>
            /// Gets or sets the timestamp of the log entry.
            /// </summary>
            public required DateTime Timestamp { get; set; }
            /// <summary>
            /// Gets or sets the message of the log entry.
            /// </summary>
            public required string Message { get; set; }
        }

        /// <summary>
        /// Represents a remote volume entry for listing affected remote volumes.
        /// </summary>
        private class ListResultRemoteVolume : Interface.IListResultRemoteVolume
        {
            /// <summary>
            /// Gets or sets the name of the remote volume.
            /// </summary>
            public required string Name { get; set; }
        }

        /// <summary>
        /// Retrieves the fileset times from the database.
        /// </summary>
        /// <param name="items">The items to filter the filesets by.</param>
        /// <returns>An asynchronous enumerable of fileset times.</returns>
        public async IAsyncEnumerable<Interface.IListResultFileset> GetFilesets(IEnumerable<string> items)
        {
            var filesets = await FilesetTimes()
                .ToArrayAsync()
                .ConfigureAwait(false);

            var dict = new Dictionary<long, long>();
            for (var i = 0; i < filesets.Length; i++)
                dict[filesets[i].Key] = i;

            var sql = $@"
                SELECT DISTINCT ""FilesetID""
                FROM (
                    SELECT ""FilesetID""
                    FROM ""FilesetEntry""
                    WHERE ""FileID"" IN (
                        SELECT ""ID""
                        FROM ""FileLookup""
                        WHERE ""BlocksetID"" IN (
                            SELECT ""BlocksetID""
                            FROM ""BlocksetEntry""
                            WHERE ""BlockID"" IN (
                                SELECT ""ID""
                                FROM ""Block""
                                WHERE ""VolumeID"" IN (
                                    SELECT ""ID""
                                    FROM ""RemoteVolume""
                                    WHERE ""Name"" IN (@Names)
                                )
                            )
                        )
                    )
                    UNION
                        SELECT ""ID""
                        FROM ""Fileset""
                        WHERE ""VolumeID"" IN (
                            SELECT ""ID""
                            FROM ""RemoteVolume""
                            WHERE ""Name"" IN (@Names)
                        )
                )
            ";

            await using var tmptable = await TemporaryDbValueList.CreateAsync(this, items).ConfigureAwait(false);
            await using var cmd = await m_connection.CreateCommand(sql).SetTransaction(m_rtr).ExpandInClauseParameterMssqliteAsync("@Names", tmptable).ConfigureAwait(false);
            await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await rd.ReadAsync().ConfigureAwait(false))
            {
                var v = dict[rd.ConvertValueToInt64(0)];
                yield return new ListResultFileset()
                {
                    Version = v,
                    Time = filesets[v].Value
                };
            }
        }

        /// <summary>
        /// Retrieves the list of files from the database.
        /// </summary>
        /// <param name="items">The items to filter the files by.</param>
        /// <returns>An asynchronous enumerable of file results.</returns>
        public async IAsyncEnumerable<Interface.IListResultFile> GetFiles(IEnumerable<string> items)
        {
            var sql = $@"
                SELECT DISTINCT ""Path""
                FROM (
                    SELECT ""Path""
                    FROM ""File""
                    WHERE ""BlocksetID"" IN (
                        SELECT ""BlocksetID""
                        FROM ""BlocksetEntry""
                        WHERE ""BlockID"" IN (
                            SELECT ""ID""
                            FROM ""Block""
                            WHERE ""VolumeID"" IN (
                                SELECT ""ID""
                                FROM ""RemoteVolume""
                                WHERE ""Name"" IN (@Names)
                            )
                        )
                    )
                    UNION
                        SELECT ""Path""
                        FROM ""File""
                        WHERE ""MetadataID"" IN (
                            SELECT ""ID""
                            FROM ""Metadataset""
                            WHERE ""BlocksetID"" IN (
                                SELECT ""BlocksetID""
                                FROM ""BlocksetEntry""
                                WHERE ""BlockID"" IN (
                                    SELECT ""ID""
                                    FROM ""Block""
                                    WHERE ""VolumeID"" IN (
                                        SELECT ""ID""
                                        FROM ""RemoteVolume""
                                        WHERE ""Name"" IN (@Names)
                                    )
                                )
                            )
                        )
                    UNION
                        SELECT ""Path""
                        FROM ""File""
                        WHERE ""ID"" IN (
                            SELECT ""FileID""
                            FROM ""FilesetEntry""
                            WHERE ""FilesetID"" IN (
                                SELECT ""ID""
                                FROM ""Fileset""
                                WHERE ""VolumeID"" IN (
                                    SELECT ""ID""
                                    FROM ""RemoteVolume""
                                    WHERE ""Name"" IN (@Names)
                                )
                            )
                        )
                )
                ORDER BY ""Path""
            ";

            await using var tmptable = await TemporaryDbValueList.CreateAsync(this, items).ConfigureAwait(false);
            await using var cmd = await m_connection.CreateCommand(sql).ExpandInClauseParameterMssqliteAsync("@Names", tmptable).ConfigureAwait(false);
            await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await rd.ReadAsync().ConfigureAwait(false))
                yield return new ListResultFile()
                {
                    Path = rd.ConvertValueToString(0) ?? throw new InvalidOperationException("Path is null"),
                    Sizes = null
                };
        }

        /// <summary>
        /// Retrieves the log lines from the database that match the specified items.
        /// The log lines are retrieved from both the LogData and RemoteOperation tables.
        /// </summary>
        /// <param name="items">The items to filter the log lines by.</param>
        /// <returns>An asynchronous enumerable of log line results.</returns>
        public async IAsyncEnumerable<Interface.IListResultRemoteLog> GetLogLines(IEnumerable<string> items)
        {
            await using var cmd = m_connection.CreateCommand();

            foreach (var slice in items.Chunk(CHUNK_SIZE / 2))
            {
                var sql = $@"
                    SELECT
                        ""TimeStamp"",
                        ""Message""
                            || ' '
                            ||
                            CASE
                                WHEN ""Exception"" IS NULL
                                THEN ''
                                ELSE ""Exception""
                            END
                    FROM ""LogData""
                    WHERE
                        {string.Join(" OR ", slice.Select((x, i) => @$"""Message"" LIKE @Message{i}"))}
                    UNION
                        SELECT
                            ""Timestamp"",
                            ""Data""
                        FROM ""RemoteOperation""
                        WHERE ""Path"" IN (@Paths)
                ";

                cmd.SetCommandAndParameters(sql)
                    .ExpandInClauseParameterMssqlite("@Paths", items.ToArray());
                foreach ((var x, var i) in items.Select((x, i) => (x, i)))
                    cmd.SetParameterValue($"@Message{i}", "%" + x + "%");

                await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await rd.ReadAsync().ConfigureAwait(false))
                    yield return new ListResultRemoteLog()
                    {
                        Timestamp = ParseFromEpochSeconds(rd.ConvertValueToInt64(0)),
                        Message = rd.ConvertValueToString(1) ?? ""
                    };
            }
        }

        /// <summary>
        /// Retrieves the remote volumes that match the specified items.
        /// This method queries the database for remote volumes associated with filesets and metadata datasets.
        /// </summary>
        /// <param name="items">The names of the remote volumes to filter by.</param>
        /// <returns>An asynchronous enumerable of remote volume results.</returns>
        public async IAsyncEnumerable<Interface.IListResultRemoteVolume> GetVolumes(IEnumerable<string> items)
        {
            var sql = $@"
                SELECT DISTINCT ""Name""
                FROM (
                    SELECT ""Name""
                    FROM ""Remotevolume""
                    WHERE ""ID"" IN (
                        SELECT ""VolumeID""
                        FROM ""Block""
                        WHERE ""ID"" IN (
                            SELECT ""BlockID""
                            FROM ""BlocksetEntry""
                            WHERE ""BlocksetID"" IN (
                                SELECT ""BlocksetID""
                                FROM ""FileLookup""
                                WHERE ""ID"" IN (
                                    SELECT ""FileID""
                                    FROM ""FilesetEntry""
                                    WHERE ""FilesetID"" IN (
                                        SELECT ""ID""
                                        FROM ""Fileset""
                                        WHERE ""VolumeID"" IN (
                                            SELECT ""ID""
                                            FROM ""RemoteVolume""
                                            WHERE ""Name"" IN (@Names)
                                        )
                                    )
                                )
                            )
                        )
                    )
                UNION
                    SELECT ""Name""
                    FROM ""Remotevolume""
                    WHERE ""ID"" IN (
                        SELECT ""VolumeID""
                        FROM ""Block""
                        WHERE ""ID"" IN (
                            SELECT ""BlockID""
                            FROM ""BlocksetEntry""
                            WHERE ""BlocksetID"" IN (
                                SELECT ""BlocksetID""
                                FROM ""Metadataset""
                                WHERE ""ID"" IN (
                                    SELECT ""MetadataID""
                                    FROM ""FileLookup""
                                    WHERE ""ID"" IN (
                                        SELECT ""FileID""
                                        FROM ""FilesetEntry""
                                        WHERE ""FilesetID"" IN (
                                            SELECT ""ID""
                                            FROM ""Fileset""
                                            WHERE ""VolumeID"" IN (
                                                SELECT ""ID""
                                                FROM ""RemoteVolume""
                                                WHERE ""Name"" IN (@Names)
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
            ";

            await using var cmd = m_connection.CreateCommand(sql).ExpandInClauseParameterMssqlite("@Names", items.ToArray());
            await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await rd.ReadAsync().ConfigureAwait(false))
                yield return new ListResultRemoteVolume()
                {
                    Name = rd.ConvertValueToString(0) ?? ""
                };
        }
    }
}

