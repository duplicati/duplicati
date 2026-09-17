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
        CheckPermissions,
        CountItems
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

        if (op == Operation.CountItems)
            return await CountItemsAsync(client, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// The result key under which the item count JSON is returned.
    /// </summary>
    private const string COUNT_RESULT_KEY = "counts";

    /// <summary>
    /// Counts the number of top-level items (users, groups, shared drives, sites) and, for
    /// users, breaks the accounts down by whether they consume a Duplicati license seat.
    /// </summary>
    /// <remarks>
    /// Every classification is decided from the listing itself, so the cost is one paged listing
    /// per top-level type and no request is made per item. That keeps the count usable on
    /// tenants with tens of thousands of accounts.
    /// </remarks>
    /// <param name="client">The initialized source provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary containing a single JSON-serialized <see cref="CountResult"/>.</returns>
    private static async Task<IDictionary<string, string>> CountItemsAsync(SourceProvider client, CancellationToken cancellationToken)
    {
        var result = new CountResult();

        // Users
        await foreach (var user in client.ListAllUsersAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Users.Total++;

            switch (SourceProvider.ClassifyUser(user))
            {
                case SourceProvider.UserCategory.Active:
                    result.Users.Active++;
                    break;
                case SourceProvider.UserCategory.Suspended:
                    result.Users.Suspended++;
                    break;
                case SourceProvider.UserCategory.Archived:
                    result.Users.Archived++;
                    break;
            }
        }

        // Groups
        await foreach (var _ in client.ListAllGroupsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Groups.Total++;
        }

        // Shared drives
        await foreach (var _ in SourceProvider.ListAllSharedDrivesAsync(client.ApiHelper.GetDriveService(), cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.SharedDrives.Total++;
        }

        // Sites
        await foreach (var _ in client.ListAllSitesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Sites.Total++;
        }

        return new Dictionary<string, string>
        {
            [COUNT_RESULT_KEY] = JsonSerializer.Serialize(result)
        };
    }

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();

    /// <summary>
    /// The item-count breakdown returned by <see cref="Operation.CountItems"/>.
    /// </summary>
    private sealed class CountResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("users")]
        public UserCounts Users { get; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("groups")]
        public TotalCounts Groups { get; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("sharedDrives")]
        public TotalCounts SharedDrives { get; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("sites")]
        public TotalCounts Sites { get; } = new();
    }

    /// <summary>
    /// The user item-count breakdown. Only <see cref="Active"/> accounts require a license
    /// seat; <see cref="Suspended"/> and <see cref="Archived"/> accounts are backed up for free.
    /// </summary>
    private sealed class UserCounts
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public int Total { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("active")]
        public int Active { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("suspended")]
        public int Suspended { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("archived")]
        public int Archived { get; set; }
    }

    /// <summary>
    /// The item count for a top-level type where every item requires a seat.
    /// </summary>
    private sealed class TotalCounts
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public int Total { get; set; }
    }

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
