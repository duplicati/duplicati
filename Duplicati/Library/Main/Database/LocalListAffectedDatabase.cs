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
    internal class LocalListAffectedDatabase : LocalDatabase
    {
        public LocalListAffectedDatabase(string path)
            : base(path, "ListAffected", false)
        {
            ShouldCloseConnection = true;
        }

        private class ListResultFileset : Duplicati.Library.Interface.IListResultFileset
        {
            public long Version { get; set; }
            public DateTime Time { get; set; }
            public long FileCount { get; set; }
            public long FileSizes { get; set; }
        }

        private class ListResultFile : Duplicati.Library.Interface.IListResultFile
        {
            public string Path { get; set; }
            public IEnumerable<long> Sizes { get; set; }
        }

        private class ListResultRemoteLog : Duplicati.Library.Interface.IListResultRemoteLog
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; }
        }

        private class ListResultRemoteVolume : Duplicati.Library.Interface.IListResultRemoteVolume
        {
            public string Name { get; set; }
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultFileset> GetFilesets(IEnumerable<string> items)
        {
            var filesets = FilesetTimes.ToArray();
            var dict = new Dictionary<long, long>();
            for(var i = 0; i < filesets.Length; i++)
                dict[filesets[i].Key] = i;

            var sql = string.Format(
                @"SELECT DISTINCT ""FilesetID"" FROM (" +
                @"SELECT ""FilesetID"" FROM ""FilesetEntry"" WHERE ""FileID"" IN ( SELECT ""ID"" FROM ""File"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN ( SELECT ""ID"" From ""Block"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0})))))" +
                " UNION " +
                @"SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))" +
                ")",
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(items);
            it.AddRange(items);

            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                {
                    var v = dict[rd.GetInt64(0)];
                    yield return new ListResultFileset() {
                        Version = v,
                        Time = filesets[v].Value
                    };
                }
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultFile> GetFiles(IEnumerable<string> items)
        {
            var sql = string.Format(
                @"SELECT DISTINCT ""Path"" FROM (" +
                @"SELECT ""Path"" FROM ""File"" WHERE ""BlocksetID"" IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT ""ID"" from ""RemoteVolume"" WHERE ""Name"" IN ({0}))))" +
                @" UNION " +
                @"SELECT ""Path"" FROM ""File"" WHERE ""MetadataID"" IN (SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" IN (SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" IN (SELECT ""ID"" from ""RemoteVolume"" WHERE ""Name"" IN ({0})))))" +
                @" UNION " +
                @"SELECT ""Path"" FROM ""File"" WHERE ""ID"" IN ( SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" IN ( SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))))" +
                @") ORDER BY ""Path"" ",
                string.Join(",", items.Select(x => "?"))
            );
                
            var it = new List<string>(items);
            it.AddRange(items);
            it.AddRange(items);

            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultFile() {
                        Path = Convert.ToString(rd.GetValue(0))
                    };
            
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultRemoteLog> GetLogLines(IEnumerable<string> items)
        {
            var sql = string.Format(
                @"SELECT ""TimeStamp"", ""Message"" || "" "" || CASE WHEN ""Exception"" IS NULL THEN """" ELSE ""Exception"" END FROM ""LogData"" WHERE {0}" +
                @" UNION " +
                @"SELECT ""Timestamp"", ""Data"" FROM ""RemoteOperation"" WHERE ""Path"" IN ({1})",
                string.Join(" OR ", items.Select(x => @"""Message"" LIKE ?")),
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(from n in items select "%" + n + "%");
            it.AddRange(items);

            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultRemoteLog() {
                        Timestamp = ParseFromEpochSeconds(rd.GetInt64(0)),
                        Message = rd.GetString(1)
                    };
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultRemoteVolume> GetVolumes(IEnumerable<string> items)
        {
            var sql = string.Format(
                @"SELECT DISTINCT ""Name"" FROM ( " +
                @" SELECT ""Name"" FROM ""Remotevolume"" WHERE ""ID"" IN ( SELECT ""VolumeID"" FROM ""Block"" WHERE ""ID"" IN ( SELECT ""BlockID"" FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""File"" WHERE ""ID"" IN ( SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" IN ( SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))))))) " +
                @" UNION " +
                @" SELECT ""Name"" FROM ""Remotevolume"" WHERE ""ID"" IN ( SELECT ""VolumeID"" FROM ""Block"" WHERE ""ID"" IN ( SELECT ""BlockID"" FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""Metadataset"" WHERE ""ID"" IN ( SELECT ""MetadataID"" FROM ""File"" WHERE ""ID"" IN ( SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" IN ( SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))))))))" +
                @")",
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(items);
            it.AddRange(items);

            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultRemoteVolume() {
                        Name = Convert.ToString(rd.GetValue(0))
                    };
        }
    }
}

