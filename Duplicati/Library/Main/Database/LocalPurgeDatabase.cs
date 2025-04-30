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

#nullable enable

using System;
using System.Data;
using System.Collections.Generic;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Database
{
    internal class LocalPurgeDatabase : LocalDeleteDatabase
    {
        public LocalPurgeDatabase(string path, long pagecachesize)
            : base(path, "Purge", pagecachesize)
        {
            ShouldCloseConnection = true;
        }

        public LocalPurgeDatabase(LocalDatabase db)
            : base(db)
        {
        }

        public ITemporaryFileset CreateTemporaryFileset(long parentid, IDbTransaction transaction)
        {
            return new TemporaryFileset(parentid, this, m_connection, transaction);
        }

        public string GetRemoteVolumeNameForFileset(long id, IDbTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(transaction, @"SELECT ""B"".""Name"" FROM ""Fileset"" A, ""RemoteVolume"" B WHERE ""A"".""VolumeID"" = ""B"".""ID"" AND ""A"".""ID"" = @FilesetId ")
                .SetParameterValue("@FilesetId", id);
            using (var rd = cmd.ExecuteReader())
                if (!rd.Read())
                    throw new Exception($"No remote volume found for fileset with id {id}");
                else
                    return rd.ConvertValueToString(0) ?? throw new Exception($"Remote volume name for fileset with id {id} is null");
        }

        internal long CountOrphanFiles(IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
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

            void ApplyFilter(Library.Utility.IFilter filter);
            void ApplyFilter(Action<IDbCommand, long, string> filtercommand);
            Tuple<long, long> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup);
            IEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles();
        }

        private class TemporaryFileset : ITemporaryFileset
        {
            private readonly IDbConnection m_connection;
            private readonly IDbTransaction m_transaction;
            private readonly string m_tablename;
            private readonly LocalPurgeDatabase m_parentdb;

            public long ParentID { get; private set; }
            public long RemovedFileCount { get; private set; }
            public long RemovedFileSize { get; private set; }

            public TemporaryFileset(long parentid, LocalPurgeDatabase parentdb, IDbConnection connection, IDbTransaction transaction)
            {
                ParentID = parentid;
                m_parentdb = parentdb;
                m_connection = connection;
                m_transaction = transaction;
                m_tablename = "TempDeletedFilesTable-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                using (var cmd = m_connection.CreateCommand(m_transaction))
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tablename}"" (""FileID"" INTEGER PRIMARY KEY) "));
            }

            public void ApplyFilter(Action<IDbCommand, long, string> filtercommand)
            {
                using (var cmd = m_connection.CreateCommand(m_transaction))
                    filtercommand(cmd, ParentID, m_tablename);

                PostFilterChecks();
            }

            public void ApplyFilter(Library.Utility.IFilter filter)
            {
                if (Library.Utility.Utility.IsFSCaseSensitive && filter is FilterExpression expression && expression.Type == Duplicati.Library.Utility.FilterType.Simple)
                {
                    // File list based
                    // unfortunately we cannot do this if the filesystem is not case-sensitive as
                    // SQLite only supports ASCII compares
                    var p = expression.GetSimpleList();
                    var filenamestable = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    using (var cmd = m_connection.CreateCommand(m_transaction))
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{filenamestable}"" (""Path"" TEXT NOT NULL) "));
                        cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{filenamestable}"" (""Path"") VALUES (@Path)"));
                        foreach (var s in p)
                            cmd.SetParameterValue("@Path", s)
                                .ExecuteNonQuery();

                        cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tablename}"" (""FileID"") SELECT DISTINCT ""A"".""FileID"" FROM ""FilesetEntry"" A, ""File"" B WHERE ""A"".""FilesetID"" = @FilesetId AND ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""Path"" IN ""{filenamestable}"""))
                            .SetParameterValue("@FilesetId", ParentID)
                            .ExecuteNonQuery();
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{filenamestable}"" "));
                    }
                }
                else
                {
                    // Do row-wise iteration
                    var values = new object[2];
                    using (var cmd = m_connection.CreateCommand(m_transaction))
                    using (var cmd2 = m_connection.CreateCommand(m_transaction))
                    {
                        cmd2.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tablename}"" (""FileID"") VALUES (@FileId)"));
                        cmd.SetCommandAndParameters(@"SELECT ""B"".""Path"", ""A"".""FileID"" FROM ""FilesetEntry"" A, ""File"" B WHERE ""A"".""FilesetID"" = @FilesetId AND ""A"".""FileID"" = ""B"".""ID"" ")
                            .SetParameterValue("@FilesetId", ParentID);
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                            {
                                rd.GetValues(values);
                                if (values[0] != null && values[0] != DBNull.Value && Library.Utility.FilterExpression.Matches(filter, values[0].ToString()))
                                {
                                    cmd2.SetParameterValue("@FileId", values[1])
                                        .ExecuteNonQuery();
                                }
                            }
                    }
                }

                PostFilterChecks();
            }

            private void PostFilterChecks()
            {
                using (var cmd = m_connection.CreateCommand(m_transaction))
                {
                    RemovedFileCount = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM ""{m_tablename}"""), 0);
                    RemovedFileSize = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT SUM(""C"".""Length"") FROM ""{m_tablename}"" A, ""FileLookup"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" "), 0);
                    var filesetcount = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetID"" = {ParentID}"), 0);
                    if (filesetcount == RemovedFileCount)
                        throw new Interface.UserInformationException($"Refusing to purge {RemovedFileCount} files from fileset with ID {ParentID}, as that would remove the entire fileset.\nTo delete a fileset, use the \"delete\" command.", "PurgeWouldRemoveEntireFileset");
                }
            }

            public Tuple<long, long> ConvertToPermanentFileset(string name, DateTime timestamp, bool isFullBackup)
            {
                var remotevolid = m_parentdb.RegisterRemoteVolume(name, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);
                var filesetid = m_parentdb.CreateFileset(remotevolid, timestamp, m_transaction);
                m_parentdb.UpdateFullBackupStateInFileset(filesetid, isFullBackup);

                using (var cmd = m_connection.CreateCommand(m_transaction))
                    cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT @TargetFilesetId, ""FileID"", ""LastModified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @SourceFilesetId AND ""FileID"" NOT IN ""{m_tablename}"" "))
                    .SetParameterValue("@TargetFilesetId", filesetid)
                    .SetParameterValue("@SourceFilesetId", ParentID)
                    .ExecuteNonQuery();

                return new Tuple<long, long>(remotevolid, filesetid);
            }

            public IEnumerable<KeyValuePair<string, long>> ListAllDeletedFiles()
            {
                using (var cmd = m_connection.CreateCommand(m_transaction))
                using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT ""B"".""Path"", ""C"".""Length"" FROM ""{m_tablename}"" A, ""File"" B, ""Blockset"" C WHERE ""A"".""FileID"" = ""B"".""ID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" ")))
                    while (rd.Read())
                        yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
            }

            public void Dispose()
            {
                try
                {
                    using (var cmd = m_connection.CreateCommand(m_transaction))
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tablename}"""));
                }
                catch
                {
                }
            }
        }

    }
}
