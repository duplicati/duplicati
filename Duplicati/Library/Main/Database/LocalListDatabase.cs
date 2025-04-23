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

namespace Duplicati.Library.Main.Database
{
    internal class LocalListDatabase : LocalDatabase
    {
        public LocalListDatabase(string path)
            : base(path, "List", false)
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

    }
}

