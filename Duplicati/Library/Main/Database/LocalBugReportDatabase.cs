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
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Main.Database
{
    internal class LocalBugReportDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalBugReportDatabase));

        public LocalBugReportDatabase(string path)
            : base(path, "BugReportCreate", false)
        {
            ShouldCloseConnection = true;
        }

        public void Fix()
        {
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = tr;
                var tablename = "PathMap-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                // TODO: Rewrite this to use PathPrefix
                // TODO: Needs to be much faster
                using(var upcmd = m_connection.CreateCommand())
                {
                
                    upcmd.Transaction = tr;
                    upcmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER PRIMARY KEY, ""RealPath"" TEXT NOT NULL, ""Obfuscated"" TEXT NULL)", tablename));
                    upcmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (""RealPath"") SELECT DISTINCT ""Path"" FROM ""File"" ORDER BY ""Path"" ", tablename));
                    upcmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Obfuscated"" = ? || length(""RealPath"") || ? || ""ID"" || (CASE WHEN substr(""RealPath"", length(""RealPath"")) = ? THEN ? ELSE ? END) ", tablename), !OperatingSystem.IsWindows() ? "/" : "X:\\", Util.DirectorySeparatorString, Util.DirectorySeparatorString, Util.DirectorySeparatorString, ".bin");
                    
                    /*long id = 1;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""RealPath"", ""Obfuscated"" FROM ""{0}"" ", tablename)))
                        while(rd.Read())
                        {
                            upcmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = replace(""Message"", ?, ?), ""Exception"" = replace(""Exception"", ?, ?)", rd.GetValue(0), rd.GetValue(1), rd.GetValue(0), rd.GetValue(1) );
                            id++;
                        }
                        */
                }

                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = 'ERASED!' WHERE ""Message"" LIKE '%/%' OR ""Message"" LIKE '%:\%' ");                
                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Exception"" = 'ERASED!' WHERE ""Exception"" LIKE '%/%' OR ""Exception"" LIKE '%:\%' ");                

                cmd.ExecuteNonQuery(@"UPDATE ""Configuration"" SET ""Value"" = 'ERASED!' WHERE ""Key"" = 'passphrase' ");

                cmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""FixedFile"" AS SELECT ""B"".""ID"" AS ""ID"", ""A"".""Obfuscated"" AS ""Path"", ""B"".""BlocksetID"" AS ""BlocksetID"", ""B"".""MetadataID"" AS ""MetadataID"" FROM ""{0}"" ""A"", ""File"" ""B"" WHERE ""A"".""RealPath"" = ""B"".""Path"" ", tablename));
                cmd.ExecuteNonQuery(@"DROP VIEW ""File"" ");
                cmd.ExecuteNonQuery(@"DROP TABLE ""FileLookup"" ");
                cmd.ExecuteNonQuery(@"DROP TABLE ""PathPrefix"" ");

                cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", tablename));
                
                using(new Logging.Timer(LOGTAG, "CommitUpdateBugReport", "CommitUpdateBugReport"))
                    tr.Commit();
                
                cmd.Transaction = null;

                cmd.ExecuteNonQuery("VACUUM");
            }
        }
    }
}

