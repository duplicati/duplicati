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
using System.Collections.Generic;
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.Database.DatabaseConnectionManager;

namespace Duplicati.Library.Main.Database
{
    internal class LocalPurgeDatabase : LocalDeleteDatabase
    {
        public LocalPurgeDatabase(DatabaseConnectionManager manager)
            : base(manager, "Purge")
        {
        }

        public LocalPurgeDatabase(LocalDatabase db)
            : base(db)
        {
        }

        public ITemporaryFileset CreateTemporaryFileset(long parentid)
        {
            return new TemporaryFileset(parentid, this, m_manager);
        }

        public string GetRemoteVolumeNameForFileset(long id)
        {
            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(@"SELECT ""B"".""Name"" FROM ""Fileset"" A, ""RemoteVolume"" B WHERE ""A"".""VolumeID"" = ""B"".""ID"" AND ""A"".""ID"" = ? ", id))
                if (!rd.Read())
                    throw new Exception(string.Format("No remote volume found for fileset with id {0}", id));
                else
                    return rd.ConvertValueToString(0);
        }

        internal long CountOrphanFiles()
        {
            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(@"SELECT COUNT(*) FROM ""FileLookup"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""FileID"" FROM ""FilesetEntry"")"))
                if (rd.Read())
                    return rd.ConvertValueToInt64(0, 0);
                else
                    return 0;
        }

        public interface ITemporaryFileset : IDisposable
        {
            long ParentID { get; }
            long RemovedFileCount { get; }
            long RemovedFileSize { get; }

            void ApplyFilter(IFilter filter);
            void ApplyFilter(Action<DatabaseCommand, long, string> filtercommand);
            Tuple<long, long> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup);
            IEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles();
        }

        private class TemporaryFileset : ITemporaryFileset
        {
            private readonly DatabaseConnectionManager m_manager;
            private readonly string m_tablename;
            private readonly LocalPurgeDatabase m_parentdb;

            public long ParentID { get; private set; }
            public long RemovedFileCount { get; private set; }
            public long RemovedFileSize { get; private set; }

            public TemporaryFileset(long parentid, LocalPurgeDatabase parentdb, DatabaseConnectionManager manager)
            {
                this.ParentID = parentid;
                m_parentdb = parentdb;
                m_manager = manager;
                m_tablename = "TempDeletedFilesTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                using (var cmd = m_manager.CreateCommand())
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""FileID"" INTEGER PRIMARY KEY) ", m_tablename));
            }

            public void ApplyFilter(Action<DatabaseCommand, long, string> filtercommand)
            {
                using (var cmd = m_manager.CreateCommand())
                    filtercommand(cmd, ParentID, m_tablename);

                PostFilterChecks();
            }

            public void ApplyFilter(IFilter filter)
            {
                if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == FilterType.Simple)
                {
                    // File list based
                    // unfortunately we cannot do this if the filesystem is not case-sensitive as
                    // SQLite only supports ASCII compares
                    var p = expression.GetSimpleList();
                    var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    using (var cmd = m_manager.CreateCommand())
                    {
                        cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Path"" TEXT NOT NULL) ", filenamestable));
                        cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Path"") VALUES (?)", filenamestable);
                        cmd.AddParameter();

                        foreach (var s in p)
                        {
                            cmd.SetParameterValue(0, s);
                            cmd.ExecuteNonQuery();
                        }

                        cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (""FileID"") SELECT DISTINCT ""A"".""FileID"" FROM ""FilesetEntry"" A, ""File"" B WHERE ""A"".""FilesetID"" = ? AND ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""Path"" IN ""{1}""", m_tablename, filenamestable), ParentID);
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", filenamestable));
                    }
                }
                else
                {
                    // Do row-wise iteration
                    object[] values = new object[2];
                    using (var cmd = m_manager.CreateCommand())
                    using (var cmd2 = m_manager.CreateCommand())
                    {
                        cmd2.CommandText = string.Format(@"INSERT INTO ""{0}"" (""FileID"") VALUES (?)", m_tablename);
                        cmd2.AddParameters(1);

                        using (var rd = cmd.ExecuteReader(@"SELECT ""B"".""Path"", ""A"".""FileID"" FROM ""FilesetEntry"" A, ""File"" B WHERE ""A"".""FilesetID"" = ? AND ""A"".""FileID"" = ""B"".""ID"" ", ParentID))
                            while (rd.Read())
                            {
                                rd.GetValues(values);
                                if (values[0] != null && values[0] != DBNull.Value && Library.Utility.FilterExpression.Matches(filter, values[0].ToString()))
                                {
                                    cmd2.SetParameterValue(0, values[1]);
                                    cmd2.ExecuteNonQuery();
                                }
                            }
                    }
                }

                PostFilterChecks();
            }

            private void PostFilterChecks()
            {
                using (var cmd = m_manager.CreateCommand())
                {
                    RemovedFileCount = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}""", m_tablename), 0);
                    RemovedFileSize = cmd.ExecuteScalarInt64(string.Format(@"SELECT SUM(""C"".""Length"") FROM ""{0}"" A, ""FileLookup"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" ", m_tablename), 0);
                    var filesetcount = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetID"" = " + ParentID), 0);
                    if (filesetcount == RemovedFileCount)
                        throw new Interface.UserInformationException(string.Format("Refusing to purge {0} files from fileset with ID {1}, as that would remove the entire fileset.\nTo delete a fileset, use the \"delete\" command.", RemovedFileCount, ParentID), "PurgeWouldRemoveEntireFileset");
                }
            }

            public Tuple<long, long> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup)
            {
                var remotevolid = m_parentdb.RegisterRemoteVolume(name, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                var filesetid = m_parentdb.CreateFileset(remotevolid, timestamp);
                m_parentdb.UpdateFullBackupStateInFileset(filesetid, isFullBackup);

                using (var cmd = m_manager.CreateCommand())
                    cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT ?, ""FileID"", ""LastModified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN ""{0}"" ", m_tablename), filesetid, ParentID);

                return new Tuple<long, long>(remotevolid, filesetid);
            }

            public IEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles()
            {
                using (var cmd = m_manager.CreateCommand())
                using (var rd = cmd.ExecuteReader(string.Format(@"SELECT ""B"".""Path"", ""C"".""Length"" FROM ""{0}"" A, ""File"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" ", m_tablename)))
                    while (rd.Read())
                        yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
            }

            public void Dispose()
            {
                try
                {
                    using (var cmd = m_manager.CreateCommand())
                        cmd.ExecuteNonQuery(@"DROP TABLE IF EXISTS ""{0}""", m_tablename);
                }
                catch
                {
                }
            }
        }

    }
}
