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
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Main.Database
{
    internal class LocalBugReportDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalBugReportDatabase));

        public static async Task<LocalBugReportDatabase> CreateAsync(string path, long pagecachesize)
        {
            var db = new LocalBugReportDatabase();

            db = (LocalBugReportDatabase)await CreateLocalDatabaseAsync(db, path, "BugReportCreate", false, pagecachesize);
            db.ShouldCloseConnection = true;

            return db;
        }

            {
                cmd.Transaction = tr;
                var tablename = "PathMap-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                // TODO: Rewrite this to use PathPrefix
                // TODO: Needs to be much faster
                using (var upcmd = m_connection.CreateCommand())
                {

                    upcmd.Transaction = tr;
                    upcmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" (""ID"" INTEGER PRIMARY KEY, ""RealPath"" TEXT NOT NULL, ""Obfuscated"" TEXT NULL)"));
                    upcmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""{tablename}"" (""RealPath"") SELECT DISTINCT ""Path"" FROM ""File"" ORDER BY ""Path"" "));
                    upcmd.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{tablename}"" SET ""Obfuscated"" = @StartPath || length(""RealPath"") || @DirSep || ""ID"" || (CASE WHEN substr(""RealPath"", length(""RealPath"")) = @DirSep THEN @DirSep ELSE @FileExt END) "))
                        .SetParameterValue("@StartPath", !OperatingSystem.IsWindows() ? "/" : "X:\\")
                        .SetParameterValue("@DirSep", Util.DirectorySeparatorString)
                        .SetParameterValue("@FileExt", ".bin")
                        .ExecuteNonQuery();

                    /*long id = 1;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""RealPath"", ""Obfuscated"" FROM ""{0}"" ", tablename)))
                        while(rd.Read())
                        {
                            upcmd.SetCommandAndParameters(@"UPDATE ""LogData"" SET ""Message"" = replace(""Message"", @RealPath, @Obfuscated), ""Exception"" = replace(""Exception"", @RealPath, @Obfuscated)")
                                .SetParameterValue("@RealPath", rd.GetValue(0))
                                .SetParameterValue("@Obfuscated", rd.GetValue(1))
                                .ExecuteNonQuery();
                            id++;
                        }
                        */
                }

                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = 'ERASED!' WHERE ""Message"" LIKE '%/%' OR ""Message"" LIKE '%:\%' ");
                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Exception"" = 'ERASED!' WHERE ""Exception"" LIKE '%/%' OR ""Exception"" LIKE '%:\%' ");

                cmd.ExecuteNonQuery(@"UPDATE ""Configuration"" SET ""Value"" = 'ERASED!' WHERE ""Key"" = 'passphrase' ");

                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TABLE ""FixedFile"" AS SELECT ""B"".""ID"" AS ""ID"", ""A"".""Obfuscated"" AS ""Path"", ""B"".""BlocksetID"" AS ""BlocksetID"", ""B"".""MetadataID"" AS ""MetadataID"" FROM ""{tablename}"" ""A"", ""File"" ""B"" WHERE ""A"".""RealPath"" = ""B"".""Path"" "));
                cmd.ExecuteNonQuery(@"DROP VIEW ""File"" ");
                cmd.ExecuteNonQuery(@"DROP TABLE ""FileLookup"" ");
                cmd.ExecuteNonQuery(@"DROP TABLE ""PathPrefix"" ");

                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{tablename}"" "));

                using (new Logging.Timer(LOGTAG, "CommitUpdateBugReport", "CommitUpdateBugReport"))
                    tr.Commit();

                cmd.Transaction = null;

                cmd.ExecuteNonQuery("VACUUM");
            }
        }
    }
}

