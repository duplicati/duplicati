using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public partial class LocalBlocklistUpdateDatabase : LocalRestoreDatabase
    {
        protected string m_tempblockvolumetable;
        protected string m_temphashtable;

        public LocalBlocklistUpdateDatabase(string path, long blocksize)
            : base(new LocalDatabase(path, "Rebuild"), blocksize)
        {
        }

        public LocalBlocklistUpdateDatabase(LocalDatabase parentdb, long blocksize)
            : base(parentdb, blocksize)
        {
        }

        public void FindMissingBlocklistHashes()
        {
            m_tempblockvolumetable = "MissingBlocks-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            m_temphashtable = "Hashlist-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""Block"".""VolumeID"" AS ""VolumeID"", ""BlocklistHash"".""Hash"" AS ""Hash"", 0 AS ""Restored"" FROM ""Block"", ""BlocklistHash""  WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" ", m_tempblockvolumetable));
                cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT DISTINCT ""Blockset"".""ID"", 0, ""Block"".""ID"" FROM ""Blockset"", ""Block"" WHERE ""Blockset"".""Fullhash"" = ""Block"".""Hash"" AND ""Blockset"".""Length"" < {0} AND ""Blockset"".""ID"" NOT IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"") ", m_blocksize));
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Hash"" TEXT NOT NULL, ""Index"" INTEGER NOT NULL)", m_temphashtable));

            }
        }

        public IEnumerable<string> GetBlockLists(long volumeid)
        {
            return new BlocklistsEnumerable(m_connection, m_tempblockvolumetable, volumeid);
        }

        public void UpdateBlocklist(string hash, IEnumerable<string> hashes, long hashsize, System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;

                cmd.CommandText = string.Format(@"UPDATE ""{0}"" SET ""Restored"" = 1 WHERE ""Restored"" = 0 AND ""Hash"" = ? ", m_tempblockvolumetable);
                cmd.AddParameter(hash);
                var c = cmd.ExecuteNonQuery();
                if (c != 1)
                    throw new Exception(string.Format("Blocklist not found {0}", hash));

                cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}""", m_temphashtable));
                cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Hash"", ""Index"") VALUES (?, ?)", m_temphashtable);
                long ix = 0;
                foreach (var h in hashes)
                {
                    c = cmd.ExecuteNonQuery(null, h, ix);
                    if (c != 1)
                        throw new Exception(string.Format("Insert row error {0}", h));
                    ix++;
                }

                cmd.CommandText = string.Format(@"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT ""A"".""BlocksetID"", (""A"".""Index"" * ?) + ""B"".""Index"", ""C"".""ID"" FROM ""BlocklistHash"" A, ""{0}"" B, ""Block"" C WHERE ""A"".""Hash"" = ? AND ""C"".""Hash"" = ""B"".""Hash"" ", m_temphashtable);
                c = cmd.ExecuteNonQuery(null, (m_blocksize / hashsize), hash);
                if (c == 0 || c % ix != 0)
                    throw new Exception(string.Format("Wrong number of inserts, got {0} records from {1} hashes!", c, ix));
            }
        }

        public IList<IRemoteVolume> GetMissingBlockListVolumes()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                List<IRemoteVolume> result = new List<IRemoteVolume>();
                cmd.CommandText = string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT DISTINCT ""VolumeID"" FROM ""{0}"")", m_tempblockvolumetable);
                using (var rd = cmd.ExecuteReader())
                {
                    object[] r = new object[3];
                    while (rd.Read())
                    {
                        rd.GetValues(r);
                        result.Add(new RemoteVolume(
                            r[0] == null ? null : r[0].ToString(),
                            r[1] == null ? null : r[1].ToString(),
                            r[2] == null ? -1 : Convert.ToInt64(r[2])
                        ));
                    }
                }

                return result;
            }
        }

        public void VerifyDatabaseIntegrity()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM (SELECT SUM(""Block"".""Size"") AS ""CalcLen"", ""Blockset"".""Length"" AS ""Length"", ""BlocksetEntry"".""BlocksetID"" FROM ""Block"", ""BlocksetEntry"", ""Blockset"" WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Blockset"".""ID"" = ""BlocksetEntry"".""BlocksetID"" GROUP BY ""BlocksetEntry"".""BlocksetID"") WHERE ""CalcLen"" != ""Length""";
                var c = Convert.ToInt64(cmd.ExecuteScalar());
                if (c != 0)
                    throw new InvalidDataException("Inconsistency detected, not all blocklists were restored correctly");
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_tempblockvolumetable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE ""{0}""", m_tempblockvolumetable);
                        cmd.ExecuteNonQuery();
                    }
                    finally { m_tempblockvolumetable = null; }

                if (m_temphashtable != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE ""{0}""", m_temphashtable);
                        cmd.ExecuteNonQuery();
                    }
                    finally { m_temphashtable = null; }
            }
        }
    }
}
