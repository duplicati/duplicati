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
using Duplicati.Library.Interface;
using Microsoft.Data.Sqlite;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Keeps a <c>--version</c> on a queued command pointing at the backup versions it named
/// when the command was sent.
/// </summary>
/// <remarks>
/// A version number is a place in the list of backup versions, newest first. A command
/// sent from the command line page waits in the task queue, and a backup that runs before
/// it adds a version at the front and moves every number up by one. Left alone, the
/// command then acts on the version next to the one it named; for <c>delete</c> that is
/// the wrong backup version removed. The numbers are therefore turned into the versions'
/// times when the command is sent, and back into the current numbers when it runs.
/// </remarks>
internal static class CommandlineVersionPinning
{
    /// <summary>
    /// The versions a command named, by the time of each backup version
    /// </summary>
    /// <param name="ArgumentIndex">The index of the <c>--version</c> argument to rewrite.</param>
    /// <param name="DatabasePath">The local database the versions were read from.</param>
    /// <param name="Timestamps">The named versions' times, in epoch seconds, in the order they were named.</param>
    internal sealed record PinnedVersions(int ArgumentIndex, string DatabasePath, long[] Timestamps);

    /// <summary>
    /// Records which backup versions the command's <c>--version</c> names right now.
    /// </summary>
    /// <param name="args">The command's arguments, options written as <c>--name=value</c>.</param>
    /// <returns>The pinned versions, or <c>null</c> when there is nothing to pin: no
    /// <c>--version</c>, no local database to read the versions from, or a number that names
    /// no version, which the command reports itself as before.</returns>
    internal static PinnedVersions? Pin(string[] args)
    {
        // The last one wins, as it does for the command line parser
        var versionIndex = LastOptionIndex(args, "version");
        if (versionIndex < 0)
            return null;

        var dbpathIndex = LastOptionIndex(args, "dbpath");
        if (dbpathIndex < 0)
            return null;

        var dbpath = OptionValue(args[dbpathIndex]);
        if (string.IsNullOrWhiteSpace(dbpath) || !File.Exists(dbpath))
            return null;

        // Read the numbers the same way the operations do
        var versions = new Library.Main.Options(new Dictionary<string, string?> { ["version"] = OptionValue(args[versionIndex]) }).Version;
        if (versions == null || versions.Length == 0)
            return null;

        var timestamps = ReadFilesetTimestamps(dbpath);
        if (versions.Any(x => x < 0 || x >= timestamps.Length))
            return null;

        return new PinnedVersions(versionIndex, dbpath, versions.Select(x => timestamps[x]).ToArray());
    }

    /// <summary>
    /// Rewrites the command's <c>--version</c> with the current numbers of the pinned versions.
    /// </summary>
    /// <param name="args">The command's arguments.</param>
    /// <param name="pinned">The versions pinned when the command was sent.</param>
    /// <returns>A copy of the arguments with the <c>--version</c> rewritten.</returns>
    /// <exception cref="UserInformationException">A pinned version no longer exists, so the
    /// command is not run rather than run on other versions.</exception>
    internal static string[] Apply(string[] args, PinnedVersions pinned)
    {
        var timestamps = ReadFilesetTimestamps(pinned.DatabasePath);
        var numbers = new List<long>();
        foreach (var timestamp in pinned.Timestamps)
        {
            var number = Array.IndexOf(timestamps, timestamp);
            if (number < 0)
                throw new UserInformationException(
                    $"The backup version from {DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime} that --version named when this command was sent no longer exists, so the command was not run. Check the versions and send it again.",
                    "CommandlineVersionNoLongerExists");

            numbers.Add(number);
        }

        var result = (string[])args.Clone();
        result[pinned.ArgumentIndex] = "--version=" + string.Join(",", numbers);
        return result;
    }

    /// <summary>
    /// Reads the backup versions' times from the local database, newest first, which is
    /// the order the version numbers count in.
    /// </summary>
    /// <remarks>
    /// The database is opened read-only, without the operations' database lock. The
    /// command may be sent while a backup is writing to the same database, and waiting for
    /// that lock would read the list after the backup, with the numbers already moved.
    /// </remarks>
    /// <param name="dbpath">The path to the local database.</param>
    /// <returns>The versions' times in epoch seconds.</returns>
    private static long[] ReadFilesetTimestamps(string dbpath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbpath,
            Mode = SqliteOpenMode.ReadOnly,
            // A pooled connection would keep the file open after the command has run
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Timestamp"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC";

        var result = new List<long>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetInt64(0));

        return result.ToArray();
    }

    /// <summary>
    /// Finds the last argument that sets an option.
    /// </summary>
    /// <param name="args">The arguments to search.</param>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <returns>The index of the argument, or -1.</returns>
    private static int LastOptionIndex(string[] args, string name)
    {
        var prefix = "--" + name + "=";
        for (var i = args.Length - 1; i >= 0; i--)
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    /// <summary>
    /// Gets the value of a <c>--name=value</c> argument, without a surrounding pair of
    /// quotes, as the command line parser reads it.
    /// </summary>
    /// <param name="arg">The argument.</param>
    /// <returns>The value.</returns>
    private static string OptionValue(string arg)
    {
        var value = arg.Substring(arg.IndexOf('=') + 1);
        if (value.Length > 1 && value.StartsWith('"') && value.EndsWith('"'))
            value = value.Substring(1, value.Length - 2);

        return value;
    }
}
