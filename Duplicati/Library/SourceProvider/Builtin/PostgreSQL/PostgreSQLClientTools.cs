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

using System.Diagnostics;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// Locates and runs the PostgreSQL client programs
/// </summary>
public static class PostgreSQLClientTools
{
    /// <summary>The dump program</summary>
    public const string PgDump = "pg_dump";
    /// <summary>The SQL client program</summary>
    public const string Psql = "psql";

    /// <summary>
    /// The server major versions the provider is tested against, newest first
    /// </summary>
    public static readonly int[] SupportedMajorVersions = [18, 17, 16, 15, 14];

    /// <summary>The first major version where pg_dump names large objects in its options; before it they are blobs</summary>
    private const int FirstMajorWithLargeObjectsOption = 16;
    /// <summary>The pg_dump option that leaves out large objects</summary>
    public const string NoLargeObjectsArgument = "--no-large-objects";
    /// <summary>The pg_dump option that leaves out large objects, before version 16</summary>
    public const string NoBlobsArgument = "--no-blobs";

    /// <summary>
    /// The folder the Debian packages install the client of a major version in; {0} is the major version
    /// </summary>
    private const string DebianClientFolder = "/usr/lib/postgresql/{0}/bin";
    /// <summary>The folder Postgres.app keeps the client of a major version in</summary>
    private const string PostgresAppClientFolder = "/Applications/Postgres.app/Contents/Versions/{0}/bin";
    /// <summary>The folder Homebrew installs the client of a major version in, on Apple silicon</summary>
    private const string HomebrewClientFolder = "/opt/homebrew/opt/postgresql@{0}/bin";
    /// <summary>The folder Homebrew installs the client of a major version in, on Intel</summary>
    private const string HomebrewIntelClientFolder = "/usr/local/opt/postgresql@{0}/bin";

    /// <summary>
    /// The folder the Windows installer puts the client of a major version in, below the program files folder
    /// </summary>
    private static readonly string WindowsClientFolder = Path.Combine("PostgreSQL", "{0}", "bin");
    /// <summary>The extension of a program on Windows</summary>
    private const string WindowsExecutableExtension = ".exe";

    /// <summary>
    /// The folders a client of a major version is installed in on this system, in the order they
    /// are tried; {0} is the major version
    /// </summary>
    public static readonly IReadOnlyList<string> PackagedClientFolders =
        OperatingSystem.IsMacOS() ? [PostgresAppClientFolder, HomebrewClientFolder, HomebrewIntelClientFolder]
        : OperatingSystem.IsWindows() ? WindowsClientFolders()
        : [DebianClientFolder];

    /// <summary>
    /// The folders of the Windows installer: below the program files folder, and below the one for
    /// 32-bit programs
    /// </summary>
    private static string[] WindowsClientFolders()
        => new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // The folder is not part of the format, so braces in it are kept as they are
            .Select(x => Path.Combine(x.Replace("{", "{{").Replace("}", "}}"), WindowsClientFolder))
            .ToArray();

    /// <summary>
    /// The file name of a client program on this system
    /// </summary>
    /// <param name="tool">The program name</param>
    /// <returns>The file name</returns>
    public static string ExecutableFileName(string tool)
        => OperatingSystem.IsWindows() ? tool + WindowsExecutableExtension : tool;

    /// <summary>
    /// The most text kept from the error output of a program
    /// </summary>
    private const int MaxErrorOutput = 8 * 1024;

    /// <summary>
    /// The seconds a connection attempt may take
    /// </summary>
    private const string ConnectTimeoutSeconds = "30";

    /// <summary>
    /// The name the connection shows on the server
    /// </summary>
    private const string ApplicationName = "duplicati";

    /// <summary>
    /// The output of a dump program: the archive stream, and how the program ended
    /// </summary>
    public interface IDumpOutput : IDisposable
    {
        /// <summary>The name of the program</summary>
        string Tool { get; }
        /// <summary>The standard output of the program</summary>
        Stream Output { get; }
        /// <summary>
        /// Waits for the program to end
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The exit code and what the program wrote as errors</returns>
        Task<(int ExitCode, string ErrorOutput)> WaitForExitAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// A client program that was started and whose output is read as a stream
    /// </summary>
    public sealed class RunningTool : IDumpOutput
    {
        private readonly Process m_process;
        private readonly Task<string> m_errorOutput;

        /// <summary>The name of the program</summary>
        public string Tool { get; }
        /// <summary>The standard output of the program</summary>
        public Stream Output => m_process.StandardOutput.BaseStream;

        internal RunningTool(string tool, Process process)
        {
            Tool = tool;
            m_process = process;
            m_errorOutput = ReadErrorOutputAsync(process.StandardError);
        }

        /// <summary>
        /// Waits for the program to end
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The exit code and what the program wrote as errors</returns>
        public async Task<(int ExitCode, string ErrorOutput)> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await m_process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (m_process.ExitCode, (await m_errorOutput.ConfigureAwait(false)).Trim());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (!m_process.HasExited)
                    m_process.Kill(true);
            }
            catch
            {
            }

            m_process.Dispose();
        }
    }

    /// <summary>
    /// Reads the error output of a program, keeping only the last part
    /// </summary>
    private static async Task<string> ReadErrorOutputAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length > MaxErrorOutput)
                sb.Remove(0, sb.Length - MaxErrorOutput);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds a client program of a major version: in the configured folder, in the folders a
    /// packaged client is installed in, and last on the search path
    /// </summary>
    /// <param name="tool">The program name</param>
    /// <param name="major">The major version</param>
    /// <param name="options">The provider options</param>
    /// <returns>The path of the program, or its bare name when it is left to the search path</returns>
    public static string LocateTool(string tool, int major, PostgreSQLOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ClientPath))
            return Path.Combine(options.ClientPath, tool);

        return FindPackagedTool(tool, major, PackagedClientFolders) ?? tool;
    }

    /// <summary>
    /// Finds a client program of a major version in the folders a packaged client is installed in
    /// </summary>
    /// <param name="tool">The program name</param>
    /// <param name="major">The major version</param>
    /// <param name="folders">The folders to look in, in order; {0} is the major version</param>
    /// <returns>The path of the program, or null if none of the folders has it</returns>
    public static string? FindPackagedTool(string tool, int major, IEnumerable<string> folders)
        => folders
            .Select(folder => Path.Combine(string.Format(folder, major), ExecutableFileName(tool)))
            .FirstOrDefault(File.Exists);

    /// <summary>
    /// Builds the start information for a client program. The connection, and the password in
    /// particular, is passed in the environment of the program and never as arguments.
    /// </summary>
    /// <param name="path">The program to run</param>
    /// <param name="arguments">The arguments</param>
    /// <param name="options">The provider options</param>
    /// <returns>The start information</returns>
    public static ProcessStartInfo BuildStartInfo(string path, IEnumerable<string> arguments, PostgreSQLOptions options)
    {
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        psi.Environment["PGHOST"] = options.Host;
        psi.Environment["PGPORT"] = options.Port.ToString();
        psi.Environment["PGDATABASE"] = options.Database;
        psi.Environment["PGUSER"] = options.Username;
        psi.Environment["PGSSLMODE"] = options.SslMode;
        psi.Environment["PGCONNECT_TIMEOUT"] = ConnectTimeoutSeconds;
        psi.Environment["PGAPPNAME"] = ApplicationName;
        if (options.Password != null)
            psi.Environment["PGPASSWORD"] = options.Password;
        if (options.SslRootCert != null)
            psi.Environment["PGSSLROOTCERT"] = options.SslRootCert;

        return psi;
    }

    /// <summary>
    /// The arguments for a dump that is written to standard output in the custom format, uncompressed
    /// </summary>
    /// <param name="options">The provider options</param>
    /// <param name="major">The major version of pg_dump</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> BuildDumpArguments(PostgreSQLOptions options, int major)
    {
        var arguments = new List<string> { "--format=custom", "--compress=0", "--no-password" };
        if (!options.IncludeLargeObjects)
            arguments.Add(major >= FirstMajorWithLargeObjectsOption ? NoLargeObjectsArgument : NoBlobsArgument);
        return arguments;
    }

    /// <summary>
    /// The arguments for running one statement and printing the result as unaligned rows
    /// </summary>
    /// <param name="sql">The statement</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> BuildQueryArguments(string sql)
        => ["--no-psqlrc", "--no-password", "--tuples-only", "--no-align", "--set=ON_ERROR_STOP=1", "--command=" + sql];

    /// <summary>
    /// Starts a program and hands back its output as a stream
    /// </summary>
    /// <param name="tool">The program name, for messages</param>
    /// <param name="startInfo">The start information</param>
    /// <returns>The running program</returns>
    public static RunningTool Start(string tool, ProcessStartInfo startInfo)
    {
        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("No process was started");
        }
        catch (Exception ex)
        {
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolNotStarted(tool, ex.Message), "PostgreSQLToolNotStarted", ex);
        }

        process.StandardInput.Close();
        return new RunningTool(tool, process);
    }

    /// <summary>
    /// Runs a program to its end and returns what it wrote
    /// </summary>
    /// <param name="tool">The program name, for messages</param>
    /// <param name="startInfo">The start information</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The standard output</returns>
    public static async Task<string> RunAsync(string tool, ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var running = Start(tool, startInfo);
        using var reader = new StreamReader(running.Output, Encoding.UTF8);
        var output = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var (exitCode, errorOutput) = await running.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(tool, exitCode, errorOutput), "PostgreSQLToolFailed");

        return output;
    }

    /// <summary>
    /// Runs one statement with psql
    /// </summary>
    /// <param name="psqlPath">The psql program</param>
    /// <param name="sql">The statement</param>
    /// <param name="options">The provider options</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The result, one row per line with columns separated by |</returns>
    public static Task<string> QueryAsync(string psqlPath, string sql, PostgreSQLOptions options, CancellationToken cancellationToken)
        => RunAsync(Psql, BuildStartInfo(psqlPath, BuildQueryArguments(sql), options), cancellationToken);

    /// <summary>
    /// Finds a psql to ask the server for its version with, before the version is known:
    /// the configured folder, the newest packaged client, and last the search path
    /// </summary>
    /// <param name="options">The provider options</param>
    /// <returns>The path of the program</returns>
    public static string LocateAnyPsql(PostgreSQLOptions options)
        => SupportedMajorVersions
            .Select(major => LocateTool(Psql, major, options))
            .FirstOrDefault(path => path != Psql)
            ?? Psql;

    /// <summary>
    /// Reads the major version from the output of a program run with --version,
    /// as in "pg_dump (PostgreSQL) 17.11 (Debian 17.11-1)"
    /// </summary>
    /// <param name="versionOutput">The output</param>
    /// <returns>The major version, or null if the output is not understood</returns>
    public static int? ParseToolMajorVersion(string versionOutput)
    {
        foreach (var word in versionOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(word.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length > 0 && word.Length > digits.Length && word[digits.Length] == '.' && int.TryParse(digits, out var major))
                return major;
        }

        return null;
    }

    /// <summary>
    /// Gets the major version from the server_version_num setting, as in 170011
    /// </summary>
    /// <param name="serverVersionNum">The setting value</param>
    /// <returns>The major version</returns>
    public static int MajorFromServerVersionNum(int serverVersionNum)
        => serverVersionNum / 10000;

    /// <summary>
    /// Checks that a program is of the major version the server needs
    /// </summary>
    /// <param name="tool">The program name</param>
    /// <param name="path">The program path</param>
    /// <param name="major">The major version needed</param>
    /// <param name="options">The provider options</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public static async Task VerifyToolVersionAsync(string tool, string path, int major, PostgreSQLOptions options, CancellationToken cancellationToken)
    {
        var output = (await RunAsync(tool, BuildStartInfo(path, ["--version"], options), cancellationToken).ConfigureAwait(false)).Trim();
        if (ParseToolMajorVersion(output) != major)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ClientVersionMismatch(tool, output, major), "PostgreSQLClientVersionMismatch");
    }
}
