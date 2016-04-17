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
using System.Linq;
using System.Collections.Generic;
using System.Text;

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
            string Path { get; }
            IEnumerable<long> Sizes { get; }
        }
        
        public interface IFileset
        {
            long Version { get; }
            DateTime Time { get; }
            long FileCount { get; }
            long FileSizes { get; }
        }
                
        public interface IFileSets : IDisposable
        {
            IEnumerable<IFileset> Sets { get; }
            IEnumerable<IFileset> QuickSets { get; }
            IEnumerable<IFileversion> SelectFiles(Library.Utility.IFilter filter);
            IEnumerable<IFileversion> GetLargestPrefix(Library.Utility.IFilter filter);
            IEnumerable<IFileversion> SelectFolderContents(Library.Utility.IFilter filter);
            void TakeFirst ();
        }
        
        private class FileSets : IFileSets
        {
            private System.Data.IDbConnection m_connection;
            private string m_tablename;
            private KeyValuePair<long, DateTime>[] m_filesets;
            
            public FileSets(LocalListDatabase owner, DateTime time, long[] versions)
            {
                m_connection = owner.m_connection;
                m_filesets = owner.FilesetTimes.ToArray();
                m_tablename = "Filesets-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
				var tmp = owner.GetFilelistWhereClause(time, versions, m_filesets);
				string query = tmp.Item1;
				var args = tmp.Item2;
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""ID"" AS ""FilesetID"", ""Timestamp"" AS ""Timestamp"" FROM ""Fileset"" " + query, m_tablename), args);
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_FilesetIDTimestampIndex"" ON ""{0}"" (""FilesetID"", ""Timestamp"" DESC)", m_tablename));
                }
            }
            
            private class Fileset : IFileset
            {
                public long Version { get; private set; }
                public DateTime Time { get; private set; }
                public long FileCount { get; private set; }
                public long FileSizes { get; private set; }
                
                public Fileset(long version, DateTime time, long filecount, long filesizes)
                {
                    this.Version = version;
                    this.Time = time;
                    this.FileCount = filecount;
                    this.FileSizes = filesizes;
                }
            }
            
            private class Fileversion : IFileversion
            {
                private System.Data.IDataReader m_reader;
                public string Path { get; private set; }
                public bool More { get; private set; }
                
                public Fileversion(System.Data.IDataReader reader) 
                { 
                    m_reader = reader; 
                    this.Path = reader.GetValue(0).ToString();
                    this.More = true;
                }
                
                public IEnumerable<long> Sizes
                {
                    get
                    {
                        while(this.More && this.Path == m_reader.GetValue(0).ToString())
                        {
                            yield return m_reader.ConvertValueToInt64(1, -1);
                            this.More = m_reader.Read();
                        }
                    }
                }
            }
            
            private class FileversionFixed : IFileversion
            {
                public string Path { get; internal set; }
                public IEnumerable<long> Sizes { get { return new long[0]; } }
            }
            
            public IEnumerable<IFileversion> GetLargestPrefix(Library.Utility.IFilter filter)
            {
                using(var tmpnames = new FilteredFilenameTable(m_connection, filter, null))
                using(var cmd = m_connection.CreateCommand())                
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{1}"") ) ", tmpnames.Tablename, m_tablename));
                    
                    // Then we recursively find the largest prefix
                    cmd.CommandText = string.Format(@"SELECT ""Path"" FROM ""{0}"" ORDER BY LENGTH(""Path"") DESC LIMIT 1", tmpnames.Tablename);
                    var v0 = cmd.ExecuteScalar();
                    string maxpath = "";
                    if (v0 != null)
                        maxpath = v0.ToString();
    
                    cmd.CommandText = string.Format(@"SELECT COUNT(*) FROM ""{0}""", tmpnames.Tablename);
                    var filecount = cmd.ExecuteScalarInt64(0);
                    long foundfiles = -1;
    
                    //TODO: Handle FS case-sensitive?
                    cmd.CommandText = string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE SUBSTR(""Path"", 1, ?) = ?", tmpnames.Tablename);
                    cmd.AddParameter();
                    cmd.AddParameter();
    
                    while (filecount != foundfiles && maxpath.Length > 0)
                    {
                        var mp = Library.Utility.Utility.AppendDirSeparator(maxpath);
                        cmd.SetParameterValue(0, mp.Length);
                        cmd.SetParameterValue(1, mp);
                        foundfiles = cmd.ExecuteScalarInt64(0);
    
                        if (filecount != foundfiles)
                        {
                            var oldlen = maxpath.Length;
                            maxpath = Library.Snapshots.SnapshotUtility.SystemIO.PathGetDirectoryName(maxpath);
                            if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen)
                                maxpath = "";
                        }
                    }
    
                    return 
                        new IFileversion[] {
                            new FileversionFixed() { Path = maxpath == "" ? "" : Library.Utility.Utility.AppendDirSeparator(maxpath) }
                        };
                }
    
            }
            
            private IEnumerable<string> SelectFolderEntries(System.Data.IDbCommand cmd, string prefix, string table)
            {
                if (!string.IsNullOrEmpty(prefix))
                    prefix = Duplicati.Library.Utility.Utility.AppendDirSeparator(prefix);
                
                var ppl = prefix.Length;
                using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""Path"" FROM ""{0}"" ", table)))
                    while (rd.Read())
                    {
                        var s = rd.GetString(0);
                        if (!s.StartsWith(prefix))
                            continue;
                        
                        s = s.Substring(ppl);
                        var ix = s.IndexOf(System.IO.Path.DirectorySeparatorChar);
                        if (ix > 0 && ix != s.Length - 1)
                            s = s.Substring(0, ix + 1);
                        yield return prefix + s;
                    }
            }
            
            public IEnumerable<IFileversion> SelectFolderContents(Library.Utility.IFilter filter)
            {
                var tbname = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    string pathprefix;
                    if (filter == null || filter.Empty)
                        pathprefix = "";
                    else if (filter as Library.Utility.FilterExpression == null || ((Library.Utility.FilterExpression)filter).Type != Duplicati.Library.Utility.FilterType.Simple || ((Library.Utility.FilterExpression)filter).GetSimpleList().Length != 1)
                        throw new ArgumentException("Filter for list-folder-contents must be a path prefix with no wildcards", "filter");
                    else
                        pathprefix = ((Library.Utility.FilterExpression)filter).GetSimpleList().First();
                    
                    if (pathprefix.Length > 0 || Duplicati.Library.Utility.Utility.IsClientLinux)
                        pathprefix = Duplicati.Library.Utility.Utility.AppendDirSeparator(pathprefix);
                    
                    using(var tmpnames = new FilteredFilenameTable(m_connection, new Library.Utility.FilterExpression(pathprefix + "*", true), null))
                    using(var cmd = m_connection.CreateCommand())
                    {
                        //First we trim the filelist to exclude filenames not found in any of the filesets
                        cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{1}"") ) ", tmpnames.Tablename, m_tablename));  
                    
                        // If we had instr support this would work:
                        /*var distinctPaths = @"SELECT DISTINCT :1 || " +
                            @"CASE(INSTR(SUBSTR(""Path"", :2), ""/"")) " +
                            @"WHEN 0 THEN SUBSTR(""Path"", :2) " +
                            @"ELSE SUBSTR(""Path"", :2,  INSTR(SUBSTR(path, :2), ""/"")) " +
                            @"END AS ""Path"", ""FilesetID"" " +
                            @" FROM (" + cartesianPathFileset + @")";*/
                        
                        // Instead we manually iterate the paths
                        cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL)", tbname));
                        
                        using(var c2 = m_connection.CreateCommand())
                        {
                            c2.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"") VALUES (?)", tbname);
                            c2.AddParameter();
                        
                            foreach(var n in SelectFolderEntries(cmd, pathprefix, tmpnames.Tablename).Distinct())
                            {
                                c2.SetParameterValue(0, n);
                                c2.ExecuteNonQuery();
                            }

                            c2.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}_PathIndex"" ON ""{0}"" (""Path"")", tbname));
                        }
                        
                        //Then we select the matching results
                        var filesets = string.Format(@"SELECT ""FilesetID"", ""Timestamp"" FROM ""{0}"" ORDER BY ""Timestamp"" DESC", m_tablename);
                        var cartesianPathFileset = string.Format(@"SELECT ""A"".""Path"", ""B"".""FilesetID"" FROM ""{0}"" A, (" + filesets + @") B ORDER BY ""A"".""Path"" ASC, ""B"".""Timestamp"" DESC", tbname, m_tablename);
                                                
                        var filesWithSizes = @"SELECT ""Length"", ""FilesetEntry"".""FilesetID"", ""File"".""Path"" FROM ""Blockset"", ""FilesetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FileID"" = ""File"".""ID"" ";
                        var query = @"SELECT ""C"".""Path"", ""D"".""Length"", ""C"".""FilesetID"" FROM (" + cartesianPathFileset + @") C LEFT OUTER JOIN (" + filesWithSizes + @") D ON ""C"".""FilesetID"" = ""D"".""FilesetID"" AND ""C"".""Path"" = ""D"".""Path""";
                        
                        cmd.AddParameter(pathprefix, "1");                    
                        cmd.AddParameter(pathprefix.Length + 1, "2");
                        
                        using(var rd = cmd.ExecuteReader(query))
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
                                    
                                } while(more);
                            }
                    }                    
                }
                finally
                {
                    try
                    {
                        using(var c = m_connection.CreateCommand())
                            c.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}""", tbname));
                    }
                    catch
                    {
                    }
                }
            }
            
            public IEnumerable<IFileversion> SelectFiles(Library.Utility.IFilter filter)
            {
                using(var tmpnames = new FilteredFilenameTable(m_connection, filter, null))
                using(var cmd = m_connection.CreateCommand())                
                {
                    //First we trim the filelist to exclude filenames not found in any of the filesets
                    cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" WHERE ""Path"" NOT IN (SELECT DISTINCT ""Path"" FROM ""File"", ""FilesetEntry"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""FilesetEntry"".""FilesetID"" IN (SELECT ""FilesetID"" FROM ""{1}"") ) ", tmpnames.Tablename, m_tablename));  
                
                    //Then we select the matching results
                    var filesets = string.Format(@"SELECT ""FilesetID"", ""Timestamp"" FROM ""{0}"" ORDER BY ""Timestamp"" DESC", m_tablename);
                    var cartesianPathFileset = string.Format(@"SELECT ""A"".""Path"", ""B"".""FilesetID"" FROM ""{0}"" A, (" + filesets + @") B ORDER BY ""A"".""Path"" ASC, ""B"".""Timestamp"" DESC", tmpnames.Tablename, m_tablename);
                    var filesWithSizes = @"SELECT ""Length"", ""FilesetEntry"".""FilesetID"", ""File"".""Path"" FROM ""Blockset"", ""FilesetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FileID"" = ""File"".""ID"" ";
                    var query = @"SELECT ""C"".""Path"", ""D"".""Length"", ""C"".""FilesetID"" FROM (" + cartesianPathFileset + @") C LEFT OUTER JOIN (" + filesWithSizes + @") D ON ""C"".""FilesetID"" = ""D"".""FilesetID"" AND ""C"".""Path"" = ""D"".""Path""";
                    using(var rd = cmd.ExecuteReader(query))
                        if(rd.Read())
                        {
                            bool more;
                            do
                            {
                                var f = new Fileversion(rd);
                                yield return f;
                                more = f.More;
                            } while(more);
                        }
                }
            }
            
            public void TakeFirst()
            {
                using(var cmd = m_connection.CreateCommand())
                    cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" WHERE ""FilesetID"" NOT IN (SELECT ""FilesetID"" FROM ""{0}"" ORDER BY ""Timestamp"" DESC LIMIT 1 )", m_tablename));
            }

            public IEnumerable<IFileset> QuickSets
            {
                get
                {
                    var dict = new Dictionary<long, long>();
                    for(var i = 0; i < m_filesets.Length; i++)
                        dict[m_filesets[i].Key] = i;

                    using(var cmd = m_connection.CreateCommand())
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC ", m_tablename)))
                        while (rd.Read())
                        {
                            var id = rd.GetInt64(0);
                            var e = dict[id];
                            
                            yield return new Fileset(e, m_filesets[e].Value, -1L, -1L);
                        }
                }
            }

            public IEnumerable<IFileset> Sets 
            { 
                get
                {
                    var dict = new Dictionary<long, long>();
                    for(var i = 0; i < m_filesets.Length; i++)
                        dict[m_filesets[i].Key] = i;
                    
                    var summation = @"SELECT ""A"".""FilesetID"" AS ""FilesetID"", COUNT(*) AS ""FileCount"", SUM(""C"".""Length"") AS ""FileSizes"" FROM ""FilesetEntry"" A, ""File"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" GROUP BY ""A"".""FilesetID"" ";
                    
                    using(var cmd = m_connection.CreateCommand())
                    using (var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""A"".""FilesetID"", ""B"".""FileCount"", ""B"".""FileSizes"" FROM ""{0}"" A LEFT OUTER JOIN ( " + summation + @" ) B ON ""A"".""FilesetID"" = ""B"".""FilesetID"" ORDER BY ""A"".""Timestamp"" DESC ", m_tablename)))
                        while(rd.Read())
                        {
                            var id = rd.GetInt64(0);
                            var e = dict[id];
                            
                            var filecount = rd.ConvertValueToInt64(1, -1L);
                            var filesizes = rd.ConvertValueToInt64(2, -1L);

                            yield return new Fileset(e, m_filesets[e].Value, filecount, filesizes);
                        }
                    
                }
            }
            
            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try 
                    { 
                        using(var cmd = m_connection.CreateCommand())
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_tablename));
                    }
                    catch {}
                    finally { m_tablename = null; }
                }
                
            }
        }
        
        public IFileSets SelectFileSets(DateTime time, long[] versions)
        {
            return new FileSets(this, time, versions);
        }
        
    }
}

