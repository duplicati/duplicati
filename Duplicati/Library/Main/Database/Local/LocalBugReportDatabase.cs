// Copyright (C) 2026, The Duplicati Team
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Database.Local
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
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalBugReportDatabase"/>.</returns>
        public static async Task<LocalBugReportDatabase> CreateAsync(string path, LocalBugReportDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalBugReportDatabase();

            dbnew = (LocalBugReportDatabase)
                await CreateLocalDatabaseAsync(path, "BugReportCreate", false, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Obfuscates sensitive data in the database, readying it for a bug report.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the obfuscation is finished.</returns>
        public async Task ObfuscateAsync(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            var tablename = $"PathMap-{Library.Utility.Utility.GetHexGuid()}";

            // TODO: Rewrite this to use PathPrefix
            // TODO: Needs to be much faster
            await using (var upcmd = m_connection.CreateCommand(m_rtr))
            {
                await upcmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{tablename}"" (
                            ""ID"" INTEGER PRIMARY KEY,
                            ""RealPath"" TEXT NOT NULL,
                            ""Obfuscated"" TEXT NULL
                        )
                    ", token)
                    .ConfigureAwait(false);

                await upcmd.ExecuteNonQueryAsync($@"
                        INSERT INTO ""{tablename}"" (""RealPath"")
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                        ORDER BY ""Path""
                    ", token)
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
                    .ExecuteNonQueryAsync(true, token)
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

            // The logs are kept with their paths redacted, as the logs of the latest operations help to find a problem
            var logs = new List<(long ID, string? Message, string? Exception)>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(@"SELECT ""ID"", ""Message"", ""Exception"" FROM ""LogData"" ", token).ConfigureAwait(false))
            {
                var message = rd.ConvertValueToString(1);
                var exception = rd.ConvertValueToString(2);
                var redactedMessage = RedactLogMessage(message);
                var redactedException = RedactLogText(exception);
                if (redactedMessage != message || redactedException != exception)
                    logs.Add((rd.ConvertValueToInt64(0), redactedMessage, redactedException));
            }

            await using (var upcmd = m_connection.CreateCommand(m_rtr))
            {
                upcmd.SetCommandAndParameters(@"UPDATE ""LogData"" SET ""Message"" = @Message, ""Exception"" = @Exception WHERE ""ID"" = @ID");
                foreach (var (id, message, exception) in logs)
                    await upcmd
                        .SetParameterValue("@ID", id)
                        .SetParameterValue("@Message", message)
                        .SetParameterValue("@Exception", exception)
                        .ExecuteNonQueryAsync(true, token)
                        .ConfigureAwait(false);
            }

            await cmd.ExecuteNonQueryAsync(@"
                    UPDATE ""Configuration""
                    SET ""Value"" = 'ERASED!'
                    WHERE ""Key"" = 'passphrase'
                ", token)
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
                ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync(@"DROP VIEW ""File"" ", token)
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync(@"DROP TABLE ""FileLookup"" ", token)
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync(@"DROP TABLE ""PathPrefix"" ", token)
                .ConfigureAwait(false);
            await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tablename}"" ", token)
                .ConfigureAwait(false);

            using (new Logging.Timer(LOGTAG, "CommitUpdateBugReport", "CommitUpdateBugReport"))
                await m_rtr.CommitAsync(token: token).ConfigureAwait(false);

            cmd.SetTransaction(m_rtr);
            await m_rtr.CommitAsync(restart: false, token: token).ConfigureAwait(false);

            cmd.Transaction = null;
            await cmd.ExecuteNonQueryAsync("VACUUM", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Redacts the paths in a stored log message. The result of an operation is stored as JSON, and its string
        /// values are redacted one by one, as the path detection on the compact JSON text would also take the quotes
        /// and the following properties as part of a path. Other messages are redacted as text.
        /// </summary>
        /// <param name="message">The stored message.</param>
        /// <returns>The message with its paths redacted.</returns>
        internal static string? RedactLogMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var start = message.TrimStart();
            if (start.StartsWith('{') || start.StartsWith('['))
            {
                try
                {
                    // Dates and numbers are kept as they were written
                    using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(message))
                    {
                        DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
                        FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Decimal
                    };
                    var root = Newtonsoft.Json.Linq.JToken.ReadFrom(reader);
                    var tokens = root is Newtonsoft.Json.Linq.JContainer container ? container.DescendantsAndSelf() : [root];
                    foreach (var value in tokens.OfType<Newtonsoft.Json.Linq.JValue>().ToList())
                        if (value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            value.Value = RedactLogText((string?)value.Value);

                    return root.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Not JSON, so it is redacted as text
                }
            }

            return RedactLogText(message);
        }

        /// <summary>
        /// Redacts the paths in a log text, more strictly than in a report, as the bug report may be shared in public.
        /// A path can contain spaces, which ends the path that <see cref="SensitiveDataFilter.RedactPaths"/> finds,
        /// so the rest of a line after a path is redacted too.
        /// A word with a path separator that is not found as a path, such as a relative path, may start after a
        /// name with spaces, so the words before it are redacted as well, back to a word that ends with a colon.
        /// </summary>
        /// <param name="text">The text to redact.</param>
        /// <returns>The text with its paths redacted.</returns>
        internal static string? RedactLogText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineEnd = line.EndsWith('\r') ? "\r" : string.Empty;
                line = SensitiveDataFilter.RedactPaths(line.Substring(0, line.Length - lineEnd.Length));

                var path = line.IndexOf(SensitiveDataFilter.REDACTED, StringComparison.Ordinal);
                if (path >= 0)
                    line = line.Substring(0, path + SensitiveDataFilter.REDACTED.Length);

                var words = line.Split(' ');
                var other = Array.FindIndex(words, word => word != SensitiveDataFilter.REDACTED && SensitiveDataFilter.LooksLikePath(word));
                if (other >= 0)
                {
                    // The indentation is kept, as it is the empty words at the start of the line
                    var indent = Array.FindIndex(words, word => word.Length > 0);
                    var start = other;
                    while (start > indent && !words[start - 1].EndsWith(':'))
                        start--;
                    line = string.Join(' ', words.Take(start).Append(SensitiveDataFilter.REDACTED));
                }

                lines[i] = line + lineEnd;
            }

            return string.Join('\n', lines);
        }
    }
}

