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

    /// <summary>
    /// A local database for bug reports, which obfuscates sensitive data before generating a bug report.
    /// </summary>
    internal class LocalBugReportDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalBugReportDatabase));

        /// <summary>
        /// Creates a new instance of the <see cref="LocalBugReportDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="pagecachesize">The size of the page cache.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalBugReportDatabase"/>.</returns>
        public static async Task<LocalBugReportDatabase> CreateAsync(string path, long pagecachesize, LocalBugReportDatabase? dbnew = null)
        {
            dbnew ??= new LocalBugReportDatabase();

            dbnew = (LocalBugReportDatabase)
                await CreateLocalDatabaseAsync(path, "BugReportCreate", false, pagecachesize, dbnew)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Obfuscates sensitive data in the database, readying it for a bug report.
        /// </summary>
        /// <returns>A task that completes when the obfuscation is finished.</returns>
        public async Task Fix()
        {
            using var cmd = m_connection.CreateCommand(m_rtr);
            var tablename = "PathMap-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            // TODO: Rewrite this to use PathPrefix
            // TODO: Needs to be much faster
            using (var upcmd = m_connection.CreateCommand(m_rtr))
            {
                await upcmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{tablename}"" (
                            ""ID"" INTEGER PRIMARY KEY,
                            ""RealPath"" TEXT NOT NULL,
                            ""Obfuscated"" TEXT NULL
                        )
                    ")
                    .ConfigureAwait(false);

                await upcmd.ExecuteNonQueryAsync($@"
                        INSERT INTO ""{tablename}"" (""RealPath"")
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                        ORDER BY ""Path""
                    ")
                    .ConfigureAwait(false);

                await upcmd.SetCommandAndParameters($@"
                        UPDATE ""{tablename}""
                        SET ""Obfuscated"" =
                            @StartPath
                            || length(""RealPath"")
                            || @DirSep
                            || ""ID""
                            || (
                                CASE
                                    WHEN substr(""RealPath"", length(""RealPath"")) = @DirSep
                                    THEN @DirSep
                                    ELSE @FileExt
                                END
                            )
                        ")
                    .SetParameterValue("@StartPath", !OperatingSystem.IsWindows() ? "/" : "X:\\")
                    .SetParameterValue("@DirSep", Util.DirectorySeparatorString)
                    .SetParameterValue("@FileExt", ".bin")
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);

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

            await cmd.ExecuteNonQueryAsync(@"
                    UPDATE ""LogData""
                    SET ""Message"" = 'ERASED!'
                    WHERE
                        ""Message"" LIKE '%/%'
                        OR ""Message"" LIKE '%:\%'
                ")
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync(@"
                    UPDATE ""LogData""
                    SET ""Exception"" = 'ERASED!'
                    WHERE
                        ""Exception"" LIKE '%/%'
                        OR ""Exception"" LIKE '%:\%'
                ")
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync(@"
                    UPDATE ""Configuration""
                    SET ""Value"" = 'ERASED!'
                    WHERE ""Key"" = 'passphrase'
                ")
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                    CREATE TABLE ""FixedFile"" AS
                    SELECT
                        ""B"".""ID"" AS ""ID"",
                        ""A"".""Obfuscated"" AS ""Path"",
                        ""B"".""BlocksetID"" AS ""BlocksetID"",
                        ""B"".""MetadataID"" AS ""MetadataID""
                    FROM
                        ""{tablename}"" ""A"",
                        ""File"" ""B""
                    WHERE ""A"".""RealPath"" = ""B"".""Path""
                ")
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync(@"DROP VIEW ""File"" ")
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync(@"DROP TABLE ""FileLookup"" ")
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync(@"DROP TABLE ""PathPrefix"" ")
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tablename}"" ")
                .ConfigureAwait(false);

            using (new Logging.Timer(LOGTAG, "CommitUpdateBugReport", "CommitUpdateBugReport"))
                await m_rtr.CommitAsync().ConfigureAwait(false);

            cmd.SetTransaction(m_rtr);
            await cmd.ExecuteNonQueryAsync("VACUUM").ConfigureAwait(false);
            await m_rtr.CommitAsync().ConfigureAwait(false);
        }
    }
}

