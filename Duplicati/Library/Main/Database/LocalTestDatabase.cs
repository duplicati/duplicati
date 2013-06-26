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
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    public class LocalTestDatabase : LocalDatabase
    {
        public LocalTestDatabase(string path)
            : base(LocalDatabase.CreateConnection(path), "Test")
        {        
        }
        
        private class RemoteVolume : IRemoteVolume
        {
            public long ID { get; private set; }
            public string Name { get; private set; }
            public long Size { get; private set; }
            public string Hash { get; private set; }
            
            public RemoteVolume(System.Data.IDataReader rd)
            {
                this.ID = Convert.ToInt64(rd.GetValue(0));
                this.Name = rd.GetValue(1).ToString();
                this.Size = Convert.ToInt64(rd.GetValue(2));
                this.Hash = rd.GetValue(3).ToString();
            }
        }
        
        public IEnumerable<IRemoteVolume> SelectTestTargets(long samples, Options options)
        {
            var tp = GetFilelistWhereClause(options.Time, options.Version);
            var rnd = new Random();
            
            samples = Math.Max(1, samples);
            using(var cmd = m_connection.CreateCommand())
            {
                //First we select some filesets
                var files = new List<RemoteVolume>();
                var whereClause = string.IsNullOrEmpty(tp.Item1) ? " WHERE " : (" " + tp.Item1 + " AND ");
                using(var rd = cmd.ExecuteReader(@"SELECT ""A"".""VolumeID"", ""A"".""Name"", ""A"".""Size"", ""A"".""Hash"" FROM (SELECT ""ID"" AS ""VolumeID"", ""Name"", ""Size"", ""Hash"" FROM ""Remotevolume"") A, ""Fileset"" " +  whereClause + @" ""A"".""VolumeID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp"" " , tp.Item2))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                        
                if (files.Count == 0)
                    yield break;

                if (string.IsNullOrEmpty(tp.Item1))
                {
                    //No explicit fileset(s) selected, choose some samples
                    while (files.Count > samples)
                        files.RemoveAt(rnd.Next(files.Count));
                }
                
                foreach(var f in files)
                    yield return f;
                
                //Then we select some index files
                files.Clear();
                
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"" FROM ""Remotevolume"" WHERE ""Type"" = ? ", RemoteVolumeType.Index.ToString()))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                        
                while (files.Count > samples)
                    files.RemoveAt(rnd.Next(files.Count));
                    
                foreach(var f in files)
                    yield return f;
                //And finally some block files
                files.Clear();
                
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"" FROM ""Remotevolume"" WHERE ""Type"" = ? ", RemoteVolumeType.Blocks.ToString()))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                        
                while (files.Count > samples)
                    files.RemoveAt(rnd.Next(files.Count));
                    
                foreach(var f in files)
                    yield return f;
            }
        }
        
        private abstract class Basiclist : IDisposable
        {
              protected System.Data.IDbConnection m_connection;
              protected string m_volumename;
              protected string m_tablename;
              protected System.Data.IDbTransaction m_transaction;
              protected System.Data.IDbCommand m_insertCommand;
              protected abstract string TABLE_PREFIX { get; }
              protected abstract string TABLEFORMAT { get; }
              protected abstract string INSERTCOMMAND { get; }
              protected abstract int INSERTARGUMENTS { get; }
              
              public Basiclist(System.Data.IDbConnection connection, string volumename)
              {
                m_connection = connection;
                m_volumename = volumename;
                m_transaction = m_connection.BeginTransaction();
                var tablename = TABLE_PREFIX + "-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" {1}", tablename, TABLEFORMAT));
                    m_tablename = tablename;
                }
                
                m_insertCommand = m_connection.CreateCommand();
                m_insertCommand.Transaction = m_transaction;
                m_insertCommand.CommandText = string.Format(@"INSERT INTO ""{0}"" {1}", m_tablename, INSERTCOMMAND);
                m_insertCommand.AddParameters(INSERTARGUMENTS);
            }

            public virtual void Dispose()
            {
                if (m_tablename != null)
                    try 
                    { 
                        using(var cmd = m_connection.CreateCommand())
                        {
                            cmd.Transaction = m_transaction;
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}""", m_tablename));
                        }
                    }
                    catch {}
                    finally { m_tablename = null; }
                    
                if (m_insertCommand != null)
                    try { m_insertCommand.Dispose(); }
                    catch {}
                    finally { m_insertCommand = null; }
                    
                if (m_transaction != null)
                    try { m_transaction.Rollback(); }
                    catch {}
                    finally { m_transaction = null; }
            }
        }
        
        public interface IFilelist : IDisposable
        {
            void Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time);
            IEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }
        
        private class Filelist : Basiclist, IFilelist
        {
            protected override string TABLE_PREFIX { get { return "Filelist"; } }
            protected override string TABLEFORMAT { get { return @"(""Path"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Hash"" TEXT NULL, ""Metasize"" INTEGER NOT NULL, ""Metahash"" TEXT NOT NULL)"; } }
            protected override string INSERTCOMMAND { get { return @"(""Path"", ""Size"", ""Hash"", ""Metasize"", ""Metahash"") VALUES (?,?,?,?,?)"; } }
            protected override int INSERTARGUMENTS { get { return 5; } }

            public Filelist(System.Data.IDbConnection connection, string volumename)
                : base(connection, volumename)
            { 
            }
            
            public void Add(string path, long size, string hash, long metasize, string metahash, IEnumerable<string> blocklistHashes, FilelistEntryType type, DateTime time)
            {
                m_insertCommand.SetParameterValue(0, path);
                m_insertCommand.SetParameterValue(1, hash == null ? - 1: size);
                m_insertCommand.SetParameterValue(2, hash);
                m_insertCommand.SetParameterValue(3, metasize);
                m_insertCommand.SetParameterValue(4, metahash);
                m_insertCommand.ExecuteNonQuery();
            }
            
            public IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                
                var create = @"CREATE TEMPORARY TABLE ""{1}"" AS SELECT ""A"".""Path"" AS ""Path"", CASE WHEN ""B"".""Fullhash"" IS NULL THEN -1 ELSE ""B"".""Length"" END AS ""Size"", ""B"".""Fullhash"" AS ""Hash"", ""C"".""Length"" AS ""Metasize"", ""C"".""Fullhash"" AS ""Metahash"" FROM (SELECT ""File"".""Path"", ""File"".""BlocksetID"" AS ""FileBlocksetID"", ""Metadataset"".""BlocksetID"" AS ""MetadataBlocksetID"" from ""Remotevolume"", ""Fileset"", ""FilesetEntry"", ""File"", ""Metadataset"" WHERE ""Remotevolume"".""Name"" = ? AND ""Fileset"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""File"".""MetadataID"" = ""Metadataset"".""ID"") A LEFT OUTER JOIN ""Blockset"" B ON ""B"".""ID"" = ""A"".""FileBlocksetID"" LEFT OUTER JOIN ""Blockset"" C ON ""C"".""ID""=""A"".""MetadataBlocksetID"" ";
                var extra = @"SELECT ? AS ""Type"", ""{0}"".""Path"" AS ""Path"" FROM ""{0}"" WHERE ""{0}"".""Path"" NOT IN ( SELECT ""Path"" FROM ""{1}"" )";
                var missing = @"SELECT ? AS ""Type"", ""Path"" AS ""Path"" FROM ""{1}"" WHERE ""Path"" NOT IN (SELECT ""Path"" FROM ""{0}"")";
                var modified = @"SELECT ? AS ""Type"", ""E"".""Path"" AS ""Path"" FROM ""{0}"" E, ""{1}"" D WHERE ""D"".""Path"" = ""E"".""Path"" AND (""D"".""Size"" != ""E"".""Size"" OR ""D"".""Hash"" != ""E"".""Hash"" OR ""D"".""Metasize"" != ""E"".""Metasize"" OR ""D"".""Metahash"" != ""E"".""Metahash"")  ";
                var drop = @"DROP TABLE ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, m_tablename, cmpName), m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)Convert.ToInt64(rd.GetValue(0)), rd.GetValue(1).ToString() );
                        
                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(string.Format(drop, m_tablename, cmpName)); }
                        catch {}
                    }
                }
            }
        }
        
        public interface IIndexlist: IDisposable
        {
            void AddBlockLink(string filename, string hash, long length);
            IEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }
        
        private class Indexlist : Basiclist, IIndexlist
        {
            protected override string TABLE_PREFIX { get { return "Indexlist"; } }
            protected override string TABLEFORMAT { get { return @"(""Name"" TEXT NOT NULL, ""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)"; } }
            protected override string INSERTCOMMAND { get { return @"(""Name"", ""Hash"", ""Size"") VALUES (?,?,?)"; } }
            protected override int INSERTARGUMENTS { get { return 3; } }
            
            public Indexlist(System.Data.IDbConnection connection, string volumename)
                : base(connection, volumename)
            {
            }
                    
            public void AddBlockLink(string filename, string hash, long length)
            {
                m_insertCommand.SetParameterValue(0, filename);
                m_insertCommand.SetParameterValue(1, hash);
                m_insertCommand.SetParameterValue(2, length);
                m_insertCommand.ExecuteNonQuery();
            }
            
            public IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var create = @"CREATE TEMPORARY TABLE ""{1}"" AS SELECT ""A"".""Name"", ""A"".""Hash"", ""A"".""Size"" FROM ""Remotevolume"" A, ""Remotevolume"" B, ""IndexBlockLink"" WHERE ""B"".""Name"" = ? AND ""A"".""ID"" = ""IndexBlockLink"".""BlockVolumeID"" AND ""B"".""ID"" = ""IndexBlockLink"".""IndexVolumeID"" ";
                var extra = @"SELECT ? AS ""Type"", ""{0}"".""Name"" AS ""Name"" FROM ""{0}"" WHERE ""{0}"".""Name"" NOT IN ( SELECT ""Name"" FROM ""{1}"" )";
                var missing = @"SELECT ? AS ""Type"", ""Name"" AS ""Name"" FROM ""{1}"" WHERE ""Name"" NOT IN (SELECT ""Name"" FROM ""{0}"")";
                var modified = @"SELECT ? AS ""Type"", ""E"".""Name"" AS ""Name"" FROM ""{0}"" E, ""{1}"" D WHERE ""D"".""Name"" = ""E"".""Name"" AND (""D"".""Hash"" != ""E"".""Hash"" OR ""D"".""Size"" != ""E"".""Size"") ";
                var drop = @"DROP TABLE ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, m_tablename, cmpName), m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)Convert.ToInt64(rd.GetValue(0)), rd.GetValue(1).ToString() );
                        
                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(string.Format(drop, m_tablename, cmpName)); }
                        catch {}
                    }
                }
            }
        }
        
        public interface IBlocklist: IDisposable
        {
            void AddBlock(string key, long value);
            IEnumerable<KeyValuePair<Library.Interface.TestEntryStatus, string>> Compare();
        }

       private class Blocklist : Basiclist, IBlocklist
       {
            protected override string TABLE_PREFIX { get { return "Blocklist"; } }
            protected override string TABLEFORMAT { get { return @"(""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)"; } }
            protected override string INSERTCOMMAND { get { return @"(""Hash"", ""Size"") VALUES (?,?)"; } }
            protected override int INSERTARGUMENTS { get { return 2; } }
            
            public Blocklist(System.Data.IDbConnection connection, string volumename)
                : base(connection, volumename)
            { }            
        
            public void AddBlock(string hash, long size)
            {
                m_insertCommand.SetParameterValue(0, hash);
                m_insertCommand.SetParameterValue(1, size);
                m_insertCommand.ExecuteNonQuery();
            }
            
            public IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> Compare()
            {
                var cmpName = "CmpTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var create = @"CREATE TEMPORARY TABLE ""{1}"" AS SELECT ""Block"".""Hash"" AS ""Hash"", ""Block"".""Size"" AS ""Size"" FROM ""Remotevolume"", ""Block"" WHERE ""Remotevolume"".""Name"" = ? AND ""Remotevolume"".""ID"" = ""Block"".""VolumeID"" ";
                var extra = @"SELECT ? AS ""Type"", ""{0}"".""Hash"" AS ""Hash"" FROM ""{0}"" WHERE ""{0}"".""Hash"" NOT IN ( SELECT ""Hash"" FROM ""{1}"" )";
                var missing = @"SELECT ? AS ""Type"", ""Hash"" AS ""Hash"" FROM ""{1}"" WHERE ""Hash"" NOT IN (SELECT ""Hash"" FROM ""{0}"")";
                var modified = @"SELECT ? AS ""Type"", ""E"".""Hash"" AS ""Hash"" FROM ""{0}"" E, ""{1}"" D WHERE ""D"".""Hash"" = ""E"".""Hash"" AND (""D"".""Size"" != ""E"".""Size"")  ";
                var drop = @"DROP TABLE ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, m_tablename, cmpName), m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)Convert.ToInt64(rd.GetValue(0)), rd.GetValue(1).ToString() );
                        
                    }
                    finally
                    {
                        try { cmd.ExecuteNonQuery(string.Format(drop, m_tablename, cmpName)); }
                        catch {}
                    }
                }
            }
        }
        
        public IFilelist CreateFilelist(string name)
        {
            return new Filelist(m_connection, name);
        }

        public IIndexlist CreateIndexlist(string name)
        {
            return new Indexlist(m_connection, name);
        }

        public IBlocklist CreateBlocklist(string name)
        {
            return new Blocklist(m_connection, name);
        }
    }
}

