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

using System;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    internal class LocalListAffectedDatabase : LocalDatabase
    {
        public LocalListAffectedDatabase(DatabaseConnectionManager manager)
            : base(manager, "ListAffected")
        {
        }

        private class ListResultFileset : Interface.IListResultFileset
        {
            public long Version { get; set; }
            public int IsFullBackup { get; set; }
            public DateTime Time { get; set; }
            public long FileCount { get; set; }
            public long FileSizes { get; set; }
        }

        private class ListResultFile : Interface.IListResultFile
        {
            public string Path { get; set; }
            public IEnumerable<long> Sizes { get; set; }
        }

        private class ListResultRemoteLog : Interface.IListResultRemoteLog
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; }
        }

        private class ListResultRemoteVolume : Interface.IListResultRemoteVolume
        {
            public string Name { get; set; }
        }

        public IEnumerable<Interface.IListResultFileset> GetFilesets(IEnumerable<string> items)
        {
            var filesets = FilesetTimes.ToArray();
            var dict = new Dictionary<long, long>();
            for (var i = 0; i < filesets.Length; i++)
                dict[filesets[i].Key] = i;

            var sql = string.Format(
                @"SELECT DISTINCT ""FilesetID"" FROM (" +
                @"SELECT ""FilesetID"" FROM ""FilesetEntry"" WHERE ""FileID"" IN ( SELECT ""ID"" FROM ""FileLookup"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""BlocksetEntry"" WHERE ""BlockID"" IN ( SELECT ""ID"" From ""Block"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0})))))" +
                " UNION " +
                @"SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))" +
                ")",
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(items);
            it.AddRange(items);

            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                {
                    var v = dict[rd.GetInt64(0)];
                    yield return new ListResultFileset()
                    {
                        Version = v,
                        Time = filesets[v].Value
                    };
                }
        }

        public IEnumerable<Interface.IListResultFile> GetFiles(IEnumerable<string> items)
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

            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultFile()
                    {
                        Path = Convert.ToString(rd.GetValue(0))
                    };

        }

        public IEnumerable<Interface.IListResultRemoteLog> GetLogLines(IEnumerable<string> items)
        {
            var sql = string.Format(
                @"SELECT ""TimeStamp"", ""Message"" || ' ' || CASE WHEN ""Exception"" IS NULL THEN '' ELSE ""Exception"" END FROM ""LogData"" WHERE {0}" +
                @" UNION " +
                @"SELECT ""Timestamp"", ""Data"" FROM ""RemoteOperation"" WHERE ""Path"" IN ({1})",
                string.Join(" OR ", items.Select(x => @"""Message"" LIKE ?")),
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(from n in items select "%" + n + "%");
            it.AddRange(items);

            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultRemoteLog()
                    {
                        Timestamp = ParseFromEpochSeconds(rd.GetInt64(0)),
                        Message = rd.GetString(1)
                    };
        }

        public IEnumerable<Interface.IListResultRemoteVolume> GetVolumes(IEnumerable<string> items)
        {
            var sql = string.Format(
                @"SELECT DISTINCT ""Name"" FROM ( " +
                @" SELECT ""Name"" FROM ""Remotevolume"" WHERE ""ID"" IN ( SELECT ""VolumeID"" FROM ""Block"" WHERE ""ID"" IN ( SELECT ""BlockID"" FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""FileLookup"" WHERE ""ID"" IN ( SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" IN ( SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))))))) " +
                @" UNION " +
                @" SELECT ""Name"" FROM ""Remotevolume"" WHERE ""ID"" IN ( SELECT ""VolumeID"" FROM ""Block"" WHERE ""ID"" IN ( SELECT ""BlockID"" FROM ""BlocksetEntry"" WHERE ""BlocksetID"" IN ( SELECT ""BlocksetID"" FROM ""Metadataset"" WHERE ""ID"" IN ( SELECT ""MetadataID"" FROM ""FileLookup"" WHERE ""ID"" IN ( SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" IN ( SELECT ""ID"" FROM ""Fileset"" WHERE ""VolumeID"" IN ( SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" IN ({0}))))))))" +
                @")",
                string.Join(",", items.Select(x => "?"))
            );

            var it = new List<string>(items);
            it.AddRange(items);

            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(sql, it.ToArray()))
                while (rd.Read())
                    yield return new ListResultRemoteVolume()
                    {
                        Name = Convert.ToString(rd.GetValue(0))
                    };
        }
    }
}

