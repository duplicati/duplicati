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

using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.SourceProvider.Strings;

internal static class PostgreSQLSourceProvider
{
    public static string DisplayName => LC.L(@"PostgreSQL database");
    public static string Description => LC.L(@"Backs up a PostgreSQL database with pg_dump. The dump is read as a stream and stored as one entry per table, so a single table can be restored. Supports PostgreSQL 14 to 18.");
    public static string HostShort => LC.L(@"The database server");
    public static string HostLong => LC.L(@"The host name or address of the PostgreSQL server.");
    public static string PortShort => LC.L(@"The server port");
    public static string PortLong => LC.L(@"The port the PostgreSQL server listens on.");
    public static string DatabaseShort => LC.L(@"The database to back up");
    public static string DatabaseLong => LC.L(@"The name of the database to back up. A backup source covers exactly one database.");
    public static string UsernameShort => LC.L(@"The role to connect as");
    public static string UsernameLong => LC.L(@"The role used to read the database. A role that can read every table is enough; it does not need to write.");
    public static string PasswordShort => LC.L(@"The password of the role");
    public static string PasswordLong => LC.L(@"The password of the role. It is given to the client programs through their environment and never on a command line.");
    public static string SslModeShort => LC.L(@"The TLS mode of the connection");
    public static string SslModeLong => LC.L(@"How the connection is encrypted and the server verified, with the values PostgreSQL uses for sslmode.");
    public static string SslRootCertShort => LC.L(@"The root certificate file");
    public static string SslRootCertLong => LC.L(@"The path to a file with the root certificate that the server certificate is verified against, for the verify-ca and verify-full modes.");
    public static string SslRootCertContentShort => LC.L(@"The root certificate");
    public static string SslRootCertContentLong => LC.L(@"The root certificate that the server certificate is verified against, as PEM text, for the verify-ca and verify-full modes. Use this where there is no file to point to.");
    public static string RootCertGivenTwice(string fileOption, string contentOption) => LC.L(@"Set the root certificate with --{0} or with --{1}, not with both", fileOption, contentOption);
    public static string RootCertNotPem(string option) => LC.L(@"The value of --{0} is not a certificate in PEM format", option);
    public static string MaxDatabaseSizeShort => LC.L(@"The largest database to back up");
    public static string MaxDatabaseSizeLong => LC.L(@"The largest size the database may have, as the server reports it. A backup of a larger database fails before anything is read. Without this there is no limit.");
    public static string DatabaseTooLarge(string database, string size, string limit, string option) => LC.L(@"The database {0} is {1}, which is more than the {2} a backup of it may cover (--{3})", database, size, limit, option);
    public static string ListedEntryNotReadable(string path) => LC.L(@"The entry {0} was listed to be shown and has no dump to be read from", path);
    public static string IncludeLargeObjectsShort => LC.L(@"Include large objects");
    public static string IncludeLargeObjectsLong => LC.L(@"Include large objects in the backup. The role must be able to read every large object, or the backup fails.");
    public static string ClientPathShort => LC.L(@"The folder with the PostgreSQL client programs");
    public static string ClientPathLong => LC.L(@"The folder that holds pg_dump and psql. If not set, the standard installation folders for the server version are used, and then the search path. On macOS these are the folders of Postgres.app and Homebrew, and on Windows the folder of the PostgreSQL installer.");

    public static string MissingOption(string name) => LC.L(@"The option --{0} is required for a PostgreSQL source", name);
    public static string InvalidOption(string name, string value) => LC.L(@"The value ""{1}"" is not valid for the option --{0}", name, value);
    public static string UnsupportedServerVersion(int major, string supported) => LC.L(@"The server runs PostgreSQL {0}. The supported versions are {1}.", major, supported);
    public static string ClientVersionMismatch(string tool, string found, int major) => LC.L(@"The program {0} is version ""{1}"", but the server needs version {2}. Install the PostgreSQL {2} client programs or set --postgresql-client-path.", tool, found, major);
    public static string ToolFailed(string tool, int exitCode, string message) => LC.L(@"{0} failed with exit code {1}: {2}", tool, exitCode, message);
    public static string ToolNotStarted(string tool, string message) => LC.L(@"Failed to start {0}: {1}", tool, message);
    public static string UnsupportedArchiveVersion(string version, string supported) => LC.L(@"pg_dump wrote archive format {0}. The supported formats are {1}.", version, supported);
    public static string UnexpectedArchive(string what) => LC.L(@"The output of pg_dump is not the expected archive: {0}", what);
    public static string DumpEndedEarly(string message) => LC.L(@"The dump ended before it was complete: {0}", message);
    public static string TableOpenedTwice(string path) => LC.L(@"The entry {0} can only be read once", path);
    public static string TablePassed(string path) => LC.L(@"The data for {0} was already passed in the dump and cannot be read out of order", path);
    public static string TableNotInDump(string path) => LC.L(@"The dump has no data for {0}", path);
}

internal static class PostgreSQLRestoreProvider
{
    public static string DisplayName => LC.L(@"PostgreSQL database");
    public static string Description => LC.L(@"Restores a backup of a PostgreSQL database into a database. The rows are loaded as they are restored, so nothing the size of the database is written to disk. Supports PostgreSQL 14 to 18.");
    public static string ModeShort => LC.L(@"What the restore does to the database");
    public static string ModeLong => LC.L(@"Use ""database"" to restore the whole database: its objects are created, the rows are loaded, and then the indexes and constraints are created. This needs every table of the backup to be part of the restore. Use ""tables"" to load only the rows of the restored tables into tables that exist. The tables are loaded independently of each other with their constraints in place, so a table whose foreign keys refer to another restored table can fail to load, and a table that other tables refer to cannot be replaced with --overwrite. Large objects that exist are only replaced with --overwrite. In ""database"" mode, --overwrite drops and recreates every object of the backup before any rows are loaded.");
    public static string CreateTablesShort => LC.L(@"Create missing tables");
    public static string CreateTablesLong => LC.L(@"When only tables are restored, create a restored table that does not exist in the database before its rows are loaded. Only the table is created, without its indexes and constraints.");
    public static string AllowMixedVersionsShort => LC.L(@"Restore a database with tables from different dumps");
    public static string AllowMixedVersionsLong => LC.L(@"A backup version where the dump failed part-way holds some tables from an earlier dump, and they may not fit the rest of the database. A restore of the whole database from such a version, or one where it cannot be told which dump the entries come from (as with --skip-metadata), is refused unless this option is set.");
    public static string AllowMissingTablesShort => LC.L(@"Restore a database where tables are missing");
    public static string AllowMissingTablesLong => LC.L(@"A restore of the whole database needs the rows of every table, and the large objects, of the backup. A table can be missing because a filter left it out of the backup or the restore. Set this option to create the indexes and constraints anyway, with the missing tables left empty.");
    public static string LegacyRestoreNotSupported(string option) => LC.L(@"A restore to a PostgreSQL database cannot use --{0}, as the database entries can only be written from their start to their end", option);
    public static string TableOfContentsDamaged(string message) => LC.L(@"The table of contents of the database (the entry ""toc"") was not restored completely, so nothing can be restored to the database: {0}", message);
    public static string EntryRunIdsUnknown(string option) => LC.L(@"It cannot be told which dump the restored entries come from, as their metadata was not restored (as with --skip-metadata), so the restore to the database was stopped. Restore with the metadata, or set --{0} to restore this version anyway.", option);

    public static string TargetNotEmpty(string database, long tables) => LC.L(@"The database {0} already has {1} tables. A restore of the whole database needs an empty database, unless the restore is allowed to replace what exists (--overwrite).", database, tables);
    public static string TableOfContentsMissing => LC.L(@"The restore does not include the table of contents of the database (the entry ""toc""). Restore the whole database, or use --postgresql-restore-mode=tables to load only the rows of the selected tables.");
    public static string TableOfContentsNeeded(string what) => LC.L(@"{0} cannot be restored without the table of contents of the database (the entry ""toc""). Include it in the restore.", what);
    public static string NotADatabaseEntry(string path) => LC.L(@"The entry {0} is not part of a PostgreSQL backup and cannot be restored to a database", path);
    public static string TableNotInArchive(string table) => LC.L(@"The table {0} is not in the table of contents of this version of the backup", table);
    public static string ReadNotSupported(string path) => LC.L(@"The entry {0} cannot be read back from the database", path);
    public static string DataAfterEnd(string table) => LC.L(@"The rows of {0} continue after their end marker", table);
    public static string TableIncomplete(string table) => LC.L(@"The rows of {0} ended before their end marker, so nothing was loaded", table);
    public static string TableHasRows(string table) => LC.L(@"The table {0} already has rows. Allow the restore to replace what exists (--overwrite) to replace them.", table);
    public static string PartialDatabase(int restored, int total, string missing, string option) => LC.L(@"The restore loaded {0} of the {1} tables and large objects of the database, so the indexes and constraints were not created. Not restored: {2}. Restore the whole database, use --postgresql-restore-mode=tables, or set --{3} if the missing entries were left out of the backup on purpose.", restored, total, missing, option);
    public static string PartialDatabaseWarning(int restored, int total, string missing) => LC.L(@"The restore loaded {0} of the {1} tables and large objects of the database. The indexes and constraints are created with these left empty: {2}", restored, total, missing);
    public static string MixedVersions(string tables, string option) => LC.L(@"This version of the backup holds entries from an earlier dump, so the database may not be consistent: {0}. The rows are loaded, but the indexes and constraints were not created. Restore another version, or set --{1} to restore this one.", tables, option);
    public static string TablesFromOtherDump(string tables, string option) => LC.L(@"These tables come from an earlier dump than the restored table of contents, so their rows were loaded with columns that may not be theirs: {0}. Check the tables, restore them without the table of contents (the entry ""toc""), or set --{1} to accept this.", tables, option);
    public static string LargeObjectsStopped => LC.L(@"The restore was stopped before the large objects were restored");
    public static string SelectionIncomplete(string missing, string option) => LC.L(@"A restore of the whole database needs every table and the large objects of the backup, and these are not part of the restore: {0}. Nothing was changed in the database. Restore the whole database, use --postgresql-restore-mode=tables, or set --{1} to restore without them.", missing, option);
    public static string StepsFailed(string messages) => LC.L(@"The restore to the database failed: {0}", messages);
    public static string TargetOlderThanSource(int source, int target) => LC.L(@"The backup is of a PostgreSQL {0} database and the target server runs PostgreSQL {1}. A whole database can only be restored to the same or a newer version. Use --postgresql-restore-mode=tables to load the rows of tables into tables that exist.", source, target);
    public static string ClientTooOld(string tool) => LC.L(@"The program {0} is too old a release of its version to restore safely. Install the current PostgreSQL client programs.", tool);
    public static string UnsupportedArchiveClient(string version) => LC.L(@"The backup was made with pg_dump ""{0}"", which is not a version this restore supports", version);
}

internal static class PostgreSQLWebModule
{
    public static string DisplayName => LC.L(@"PostgreSQL database checks");
    public static string Description => LC.L(@"Checks a PostgreSQL database before it is backed up or restored to: whether the role can do what is needed, and how much the database holds.");
    public static string OperationShort => LC.L(@"The check to run");
    public static string OperationLong => LC.L(@"Use ""CheckPermissions"" to check what the role can do, and ""CountItems"" to count the tables and read the size of the database.");
    public static string UrlShort => LC.L(@"The URL of the database");
    public static string UrlLong => LC.L(@"The URL of the database, as it is given for a backup.");
    public static string PurposeShort => LC.L(@"What the database is checked for");
    public static string PurposeLong => LC.L(@"Use ""restore"" to check the database as the target of a restore. Without this it is checked as the source of a backup.");
    public static string RequireEncryptionShort => LC.L(@"Require an encrypted connection");
    public static string RequireEncryptionLong => LC.L(@"Fail the check when the connection to the database is not encrypted. Without this a connection that is not encrypted is only reported.");

    public static string MissingUrl => LC.L(@"The URL of the database is missing");
    public static string NotADatabaseUrl(string scheme) => LC.L(@"The URL is not a {0} URL", scheme);
    public static string UnsupportedOperation(string operation) => LC.L(@"The operation ""{0}"" is not supported", operation);
    public static string UnexpectedAnswer => LC.L(@"The server answered a check with something other than what was asked for");
    public static string NamesAndMore(string names, long more) => LC.L(@"{0} and {1} more", names, more);

    public static string Connected(string database, string host, int port, string username) => LC.L(@"Connected to the database {0} on {1}:{2} as {3}.", database, host, port, username);
    public static string Encrypted => LC.L(@"The connection is encrypted with TLS.");
    public static string NotEncrypted => LC.L(@"The connection is not encrypted.");
    public static string ServerVersion(string version) => LC.L(@"The server runs PostgreSQL {0}.", version);
    public static string ServerVersionNotBackedUp(string version, string supported) => LC.L(@"The server runs PostgreSQL {0}. Only PostgreSQL {1} can be backed up.", version, supported);
    public static string ServerVersionNotRestoredTo(string version, string supported) => LC.L(@"The server runs PostgreSQL {0}. Only PostgreSQL {1} can be restored to.", version, supported);
    public static string TablesReadable(long tables) => LC.L(@"The role can read all {0} table(s).", tables);
    public static string TablesNotReadable(string names) => LC.L(@"The role cannot read {0}.", names);
    public static string NoRowSecurity => LC.L(@"No table has row-level security.");
    public static string RowSecurityBypassed(long tables) => LC.L(@"{0} table(s) have row-level security, and the role bypasses it.", tables);
    public static string RowSecurityHidesRows(long tables) => LC.L(@"{0} table(s) have row-level security, and the role neither has BYPASSRLS nor is a superuser, so it does not see every row.", tables);
    public static string NoLargeObjects => LC.L(@"The database has no large objects.");
    public static string LargeObjectsLeftOut(long count) => LC.L(@"The database has {0} large object(s). They are not in the backup: the backup is set up to leave large objects out.", count);
    public static string LargeObjectsReadable(long count) => LC.L(@"The role can read all {0} large object(s), and they are in the backup.", count);
    public static string LargeObjectsNotReadable(long unreadable, long count, string oids) => LC.L(@"The role cannot read {0} of the {1} large object(s): OID {2}.", unreadable, count, oids);
    public static string Superuser => LC.L(@"The role is a superuser. A backup only reads; use a role that can do no more than that.");
    public static string NotSuperuser => LC.L(@"The role is not a superuser.");
    public static string CanCreate => LC.L(@"The role can create schemas in the database, which a restore of the whole database does.");
    public static string CannotCreate => LC.L(@"The role cannot create schemas in the database. A restore of the whole database needs that; loading rows into tables that exist does not.");
    public static string NoTables => LC.L(@"The database has no tables.");
    public static string HasTables(long tables) => LC.L(@"The database has {0} table(s). A restore of the whole database stops unless it may replace what exists.", tables);
}
