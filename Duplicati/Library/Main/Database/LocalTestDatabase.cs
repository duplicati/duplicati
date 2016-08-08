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

namespace Duplicati.Library.Main.Database
{
    internal class LocalTestDatabase : LocalDatabase
    {
        public LocalTestDatabase(string path)
            : base(path, "Test", true)
        {        
        }
        
        public LocalTestDatabase(LocalDatabase parent)
            : base(parent)
        {        
        }
        
        public void UpdateVerificationCount(string name)
        {
            using(var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery(@"UPDATE ""RemoteVolume"" SET ""VerificationCount"" = MAX(1, CASE WHEN ""VerificationCount"" <= 0 THEN (SELECT MAX(""VerificationCount"") FROM ""RemoteVolume"") ELSE ""VerificationCount"" + 1 END) WHERE ""Name"" = ?", name);
        }
        
        private class RemoteVolume : IRemoteVolume
        {
            public long ID { get; private set; }
            public string Name { get; private set; }
            public long Size { get; private set; }
            public string Hash { get; private set; }
            public long VerificationCount { get; private set; }
            
            public RemoteVolume(System.Data.IDataReader rd)
            {
                this.ID = rd.GetInt64(0);
                this.Name = rd.GetString(1);
                this.Size = rd.ConvertValueToInt64(2);
                this.Hash = rd.ConvertValueToString(3);
                this.VerificationCount = rd.ConvertValueToInt64(4);
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
            
                for(var i = min; i <= max; i++)
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
            using(var cmd = m_connection.CreateCommand())
            {
                // Select any broken items
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE (""State"" = ? OR ""State"" = ?) AND (""Hash"" = """" OR ""Hash"" IS NULL OR ""Size"" < 0) ", RemoteVolumeState.Verified.ToString(), RemoteVolumeState.Uploaded.ToString()))
                    while (rd.Read())
                        yield return new RemoteVolume(rd);

                //Grab the max value
                var max = cmd.ExecuteScalarInt64(@"SELECT MAX(""VerificationCount"") FROM ""RemoteVolume""", 0);
            
                //First we select some filesets
                var files = new List<RemoteVolume>();
                var whereClause = string.IsNullOrEmpty(tp.Item1) ? " WHERE " : (" " + tp.Item1 + " AND ");
                using(var rd = cmd.ExecuteReader(@"SELECT ""A"".""VolumeID"", ""A"".""Name"", ""A"".""Size"", ""A"".""Hash"", ""A"".""VerificationCount"" FROM (SELECT ""ID"" AS ""VolumeID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""State"" IN (?, ?)) A, ""Fileset"" " +  whereClause + @" ""A"".""VolumeID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp"" ", RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString(), tp.Item2))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                        
                if (files.Count == 0)
                    yield break;

                if (string.IsNullOrEmpty(tp.Item1))
                    files = FilterByVerificationCount(files, samples, max).ToList();
                
                foreach(var f in files)
                    yield return f;
                
                //Then we select some index files
                files.Clear();
                
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""Type"" = ? AND ""State"" IN (?, ?)", RemoteVolumeType.Index.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                                            
                foreach(var f in FilterByVerificationCount(files, samples, max))
                    yield return f;

                //And finally some block files
                files.Clear();
                
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Name"", ""Size"", ""Hash"", ""VerificationCount"" FROM ""Remotevolume"" WHERE ""Type"" = ? AND ""State"" IN (?, ?)", RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                    while (rd.Read())
                        files.Add(new RemoteVolume(rd));
                        
                foreach(var f in FilterByVerificationCount(files, samples, max))
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
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tablename));
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
                var drop = @"DROP TABLE IF EXISTS ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, m_tablename, cmpName), m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)rd.GetInt64(0), rd.GetString(1));
                        
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
                var drop = @"DROP TABLE IF EXISTS ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, m_tablename, cmpName), m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)rd.GetInt64(0), rd.GetString(1) );
                        
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
                var curBlocks = @"SELECT ""Block"".""Hash"" AS ""Hash"", ""Block"".""Size"" AS ""Size"" FROM ""Remotevolume"", ""Block"" WHERE ""Remotevolume"".""Name"" = ? AND ""Remotevolume"".""ID"" = ""Block"".""VolumeID""";
                var delBlocks = @"SELECT ""DeletedBlock"".""Hash"" AS ""Hash"", ""DeletedBlock"".""Size"" AS ""Size"" FROM ""DeletedBlock"", ""RemoteVolume"" WHERE ""RemoteVolume"".""Name"" = ? AND ""RemoteVolume"".""ID"" = ""DeletedBlock"".""VolumeID""";
                var create = @"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""Hash"" AS ""Hash"", ""Size"" AS ""Size"" FROM ({1} UNION {2})";
                var extra = @"SELECT ? AS ""Type"", ""{0}"".""Hash"" AS ""Hash"" FROM ""{0}"" WHERE ""{0}"".""Hash"" NOT IN ( SELECT ""Hash"" FROM ""{1}"" )";
                var missing = @"SELECT ? AS ""Type"", ""Hash"" AS ""Hash"" FROM ""{1}"" WHERE ""Hash"" NOT IN (SELECT ""Hash"" FROM ""{0}"")";
                var modified = @"SELECT ? AS ""Type"", ""E"".""Hash"" AS ""Hash"" FROM ""{0}"" E, ""{1}"" D WHERE ""D"".""Hash"" = ""E"".""Hash"" AND ""D"".""Size"" != ""E"".""Size""  ";
                var drop = @"DROP TABLE IF EXISTS ""{1}"" ";
                
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction;
                    
                    try
                    {
                        cmd.ExecuteNonQuery(string.Format(create, cmpName, curBlocks, delBlocks), m_volumename, m_volumename);
                        using(var rd = cmd.ExecuteReader(string.Format(extra + " UNION " + missing + " UNION " + modified, m_tablename, cmpName), (int)Library.Interface.TestEntryStatus.Extra, (int)Library.Interface.TestEntryStatus.Missing, (int)Library.Interface.TestEntryStatus.Modified))
                            while(rd.Read())
                                yield return new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>((Duplicati.Library.Interface.TestEntryStatus)rd.GetInt64(0), rd.GetString(1) );
                        
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

