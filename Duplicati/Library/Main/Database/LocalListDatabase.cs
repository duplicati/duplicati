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
    public class LocalListDatabase : LocalDatabase
    {
        public LocalListDatabase(string path)
            : base(path, "List")
        {
        }
        
        public IEnumerable<KeyValuePair<long, DateTime>> FilesetTimes
        { 
            get 
            {
                using(var cmd = m_connection.CreateCommand())
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC"))
                    while (rd.Read())
                        yield return new KeyValuePair<long, DateTime>(Convert.ToInt64(rd.GetValue(0)), Convert.ToDateTime(rd.GetValue(1)).ToLocalTime());
            }
        }
        
        public interface IFileversion
        {
            string Path { get; }
            IEnumerable<long> Sizes { get; }
        }
                
        public interface IFileSets : IDisposable
        {
            IEnumerable<KeyValuePair<long, DateTime>> Times { get; }
            IEnumerable<IFileversion> SelectFiles(FilterExpression filter);
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
                string query = "";
                var args = new List<object>();
                if (time.Ticks > 0 || (versions != null && versions.Length > 0))
                {
                    query = " WHERE ";
                    if (time.Ticks > 0)
                    {
                        if (time.Kind == DateTimeKind.Unspecified)
                            throw new Exception("Invalid DateTime given, must be either local or UTC");
                            
                        query += @" ""Timestamp"" < ?";
                        args.Add(time.ToUniversalTime());
                    }
                    
                    if (versions != null && versions.Length > 0)
                    {
                        var qs ="";
                        
                        foreach(var v in versions)
                            if (v >= 0 && v < m_filesets.Length)
                            {
                                args.Add(m_filesets[v].Key);
                                qs += "?,";
                            }
                            
                        if (qs.Length > 0)
                        {
                            qs = qs.Substring(0, qs.Length - 1);
                            
                            if (args.Count != 0)
                                query += " AND ";
                                                
                            query += @" ""ID"" IN (" + qs + ")";
                        }
                    }
                }   
                
                using(var cmd = m_connection.CreateCommand())
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""{0}"" AS SELECT DISTINCT ""ID"" AS ""FilesetID"", ""Timestamp"" AS ""Timestamp"" FROM ""Fileset"" " + query, m_tablename), args.ToArray());
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
            
            public IEnumerable<IFileversion> SelectFiles(FilterExpression filter)
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
            
            public IEnumerable<KeyValuePair<long, DateTime>> Times 
            { 
                get
                {
                    var dict = new Dictionary<long, long>();
                    for(var i = 0; i < m_filesets.Length; i++)
                        dict[m_filesets[i].Key] = i;
                    
                    using(var cmd = m_connection.CreateCommand())
                    using (var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""FilesetID"" FROM ""{0}"" ORDER BY ""Timestamp"" DESC ", m_tablename)))
                        while(rd.Read())
                        {
                            var id = Convert.ToInt64(rd.GetValue(0));
                            var e = dict[id];
                            
                            yield return new KeyValuePair<long, DateTime>(e, m_filesets[e].Value);
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
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", m_tablename));
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

