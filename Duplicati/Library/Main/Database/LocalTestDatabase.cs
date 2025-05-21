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

namespace Duplicati.Library.Main.Database
{
    internal class LocalTestDatabase : LocalDatabase
    {
        public static async Task<LocalTestDatabase> CreateAsync(string path, long pagecachesize, LocalTestDatabase? dbnew = null)
        {
            dbnew ??= new LocalTestDatabase();

            dbnew = (LocalTestDatabase)await CreateLocalDatabaseAsync(path, "Test", true, pagecachesize, dbnew);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        public static async Task<LocalTestDatabase> CreateAsync(LocalDatabase dbparent, LocalTestDatabase? dbnew = null)
        {
            dbnew ??= new LocalTestDatabase();

            return (LocalTestDatabase)await CreateLocalDatabaseAsync(dbparent, dbnew);
        }

        public void UpdateVerificationCount(string name)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
                cmd.SetCommandAndParameters(@"UPDATE ""RemoteVolume"" SET ""VerificationCount"" = MAX(1, CASE WHEN ""VerificationCount"" <= 0 THEN (SELECT MAX(""VerificationCount"") FROM ""RemoteVolume"") ELSE ""VerificationCount"" + 1 END) WHERE ""Name"" = @Name")
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQuery();
        }

        private record RemoteVolume : IRemoteVolume
        {
            public long ID { get; init; }
            public string Name { get; init; }
            public long Size { get; init; }
            public string Hash { get; init; }
            public long VerificationCount { get; init; }

            public RemoteVolume(SqliteDataReader rd)
            {
                ID = rd.ConvertValueToInt64(0);
                Name = rd.ConvertValueToString(1) ?? "";
                Size = rd.ConvertValueToInt64(2);
                Hash = rd.ConvertValueToString(3) ?? throw new ArgumentNullException("Hash cannot be null");
                VerificationCount = rd.ConvertValueToInt64(4);
            }
        }

        private IEnumerable<RemoteVolume> FilterByVerificationCount(IEnumerable<RemoteVolume> volumes, long samples, long maxverification)
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

        public IEnumerable<IRemoteVolume> SelectTestTargets(long samples, Options options)
        {
            var tp = GetFilelistWhereClause(options.Time, options.Version);

            samples = Math.Max(1, samples);
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                // Select any broken items
                cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE (""State"" IN (@States)) AND (""Hash"" = '' OR ""Hash"" IS NULL OR ""Size"" <= 0) AND (""ArchiveTime"" = 0)")
                    .ExpandInClauseParameter("@States", [RemoteVolumeState.Verified.ToString(), RemoteVolumeState.Uploaded.ToString()]);

                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        yield return new RemoteVolume(rd);

                //Grab the max value
                var max = cmd.ExecuteScalarInt64(@"SELECT MAX(""VerificationCount"") FROM ""RemoteVolume""", 0);

                //First we select some filesets
                var files = new List<RemoteVolume>();
                var whereClause = string.IsNullOrEmpty(tp.Item1) ? " WHERE " : (" " + tp.Item1 + " AND ");
                using (var rd = cmd.SetCommandAndParameters(@"SELECT ""A"".""VolumeID"", ""A"".""Name"", ""A"".""Size"", ""A"".""Hash"", ""A"".""VerificationCount"" FROM (SELECT ""ID"" AS ""VolumeID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""ArchiveTime"" = 0 AND ""State"" IN (@State1, @State2)) A, ""Fileset"" " + whereClause + @" ""A"".""VolumeID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp"" ")
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

                cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""Type"" = @Type AND ""State"" IN (@States) AND ""ArchiveTime"" = 0")
                    .SetParameterValue("@Type", RemoteVolumeType.Index.ToString())
                    .ExpandInClauseParameter("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));

                foreach (var f in FilterByVerificationCount(files, samples, max))
                    yield return f;

                if (options.FullRemoteVerification == Options.RemoteTestStrategy.ListAndIndexes)
                    yield break;

                //And finally some block files
                files.Clear();

                cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""Type"" = @Type AND ""State"" IN (@States) AND ""ArchiveTime"" = 0")
                    .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                    .ExpandInClauseParameter("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));

                foreach (var f in FilterByVerificationCount(files, samples, max))
                    yield return f;
            }
        }

        private abstract class Basiclist : IDisposable
        {
            protected LocalDatabase m_db = null!;
            protected string m_volumename = null!;
            protected string m_tablename = null!;
            protected SqliteCommand m_insertCommand = null!;

            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            protected Basiclist(SqliteConnection connection, ReusableTransaction rtr, string volumename, string tablePrefix, string tableFormat, string insertCommand)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            protected Basiclist() { }

            protected static async Task<Basiclist> CreateAsync(Basiclist bl, LocalDatabase db, string volumename, string tablePrefix, string tableFormat, string insertCommand)
            {
                bl.m_db = db;
                bl.m_volumename = volumename;
                var tablename = tablePrefix + "-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                using (var cmd = bl.m_db.Connection.CreateCommand(bl.m_db.Transaction))
                {
                    cmd.ExecuteNonQuery($@"CREATE TEMPORARY TABLE ""{tablename}"" {tableFormat}");
                    bl.m_tablename = tablename;
                }

                bl.m_insertCommand = bl.m_db.Connection.CreateCommand(bl.m_db.Transaction, $@"INSERT INTO ""{bl.m_tablename}"" {insertCommand}");

                return bl;
            }

            public virtual void Dispose()
            {
                if (m_tablename != null)
                    try
                    {
                        using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction.Transaction))
                            cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""{m_tablename}""");
                    }
                    catch { }
                    finally { m_tablename = null!; }

                m_insertCommand?.Dispose();
            }
        }

        public interface IFilelist : IDisposable
        {
            void Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time);
            IEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare();
        }

        private class Filelist : Basiclist, IFilelist
        {
            private const string TABLE_PREFIX = "Filelist";
            private const string TABLE_FORMAT = @"(""Path"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Hash"" TEXT NULL, ""Metasize"" INTEGER NOT NULL, ""Metahash"" TEXT NOT NULL)";
            private const string INSERT_COMMAND = @"(""Path"", ""Size"", ""Hash"", ""Metasize"", ""Metahash"") VALUES (@Path,@Size,@Hash,@Metasize,@Metahash)";

            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Filelist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
                : base(connection, rtr, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            private Filelist() { }

            public static async Task<Filelist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Filelist();
                return (Filelist)await CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND);
            }

            public void Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time)
            {
                m_insertCommand.SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@Size", hash == null ? -1 : size)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Metasize", metasize)
                    .SetParameterValue("@Metahash", metahash)
                    .ExecuteNonQuery();
            }

            public IEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                var create = $@"CREATE TEMPORARY TABLE ""{cmpName}"" AS SELECT ""A"".""Path"" AS ""Path"", CASE WHEN ""B"".""Fullhash"" IS NULL THEN -1 ELSE ""B"".""Length"" END AS ""Size"", ""B"".""Fullhash"" AS ""Hash"", ""C"".""Length"" AS ""Metasize"", ""C"".""Fullhash"" AS ""Metahash"" FROM (SELECT ""File"".""Path"", ""File"".""BlocksetID"" AS ""FileBlocksetID"", ""Metadataset"".""BlocksetID"" AS ""MetadataBlocksetID"" from ""Remotevolume"", ""Fileset"", ""FilesetEntry"", ""File"", ""Metadataset"" WHERE ""Remotevolume"".""Name"" = @Name AND ""Fileset"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""MetadataID"" = ""Metadataset"".""ID"") A LEFT OUTER JOIN ""Blockset"" B ON ""B"".""ID"" = ""A"".""FileBlocksetID"" LEFT OUTER JOIN ""Blockset"" C ON ""C"".""ID""=""A"".""MetadataBlocksetID"" ";
                var extra = $@"SELECT @TypeExtra AS ""Type"", ""{m_tablename}"".""Path"" AS ""Path"" FROM ""{m_tablename}"" WHERE ""{m_tablename}"".""Path"" NOT IN ( SELECT ""Path"" FROM ""{cmpName}"" )";
                var missing = $@"SELECT @TypeMissing AS ""Type"", ""Path"" AS ""Path"" FROM ""{cmpName}"" WHERE ""Path"" NOT IN (SELECT ""Path"" FROM ""{m_tablename}"")";
                var modified = $@"SELECT @TypeModified AS ""Type"", ""E"".""Path"" AS ""Path"" FROM ""{m_tablename}"" E, ""{cmpName}"" D WHERE ""D"".""Path"" = ""E"".""Path"" AND (""D"".""Size"" != ""E"".""Size"" OR ""D"".""Hash"" != ""E"".""Hash"" OR ""D"".""Metasize"" != ""E"".""Metasize"" OR ""D"".""Metahash"" != ""E"".""Metahash"")  ";
                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        cmd.SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQuery();

                        cmd.SetCommandAndParameters($"{extra} UNION {missing} UNION {modified}")
                            .SetParameterValue("@TypeExtra", (int)Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Interface.TestEntryStatus.Modified);

                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                                yield return new KeyValuePair<Interface.TestEntryStatus, string>((Interface.TestEntryStatus)rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");

                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(drop); }
                        catch { }
                    }
                }
            }
        }

        public interface IIndexlist : IDisposable
        {
            void AddBlockLink(string filename, string hash, long length);
            IEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }

        private class Indexlist : Basiclist, IIndexlist
        {
            private const string TABLE_PREFIX = "Indexlist";
            private const string TABLE_FORMAT = @"(""Name"" TEXT NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)";
            private const string INSERT_COMMAND = @"(""Name"", ""Hash"", ""Size"") VALUES (@Name,@Hash,@Size)";

            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Indexlist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
                : base(connection, rtr, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            private Indexlist() { }

            public static async Task<Indexlist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Indexlist();
                return (Indexlist)await CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND);
            }

            public void AddBlockLink(string filename, string hash, long length)
            {
                m_insertCommand.SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Name", filename)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", length)
                    .ExecuteNonQuery();
            }

            public IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var create = $@"CREATE TEMPORARY TABLE ""{cmpName}"" AS SELECT ""A"".""Name"", ""A"".""Hash"", ""A"".""Size"" FROM ""Remotevolume"" A, ""Remotevolume"" B, ""IndexBlockLink"" WHERE ""B"".""Name"" = @Name AND ""A"".""ID"" = ""IndexBlockLink"".""BlockVolumeID"" AND ""B"".""ID"" = ""IndexBlockLink"".""IndexVolumeID"" ";
                var extra = $@"SELECT @TypeExtra AS ""Type"", ""{m_tablename}"".""Name"" AS ""Name"" FROM ""{m_tablename}"" WHERE ""{m_tablename}"".""Name"" NOT IN ( SELECT ""Name"" FROM ""{cmpName}"" )";
                var missing = $@"SELECT @TypeMissing AS ""Type"", ""Name"" AS ""Name"" FROM ""{cmpName}"" WHERE ""Name"" NOT IN (SELECT ""Name"" FROM ""{m_tablename}"")";
                var modified = $@"SELECT @TypeModified AS ""Type"", ""E"".""Name"" AS ""Name"" FROM ""{m_tablename}"" E, ""{cmpName}"" D WHERE ""D"".""Name"" = ""E"".""Name"" AND (""D"".""Hash"" != ""E"".""Hash"" OR ""D"".""Size"" != ""E"".""Size"") ";
                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        cmd.SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQuery();

                        cmd.SetCommandAndParameters($"{extra} UNION {missing} UNION {modified}")
                            .SetParameterValue("@TypeExtra", (int)Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Interface.TestEntryStatus.Modified);

                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                                yield return new KeyValuePair<Interface.TestEntryStatus, string>((Interface.TestEntryStatus)rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");

                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(drop); }
                        catch { }
                    }
                }
            }
        }

        public interface IBlocklist : IDisposable
        {
            void AddBlock(string key, long value);
            IEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }

        private class Blocklist : Basiclist, IBlocklist
        {
            private const string TABLE_PREFIX = "Blocklist";
            private const string TABLE_FORMAT = @"(""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)";
            private const string INSERT_COMMAND = @"(""Hash"", ""Size"") VALUES (@Hash,@Size)";

            [Obsolete("Calling this constructor will throw an exception. Use the CreateAsync method instead.")]
            public Blocklist(SqliteConnection connection, string volumename, ReusableTransaction rtr)
                : base(connection, rtr, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND)
            {
                throw new NotSupportedException("Use CreateAsync method instead.");
            }

            private Blocklist() { }

            public static async Task<Blocklist> CreateAsync(LocalDatabase db, string volumename)
            {
                var bl = new Blocklist();
                return (Blocklist)await CreateAsync(bl, db, volumename, TABLE_PREFIX, TABLE_FORMAT, INSERT_COMMAND);
            }

            public void AddBlock(string hash, long size)
            {
                m_insertCommand.SetTransaction(m_db.Transaction)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQuery();
            }

            public IEnumerable<KeyValuePair<Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var curBlocks = @"SELECT ""Block"".""Hash"" AS ""Hash"", ""Block"".""Size"" AS ""Size"" FROM ""Remotevolume"", ""Block"" WHERE ""Remotevolume"".""Name"" = @Name AND ""Remotevolume"".""ID"" = ""Block"".""VolumeID""";
                var duplBlocks = @"SELECT ""Block"".""Hash"" AS ""Hash"", ""Block"".""Size"" AS ""Size"" FROM ""DuplicateBlock"", ""Block"" WHERE ""DuplicateBlock"".""VolumeID"" = (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = @Name) AND ""Block"".""ID"" = ""DuplicateBlock"".""BlockID""";
                var delBlocks = @"SELECT ""DeletedBlock"".""Hash"" AS ""Hash"", ""DeletedBlock"".""Size"" AS ""Size"" FROM ""DeletedBlock"", ""RemoteVolume"" WHERE ""RemoteVolume"".""Name"" = @Name AND ""RemoteVolume"".""ID"" = ""DeletedBlock"".""VolumeID""";
                var create = $@"CREATE TEMPORARY TABLE ""{cmpName}"" AS SELECT DISTINCT ""Hash"" AS ""Hash"", ""Size"" AS ""Size"" FROM ({curBlocks} UNION {delBlocks} UNION {duplBlocks})";
                var extra = $@"SELECT @TypeExtra AS ""Type"", ""{m_tablename}"".""Hash"" AS ""Hash"" FROM ""{m_tablename}"" WHERE ""{m_tablename}"".""Hash"" NOT IN ( SELECT ""Hash"" FROM ""{cmpName}"" )";
                var missing = $@"SELECT @TypeMissing AS ""Type"", ""Hash"" AS ""Hash"" FROM ""{cmpName}"" WHERE ""Hash"" NOT IN (SELECT ""Hash"" FROM ""{m_tablename}"")";
                var modified = $@"SELECT @TypeModified AS ""Type"", ""E"".""Hash"" AS ""Hash"" FROM ""{m_tablename}"" E, ""{cmpName}"" D WHERE ""D"".""Hash"" = ""E"".""Hash"" AND ""D"".""Size"" != ""E"".""Size""  ";
                var drop = $@"DROP TABLE IF EXISTS ""{cmpName}"" ";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    try
                    {
                        cmd.SetCommandAndParameters(create)
                            .SetParameterValue("@Name", m_volumename)
                            .ExecuteNonQuery();

                        cmd.SetCommandAndParameters($"{extra} UNION {missing} UNION {modified}")
                            .SetParameterValue("@TypeExtra", (int)Library.Interface.TestEntryStatus.Extra)
                            .SetParameterValue("@TypeMissing", (int)Library.Interface.TestEntryStatus.Missing)
                            .SetParameterValue("@TypeModified", (int)Library.Interface.TestEntryStatus.Modified);
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");

                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(drop); }
                        catch { }
                    }
                }
            }
        }

        public IFilelist CreateFilelist(string name)
        {
            return new Filelist(m_connection, name, rtr);
        }

        public IIndexlist CreateIndexlist(string name)
        {
            return new Indexlist(m_connection, name, rtr);
        }

        public IBlocklist CreateBlocklist(string name)
        {
            return new Blocklist(m_connection, name, rtr);
        }
    }
}

