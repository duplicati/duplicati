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

using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Text.RegularExpressions;
using System.Linq;
using Duplicati.Library.SQLiteHelper.DBUpdates;
using Duplicati.Library.SQLiteHelper.DBSchemaUpgrades;
using System.Globalization;

namespace Duplicati.Library.SQLiteHelper
{
    /// <summary>
    /// This class will read embedded files from the given folder.
    /// Updates should have the form &quot;1.Sample upgrade.sql&quot;.
    /// When the database schema changes, simply put a new file into the folder
    /// and set it to be embedded in the binary.
    ///
    /// Additionally, it's possible to execute custom code before and after
    /// the SQL is executed. To set up a custom upgrade stage, add your
    /// code to DbUpgradesRegistry along with the DB version to apply it with.
    ///
    /// Even if all the DB upgrade code is handled in C#, you still have to add
    /// a dummy SQL file to indicate the version ID is already taken.
    ///
    /// Each upgrade file should ONLY upgrade from the previous version.
    /// If done correctly, a user may be upgrade from the very first version
    /// to the very latest.
    ///
    /// The Schema.sql file should ALWAYS have the latest schema, as that will
    /// ensure that new installs do not run upgrades after installation.
    /// Also remember to update the last line in Schema.sql to insert the
    /// current version number in the version table.
    ///
    /// Currently no upgrades may contain semicolons, except as the SQL statement
    /// delimiter.
    ///
    /// Each upgrade should have a downgrade script with the same number in the
    /// &quot;Downgrade&quot; subfolder, e.g. &quot;Downgrade/22. Remove table.sql&quot;.
    /// The upgrader records every version's upgrade and downgrade script in the
    /// &quot;SchemaScript&quot; table, together with the newest version it knows, so
    /// an older DatabaseTool can downgrade a database that a newer release upgraded.
    /// Because older releases run them, downgrade scripts must be plain SQL (no
    /// {#if} blocks), must not touch the &quot;Version&quot; or &quot;SchemaScript&quot;
    /// tables, and may refuse a downgrade by returning a single row with a message.
    /// </summary>
    public static class DatabaseUpgrader
    {
        //This is the "folder" where the embedded resources can be found
        private const string FOLDER_NAME = "Database_schema";

        //This is the name of the schema sql
        private const string SCHEMA_NAME = "Schema.sql";

        //This is the subfolder of FOLDER_NAME with the downgrade scripts
        private const string DOWNGRADE_FOLDER_NAME = "Downgrade";

        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(DatabaseUpgrader));

        /// <summary> Helper func to evaluate a condition like "sqlitever > 3.8.2" </summary>
        private static bool evalCondition(string cond, IDictionary<string, IComparable> vars)
        {
            var ops = new Dictionary<string, Func<IComparable, IComparable, bool>>()
             {
                {"<=", (x,y) => x.CompareTo(y) <= 0},
                {">=", (x,y) => x.CompareTo(y) >= 0},
                {"!=", (x,y) => x.CompareTo(y) != 0},
                {"==", (x,y) => x.CompareTo(y) == 0},
                {"<",  (x,y) => x.CompareTo(y) <  0},
                {">",  (x,y) => x.CompareTo(y) >  0},
                {"=",  (x,y) => x.CompareTo(y) == 0},
            };

            // build RegEx list with operators
            var opsList = "(" + string.Join("|", ops.Keys.Select(sop => Regex.Escape(sop) + (sop.Length == 1 ? @"(?!\=)" : ""))) + ")";
            var condPattern = string.Format(@"^\s*(?<VARIABLE>[a-zA-Z_][a-zA-Z0-9_]*)\s*(?<OPERATOR>{0})(?<LITERAL>.*)$"
                , opsList);

            // match condition to retrieve parts
            var m = Regex.Match(cond, condPattern, RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) throw new ArgumentException(string.Format("Malformed condition '{0}'.", cond));
            var variable = m.Groups["VARIABLE"].Value;
            var op = m.Groups["OPERATOR"].Value;
            var literal = m.Groups["LITERAL"].Value.Trim();

            // find variable and convert literal to correct type
            IComparable varVal;
            if (!vars.TryGetValue(variable, out varVal))
                throw new KeyNotFoundException(string.Format("Unknown variable '{0}' used in condition.", variable));

            IComparable litVal;
            try
            {
                if (varVal.GetType() == typeof(Version))
                    litVal = Version.Parse(literal);
                else // good for most other value types
                    litVal = (IComparable)System.Convert.ChangeType(literal, varVal.GetType(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            { throw new FormatException(string.Format("Failed to convert literal '{0}' to desired type '{1}' for comparison", literal, varVal.GetType().Name), ex); }

            return ops[op](varVal, litVal);
        }

        /// <summary>
        /// Preparses an SQL in a very simple way to support conditional statements / clauses.
        /// Nesting is supported by using {#if_xx} {#else_xx} {#endif_xx} with xx being a number inside the blocks.
        /// </summary>
        public static string PreparseSQL(string sql, IDictionary<string, IComparable> vars)
        {
            var prepPattern = @"\{\#if(?<NEST>(_\d*)?)\s+(?<CONDITION>[^\}]*)}(?<THENPART>.*?)(?:\{\#else\k<NEST>\}(?<ELSEPART>.*?))?\{\#endif\k<NEST>\}";
            var parsePoints = Regex.Matches(sql, prepPattern, RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline);

            StringBuilder retSql = new StringBuilder();
            int curPos = 0;
            foreach (Match pp in parsePoints)
            {
                var cond = pp.Groups["CONDITION"].Value;
                var thenpart = pp.Groups["THENPART"].Value;
                var elsepart = pp.Groups["ELSEPART"].Success ? pp.Groups["ELSEPART"].Value : null;

                retSql.Append(sql.Substring(curPos, pp.Index - curPos));
                if (evalCondition(cond, vars))
                    retSql.Append(PreparseSQL(thenpart, vars));
                else if (elsepart != null)
                    retSql.Append(PreparseSQL(elsepart, vars));

                curPos = pp.Index + pp.Length;
            }
            retSql.Append(sql.Substring(curPos, sql.Length - curPos));
            return retSql.ToString();
        }

        /// <summary>
        /// Reads the schema, upgrade and downgrade scripts embedded beside the given marker type.
        /// The resource names look like
        /// &quot;Duplicati.Library.Main.Database.Local.Database_schema.22. Add table.sql&quot; for upgrades and
        /// &quot;Duplicati.Library.Main.Database.Local.Database_schema.Downgrade.22. Remove table.sql&quot; for downgrades,
        /// where the number is the version the upgrade leads to, and the version the downgrade leaves.
        /// </summary>
        /// <param name="eltype">A type in the namespace that holds the <c>Database schema</c> folder</param>
        /// <returns>The embedded scripts</returns>
        public static SchemaScriptSet GetSchemaScripts(Type eltype)
        {
            var asm = eltype.Assembly;

            string ReadResource(string name)
            {
                using var rd = new System.IO.StreamReader(asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing resource {name}"));
                return rd.ReadToEnd();
            }

            var prefix = (eltype.Namespace ?? "") + "." + FOLDER_NAME + ".";
            var downgradePrefix = prefix + DOWNGRADE_FOLDER_NAME + ".";
            var schema = ReadResource(prefix + SCHEMA_NAME);

            var upgrades = new List<SchemaScriptFile>();
            var downgrades = new List<SchemaScriptFile>();
            foreach (var s in asm.GetManifestResourceNames())
            {
                if (!s.StartsWith(prefix, StringComparison.Ordinal) || s == prefix + SCHEMA_NAME || !s.EndsWith(".sql", StringComparison.Ordinal))
                    continue;

                var isDowngrade = s.StartsWith(downgradePrefix, StringComparison.Ordinal);
                var filename = s.Substring(isDowngrade ? downgradePrefix.Length : prefix.Length);
                var dot = filename.IndexOf('.');
                if (dot <= 0 || !int.TryParse(filename.Substring(0, dot), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
                    continue;

                (isDowngrade ? downgrades : upgrades).Add(new SchemaScriptFile(version, filename, ReadResource(s)));
            }

            // The upgrader numbers the database by script count, so a gap would record the wrong version
            var upgradeVersions = upgrades.Select(x => x.Version).Distinct().OrderBy(x => x).ToList();
            if (!upgradeVersions.SequenceEqual(Enumerable.Range(1, upgradeVersions.Count)))
                throw new InvalidOperationException($"The upgrade scripts in {prefix} must be numbered 1 to {upgradeVersions.Count} without gaps, found: {string.Join(", ", upgradeVersions)}");

            return new SchemaScriptSet(
                schema,
                upgrades.OrderBy(x => x.Version).ThenBy(x => x.Filename, StringComparer.Ordinal).ToList(),
                downgrades.OrderBy(x => x.Version).ThenBy(x => x.Filename, StringComparer.Ordinal).ToList());
        }

        public static void UpgradeDatabase(IDbConnection connection, string sourcefile, Type eltype)
        {
            //Get updates, and sort them according to version
            //This enables upgrading through several versions
            //ea, from 1 to 8, by stepping 2->3->4->5->6->7->8
            var scripts = GetSchemaScripts(eltype);
            var versions = scripts.Upgrades
                .Select(x => x.Version)
                .Distinct()
                .Select(scripts.GetUpgradeScript)
                .ToList();

            UpgradeDatabase(connection, sourcefile, scripts, versions);
        }

        /// <summary>
        /// Brings the <c>SchemaScript</c> table in line with a database at the given version.
        /// Every version from 1 to <paramref name="dbversion"/> gets a row with the scripts from
        /// <paramref name="scripts"/>, unless the row was written by a newer release, and rows
        /// above <paramref name="dbversion"/> are removed. The table lets an older DatabaseTool
        /// downgrade a database that a newer release upgraded.
        /// </summary>
        /// <param name="connection">The database connection</param>
        /// <param name="transaction">The transaction to write in</param>
        /// <param name="scripts">The scripts known by this binary</param>
        /// <param name="dbversion">The schema version the database is at</param>
        public static void WriteSchemaScriptTable(IDbConnection connection, IDbTransaction transaction, SchemaScriptSet scripts, int dbversion)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SchemaScript"" (
                    ""Version"" INTEGER PRIMARY KEY,
                    ""UpdateScript"" TEXT NOT NULL,
                    ""DowngradeScript"" TEXT NULL,
                    ""WriterVersion"" INTEGER NOT NULL
                )
            ";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                INSERT INTO ""SchemaScript"" (""Version"", ""UpdateScript"", ""DowngradeScript"", ""WriterVersion"")
                VALUES (@Version, @UpdateScript, @DowngradeScript, @WriterVersion)
                ON CONFLICT (""Version"") DO UPDATE SET
                    ""UpdateScript"" = excluded.""UpdateScript"",
                    ""DowngradeScript"" = excluded.""DowngradeScript"",
                    ""WriterVersion"" = excluded.""WriterVersion""
                WHERE excluded.""WriterVersion"" >= ""SchemaScript"".""WriterVersion""
            ";

            var versionParam = AddParameter(cmd, "@Version");
            var updateParam = AddParameter(cmd, "@UpdateScript");
            var downgradeParam = AddParameter(cmd, "@DowngradeScript");
            AddParameter(cmd, "@WriterVersion").Value = scripts.LatestVersion;

            for (var v = 1; v <= dbversion; v++)
            {
                versionParam.Value = v;
                updateParam.Value = scripts.GetUpgradeScript(v);
                downgradeParam.Value = (object)scripts.GetDowngradeScript(v) ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            cmd.Parameters.Clear();
            cmd.CommandText = @"DELETE FROM ""SchemaScript"" WHERE ""Version"" > @Version";
            AddParameter(cmd, "@Version").Value = dbversion;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Adds a named parameter to the command
        /// </summary>
        /// <param name="cmd">The command</param>
        /// <param name="name">The parameter name</param>
        /// <returns>The parameter</returns>
        private static IDbDataParameter AddParameter(IDbCommand cmd, string name)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
            return p;
        }

        /// <summary>
        /// Writes the <c>SchemaScript</c> rows after an upgrade, or when they are missing or were
        /// written by an older release. On a database that is already current and has all rows
        /// from this release or a newer one, nothing is written.
        /// A failure is logged and does not stop the database from being used.
        /// </summary>
        /// <param name="connection">The database connection</param>
        /// <param name="scripts">The scripts known by this binary</param>
        /// <param name="applied">Whether this call applied any upgrade</param>
        private static void SyncSchemaScriptTable(IDbConnection connection, SchemaScriptSet scripts, bool applied)
        {
            var latest = scripts.LatestVersion;
            try
            {
                if (!applied)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM sqlite_master
                        WHERE type = 'table' AND name = 'SchemaScript'
                    ";
                    if (Convert.ToInt64(cmd.ExecuteScalar()) == 1)
                    {
                        cmd.CommandText = @"
                            SELECT COUNT(*), IFNULL(MAX(""Version""), 0), IFNULL(MIN(""WriterVersion""), 0)
                            FROM ""SchemaScript""
                        ";
                        using var rd = cmd.ExecuteReader();
                        if (rd.Read() && rd.GetInt64(0) == latest && rd.GetInt64(1) == latest && rd.GetInt64(2) >= latest)
                            return;
                    }
                }

                using var tr = connection.BeginTransaction();
                WriteSchemaScriptTable(connection, tr, scripts, latest);
                tr.Commit();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "SchemaScriptWriteFailed", ex, "Failed to record the schema scripts in the database, an older version may not be able to downgrade it: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Ensures that the database is up to date
        /// </summary>
        /// <param name="connection">The database connection to use</param>
        /// <param name="sourcefile">The file the database is placed in</param>
        /// <param name="scripts">The embedded scripts</param>
        /// <param name="versions">The upgrade scripts, one per version, in version order</param>
        private static void UpgradeDatabase(IDbConnection connection, string sourcefile, SchemaScriptSet scripts, IList<string> versions)
        {
            var schema = scripts.Schema;
            var applied = false;
            if (connection.State != ConnectionState.Open)
            {
                if (string.IsNullOrEmpty(connection.ConnectionString))
                    connection.ConnectionString = $"Data Source={sourcefile};Pooling=false";

                connection.Open();
            }


            int dbversion = 0;
            using (var cmd = connection.CreateCommand())
            {
                try
                {
                    //See if the version table is present,
                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM SQLITE_MASTER
                        WHERE Name LIKE 'Version'
                    ";
                    int count = Convert.ToInt32(cmd.ExecuteScalar());

                    if (count == 0)
                        dbversion = -1; //Empty
                    else if (count == 1)
                    {
                        cmd.CommandText = @"
                            SELECT max(Version)
                            FROM Version
                        ";
                        dbversion = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    else
                        throw new Exception(Strings.DatabaseUpgrader.TableLayoutError);

                }
                catch (Exception ex)
                {
                    //Hopefully a more explanatory error message
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.DatabaseUpgrader.DatabaseFormatError(ex.Message), "DatabaseFormatError", ex);
                }

                Dictionary<string, IComparable> preparserVars = null;

                if (dbversion > versions.Count)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.DatabaseUpgrader.InvalidVersionError(dbversion, versions.Count, System.IO.Path.GetDirectoryName(sourcefile)), "DatabaseVersionNotSupportedError");
                else if (dbversion < versions.Count) // will need action, collect vars for preparser
                {
                    preparserVars = new Dictionary<string, IComparable>(StringComparer.OrdinalIgnoreCase);
                    cmd.CommandText = "SELECT sqlite_version()";
                    System.Version sqliteversion;
                    if (Version.TryParse(cmd.ExecuteScalar().ToString(), out sqliteversion))
                        preparserVars["sqlite_version"] = sqliteversion;

                    preparserVars["db_version"] = dbversion;
                }

                //On a new database, we just load the most current schema, and upgrade from there
                //This avoids potentially lengthy upgrades
                if (dbversion == -1)
                {
                    cmd.CommandText = PreparseSQL(schema, preparserVars);
                    cmd.ExecuteNonQuery();
                    UpgradeDatabase(connection, sourcefile, scripts, versions);
                    return;
                }
                else if (versions.Count > dbversion)
                {
                    // In some cases (mostly test setups)
                    // the database upgrades can happen within a second of each other
                    // causing the upgrades to fail. This scheme adds up to 15 seconds
                    // delay, making room for multiple rapid upgrade calls
                    var backupfile = string.Empty;
                    for (var i = 0; i < 10; i++)
                    {
                        backupfile = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(sourcefile),
                            Strings.DatabaseUpgrader.BackupFilenamePrefix + " " + System.IO.Path.GetFileNameWithoutExtension(sourcefile) + " " + (DateTime.Now + TimeSpan.FromSeconds(i * 1.5)).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".sqlite");

                        if (!System.IO.File.Exists(backupfile))
                            break;
                    }

                    try
                    {
                        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(backupfile)))
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupfile));

                        //Keep a backup
                        System.IO.File.Copy(sourcefile, backupfile, false);

                        for (int i = dbversion; i < versions.Count; i++)
                        {
                            IDbSchemaUpgrade dbCodeUpgrade;

                            // The versions in the registry are 1-based, the loop index is zero based.
                            bool hookFound = DbUpgradesRegistry.CodeChanges.TryGetValue(i + 1, out dbCodeUpgrade);

                            if (hookFound)
                            {
                                dbCodeUpgrade.BeforeSql(connection);
                            }

                            //TODO: Find a better way to split SQL statements, as there may be embedded semicolons
                            //in the SQL, like "UPDATE x WHERE y = ';';"

                            // Preparse before splitting to enable statement spanning conditional blocks
                            string versionscript = PreparseSQL(versions[i], preparserVars);

                            //We split them to get a better error message
                            foreach (string c in versionscript.Split(';'))
                                if (c.Trim().Length > 0)
                                {
                                    cmd.CommandText = c;
                                    cmd.ExecuteNonQuery();
                                }

                            if (hookFound)
                            {
                                dbCodeUpgrade.AfterSql(connection);
                            }

                            // after upgrade, db_version should have changed to i + 1. If logic changes, just requery.
                            preparserVars["db_version"] = i + 1;
                        }

                        //Update databaseversion, so we don't run the scripts again
                        cmd.CommandText = "Update version SET Version = " + versions.Count.ToString(CultureInfo.InvariantCulture);
                        cmd.ExecuteNonQuery();
                        applied = true;
                    }
                    catch (Exception ex)
                    {
                        connection.Close();
                        //Restore the database
                        System.IO.File.Copy(backupfile, sourcefile, true);
                        throw new Exception(Strings.DatabaseUpgrader.UpgradeFailure(cmd.CommandText, ex.Message), ex);
                    }
                }
            }

            SyncSchemaScriptTable(connection, scripts, applied);
        }
    }

    /// <summary>
    /// A numbered script embedded beside a database schema
    /// </summary>
    /// <param name="Version">The version the script upgrades to, or downgrades from</param>
    /// <param name="Filename">The file name of the script</param>
    /// <param name="Content">The script contents</param>
    public sealed record SchemaScriptFile(int Version, string Filename, string Content);

    /// <summary>
    /// The scripts embedded for one database schema
    /// </summary>
    /// <param name="Schema">The contents of <c>Schema.sql</c></param>
    /// <param name="Upgrades">The upgrade scripts, in version order</param>
    /// <param name="Downgrades">The downgrade scripts, in version order</param>
    public sealed record SchemaScriptSet(string Schema, IReadOnlyList<SchemaScriptFile> Upgrades, IReadOnlyList<SchemaScriptFile> Downgrades)
    {
        /// <summary>
        /// The newest schema version these scripts lead to
        /// </summary>
        public int LatestVersion => Upgrades.Count == 0 ? 0 : Upgrades.Max(x => x.Version);

        /// <summary>
        /// Gets the upgrade script that leads to the given version
        /// </summary>
        /// <param name="version">The version</param>
        /// <returns>The script, with several files for one version joined in order</returns>
        public string GetUpgradeScript(int version)
            => string.Concat(Upgrades.Where(x => x.Version == version).Select(x => x.Content));

        /// <summary>
        /// Gets the downgrade script that leaves the given version
        /// </summary>
        /// <param name="version">The version</param>
        /// <returns>The script, or <c>null</c> if there is none</returns>
        public string GetDowngradeScript(int version)
        {
            var files = Downgrades.Where(x => x.Version == version).ToList();
            return files.Count == 0 ? null : string.Concat(files.Select(x => x.Content));
        }
    }
}
