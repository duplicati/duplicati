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
    internal class LocalListAffectedDatabase : LocalDatabase
    {
        public static async Task<LocalListAffectedDatabase> CreateAsync(string path, long pagecachesize, LocalListAffectedDatabase? dbnew = null)
        {
            dbnew ??= new LocalListAffectedDatabase();

            dbnew = (LocalListAffectedDatabase)
                await CreateLocalDatabaseAsync(path, "ListAffected", false, pagecachesize, dbnew)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        private class ListResultFileset : Interface.IListResultFileset
        {
            public long Version { get; set; }
            public int IsFullBackup { get; set; }
            public DateTime Time { get; set; }
            public long FileCount { get; set; }
            public long FileSizes { get; set; }
        }

        private class ListResultFile : Interface.IListResultFile
        {
            public required string Path { get; set; }
            public required IEnumerable<long>? Sizes { get; set; }
        }

        private class ListResultRemoteLog : Interface.IListResultRemoteLog
        {
            public required DateTime Timestamp { get; set; }
            public required string Message { get; set; }
        }

        private class ListResultRemoteVolume : Interface.IListResultRemoteVolume
        {
            public required string Name { get; set; }
        }

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

            using (var tmptable = await TemporaryDbValueList.CreateAsync(this, items).ConfigureAwait(false))
            using (var cmd = await m_connection.CreateCommand(sql).SetTransaction(m_rtr).ExpandInClauseParameterMssqliteAsync("@Names", tmptable).ConfigureAwait(false))
            using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
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

            using (var tmptable = await TemporaryDbValueList.CreateAsync(this, items).ConfigureAwait(false))
            using (var cmd = await m_connection.CreateCommand(sql).ExpandInClauseParameterMssqliteAsync("@Names", tmptable).ConfigureAwait(false))
            using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                while (await rd.ReadAsync().ConfigureAwait(false))
                    yield return new ListResultFile()
                    {
                        Path = rd.ConvertValueToString(0) ?? throw new InvalidOperationException("Path is null"),
                        Sizes = null
                    };
        }

        public async IAsyncEnumerable<Interface.IListResultRemoteLog> GetLogLines(IEnumerable<string> items)
        {
            using var cmd = m_connection.CreateCommand();

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

                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                        yield return new ListResultRemoteLog()
                        {
                            Timestamp = ParseFromEpochSeconds(rd.ConvertValueToInt64(0)),
                            Message = rd.ConvertValueToString(1) ?? ""
                        };
            }
        }

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

            using (var cmd = m_connection.CreateCommand(sql).ExpandInClauseParameterMssqlite("@Names", items.ToArray()))
            using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                while (await rd.ReadAsync().ConfigureAwait(false))
                    yield return new ListResultRemoteVolume()
                    {
                        Name = rd.ConvertValueToString(0) ?? ""
                    };
        }
    }
}

