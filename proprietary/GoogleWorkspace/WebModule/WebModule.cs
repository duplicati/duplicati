// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;

namespace Duplicati.Proprietary.GoogleWorkspace;

public class WebModule : IWebModule
{
    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.Common.WebModuleDisplayName;

    public string Description => Strings.Common.WebModuleDescription;

    public enum Operation
    {
        ListDestination,
        ListDestinationRestoreTargets,
        CheckPermissions
    }

    private static readonly Operation DEFAULT_OPERATION = Operation.ListDestination;
    private const string KEY_OPERATION = "operation";
    private const string KEY_URL = "url";
    private const string KEY_PATH = "path";

    private static readonly IReadOnlySet<string> RESTORE_TARGET_TYPES = new HashSet<string>
    {
        SourceItemType.User.ToString(),
        SourceItemType.UserGmail.ToString(),
        SourceItemType.UserDrive.ToString(),
        SourceItemType.UserCalendar.ToString(),
        SourceItemType.UserContacts.ToString(),
        SourceItemType.UserTasks.ToString(),
        SourceItemType.UserKeep.ToString(),
        SourceItemType.UserChat.ToString(),
        SourceItemType.GmailLabel.ToString(),
        SourceItemType.DriveFolder.ToString(),
        SourceItemType.Calendar.ToString(),
        SourceItemType.TaskList.ToString(),
        SourceItemType.ContactGroup.ToString(),
        SourceItemType.Group.ToString(),
        SourceItemType.SharedDrives.ToString(),
        SourceItemType.Site.ToString(),
        SourceItemType.ChatSpace.ToString(),
    };

    private static readonly IReadOnlySet<string> RESTORE_TARGET_LEAF_TYPES = new HashSet<string>
    {
        SourceItemType.GmailLabel.ToString(),
        SourceItemType.DriveFolder.ToString(),
        SourceItemType.Calendar.ToString(),
        SourceItemType.TaskList.ToString(),
        SourceItemType.ContactGroup.ToString(),
    };

    private static readonly IReadOnlySet<string> RESTORE_TARGET_NONSELECTABLE_FOLDERS = new HashSet<string>
    {
        SourceItemType.MetaRoot.ToString(),
        SourceItemType.MetaRootUsers.ToString(),
        SourceItemType.MetaRootGroups.ToString(),
        SourceItemType.MetaRootSharedDrives.ToString(),
        SourceItemType.MetaRootSites.ToString(),
        SourceItemType.MetaRootOrganizationalUnits.ToString(),
    };


    public IList<ICommandLineArgument> SupportedCommands => [
            new CommandLineArgument(KEY_OPERATION, CommandLineArgument.ArgumentType.Enumeration, Strings.Common.WebModuleOperationShort, Strings.Common.WebModuleOperationLong, DEFAULT_OPERATION.ToString(), null, Enum.GetNames(typeof(Operation))),
            new CommandLineArgument(KEY_URL, CommandLineArgument.ArgumentType.String, Strings.Common.WebModuleURLShort, Strings.Common.WebModuleURLLong),
            new CommandLineArgument(KEY_PATH, CommandLineArgument.ArgumentType.String, Strings.Common.WebModulePathShort, Strings.Common.WebModulePathLong)
    ];

    public async Task<IDictionary<string, string>> Execute(IDictionary<string, string?> options, CancellationToken cancellationToken)
    {
        var op = Utility.ParseEnumOption(options.AsReadOnly(), KEY_OPERATION, DEFAULT_OPERATION);
        options.TryGetValue(KEY_URL, out var url);
        options.TryGetValue(KEY_PATH, out var path);

        if (!Enum.IsDefined(op))
            throw new UserInformationException($"Unsupported operation: {op}", "UnsupportedOperation");

        if (string.IsNullOrWhiteSpace(url))
            throw new UserInformationException($"Missing URL", "MissingURL");

        var forwardoptions = new Dictionary<string, string?>()
        {
            { "store-metadata-content-in-database", "true" }
        };

        var uri = new Library.Utility.RelaxedUri(url);
        foreach (var key in uri.QueryParameters.AllKeys)
            forwardoptions[key!] = uri.QueryParameters[key];

        using var client = new SourceProvider(url, "", forwardoptions, false);
        await client.InitializeAsync(cancellationToken);

        if (op == Operation.CheckPermissions)
            return await CheckPermissionsAsync(client, cancellationToken).ConfigureAwait(false);

        var targetEntry = await client.GetEntryAsync((path ?? "").TrimStart('/'), isFolder: true, cancellationToken).ConfigureAwait(false);
        if (targetEntry == null)
            throw new DirectoryNotFoundException($"Path not found: {path}");

        var result = new Dictionary<string, string>();
        await foreach (var entry in targetEntry.Enumerate(cancellationToken))
        {
            if (op == Operation.ListDestinationRestoreTargets)
            {
                if (!entry.IsFolder)
                    continue;
            }

            var targetpath = entry.Path;
            var metadata = new Dictionary<string, string?>();
            try
            {
                if (!entry.IsMetaEntry)
                    metadata = await entry.GetMinorMetadata(cancellationToken);
            }
            catch
            {
                // ignore metadata errors
            }

            // For restore targets, treat leafs as non-folders
            if (op == Operation.ListDestinationRestoreTargets)
            {
                var type = metadata.GetValueOrDefault("gsuite:Type");
                if (type != null)
                {
                    if (RESTORE_TARGET_LEAF_TYPES.Contains(type))
                        targetpath = targetpath.TrimEnd(Path.DirectorySeparatorChar);

                    if (!RESTORE_TARGET_NONSELECTABLE_FOLDERS.Contains(type) && !RESTORE_TARGET_TYPES.Contains(type))
                        continue;
                }
            }

            result[targetpath] = JsonSerializer.Serialize(metadata);
        }

        return result;

    }

    /// <summary>
    /// The result key under which the permission status list JSON is returned.
    /// </summary>
    private const string PERMISSIONS_RESULT_KEY = "permissions";

    /// <summary>
    /// Compares the OAuth scopes granted to the configured credentials with the scopes
    /// required for backup and restore operations. Granted scopes that are not required
    /// are included in the report, flagged as not needed, so that over-privileged
    /// credentials can be identified.
    /// </summary>
    /// <param name="client">The initialized source provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary containing a single JSON-serialized list of <see cref="PermissionStatus"/>.</returns>
    private static async Task<IDictionary<string, string>> CheckPermissionsAsync(SourceProvider client, CancellationToken cancellationToken)
    {
        var probe = GoogleScopes.Required.Concat(GoogleScopes.KnownExtras).Select(s => s.Name);
        var granted = await client.ApiHelper.GetGrantedScopesAsync(probe, cancellationToken).ConfigureAwait(false);

        // A scope is enabled when it is granted directly, or when a granted write scope
        // covers it (e.g. gmail.modify includes gmail.readonly access)
        var result = GoogleScopes.Required
            .Select(s => new PermissionStatus
            {
                Name = s.Name,
                Description = s.Description,
                RequiredForBackup = s.RequiredForBackup,
                RequiredForRestore = s.RequiredForRestore,
                Enabled = granted.Contains(s.Name) || (s.CoveredBy != null && granted.Contains(s.CoveredBy))
            })
            .ToList();

        // Include granted scopes that are not required for backup or restore
        var requiredNames = new HashSet<string>(GoogleScopes.Required.Select(s => s.Name), StringComparer.Ordinal);
        var extraDescriptions = GoogleScopes.KnownExtras.ToDictionary(s => s.Name, s => s.Description, StringComparer.Ordinal);
        foreach (var extra in granted.Where(g => !requiredNames.Contains(g)).OrderBy(g => g, StringComparer.Ordinal))
        {
            result.Add(new PermissionStatus
            {
                Name = extra,
                Description = extraDescriptions.GetValueOrDefault(extra, ""),
                RequiredForBackup = false,
                RequiredForRestore = false,
                Enabled = true
            });
        }

        return new Dictionary<string, string>
        {
            [PERMISSIONS_RESULT_KEY] = JsonSerializer.Serialize(result)
        };
    }

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();

    /// <summary>
    /// The status of a single required scope returned by <see cref="Operation.CheckPermissions"/>.
    /// </summary>
    private sealed class PermissionStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public required string Name { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public required string Description { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("requiredForBackup")]
        public bool RequiredForBackup { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("requiredForRestore")]
        public bool RequiredForRestore { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("enabled")]
        public bool Enabled { get; init; }
    }
}
