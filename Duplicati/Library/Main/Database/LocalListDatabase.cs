//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
            : base(path, "List")
        {
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
            IEnumerable<IFileversion> SelectFiles(Library.Utility.IFilter filter);
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
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""ID"" AS ""FilesetID"", ""Timestamp"" AS ""Timestamp"" FROM ""Fileset"" " + query, m_tablename), args);
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
                            var val = m_reader.GetValue(1);
                            yield return val == null || val == DBNull.Value ? -1 : Convert.ToInt64(val);
                            this.More = m_reader.Read();
                        }
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
                            var id = Convert.ToInt64(rd.GetValue(0));
                            var e = dict[id];
                            
                            var filecount = -1L;
                            var filesizes = -1L;
                            if (rd.GetValue(1) != null && rd.GetValue(1) != DBNull.Value) 
                                filecount = Convert.ToInt64(rd.GetValue(1));
                            if (rd.GetValue(2) != null && rd.GetValue(2) != DBNull.Value) 
                                filesizes = Convert.ToInt64(rd.GetValue(2));
                            
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

