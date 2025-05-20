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

        public static async Task<LocalListChangesDatabase> CreateAsync(string path, long pagecachesize)
        {
            var db = new LocalListChangesDatabase();

            db = (LocalListChangesDatabase)await CreateLocalDatabaseAsync(db, path, "ListChanges", false, pagecachesize);
            db.ShouldCloseConnection = true;

            return db;
        }

        public interface IStorageHelper : IDisposable
        {
            void AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew);

            void AddFromDb(long filesetId, bool asNew, IFilter filter);

            IChangeCountReport CreateChangeCountReport();
            IChangeSizeReport CreateChangeSizeReport();
            IEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport();
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
                    cmd.ExecuteNonQuery($@"CREATE TEMPORARY TABLE ""{sh.m_previousTable}"" (""Path"" TEXT NOT NULL, ""FileHash"" TEXT NULL, ""MetaHash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Type"" INTEGER NOT NULL) ");
                    cmd.ExecuteNonQuery($@"CREATE TEMPORARY TABLE ""{sh.m_currentTable}"" (""Path"" TEXT NOT NULL, ""FileHash"" TEXT NULL, ""MetaHash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Type"" INTEGER NOT NULL) ");
                }

                sh.m_insertPreviousElementCommand = sh.m_db.Connection.CreateCommand($@"INSERT INTO ""{sh.m_previousTable}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (@Path,@FileHash,@MetaHash,@Size,@Type)");
                sh.m_insertCurrentElementCommand = sh.m_db.Connection.CreateCommand($@"INSERT INTO ""{sh.m_currentTable}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (@Path,@FileHash,@MetaHash,@Size,@Type)");

                return sh;
            }

            public void AddFromDb(long filesetId, bool asNew, IFilter filter)
            {
                var tablename = asNew ? m_currentTable : m_previousTable;

                var folders = $@"SELECT ""File"".""Path"" AS ""Path"", NULL AS ""FileHash"", ""Blockset"".""Fullhash"" AS ""MetaHash"", -1 AS ""Size"", {(int)Interface.ListChangesElementType.Folder} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" = -100 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" ";
                var symlinks = $@"SELECT ""File"".""Path"" AS ""Path"", NULL AS ""FileHash"", ""Blockset"".""Fullhash"" AS ""MetaHash"", -1 AS ""Size"", {(int)Interface.ListChangesElementType.Symlink} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" = -200 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" ";
                var files = $@"SELECT ""File"".""Path"" AS ""Path"", ""FB"".""FullHash"" AS ""FileHash"", ""MB"".""Fullhash"" AS ""MetaHash"", ""FB"".""Length"" AS ""Size"", {(int)Interface.ListChangesElementType.File} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" MB, ""Blockset"" FB WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" >= 0 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""MB"".""ID"" AND ""File"".""BlocksetID"" = ""FB"".""ID"" ";
                var combined = "(" + folders + " UNION " + symlinks + " UNION " + files + ")";


                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    if (filter == null || filter.Empty)
                    {
                        // Simple case, select everything
                        cmd.SetCommandAndParameters($@"INSERT INTO ""{tablename}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") SELECT ""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"" FROM {combined} A WHERE ""A"".""FilesetID"" = @FilesetId ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQuery();
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == Duplicati.Library.Utility.FilterType.Simple)
                    {
                        // File list based
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        var p = expression.GetSimpleList();
                        var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        cmd.ExecuteNonQuery($@"CREATE TEMPORARY TABLE ""{filenamestable}"" (""Path"" TEXT NOT NULL) ");
                        cmd.SetCommandAndParameters($@"INSERT INTO ""{filenamestable}"" (""Path"") VALUES (@Path)");

                        foreach (var s in p)
                            cmd.SetParameterValue("@Path", s)
                                .ExecuteNonQuery();

                        string whereClause;
                        if (expression.Result)
                        {
                            // Include filter
                            whereClause = $@"""A"".""FilesetID"" = @FilesetId AND ""A"".""Path"" IN (SELECT DISTINCT ""Path"" FROM ""{filenamestable}"")";
                        }
                        else
                        {
                            // Exclude filter
                            whereClause = $@"""A"".""FilesetID"" = @FilesetId AND ""A"".""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""{filenamestable}"")";
                        }
                        cmd.SetCommandAndParameters($@"INSERT INTO ""{tablename}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") SELECT ""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"" FROM {combined} A WHERE {whereClause} ")
                            .SetParameterValue("@FilesetId", filesetId)
                            .ExecuteNonQuery();

                        cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""{filenamestable}"" ");
                    }
                    else
                    {
                        // Do row-wise iteration
                        var values = new object[5];
                        cmd.SetCommandAndParameters($@"SELECT ""A"".""Path"", ""A"".""FileHash"", ""A"".""MetaHash"", ""A"".""Size"", ""A"".""Type"" FROM {combined} A WHERE ""A"".""FilesetID"" = @FilesetId")
                            .SetParameterValue("@FilesetId", filesetId);
                        using (var cmd2 = m_db.Connection.CreateCommand(m_db.Transaction).SetCommandAndParameters($@"INSERT INTO ""{tablename}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (@Path,@FileHash,@MetaHash,@Size,@Type)"))
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                            {
                                rd.GetValues(values);
                                if (values[0] != null && values[0] != DBNull.Value && FilterExpression.Matches(filter, values[0].ToString()))
                                {
                                    cmd2.SetParameterValue("@Path", values[0])
                                        .SetParameterValue("@FileHash", values[1])
                                        .SetParameterValue("@MetaHash", values[2])
                                        .SetParameterValue("@Size", values[3])
                                        .SetParameterValue("@Type", values[4])
                                        .ExecuteNonQuery();
                                }
                            }
                    }
                }
            }

            public void AddElement(string path, string filehash, string metahash, long size, Interface.ListChangesElementType type, bool asNew)
            {
                var cmd = asNew ? m_insertCurrentElementCommand : m_insertPreviousElementCommand;
                cmd.SetParameterValue("@Path", path);
                cmd.SetParameterValue("@FileHash", filehash);
                cmd.SetParameterValue("@MetaHash", metahash);
                cmd.SetParameterValue("@Size", size);
                cmd.SetParameterValue("@Type", (int)type);
                cmd.ExecuteNonQuery();
            }

            private static IEnumerable<string?> ReaderToStringList(SqliteDataReader rd)
            {
                using (rd)
                    while (rd.Read())
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
                    $@"SELECT ""Path"" FROM ""{m_currentTable}"" WHERE {(allTypes ? "" : @$" ""{m_currentTable}"".""Type"" = @Type AND ")} ""{m_currentTable}"".""Path"" NOT IN (SELECT ""Path"" FROM ""{m_previousTable}"")",
                    $@"SELECT ""Path"" FROM ""{m_previousTable}"" WHERE {(allTypes ? "" : @$" ""{m_previousTable}"".""Type"" = @Type AND ")} ""{m_previousTable}"".""Path"" NOT IN (SELECT ""Path"" FROM ""{m_currentTable}"")",
                    $@"SELECT ""{m_currentTable}"".""Path"" FROM ""{m_currentTable}"",""{m_previousTable}"" WHERE {(allTypes ? "" : $@" ""{m_currentTable}"".""Type"" = @Type AND ")} ""{m_currentTable}"".""Path"" = ""{m_previousTable}"".""Path"" AND (""{m_currentTable}"".""FileHash"" != ""{m_previousTable}"".""FileHash"" OR ""{m_currentTable}"".""MetaHash"" != ""{m_previousTable}"".""MetaHash"" OR ""{m_currentTable}"".""Type"" != ""{m_previousTable}"".""Type"") "
                );
            }

            public IChangeSizeReport CreateChangeSizeReport()
            {
                var sqls = GetSqls(true);
                var added = sqls.Added;
                var deleted = sqls.Deleted;
                //var modified = sqls.Modified;

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    var result = new ChangeSizeReport();

                    result.PreviousSize = cmd.ExecuteScalarInt64($@"SELECT SUM(""Size"") FROM ""{m_previousTable}"" ", 0);
                    result.CurrentSize = cmd.ExecuteScalarInt64($@"SELECT SUM(""Size"") FROM ""{m_currentTable}"" ", 0);

                    result.AddedSize = cmd.ExecuteScalarInt64($@"SELECT SUM(""Size"") FROM ""{m_currentTable}"" WHERE ""{m_currentTable}"".""Path"" IN ({added}) ", 0);
                    result.DeletedSize = cmd.ExecuteScalarInt64($@"SELECT SUM(""Size"") FROM ""{m_previousTable}"" WHERE ""{m_previousTable}"".""Path"" IN ({deleted}) ", 0);

                    return result;
                }
            }

            public IChangeCountReport CreateChangeCountReport()
            {
                var sqls = GetSqls(false);
                var added = @"SELECT COUNT(*) FROM (" + sqls.Added + ")";
                var deleted = @"SELECT COUNT(*) FROM (" + sqls.Deleted + ")";
                var modified = @"SELECT COUNT(*) FROM (" + sqls.Modified + ")";

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    var result = new ChangeCountReport();
                    result.AddedFolders = cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64(0);
                    result.AddedSymlinks = cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64(0);
                    result.AddedFiles = cmd.SetCommandAndParameters(added)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64(0);

                    result.DeletedFolders = cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64(0);
                    result.DeletedSymlinks = cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64(0);
                    result.DeletedFiles = cmd.SetCommandAndParameters(deleted)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64(0);

                    result.ModifiedFolders = cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Folder)
                        .ExecuteScalarInt64(0);
                    result.ModifiedSymlinks = cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.Symlink)
                        .ExecuteScalarInt64(0);
                    result.ModifiedFiles = cmd.SetCommandAndParameters(modified)
                        .SetParameterValue("@Type", (int)Interface.ListChangesElementType.File)
                        .ExecuteScalarInt64(0);

                    return result;
                }
            }

            public IEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> CreateChangedFileReport()
            {
                var sqls = GetSqls(false);

                using (var cmd = m_db.Connection.CreateCommand(m_db.Transaction))
                {
                    var elTypes = new[] {
                        Interface.ListChangesElementType.Folder,
                        Interface.ListChangesElementType.Symlink,
                        Interface.ListChangesElementType.File
                    };

                    IEnumerable<Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>> BuildResult(SqliteCommand cmd, string sql, Interface.ListChangesChangeType changeType)
                    {
                        cmd.SetCommandAndParameters(sql);
                        foreach (var type in elTypes)
                            foreach (var s in ReaderToStringList(cmd.SetParameterValue("@Type", (int)type).ExecuteReader()))
                                yield return new Tuple<Interface.ListChangesChangeType, Interface.ListChangesElementType, string>(changeType, type, s ?? "");
                    }

                    foreach (var r in BuildResult(cmd, sqls.Added, Interface.ListChangesChangeType.Added))
                        yield return r;
                    foreach (var r in BuildResult(cmd, sqls.Deleted, Interface.ListChangesChangeType.Deleted))
                        yield return r;
                    foreach (var r in BuildResult(cmd, sqls.Modified, Interface.ListChangesChangeType.Modified))
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
                    try { m_insertPreviousElementCommand.Dispose(); }
                    catch { }
                    finally { m_insertPreviousElementCommand = null!; }
                }

                if (m_insertCurrentElementCommand != null)
                {
                    try { m_insertCurrentElementCommand.Dispose(); }
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
    }
}

