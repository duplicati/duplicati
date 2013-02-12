using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public partial class LocalBlocklistUpdateDatabase : LocalRestoredatabase
    {
        protected string m_tempblockvolumetable;

        public LocalBlocklistUpdateDatabase(string path, long blocksize)
            : base("Rebuild", path, blocksize)
        {
        }

        public void FindMissingBlocklistHashes()
        {
            m_tempblockvolumetable = "MissingBlocks-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            using (var cmd = m_connection.CreateCommand())
            {
                //cmd.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""Block"".""File"" AS ""File"", ""BlocklistHash"".""Hash"" AS ""Hash"", 0 AS ""Restored"" FROM ""Block"", ""BlocklistHash""  WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" AND ""BlocklistHash"".""Hash"" NOT IN ( SELECT DISTINCT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"", ""Blockset"" WHERE ""BlocklistHash"".""Hash"" = ""Blockset"".""Fullhash"" AND ""Blockset"".""Length"" <= {1} AND ""BlocklistHash"".""Index"" = 0 ) ", m_tempblockvolumetable, m_blocksize);
                cmd.CommandText = string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT DISTINCT ""Block"".""File"" AS ""File"", ""BlocklistHash"".""Hash"" AS ""Hash"", 0 AS ""Restored"" FROM ""Block"", ""BlocklistHash""  WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" ", m_tempblockvolumetable);
                cmd.ExecuteNonQuery();

                cmd.CommandText = string.Format(@"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT DISTINCT ""Blockset"".""ID"", 0, ""Block"".""ID"" FROM ""Blockset"", ""Block"" WHERE ""Blockset"".""Fullhash"" = ""Block"".""Hash"" AND ""Blockset"".""Length"" < {0} AND ""Blockset"".""ID"" NOT IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"") ", m_blocksize);
                cmd.ExecuteNonQuery();
            }
        }

        public IEnumerable<string> GetBlockLists(string volumename)
        {
            return new BlocklistsEnumerable(m_connection, m_tempblockvolumetable, volumename);
        }

        public System.Data.IDbTransaction BeginTransaction()
        {
            return m_connection.BeginTransaction();
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

                cmd.CommandText = @"SELECT ""Index"", ""BlocksetID"" FROM ""BlocklistHash"" WHERE ""Hash"" = ?";
                long index;
                long id;
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read())
                        throw new Exception(string.Format("No blocklisthashes for entry \"{0}\"", hash));

                    index = Convert.ToInt64(rd.GetValue(0));
                    id = Convert.ToInt64(rd.GetValue(1));
                }

                var ix = index * (m_blocksize / hashsize);
                cmd.CommandText = @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT ?, ?, ""ID"" FROM ""Block"" WHERE ""Hash"" = ?";
                cmd.SetParameterValue(0, id);
                cmd.AddParameters(2);

                foreach (var h in hashes)
                {
                    cmd.SetParameterValue(1, ix);
                    cmd.SetParameterValue(2, h);
                    c = cmd.ExecuteNonQuery();
                    if (c != 1)
                        throw new Exception(string.Format("Block not found {0}", h));

                    ix++;
                }
            }
        }

        public IList<IRemoteVolume> GetMissingBlockListVolumes()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                List<IRemoteVolume> result = new List<IRemoteVolume>();
                cmd.CommandText = string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"" WHERE ""Name"" IN (SELECT DISTINCT ""File"" FROM ""{0}"")", m_tempblockvolumetable);
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
            }
        }
    }
}
