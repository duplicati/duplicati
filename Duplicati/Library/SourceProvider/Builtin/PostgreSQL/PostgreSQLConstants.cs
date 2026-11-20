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

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// The arguments the provider gives the PostgreSQL client programs
/// </summary>
internal static class PostgreSQLArguments
{
    /// <summary>Never prompt for a password</summary>
    public const string NoPassword = "--no-password";
    /// <summary>psql: do not read the startup file of the user</summary>
    public const string NoPsqlrc = "--no-psqlrc";
    /// <summary>psql: print no informational messages</summary>
    public const string Quiet = "--quiet";
    /// <summary>psql: end at the first statement that fails</summary>
    public const string StopOnError = "--set=ON_ERROR_STOP=1";
    /// <summary>psql: print rows only, without headers and footers</summary>
    public const string TuplesOnly = "--tuples-only";
    /// <summary>psql: print rows unaligned</summary>
    public const string NoAlign = "--no-align";
    /// <summary>psql: the statement to run; the statement follows</summary>
    public const string CommandPrefix = "--command=";
    /// <summary>Print the version of the program</summary>
    public const string Version = "--version";

    /// <summary>pg_dump: write the custom archive format</summary>
    public const string CustomFormat = "--format=custom";
    /// <summary>pg_dump: write the archive uncompressed</summary>
    public const string NoCompression = "--compress=0";
    /// <summary>The client encoding of every session the programs open</summary>
    public const string ClientEncoding = "UTF8";
    /// <summary>
    /// pg_dump: write the archive in UTF-8, whatever the encoding of the database. The names in the
    /// table of contents are read as UTF-8, and two names that differ only in characters another
    /// encoding has would otherwise be read as the same name.
    /// </summary>
    public const string Utf8Encoding = "--encoding=" + ClientEncoding;
    /// <summary>pg_dump: leave out large objects</summary>
    public const string NoLargeObjects = "--no-large-objects";
    /// <summary>pg_dump: leave out large objects, before version 16</summary>
    public const string NoBlobs = "--no-blobs";

    /// <summary>pg_restore: do not set the owners of the objects</summary>
    public const string NoOwner = "--no-owner";
    /// <summary>pg_restore: do not apply grants</summary>
    public const string NoAcl = "--no-acl";
    /// <summary>pg_restore: end at the first error</summary>
    public const string ExitOnError = "--exit-on-error";
    /// <summary>pg_restore: run everything in one transaction</summary>
    public const string SingleTransaction = "--single-transaction";
    /// <summary>pg_restore: drop each object before it is created</summary>
    public const string Clean = "--clean";
    /// <summary>pg_restore: drop only what exists</summary>
    public const string IfExists = "--if-exists";
    /// <summary>pg_restore: write the statements to standard output instead of running them</summary>
    public const string FileStandardOutput = "--file=-";
    /// <summary>pg_restore: restore the indexes, constraints and the rest that follows the data</summary>
    public const string SectionPostData = "--section=post-data";
    /// <summary>pg_restore: the file that lists the entries to restore; the path follows</summary>
    public const string UseListPrefix = "--use-list=";
    /// <summary>pg_restore: the connection string of the target; the string follows</summary>
    public const string DbNamePrefix = "--dbname=";
}

/// <summary>
/// The ids of the messages and errors the provider reports
/// </summary>
internal static class PostgreSQLEventIds
{
    public const string ToolFailed = "PostgreSQLToolFailed";
    public const string ToolNotStarted = "PostgreSQLToolNotStarted";
    public const string UnexpectedArchive = "PostgreSQLUnexpectedArchive";
    public const string UnsupportedArchiveVersion = "PostgreSQLUnsupportedArchiveVersion";
    public const string UnsupportedServerVersion = "PostgreSQLUnsupportedServerVersion";
    public const string ClientVersionMismatch = "PostgreSQLClientVersionMismatch";
    public const string InvalidOption = "PostgreSQLInvalidOption";
    public const string MissingOption = "PostgreSQLMissingOption";

    public const string RestoreMixedVersions = "PostgreSQLRestoreMixedVersions";
    public const string RestoreNoTableOfContents = "PostgreSQLRestoreNoTableOfContents";
    public const string RestoreTableNotInArchive = "PostgreSQLRestoreTableNotInArchive";
    public const string RestoreClientTooOld = "PostgreSQLRestoreClientTooOld";
    public const string RestoreUnsupportedArchive = "PostgreSQLRestoreUnsupportedArchive";
    public const string RestoreUnknownEntry = "PostgreSQLRestoreUnknownEntry";
    public const string RestoreTargetTooOld = "PostgreSQLRestoreTargetTooOld";
    public const string RestoreTargetNotEmpty = "PostgreSQLRestoreTargetNotEmpty";
    public const string RestoreTableOfContentsDamaged = "PostgreSQLRestoreTableOfContentsDamaged";
    public const string RestorePartialDatabase = "PostgreSQLRestorePartialDatabase";
    public const string RestoreLegacyNotSupported = "PostgreSQLRestoreLegacyNotSupported";
    public const string RestoreFailed = "PostgreSQLRestoreFailed";
    public const string RestoreSelectionIncomplete = "PostgreSQLRestoreSelectionIncomplete";
}
