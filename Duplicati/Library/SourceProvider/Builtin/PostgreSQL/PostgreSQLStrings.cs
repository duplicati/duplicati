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
    public static string Description => LC.L(@"Backs up a PostgreSQL database with pg_dump. The dump is read as a stream and stored as one entry per table, so a single table can be restored. Supports PostgreSQL 17 and 18.");
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
