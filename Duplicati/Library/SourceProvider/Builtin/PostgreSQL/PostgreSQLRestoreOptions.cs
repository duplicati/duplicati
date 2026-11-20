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

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// The options of a restore to a PostgreSQL database, read from the target URL and the restore options
/// </summary>
public sealed class PostgreSQLRestoreOptions
{
    /// <summary>The option that selects what a restore does to the database</summary>
    public const string ModeOption = "postgresql-restore-mode";
    /// <summary>The mode that restores the whole database: its objects, its rows, then its indexes and constraints</summary>
    public const string ModeDatabase = "database";
    /// <summary>The mode that loads the rows of the restored tables into tables that exist</summary>
    public const string ModeTables = "tables";
    /// <summary>The modes a restore can run in</summary>
    public static readonly string[] Modes = [ModeDatabase, ModeTables];

    /// <summary>The option that creates a restored table that is missing, when only tables are restored</summary>
    public const string CreateTablesOption = "postgresql-restore-create-tables";
    /// <summary>The option that allows a whole database restore of a version where tables come from different dumps</summary>
    public const string AllowMixedVersionsOption = "postgresql-restore-allow-mixed-versions";
    /// <summary>The option that allows a whole database restore where tables or the large objects are missing</summary>
    public const string AllowMissingTablesOption = "postgresql-restore-allow-missing-tables";
    /// <summary>The restore option that selects the legacy restore engine, which writes entries out of order</summary>
    public const string RestoreLegacyOption = "restore-legacy";
    /// <summary>The restore option that allows replacing what exists in the target</summary>
    public const string OverwriteOption = "overwrite";
    /// <summary>The restore option that only reports what a restore would do</summary>
    public const string DryRunOption = "dry-run";
    /// <summary>The other name the restore engine accepts for <see cref="DryRunOption"/></summary>
    public const string DryRunAliasOption = "dryrun";

    /// <summary>The connection to the target database</summary>
    public required PostgreSQLOptions Connection { get; init; }
    /// <summary>True when only the rows of the restored tables are loaded</summary>
    public required bool TablesOnly { get; init; }
    /// <summary>True when a missing table is created before its rows are loaded</summary>
    public required bool CreateTables { get; init; }
    /// <summary>True when a version with tables from different dumps may be restored as a whole database</summary>
    public required bool AllowMixedVersions { get; init; }
    /// <summary>True when a whole database may be restored without the rows of every table</summary>
    public required bool AllowMissingTables { get; init; }
    /// <summary>True when what exists in the target may be replaced</summary>
    public required bool Overwrite { get; init; }
    /// <summary>True when the restore only reports what it would do, and nothing is written</summary>
    public required bool DryRun { get; init; }

    /// <summary>
    /// The command line arguments the restore provider supports
    /// </summary>
    /// <remarks>
    /// The options of the source are all accepted, as a restore to the database a backup was
    /// taken from is given the options of that source. The one that includes large objects in
    /// a backup has no effect here: a restore takes what the backup holds.
    /// </remarks>
    public static IList<ICommandLineArgument> SupportedCommands =>
    [
        .. PostgreSQLOptions.SupportedCommands,
        new CommandLineArgument(ModeOption, CommandLineArgument.ArgumentType.Enumeration, Strings.PostgreSQLRestoreProvider.ModeShort, Strings.PostgreSQLRestoreProvider.ModeLong, ModeDatabase, [], Modes),
        new CommandLineArgument(CreateTablesOption, CommandLineArgument.ArgumentType.Boolean, Strings.PostgreSQLRestoreProvider.CreateTablesShort, Strings.PostgreSQLRestoreProvider.CreateTablesLong, "false"),
        new CommandLineArgument(AllowMixedVersionsOption, CommandLineArgument.ArgumentType.Boolean, Strings.PostgreSQLRestoreProvider.AllowMixedVersionsShort, Strings.PostgreSQLRestoreProvider.AllowMixedVersionsLong, "false"),
        new CommandLineArgument(AllowMissingTablesOption, CommandLineArgument.ArgumentType.Boolean, Strings.PostgreSQLRestoreProvider.AllowMissingTablesShort, Strings.PostgreSQLRestoreProvider.AllowMissingTablesLong, "false"),
    ];

    /// <summary>
    /// Reads the options. The loader of the provider has already added the options in the URL,
    /// where a value in the URL wins over the same option given to the restore.
    /// </summary>
    /// <param name="options">The restore options, with those of the URL</param>
    /// <returns>The parsed options</returns>
    public static PostgreSQLRestoreOptions Parse(IReadOnlyDictionary<string, string?> options)
    {
        var mode = options.TryGetValue(ModeOption, out var modeValue) && !string.IsNullOrWhiteSpace(modeValue) ? modeValue : ModeDatabase;
        if (!Modes.Contains(mode, StringComparer.Ordinal))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.InvalidOption(ModeOption, mode), PostgreSQLEventIds.InvalidOption);

        // The legacy engine opens an entry once for each volume and writes it at the offsets of the blocks
        if (Library.Utility.Utility.ParseBoolOption(options, RestoreLegacyOption))
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.LegacyRestoreNotSupported(RestoreLegacyOption), PostgreSQLEventIds.RestoreLegacyNotSupported);

        return new PostgreSQLRestoreOptions
        {
            Connection = PostgreSQLOptions.Parse(options),
            TablesOnly = mode == ModeTables,
            CreateTables = Library.Utility.Utility.ParseBoolOption(options, CreateTablesOption),
            AllowMixedVersions = Library.Utility.Utility.ParseBoolOption(options, AllowMixedVersionsOption),
            AllowMissingTables = Library.Utility.Utility.ParseBoolOption(options, AllowMissingTablesOption),
            Overwrite = Library.Utility.Utility.ParseBoolOption(options, OverwriteOption),
            DryRun = Library.Utility.Utility.ParseBoolOption(options, DryRunOption) || Library.Utility.Utility.ParseBoolOption(options, DryRunAliasOption),
        };
    }
}
