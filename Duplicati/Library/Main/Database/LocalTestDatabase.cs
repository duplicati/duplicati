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
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Represents a  local database used for testing and verification.
    /// Provides methods for creating test databases, updating verification counts, and comparing file, index, and block lists
    /// to support integrity checks and remote volume verification during backup testing.
    /// </summary>
    internal class LocalTestDatabase : LocalDatabase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LocalTestDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="pagecachesize">The size of the page cache in bytes.</param>
        /// <param name="dbnew">An optional existing <see cref="LocalTestDatabase"/> instance to use. Used to mimic constructor chaining.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the created <see cref="LocalTestDatabase"/> instance.</returns>
        public static async Task<LocalTestDatabase> CreateAsync(string path, long pagecachesize, LocalTestDatabase? dbnew = null)
        {
            dbnew ??= new LocalTestDatabase();

            dbnew = (LocalTestDatabase)
                await CreateLocalDatabaseAsync(path, "Test", true, pagecachesize, dbnew)
                    .ConfigureAwait(false);

            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LocalTestDatabase"/> class using an existing parent database.
        /// </summary>
        /// <param name="dbparent">The parent database to use for creating the new test database.</param>
        /// <param name="dbnew">An optional existing <see cref="LocalTestDatabase"/> instance to use. Used to mimic constructor chaining.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the created <see cref="LocalTestDatabase"/> instance.</returns>
        public static async Task<LocalTestDatabase> CreateAsync(LocalDatabase dbparent, LocalTestDatabase? dbnew = null)
        {
            dbnew ??= new LocalTestDatabase();

            return (LocalTestDatabase)
                await CreateLocalDatabaseAsync(dbparent, dbnew)
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the verification count for a remote volume with the specified name.
        /// Increments the count by 1, or sets it to the maximum of 1 if it was previously 0 or negative.
        /// </summary>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task UpdateVerificationCount(string name)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
                await cmd.SetCommandAndParameters(@"
                    UPDATE ""RemoteVolume""
                    SET ""VerificationCount"" = MAX(1,
                        CASE
                            WHEN ""VerificationCount"" <= 0
                            THEN (
                                SELECT MAX(""VerificationCount"")
                                FROM ""RemoteVolume""
                            )
                            ELSE ""VerificationCount"" + 1
                        END
                    )
                    WHERE ""Name"" = @Name
                ")
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
        }

        /// <summary>
        /// A record representing a remote volume, which implements the <see cref="IRemoteVolume"/> interface.
        /// </summary>
        private record RemoteVolume : IRemoteVolume
        {
            /// <summary>
            /// Gets the ID of the remote volume.
            /// </summary>
            public long ID { get; init; }
            public string Name { get; init; }
            public long Size { get; init; }
            public string Hash { get; init; }
            /// <summary>
            /// Gets the verification count of the remote volume, indicating how many times it has been verified.
            /// </summary>
            public long VerificationCount { get; init; }

            /// <summary>
            /// Initializes a new instance of the <see cref="RemoteVolume"/> record from a SqliteDataReader.
            /// </summary>
            /// <param name="rd">The SqliteDataReader containing the data for the remote volume.</param>
            public RemoteVolume(SqliteDataReader rd)
            {
                ID = rd.ConvertValueToInt64(0);
                Name = rd.ConvertValueToString(1) ?? "";
                Size = rd.ConvertValueToInt64(2);
                Hash = rd.ConvertValueToString(3) ?? throw new ArgumentNullException("Hash cannot be null");
                VerificationCount = rd.ConvertValueToInt64(4);
            }
        }

        /// <summary>
        /// Filters a list of remote volumes based on their verification count.
        /// The method selects volumes that have not been verified, those with a low verification count, and finally those with a high verification count, ensuring a balanced selection.
        /// </summary>
        /// <param name="volumes">The collection of remote volumes to filter.</param>
        /// <param name="samples">The number of samples to select.</param>
        /// <param name="maxverification">The maximum verification count to consider for filtering.</param>
        /// <returns>A list of remote volumes filtered by verification count.</returns>
        private static List<RemoteVolume> FilterByVerificationCount(IEnumerable<RemoteVolume> volumes, long samples, long maxverification)
        {
            var rnd = new Random();

            // First round is the new items
            var res = (from n in volumes where n.VerificationCount == 0 select n).ToList();
            while (res.Count > samples)
                res.RemoveAt(rnd.Next(0, res.Count));

            // Quick exit if we are done
            if (res.Count == samples)
                return res;

            // Next is the volumes that are not
            // verified as much, with preference for low verification count
            var starved = (from n in volumes where n.VerificationCount != 0 && n.VerificationCount < maxverification orderby n.VerificationCount select n);
            if (starved.Any())
            {
                var max = starved.Select(x => x.VerificationCount).Max();
                var min = starved.Select(x => x.VerificationCount).Min();

                for (var i = min; i <= max; i++)
                {
                    var p = starved.Where(x => x.VerificationCount == i).ToList();
                    while (res.Count < samples && p.Count > 0)
                    {
                        var n = rnd.Next(0, p.Count);
                        res.Add(p[n]);
                        p.RemoveAt(n);
                    }
                }

                // Quick exit if we are done
                if (res.Count == samples)
                    return res;
            }

            if (maxverification > 0)
            {
                // Last is the items that are verified mostly
                var remainder = (from n in volumes where n.VerificationCount >= maxverification select n).ToList();
                while (res.Count < samples && remainder.Count > 0)
                {
                    var n = rnd.Next(0, remainder.Count);
                    res.Add(remainder[n]);
                    remainder.RemoveAt(n);
                }
            }

            return res;
        }

        /// <summary>
        /// Asynchronously selects a set of remote volumes to be tested for integrity, based on the specified sample count and selection options.
        /// This method retrieves candidate remote volumes from the database, prioritizes them according to verification count and state, and yields the selected targets for verification.
        /// </summary>
        /// <param name="samples">The number of remote volumes to select for testing.</param>
        /// <param name="options">The options that define selection criteria, such as time, version, and verification strategy.</param>
        /// <returns>An asynchronous enumerable of <see cref="IRemoteVolume"/> representing the selected test targets.</returns>
        public async IAsyncEnumerable<IRemoteVolume> SelectTestTargets(long samples, Options options)
        {
            var tp = await GetFilelistWhereClause(options.Time, options.Version)
                .ConfigureAwait(false);

            samples = Math.Max(1, samples);
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var files = new List<RemoteVolume>();
                var max = cmd.ExecuteScalarInt64(@"
                    SELECT MAX(""VerificationCount"")
                    FROM ""RemoteVolume""
                ", 0);

                if (options.FullRemoteVerification != Options.RemoteTestStrategy.IndexesOnly)
                {
                    // Select any broken items
                    cmd.SetCommandAndParameters(@"
                        SELECT
                            ""ID"",
                            ""Name"",
                            ""Size"",
                            ""Hash"",
                            ""VerificationCount""
                        FROM
                            ""Remotevolume""
                        WHERE
                            (""State"" IN (@States))
                            AND (
                                ""Hash"" = ''
                                OR ""Hash"" IS NULL
                                OR ""Size"" <= 0
                            )
                            AND (""ArchiveTime"" = 0)
                    ")
                    .ExpandInClauseParameterMssqlite("@States", [
                        RemoteVolumeState.Verified.ToString(),
                        RemoteVolumeState.Uploaded.ToString()
                    ]);

                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read())
                            yield return new RemoteVolume(rd);

                    //First we select some filesets
                    var whereClause = string.IsNullOrEmpty(tp.Item1) ? " WHERE " : (" " + tp.Item1 + " AND ");
                    using (var rd = cmd.SetCommandAndParameters(@$"
                        SELECT
                            ""A"".""VolumeID"",
                            ""A"".""Name"",
                            ""A"".""Size"",
                            ""A"".""Hash"",
                            ""A"".""VerificationCount""
                        FROM
                            (
                                SELECT
                                    ""ID"" AS ""VolumeID"",
                                    ""Name"",
                                    ""Size"",
                                    ""Hash"",
                                    ""VerificationCount""
                                FROM ""Remotevolume""
                                WHERE
                                    ""ArchiveTime"" = 0
                                    AND ""State"" IN (
                                        @State1,
                                        @State2
                                    )
                            ) A,
                            ""Fileset""
                        {whereClause}
                            ""A"".""VolumeID"" = ""Fileset"".""VolumeID""
                        ORDER BY ""Fileset"".""Timestamp""
                    ")
                        .SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString())
                        .SetParameterValue("@State2", RemoteVolumeState.Verified.ToString())
                        .SetParameterValues(tp.Item2)
                        .ExecuteReader())
                        while (rd.Read())
                            files.Add(new RemoteVolume(rd));

                    if (files.Count == 0)
                        yield break;

                    if (string.IsNullOrEmpty(tp.Item1))
                        files = FilterByVerificationCount(files, samples, max).ToList();

                    foreach (var f in files)
                        yield return f;

                    //Then we select some index files
                    files.Clear();
                }

                cmd.SetCommandAndParameters(@"
                    SELECT
                        ""ID"",
                        ""Name"",
                        ""Size"",
                        ""Hash"",
                        ""VerificationCount""
                    FROM ""Remotevolume""
                    WHERE
                        ""Type"" = @Type
                        AND ""State"" IN (@States)
                        AND ""ArchiveTime"" = 0
                ")
                    .SetParameterValue("@Type", RemoteVolumeType.Index.ToString())
                    .ExpandInClauseParameterMssqlite("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                        files.Add(new RemoteVolume(rd));

                foreach (var f in FilterByVerificationCount(files, samples, max))
                    yield return f;

                if (options.FullRemoteVerification == Options.RemoteTestStrategy.ListAndIndexes || options.FullRemoteVerification == Options.RemoteTestStrategy.IndexesOnly)
                    yield break;

                //And finally some block files
                files.Clear();

                cmd.SetCommandAndParameters(@"
                    SELECT
                        ""ID"",
                        ""Name"",
                        ""Size"",
                        ""Hash"",
                        ""VerificationCount""
                    FROM ""Remotevolume""
                    WHERE
                        ""Type"" = @Type
                        AND ""State"" IN (@States)
                        AND ""ArchiveTime"" = 0
                ")
                    .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                    .ExpandInClauseParameterMssqlite("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

                using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await rd.ReadAsync().ConfigureAwait(false))
                        files.Add(new RemoteVolume(rd));

                foreach (var f in FilterByVerificationCount(files, samples, max))
                    yield return f;
            }
        }

        /// <summary>
        /// Base class for basic lists used in the local test database.
        /// Provides methods for creating temporary tables, inserting data, and disposing of resources.
        /// </summary>
        private abstract class Basiclist : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// The database connection used for executing commands.
            /// </summary>
            protected LocalDatabase m_db = null!;
            /// <summary>
            /// The name of the volume associated with this list.
            /// </summary>
            protected string m_volumename = null!;
            /// <summary>
            /// The name of the temporary table used for this list.
            /// </summary>
            protected string m_tablename = null!;
            /// <summary>
            /// Command used for inserting data into the temporary table.
            /// </summary>
            protected SqliteCommand m_insertCommand = null!;

            /// <summary>
            /// Calling this constructor will throw an exception. Use the CreateAsync method instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            protected Basiclist(SqliteConnection connection, ReusableTransaction rtr, string volumename, string tablePrefix, string tableFormat, string insertCommand)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            /// <summary>
            /// Protected constructor to allow derived classes to initialize without parameters.
            /// </summary>
            protected Basiclist() { }

            /// <summary>
            /// Creates a new instance of the <see cref="Basiclist"/> class asynchronously.
            /// </summary>
            /// <param name="bl">The instance of the <see cref="Basiclist"/> to initialize.</param>
            /// <param name="db">The local database to use for the list.</param>
            /// <param name="volumename">The name of the volume associated with this list.</param>
            /// <param name="tablePrefix">The prefix for the temporary table name.</param>
            /// <param name="tableFormat">The SQL format for creating the temporary table.</param>
            /// <param name="insertCommand">The SQL command for inserting data into the temporary table.</param>
            /// <returns>A task that represents the asynchronous operation. The task result contains the initialized <see cref="Basiclist"/> instance.</returns>
            protected static async Task<Basiclist> CreateAsync(Basiclist bl, LocalDatabase db, string volumename, string tablePrefix, string tableFormat, string insertCommand)
            {
                bl.m_db = db;
                bl.m_volumename = volumename;
                var tablename = tablePrefix + "-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                using (var cmd = bl.m_db.Connection.CreateCommand(bl.m_db.Transaction))
                {
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{tablename}""
                        {tableFormat}
                    ")
                        .ConfigureAwait(false);

                    bl.m_tablename = tablename;
                }

                bl.m_insertCommand = await bl.m_db.Connection.CreateCommandAsync($@"
                    INSERT INTO ""{bl.m_tablename}""
                    {insertCommand}
                ")
                    .ConfigureAwait(false);

                return bl;
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public virtual async ValueTask DisposeAsync()
            {
                if (m_tablename != null)
                    try
                    {
                        using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction.Transaction))
                            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{m_tablename}""")
                                .ConfigureAwait(false);
                    }
                    catch { }
                    finally { m_tablename = null!; }

                await m_insertCommand.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Interface for a file list used in the local test database.
        /// Provides methods for adding entries, comparing the list with remote volumes, and disposing of resources.
        /// </summary>
        public interface IFilelist : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Asynchronously adds a file entry to the file list.
            /// </summary>
            /// <param name="path">The path of the file.</param>
            /// <param name="size">The size of the file in bytes.</param>
            /// <param name="hash">The hash of the file, or null if not applicable.</param>
            /// <param name="metasize">The size of the metadata associated with the file.</param>
            /// <param name="metahash">The hash of the metadata associated with the file.</param>
            /// <param name="blocklistHashes">A collection of blocklist hashes associated with the file.</param>
            /// <param name="type">The type of the file entry.</param>
            /// <param name="time">The timestamp of the file entry.</param>
            /// <returns>A task that completes when the file entry has been added.</returns>
            Task Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time);

            /// <summary>
            /// Asynchronously compares the file list with remote volumes and yields differences.
            /// </summary>
            /// <returns>An asynchronous enumerable of key-value pairs representing the comparison results, where the key is the test entry status and the value is the file path.</returns>
            IAsyncEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare();
        }

        /// <summary>
        /// Implementation of the <see cref="IFilelist"/> interface that manages a list of files in a local test database.
        /// </summary>
        private class Filelist : Basiclist, IFilelist
        {
            /// <summary>
            /// The prefix for the temporary table name used for the file list.
            /// </summary>
            private const string TABLE_PREFIX = "Filelist";

            /// <summary>
            /// The SQL format for creating the temporary table used for the file list.
            /// </summary>
            private const string TABLE_FORMAT = @"
                (
                    ""Path"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL,
                    ""Hash"" TEXT NULL,
                    ""Metasize"" INTEGER NOT NULL,
                    ""Metahash"" TEXT NOT NULL
                )
            ";

            /// <summary>
            /// The SQL command for inserting data into the temporary table used for the file list.
            /// </summary>
            private const string INSERT_COMMAND = @"
                (
                    ""Path"",
                    ""Size"",
                    ""Hash"",
                    ""Metasize"",
                    ""Metahash""
                )
                VALUES (
                    @Path,
                    @Size,
                    @Hash,
                    @Metasize,
                    @Metahash
                )
            ";

            /// <summary>
            /// Calling this constructor will throw an exception. Use the CreateAsync method instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Filelist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
                : base(connection, rtr, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            /// <summary>
            /// Private constructor to allow derived classes to initialize without parameters and to prevent instantiation from outside, which should only be done through the CreateAsync method.
            /// </summary>
            private Filelist() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="Filelist"/> class.
            /// </summary>
            /// <param name="db">The local database to use for the file list.</param>
            /// <param name="volumename">The name of the volume associated with this file list.</param>
            /// <returns>A task that when awaited returns a new instance of the <see cref="Filelist"/> class.</returns>
            public static async Task<Filelist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Filelist();
                return (Filelist)
                    await CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
                        .ConfigureAwait(false);
            }

            public async Task Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time)
            {
                await m_insertCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@Size", hash == null ? -1 : size)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Metasize", metasize)
                    .SetParameterValue("@Metahash", metahash)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                var create = $@"
                    CREATE TEMPORARY TABLE ""{cmpName}"" AS
                    SELECT
                        ""A"".""Path"" AS ""Path"",
                        CASE
                            WHEN ""B"".""Fullhash"" IS NULL
                            THEN -1
                            ELSE ""B"".""Length"" END AS ""Size"",
                        ""B"".""Fullhash"" AS ""Hash"",
                        ""C"".""Length"" AS ""Metasize"",
                        ""C"".""Fullhash"" AS ""Metahash""
                    FROM (
                        SELECT
                            ""File"".""Path"",
                            ""File"".""BlocksetID"" AS ""FileBlocksetID"",
                            ""Metadataset"".""BlocksetID"" AS ""MetadataBlocksetID""
                        FROM
                            ""Remotevolume"",
                            ""Fileset"",
                            ""FilesetEntry"",
                            ""File"",
                            ""Metadataset""
                        WHERE
                            ""Remotevolume"".""Name"" = @Name
                            AND ""Fileset"".""VolumeID"" = ""Remotevolume"".""ID""
                            AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
                            AND ""File"".""ID"" = ""FilesetEntry"".""FileID""
                            AND ""File"".""MetadataID"" = ""Metadataset"".""ID""
                    ) A
                    LEFT OUTER JOIN ""Blockset"" B
                        ON ""B"".""ID"" = ""A"".""FileBlocksetID""
                    LEFT OUTER JOIN ""Blockset"" C
                        ON ""C"".""ID""=""A"".""MetadataBlocksetID""
                ";

                var extra = $@"
                    SELECT
                        @TypeExtra AS ""Type"",
                        ""{m_tablename}"".""Path"" AS ""Path""
                    FROM ""{m_tablename}""
                    WHERE ""{m_tablename}"".""Path"" NOT IN (
                        SELECT ""Path""
                        FROM ""{cmpName}""
                    )";

                var missing = $@"
                    SELECT
                        @TypeMissing AS ""Type"",
                        ""Path"" AS ""Path""
                    FROM ""{cmpName}""
                    WHERE ""Path"" NOT IN (
                        SELECT ""Path""
                        FROM ""{m_tablename}""
                    )
                ";

                var modified = $@"
                    SELECT
                        @TypeModified AS ""Type"",
                        ""E"".""Path"" AS ""Path""
                    FROM
                        ""{m_tablename}"" E,
                        ""{cmpName}"" D
                    WHERE
                        ""D"".""Path"" = ""E"".""Path""
                        AND (
                            ""D"".""Size"" != ""E"".""Size""
                            OR ""D"".""Hash"" != ""E"".""Hash""
                            OR ""D"".""Metasize"" != ""E"".""Metasize""
                             OR ""D"".""Metahash"" != ""E"".""Metahash""
                        )
                ";

                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        await cmd
                            .SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                        cmd
                            .SetCommandAndParameters($"{extra} UNION {missing} UNION {modified}")
                            .SetParameterValue("@TypeExtra", (int)Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Interface.TestEntryStatus.Modified);

                        using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await rd.ReadAsync().ConfigureAwait(false))
                                yield return new KeyValuePair<Interface.TestEntryStatus, string>(
                                    (Interface.TestEntryStatus)rd.ConvertValueToInt64(0),
                                    rd.ConvertValueToString(1) ?? ""
                                );

                    }
                    finally
                    {
                        try
                        {
                            await cmd
                                .ExecuteNonQueryAsync(drop)
                                .ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Interface for an index list used in the local test database.
        /// Provides methods for adding block links, comparing the index list with remote volumes, and disposing of resources.
        /// </summary>
        public interface IIndexlist : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Asynchronously adds a block link to the index list.
            /// </summary>
            /// <param name="filename">The name of the file associated with the block link.</param>
            /// <param name="hash">The hash of the block link.</param>
            /// <param name="length">The length of the block link in bytes.</param>
            /// <returns>A task that completes when the block link has been added.</returns>
            Task AddBlockLink(string filename, string hash, long length);

            /// <summary>
            /// Asynchronously compares the index list with remote volumes and yields differences.
            /// </summary>
            /// <returns>An asynchronous enumerable of key-value pairs representing the comparison results, where the key is the test entry status and the value is the file path.</returns>
            IAsyncEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }

        /// <summary>
        /// Implementation of the <see cref="IIndexlist"/> interface that manages a list of index entries in a local test database.
        /// </summary>
        private class Indexlist : Basiclist, IIndexlist
        {
            /// <summary>
            /// The prefix for the temporary table name used for the index list.
            /// </summary>
            private const string TABLE_PREFIX = "Indexlist";

            /// <summary>
            /// The SQL format for creating the temporary table used for the index list.
            /// </summary>
            private const string TABLE_FORMAT = @"
                (
                    ""Name"" TEXT NOT NULL,
                    ""Hash"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL
                )
            ";

            /// <summary>
            /// The SQL command for inserting data into the temporary table used for the index list.
            /// </summary>
            private const string INSERT_COMMAND = @"
                (
                    ""Name"",
                    ""Hash"",
                    ""Size""
                )
                VALUES (
                    @Name,
                    @Hash,
                    @Size
                )
            ";

            /// <summary>
            /// Calling this constructor will throw an exception. Use the CreateAsync method instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Indexlist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
                : base(connection, rtr, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            /// <summary>
            /// Private constructor to allow derived classes to initialize without parameters and to prevent instantiation from outside, which should only be done through the CreateAsync method.
            /// </summary>
            private Indexlist() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="Indexlist"/> class.
            /// </summary>
            /// <param name="db">The local database to use for the index list.</param>
            /// <param name="volumename">The name of the volume associated with this index list.</param>
            /// <returns>A task that when awaited returns a new instance of the <see cref="Indexlist"/> class.</returns>
            public static async Task<Indexlist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Indexlist();
                return (Indexlist)
                    await CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
                        .ConfigureAwait(false);
            }

            public async Task AddBlockLink(string filename, string hash, long length)
            {
                await m_insertCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Name", filename)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", length)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var create = $@"
                    CREATE TEMPORARY TABLE ""{cmpName}"" AS
                    SELECT
                        ""A"".""Name"",
                        ""A"".""Hash"",
                        ""A"".""Size""
                    FROM
                        ""Remotevolume"" A,
                        ""Remotevolume"" B,
                        ""IndexBlockLink""
                    WHERE
                        ""B"".""Name"" = @Name
                        AND ""A"".""ID"" = ""IndexBlockLink"".""BlockVolumeID""
                        AND ""B"".""ID"" = ""IndexBlockLink"".""IndexVolumeID""
                ";

                var extra = $@"
                    SELECT
                        @TypeExtra AS ""Type"",
                        ""{m_tablename}"".""Name"" AS ""Name""
                    FROM ""{m_tablename}""
                    WHERE ""{m_tablename}"".""Name"" NOT IN (
                        SELECT ""Name""
                        FROM ""{cmpName}""
                    )
                ";

                var missing = $@"
                    SELECT
                        @TypeMissing AS ""Type"",
                        ""Name"" AS ""Name""
                    FROM ""{cmpName}""
                    WHERE ""Name"" NOT IN (
                        SELECT ""Name""
                        FROM ""{m_tablename}""
                    )
                ";

                var modified = $@"
                    SELECT
                        @TypeModified AS ""Type"",
                        ""E"".""Name"" AS ""Name""
                    FROM
                        ""{m_tablename}"" E,
                        ""{cmpName}"" D
                    WHERE
                        ""D"".""Name"" = ""E"".""Name""
                        AND (
                            ""D"".""Hash"" != ""E"".""Hash""
                            OR ""D"".""Size"" != ""E"".""Size""
                        )
                ";

                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        await cmd
                            .SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                        cmd
                            .SetCommandAndParameters($"{extra} UNION {missing} UNION {modified}")
                            .SetParameterValue("@TypeExtra", (int)Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Interface.TestEntryStatus.Modified);

                        using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await rd.ReadAsync().ConfigureAwait(false))
                                yield return new KeyValuePair<Interface.TestEntryStatus, string>((Interface.TestEntryStatus)rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");

                    }
                    finally
                    {
                        try
                        {
                            await cmd
                                .ExecuteNonQueryAsync(drop)
                                .ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Interface for a blocklist used in the local test database.
        /// Provides methods for adding blocks, comparing the blocklist with remote volumes, and disposing of resources.
        /// </summary>
        public interface IBlocklist : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Asynchronously adds a block to the blocklist.
            /// </summary>
            /// <param name="key">The key (hash) of the block.</param>
            /// <param name="value">The size of the block in bytes.</param>
            /// <returns>A task that completes when the block has been added.</returns>
            Task AddBlock(string key, long value);

            /// <summary>
            /// Asynchronously compares the blocklist with remote volumes and yields differences.
            /// </summary>
            /// <returns>An asynchronous enumerable of key-value pairs representing the comparison results, where the key is the test entry status and the value is the block hash.</returns>
            IAsyncEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }

        public interface IBlocklistHashList : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Asynchronously adds a block hash to the blocklist hash list.
            /// </summary>
            /// <param name="hash">The hash of the block.</param>
            /// <param name="size">The size of the block in bytes.</param>
            /// <returns>A task that completes when the block hash has been added.</returns>
            Task AddBlockHash(string hash, long size);

            /// <summary>
            /// Asynchronously compares the blocklist hash list with remote volumes and yields differences.
            /// </summary>
            /// <param name="hashesPerBlock">The number of hashes per block.</param>
            /// <param name="hashSize">The size of each hash in bytes.</param>
            /// <param name="blockSize">The size of each block in bytes.</param>
            /// <returns>An asynchronous enumerable of key-value pairs representing the comparison results, where the key is the test entry status and the value is the block hash.</returns>
            IAsyncEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare(int hashesPerBlock, int hashSize, int blockSize);
        }

        /// <summary>
        /// Implementation of the <see cref="IBlocklist"/> interface that manages a list of blocks in a local test database.
        /// Provides methods for adding blocks, comparing the blocklist with remote volumes, and disposing of resources.
        /// </summary>
        private class Blocklist : Basiclist, IBlocklist
        {
            /// <summary>
            /// The prefix for the temporary table name used for the blocklist.
            /// </summary>
            private const string TABLE_PREFIX = "Blocklist";
            /// <summary>
            /// The SQL format for creating the temporary table used for the blocklist.
            /// </summary>
            private const string TABLE_FORMAT = @"(
                ""Hash"" TEXT NOT NULL,
                ""Size"" INTEGER NOT NULL
            )";
            /// <summary>
            /// The SQL command for inserting data into the temporary table used for the blocklist.
            /// </summary>
            private const string INSERT_COMMAND = @"(
                ""Hash"",
                ""Size""
            )
            VALUES (
                @Hash,
                @Size
            )";

            /// <summary>
            /// Calling this constructor will throw an exception. Use the CreateAsync method instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Blocklist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            /// <summary>
            /// Private constructor to allow derived classes to initialize without parameters and to prevent instantiation from outside, which should only be done through the CreateAsync method.
            /// </summary>
            private Blocklist() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="Blocklist"/> class.
            /// </summary>
            /// <param name="db">The local database to use for the blocklist.</param>
            /// <param name="volumename">The name of the volume associated with this blocklist.</param>
            public static async Task<Blocklist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Blocklist();
                return (Blocklist)
                    await Basiclist.CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
                        .ConfigureAwait(false);
            }

            public async Task AddBlock(string hash, long size)
            {
                await m_insertCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                var curBlocks = @"
                    SELECT
                        ""Block"".""Hash"" AS ""Hash"",
                        ""Block"".""Size"" AS ""Size""
                    FROM
                        ""Remotevolume"",
                        ""Block""
                    WHERE
                        ""Remotevolume"".""Name"" = @Name
                        AND ""Remotevolume"".""ID"" = ""Block"".""VolumeID""
                ";

                var duplBlocks = @"
                    SELECT
                        ""Block"".""Hash"" AS ""Hash"",
                        ""Block"".""Size"" AS ""Size""
                    FROM
                        ""DuplicateBlock"",
                        ""Block""
                    WHERE
                        ""DuplicateBlock"".""VolumeID"" = (
                            SELECT ""ID""
                            FROM ""RemoteVolume""
                            WHERE ""Name"" = @Name
                        )
                        AND ""Block"".""ID"" = ""DuplicateBlock"".""BlockID""
                ";

                var delBlocks = @"
                    SELECT
                        ""DeletedBlock"".""Hash"" AS ""Hash"",
                        ""DeletedBlock"".""Size"" AS ""Size""
                    FROM
                        ""DeletedBlock"",
                        ""RemoteVolume""
                    WHERE
                        ""RemoteVolume"".""Name"" = @Name
                        AND ""RemoteVolume"".""ID"" = ""DeletedBlock"".""VolumeID""
                ";

                var create = $@"
                    CREATE TEMPORARY TABLE ""{cmpName}"" AS
                    SELECT DISTINCT
                        ""Hash"" AS ""Hash"",
                        ""Size"" AS ""Size""
                    FROM (
                        {curBlocks}
                        UNION {delBlocks}
                        UNION {duplBlocks}
                    )
                ";

                var extra = $@"
                    SELECT
                        @TypeExtra AS ""Type"",
                        ""{m_tablename}"".""Hash"" AS ""Hash""
                    FROM ""{m_tablename}""
                    WHERE ""{m_tablename}"".""Hash"" NOT IN (
                        SELECT ""Hash""
                        FROM ""{cmpName}""
                    )
                ";

                var missing = $@"
                    SELECT
                        @TypeMissing AS ""Type"",
                        ""Hash"" AS ""Hash""
                    FROM ""{cmpName}""
                    WHERE ""Hash"" NOT IN (
                        SELECT ""Hash""
                        FROM ""{m_tablename}""
                    )
                ";

                var modified = $@"
                    SELECT
                        @TypeModified AS ""Type"",
                        ""E"".""Hash"" AS ""Hash""
                    FROM
                        ""{m_tablename}"" E,
                        ""{cmpName}"" D
                    WHERE
                        ""D"".""Hash"" = ""E"".""Hash""
                        AND ""D"".""Size"" != ""E"".""Size""
                ";

                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        await cmd
                            .SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                        cmd
                            .SetCommandAndParameters($@"
                                {extra}
                                UNION {missing}
                                UNION {modified}
                            ")
                            .SetParameterValue("@TypeExtra", (int)Library.Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Library.Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Library.Interface.TestEntryStatus.Modified);

                        using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await rd.ReadAsync().ConfigureAwait(false))
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");

                    }
                    finally
                    {
                        try
                        {
                            await cmd
                                .ExecuteNonQueryAsync(drop)
                                .ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Implementation of the <see cref="IBlocklistHashList"/> interface that manages a list of block hashes in a local test database.
        /// Provides methods for adding block hashes, comparing the blocklist hash list with remote volumes, and disposing of resources.
        /// </summary>
        private class BlocklistHashList : Basiclist, IBlocklistHashList
        {
            /// <summary>
            /// The prefix for the temporary table name used for the blocklist hash list.
            /// </summary>
            private const string TABLE_PREFIX = "BlocklistHashList";

            /// <summary>
            /// The SQL format for creating the temporary table used for the blocklist hash list.
            /// </summary>
            private const string TABLE_FORMAT = @"(
                ""Hash"" TEXT NOT NULL,
                ""Size"" INTEGER NOT NULL
            )";

            /// <summary>
            /// The SQL command for inserting data into the temporary table used for the blocklist hash list.
            /// </summary>
            private const string INSERT_COMMAND = @"(
                ""Hash"",
                ""Size""
            )
            VALUES (
                @Hash,
                @Size
            )";

            /// <summary>
            /// Calling this constructor will throw an exception. Use the CreateAsync method instead.
            /// </summary>
            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public BlocklistHashList(SqliteConnection connection, string volumename, ReusableTransaction rtr)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            /// <summary>
            /// Private constructor to allow derived classes to initialize without parameters and to prevent instantiation from outside, which should only be done through the CreateAsync method.
            /// </summary>
            private BlocklistHashList() { }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="BlocklistHashList"/> class.
            /// </summary>
            /// <param name="db">The local database to use for the blocklist hash list.</param>
            /// <param name="volumename">The name of the volume associated with this blocklist hash list.</param>
            /// <returns>A task that when awaited returns a new instance of the <see cref="BlocklistHashList"/> class.</returns>
            public static async Task<BlocklistHashList> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new BlocklistHashList();
                return (BlocklistHashList)
                    await Basiclist.CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
                        .ConfigureAwait(false);
            }

            public async Task AddBlockHash(string hash, long size)
            {
                await m_insertCommand
                    .SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare(int hashesPerBlock, int hashSize, int blockSize)
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                var create = $@"
                    CREATE TEMPORARY TABLE ""{cmpName}"" (
                        ""Hash"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL
                    );

                    INSERT INTO ""{cmpName}"" (
                        ""Hash"",
                        ""Size""
                    )
                    SELECT
                        b.""Hash"",
                        b.""Size""
                    FROM Block b
                    JOIN (
                        SELECT
                            blh.""Hash"",
                            CASE
                                WHEN blh.""Index"" = (((bs.""Length"" + {blockSize} - 1) / {blockSize} - 1) / {hashesPerBlock})
                                     AND ((bs.""Length"" + {blockSize} - 1) / {blockSize}) % {hashesPerBlock} != 0
                                THEN {hashSize} * ((bs.""Length"" + {blockSize} - 1) / {blockSize} % {hashesPerBlock})
                                ELSE {hashSize} * {hashesPerBlock}
                            END AS ""Size""
                        FROM BlocklistHash blh
                        JOIN Blockset bs
                            ON bs.""ID"" = blh.""BlocksetID""
                    ) expected
                        ON
                            b.""Hash"" = expected.""Hash""
                            AND b.""Size"" = expected.""Size""
                    WHERE b.""VolumeID"" IN (
                        SELECT ibl.""BlockVolumeID""
                        FROM Remotevolume idx
                        JOIN IndexBlockLink ibl
                            ON ibl.""IndexVolumeID"" = idx.""ID""
                        WHERE idx.""Name"" = @Name
                    );
                ";

                var compare = $@"
                    WITH
                        Expected AS (
                            SELECT
                                ""Hash"",
                                ""Size""
                            FROM ""{cmpName}""
                        ),
                        Actual AS (
                            SELECT
                                ""Hash"",
                                ""Size""
                            FROM ""{m_tablename}""
                        ),
                        Extra AS (
                            SELECT @TypeExtra AS Type, a.""Hash""
                            FROM Actual a
                            LEFT JOIN Expected e
                                ON
                                    a.""Hash"" = e.""Hash""
                                    AND a.""Size"" = e.""Size""
                            WHERE e.""Hash"" IS NULL
                        ),
                        Missing AS (
                            SELECT @TypeMissing AS Type, e.""Hash""
                            FROM Expected e
                            LEFT JOIN Actual a
                                ON
                                    a.""Hash"" = e.""Hash""
                                    AND a.""Size"" = e.""Size""
                            WHERE a.""Hash"" IS NULL
                        ),
                        Modified AS (
                            SELECT @TypeModified AS Type, a.""Hash""
                            FROM Actual a
                            JOIN Expected e
                                ON a.""Hash"" = e.""Hash""
                            WHERE
                                a.""Size"" != e.""Size""
                                AND NOT EXISTS (
                                    SELECT 1
                                    FROM Extra x
                                    WHERE x.""Hash"" = a.""Hash""
                                )
                        )
                    SELECT *
                    FROM Extra
                    UNION
                        SELECT *
                        FROM Missing
                    UNION
                        SELECT *
                        FROM Modified;
                ";

                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}""";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        // Create expected hash+size table filtered by volume
                        await cmd
                            .SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);

                        // Compare against actual values inserted into temp table
                        cmd
                            .SetCommandAndParameters(compare)
                            .SetParameterValue("@TypeExtra", (int)Library.Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Library.Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Library.Interface.TestEntryStatus.Modified);

                        using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await rd.ReadAsync().ConfigureAwait(false))
                                yield return new KeyValuePair<Library.Interface.TestEntryStatus, string>(
                                    (Library.Interface.TestEntryStatus)rd.ConvertValueToInt64(0),
                                    rd.ConvertValueToString(1) ?? "");
                    }
                    finally
                    {
                        try
                        {
                            await cmd
                                .ExecuteNonQueryAsync(drop)
                                .ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new filelist in the local test database.
        /// </summary>
        /// <param name="name">The name of the filelist to create.</param>
        /// <returns>A task that when awaited returns a new instance of the <see cref="IFilelist"/> interface.</returns>
        public async Task<IFilelist> CreateFilelist(string name)
        {
            return await Filelist.CreateAsync(this, name).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new indexlist in the local test database.
        /// </summary>
        /// <param name="name">The name of the indexlist to create.</param>
        /// <returns>A task that when awaited returns a new instance of the <see cref="IIndexlist"/> interface.</returns>
        public async Task<IIndexlist> CreateIndexlist(string name)
        {
            return await Indexlist.CreateAsync(this, name).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new blocklist in the local test database.
        /// </summary>
        /// <param name="name">The name of the blocklist to create.</param>
        /// <returns>A task that when awaited returns a new instance of the <see cref="IBlocklist"/> interface.</returns>
        public async Task<IBlocklist> CreateBlocklist(string name)
        {
            return await Blocklist.CreateAsync(this, name).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new blocklist hash list in the local test database.
        /// </summary>
        /// <param name="name">The name of the blocklist hash list to create.</param>
        /// <returns>A task that when awaited returns a new instance of the <see cref="IBlocklistHashList"/> interface.</returns>
        public async Task<IBlocklistHashList> CreateBlocklistHashList(string name)
        {
            return await BlocklistHashList.CreateAsync(this, name)
                .ConfigureAwait(false);
        }
    }
}

