//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListChangesDatabase : LocalDatabase
    {
        public LocalListChangesDatabase(string path)
            : base(path, "ListChanges", false)
        {
            ShouldCloseConnection = true;
        }
                
        public interface IStorageHelper : IDisposable
        {
            void AddElement(string path, string filehash, string metahash, long size, Library.Interface.ListChangesElementType type, bool asNew);
            
            void AddFromDb(long filesetId, bool asNew, Library.Utility.IFilter filter);
            
            IChangeCountReport CreateChangeCountReport();
            IChangeSizeReport CreateChangeSizeReport();
            IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>> CreateChangedFileReport();
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
            public long AddedFolders { get; internal set;  }
            public long AddedSymlinks { get; internal set;  }
            public long AddedFiles { get; internal set; }
            
            public long DeletedFolders { get; internal set; }
            public long DeletedSymlinks { get; internal set;  }
            public long DeletedFiles { get; internal set;  }
            
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
            private System.Data.IDbConnection m_connection;
            private System.Data.IDbTransaction m_transaction;
            
            private System.Data.IDbCommand m_insertPreviousElementCommand;
            private System.Data.IDbCommand m_insertCurrentElementCommand;
            
            private string m_previousTable;
            private string m_currentTable;
            
            public StorageHelper(System.Data.IDbConnection con)
            {
                m_connection = con;
                m_previousTable = "Previous-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                m_currentTable = "Current-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                
                m_transaction = m_connection.BeginTransaction();
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL, ""FileHash"" TEXT NULL, ""MetaHash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Type"" INTEGER NOT NULL) ", m_previousTable));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL, ""FileHash"" TEXT NULL, ""MetaHash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Type"" INTEGER NOT NULL) ", m_currentTable));
                }
                
                m_insertPreviousElementCommand = m_connection.CreateCommand();
                m_insertPreviousElementCommand.Transaction = m_transaction;
                m_insertPreviousElementCommand.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (?,?,?,?,?)", m_previousTable);
                m_insertPreviousElementCommand.AddParameters(5);
                
                m_insertCurrentElementCommand = m_connection.CreateCommand();
                m_insertCurrentElementCommand.Transaction = m_transaction;
                m_insertCurrentElementCommand.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (?,?,?,?,?)", m_currentTable);
                m_insertCurrentElementCommand.AddParameters(5);
            }
            
            public void AddFromDb(long filesetId, bool asNew, Library.Utility.IFilter filter)
            {
                var tablename = asNew ? m_currentTable : m_previousTable;
                
                var folders = string.Format(@"SELECT ""File"".""Path"" AS ""Path"", NULL AS ""FileHash"", ""Blockset"".""Fullhash"" AS ""MetaHash"", -1 AS ""Size"", {0} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" = -100 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" ", (int)Library.Interface.ListChangesElementType.Folder);
                var symlinks = string.Format(@"SELECT ""File"".""Path"" AS ""Path"", NULL AS ""FileHash"", ""Blockset"".""Fullhash"" AS ""MetaHash"", -1 AS ""Size"", {0} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" = -200 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" ", (int)Library.Interface.ListChangesElementType.Symlink);
                var files = string.Format(@"SELECT ""File"".""Path"" AS ""Path"", ""FB"".""FullHash"" AS ""FileHash"", ""MB"".""Fullhash"" AS ""MetaHash"", ""FB"".""Length"" AS ""Size"", {0} AS ""Type"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"" FROM ""File"",""FilesetEntry"",""Metadataset"",""Blockset"" MB, ""Blockset"" FB WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""BlocksetID"" >= 0 AND ""Metadataset"".""ID""=""File"".""MetadataID"" AND ""Metadataset"".""BlocksetID"" = ""MB"".""ID"" AND ""File"".""BlocksetID"" = ""FB"".""ID"" ", (int)Library.Interface.ListChangesElementType.File);
                var combined = "(" + folders + " UNION " + symlinks + " UNION " + files + ")";
                
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    if (filter == null || filter.Empty)
                    {
                        // Simple case, select everything
                        cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") SELECT ""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"" FROM {1} A WHERE ""A"".""FilesetID"" = ? ", tablename, combined), filesetId);
                    }
                    else if (Library.Utility.Utility.IsFSCaseSensitive && filter is Library.Utility.FilterExpression && (filter as Library.Utility.FilterExpression).Type == Duplicati.Library.Utility.FilterType.Simple)
                    {
                        // File list based
                        // unfortunately we cannot do this if the filesystem is case sensitive as
                        // SQLite only supports ASCII compares
                        var p = (filter as Library.Utility.FilterExpression).GetSimpleList();
                        var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL) ", filenamestable));
                        cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"") VALUES (?)", filenamestable);
                        cmd.AddParameter();
                    
                        foreach(var s in p)
                        {
                            cmd.SetParameterValue(0, s);
                            cmd.ExecuteNonQuery();
                        }
                    
                        cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") SELECT ""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"" FROM {1} A WHERE ""A"".""FilesetID"" = ? AND ""A"".""Path"" IN (SELECT DISTINCT ""Path"" FROM ""{2}"") ", tablename, combined, filenamestable), filesetId);
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", filenamestable));
                    }
                    else
                    {
                        // Do row-wise iteration
                        object[] values = new object[5];
                        using(var cmd2 = m_connection.CreateCommand())
                        {
                            cmd2.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"") VALUES (?,?,?,?,?)", tablename);
                            cmd2.AddParameters(5);
                            cmd2.Transaction = m_transaction;
    
                            using(var rd = cmd.ExecuteReader(@"SELECT ""Path"", ""FileHash"", ""MetaHash"", ""Size"", ""Type"" FROM {1} A WHERE ""A"".""FilesetID"" = ?", filesetId))
                                while (rd.Read())
                                {
                                    rd.GetValues(values);
                                    if (values[0] != null && values[0] != DBNull.Value && Library.Utility.FilterExpression.Matches(filter, values[0].ToString()))
                                    {
                                        cmd2.SetParameterValue(0, values[0]);
                                        cmd2.SetParameterValue(1, values[1]);
                                        cmd2.SetParameterValue(2, values[2]);
                                        cmd2.SetParameterValue(3, values[3]);
                                        cmd2.SetParameterValue(4, values[4]);
                                        cmd2.ExecuteNonQuery();
                                    }
                                }
                        }
                    }
                }
            }
            
            public void AddElement(string path, string filehash, string metahash, long size, Library.Interface.ListChangesElementType type, bool asNew)
            {
                var cmd = asNew ? m_insertCurrentElementCommand : m_insertPreviousElementCommand;
                cmd.SetParameterValue(0, path);
                cmd.SetParameterValue(1, filehash);
                cmd.SetParameterValue(2, metahash);
                cmd.SetParameterValue(3, size);
                cmd.SetParameterValue(4, (int)type);
                cmd.ExecuteNonQuery();
            }

            private static IEnumerable<string> ReaderToStringList(System.Data.IDataReader rd)
            {
                using(rd)
                    while (rd.Read())
                    {
                        var v = rd.GetValue(0);
                        if (v == null || v == DBNull.Value)
                            yield return null;
                        else
                            yield return v.ToString();
                    }
            }
            
            private Tuple<string, string, string> GetSqls(bool allTypes)
            {
                return new Tuple<string, string, string>(
                    string.Format(@"SELECT ""Path"" FROM ""{0}"" WHERE {2} ""{0}"".""Path"" NOT IN (SELECT ""Path"" FROM ""{1}"")", m_currentTable, m_previousTable, string.Format(allTypes ? "" : @" ""{0}"".""Type"" = ? AND ", m_currentTable)),
                    string.Format(@"SELECT ""Path"" FROM ""{0}"" WHERE {2} ""{0}"".""Path"" NOT IN (SELECT ""Path"" FROM ""{1}"")", m_previousTable, m_currentTable, string.Format(allTypes ? "" : @" ""{0}"".""Type"" = ? AND ", m_previousTable)),
                    string.Format(@"SELECT ""{0}"".""Path"" FROM ""{0}"",""{1}"" WHERE {2} ""{0}"".""Path"" = ""{1}"".""Path"" AND (""{0}"".""FileHash"" != ""{1}"".""FileHash"" OR ""{0}"".""MetaHash"" != ""{1}"".""MetaHash"" OR ""{0}"".""Type"" != ""{1}"".""Type"") ", m_currentTable, m_previousTable, string.Format(allTypes ? "" : @" ""{0}"".""Type"" = ? AND ", m_currentTable))
                );
            }

            public IChangeSizeReport CreateChangeSizeReport()
            {
                var sqls = GetSqls(true);
                var added = sqls.Item1;
                var deleted = sqls.Item2;
                //var modified = sqls.Item3;
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    var result = new ChangeSizeReport();
                    
                    result.PreviousSize = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""Size"") FROM ""{0}"" ", m_previousTable), 0);
                    result.CurrentSize = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""Size"") FROM ""{0}"" ", m_currentTable), 0);
                    
                    result.AddedSize = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""Size"") FROM ""{0}"" WHERE ""{0}"".""Path"" IN ({1}) ", m_currentTable, added), 0);
                    result.DeletedSize = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""Size"") FROM ""{0}"" WHERE ""{0}"".""Path"" IN ({1}) ", m_previousTable, deleted), 0);
                    
                    return result;
                }
            }
            
            public IChangeCountReport CreateChangeCountReport()
            {
                var sqls = GetSqls(false);
                var added = @"SELECT COUNT(*) FROM (" + sqls.Item1 + ")";
                var deleted = @"SELECT COUNT(*) FROM (" + sqls.Item2 + ")";
                var modified = @"SELECT COUNT(*) FROM (" + sqls.Item3 + ")";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    var result = new ChangeCountReport();
                    result.AddedFolders = cmd.ExecuteScalarInt64(added, 0, (int)Library.Interface.ListChangesElementType.Folder);
                    result.AddedSymlinks = cmd.ExecuteScalarInt64(added, 0, (int)Library.Interface.ListChangesElementType.Symlink);
                    result.AddedFiles = cmd.ExecuteScalarInt64(added, 0, (int)Library.Interface.ListChangesElementType.File);

                    result.DeletedFolders = cmd.ExecuteScalarInt64(deleted, 0, (int)Library.Interface.ListChangesElementType.Folder);
                    result.DeletedSymlinks = cmd.ExecuteScalarInt64(deleted, 0, (int)Library.Interface.ListChangesElementType.Symlink);
                    result.DeletedFiles = cmd.ExecuteScalarInt64(deleted, 0, (int)Library.Interface.ListChangesElementType.File);
                    
                    result.ModifiedFolders = cmd.ExecuteScalarInt64(modified, 0, (int)Library.Interface.ListChangesElementType.Folder);
                    result.ModifiedSymlinks = cmd.ExecuteScalarInt64(modified, 0, (int)Library.Interface.ListChangesElementType.Symlink);
                    result.ModifiedFiles = cmd.ExecuteScalarInt64(modified, 0, (int)Library.Interface.ListChangesElementType.File);
                    
                    return result;
                }                
            }
            
            public IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>> CreateChangedFileReport()
            {
                var sqls = GetSqls(false);
                var added = sqls.Item1;
                var deleted = sqls.Item2;
                var modified = sqls.Item3;
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(added, (int)Library.Interface.ListChangesElementType.Folder)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Added, Library.Interface.ListChangesElementType.Folder, s);                        
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(added, (int)Library.Interface.ListChangesElementType.Symlink)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Added, Library.Interface.ListChangesElementType.Symlink, s);
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(added, (int)Library.Interface.ListChangesElementType.File)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Added, Library.Interface.ListChangesElementType.File, s);
                
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(deleted, (int)Library.Interface.ListChangesElementType.Folder)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Deleted, Library.Interface.ListChangesElementType.Folder, s);
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(deleted, (int)Library.Interface.ListChangesElementType.Symlink)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Deleted, Library.Interface.ListChangesElementType.Symlink, s);
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(deleted, (int)Library.Interface.ListChangesElementType.File)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Deleted, Library.Interface.ListChangesElementType.File, s);
    
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(modified, (int)Library.Interface.ListChangesElementType.Folder)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Modified, Library.Interface.ListChangesElementType.Folder, s);
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(modified, (int)Library.Interface.ListChangesElementType.Symlink)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Modified, Library.Interface.ListChangesElementType.Symlink, s);
                    foreach(var s in ReaderToStringList(cmd.ExecuteReader(modified, (int)Library.Interface.ListChangesElementType.File)))
                        yield return new Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>(Library.Interface.ListChangesChangeType.Modified, Library.Interface.ListChangesElementType.File, s);
                }
            }
            
            public void Dispose()
            {
                if (m_insertPreviousElementCommand != null)
                {
                    try { m_insertPreviousElementCommand.Dispose(); }
                    catch {}
                    finally { m_insertPreviousElementCommand = null; }
                }

                if (m_insertCurrentElementCommand != null)
                {
                    try { m_insertCurrentElementCommand.Dispose(); }
                    catch {}
                    finally { m_insertCurrentElementCommand = null; }
                }
                
                if (m_transaction != null)
                {
                    try { m_transaction.Rollback(); }
                    catch {}
                    finally
                    {
                        m_previousTable = null;
                        m_currentTable= null;
                        m_transaction = null;
                    }
                }
            }
        }
        
        public IStorageHelper CreateStorageHelper()
        {
            return new StorageHelper(m_connection);
        }
    }
}

