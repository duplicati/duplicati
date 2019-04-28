//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, opensource@duplicati.com
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
                    upcmd.ExecuteNonQuery(string.Format(@"UPDATE ""{0}"" SET ""Obfuscated"" = ? || length(""RealPath"") || ? || ""ID"" || (CASE WHEN substr(""RealPath"", length(""RealPath"")) = ? THEN ? ELSE ? END) ", tablename), Platform.IsClientPosix ? "/" : "X:\\", Util.DirectorySeparatorString, Util.DirectorySeparatorString, Util.DirectorySeparatorString, ".bin");
                    
                    /*long id = 1;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""RealPath"", ""Obfuscated"" FROM ""{0}"" ", tablename)))
                        while(rd.Read())
                        {
                            upcmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = replace(""Message"", ?, ?), ""Exception"" = replace(""Exception"", ?, ?)", rd.GetValue(0), rd.GetValue(1), rd.GetValue(0), rd.GetValue(1) );
                            id++;
                        }
                        */
                }

                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Message"" = ""ERASED!"" WHERE ""Message"" LIKE ""%/%"" OR ""Message"" LIKE ""%:\%"" ");                
                cmd.ExecuteNonQuery(@"UPDATE ""LogData"" SET ""Exception"" = ""ERASED!"" WHERE ""Exception"" LIKE ""%/%"" OR ""Exception"" LIKE ""%:\%"" ");                

                cmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""FixedFile"" AS SELECT ""B"".""ID"" AS ""ID"", ""A"".""Obfuscated"" AS ""Path"", ""B"".""BlocksetID"" AS ""BlocksetID"", ""B"".""MetadataID"" AS ""MetadataID"" FROM ""{0}"" ""A"", ""File"" ""B"" WHERE ""A"".""RealPath"" = ""B"".""Path"" ", tablename));
                cmd.ExecuteNonQuery(@"DROP VIEW ""File"" ");
                cmd.ExecuteNonQuery(@"DROP TABLE ""PathPrefix"" ");

                cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", tablename));
                
                using(new Logging.Timer(LOGTAG, "CommitUpdateBugReport", "CommitUpdateBugReport"))
                    tr.Commit();
                
                cmd.Transaction = null;
            }
        }
    }
}

