using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public class WebModule : IWebModule
{
    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.WebModuleDisplayName;

    public string Description => Strings.WebModuleDescription;

    public enum Operation
    {
        ListDestination,
        ListDestinationRestoreTargets,
        CountItems
    }

    private static readonly Operation DEFAULT_OPERATION = Operation.ListDestination;
    private const string KEY_OPERATION = "operation";
    private const string KEY_URL = "url";
    private const string KEY_PATH = "path";

    private static readonly IReadOnlySet<string> RESTORE_TARGET_TYPES = new HashSet<string>
    {
        SourceItemType.User.ToString(),
        SourceItemType.UserMailbox.ToString(),
        SourceItemType.UserMailboxFolder.ToString(),
        SourceItemType.TaskList.ToString(),
        SourceItemType.Planner.ToString(),
        SourceItemType.PlannerBucket.ToString(),
        SourceItemType.Group.ToString(),
        SourceItemType.Drive.ToString(),
        SourceItemType.DriveFolder.ToString(),
        SourceItemType.Calendar.ToString(),
        SourceItemType.CalendarGroup.ToString(),
        SourceItemType.Chat.ToString(),
        SourceItemType.ChatMessage.ToString(),
        SourceItemType.Notebook.ToString(),
        SourceItemType.Site.ToString(),
    };

    private static readonly IReadOnlySet<string> RESTORE_TARGET_LEAF_TYPES = new HashSet<string>
    {
        SourceItemType.TaskListTask.ToString(),
        SourceItemType.PlannerBucket.ToString(),
        SourceItemType.DriveFolder.ToString(),
        SourceItemType.Calendar.ToString(),
        SourceItemType.ChatMessage.ToString(),
        SourceItemType.Notebook.ToString(),
    };

    private static readonly IReadOnlySet<string> RESTORE_TARGET_NONSELELCTABLE_FOLDERS = new HashSet<string>
    {
        SourceItemType.MetaRoot.ToString(),
        SourceItemType.MetaRootUsers.ToString(),
        SourceItemType.MetaRootGroups.ToString(),
        SourceItemType.MetaRootSites.ToString(),
    };


    public IList<ICommandLineArgument> SupportedCommands => [
            new CommandLineArgument(KEY_OPERATION, CommandLineArgument.ArgumentType.Enumeration, Strings.WebModuleOperationShort, Strings.WebModuleOperationLong, DEFAULT_OPERATION.ToString(), null, Enum.GetNames(typeof(Operation))),
            new CommandLineArgument(KEY_URL, CommandLineArgument.ArgumentType.String, Strings.WebModuleURLShort, Strings.WebModuleURLLong),
            new CommandLineArgument(KEY_PATH, CommandLineArgument.ArgumentType.String, Strings.WebModulePathShort, Strings.WebModulePathLong)
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

        var uri = new Library.Utility.CompatUri(url);
        foreach (var key in uri.QueryParameters.AllKeys)
            forwardoptions[key!] = uri.QueryParameters[key];

        using var client = new SourceProvider(url, "", forwardoptions);
        await client.InitializeAsync(cancellationToken);

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
                var type = metadata.GetValueOrDefault("o365:Type");
                if (type != null)
                {
                    if (RESTORE_TARGET_LEAF_TYPES.Contains(type))
                        targetpath = targetpath.TrimEnd(Path.DirectorySeparatorChar);

                    if (!RESTORE_TARGET_NONSELELCTABLE_FOLDERS.Contains(type) && !RESTORE_TARGET_TYPES.Contains(type))
                        continue;
                }
            }

            result[targetpath] = JsonSerializer.Serialize(metadata);
        }

        return result;

    }

    /// <summary>
    /// The result key under which the item-count breakdown JSON is returned.
    /// </summary>
    private const string COUNT_RESULT_KEY = "counts";

    /// <summary>
    /// Counts the number of top-level items (users, groups, sites) and, within each
    /// top-level type, breaks the items down by whether they consume a Duplicati license
    /// seat and by sub-type.
    /// </summary>
    /// <param name="client">The initialized source provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary containing a single JSON-serialized <see cref="CountResult"/>.</returns>
    private static async Task<IDictionary<string, string>> CountItemsAsync(SourceProvider client, CancellationToken cancellationToken)
    {
        var result = new CountResult();

        // Users
        await foreach (var user in client.RootApi.ListAllUsersAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Users.Total++;

            switch (await client.ClassifyUserAsync(user, cancellationToken).ConfigureAwait(false))
            {
                case SourceProvider.UserSeatCategory.Licensed:
                    result.Users.Licensed++;
                    break;
                case SourceProvider.UserSeatCategory.Unlicensed:
                    result.Users.Unlicensed++;
                    break;
                case SourceProvider.UserSeatCategory.SharedMailboxWithStorage:
                    result.Users.SharedMailboxWithStorage++;
                    break;
                case SourceProvider.UserSeatCategory.SharedMailboxWithoutStorage:
                    result.Users.SharedMailboxWithoutStorage++;
                    break;
            }
        }

        // Groups
        await foreach (var group in client.RootApi.ListAllGroupsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Groups.Total++;

            if (SourceProvider.GroupCountsAsSeat(group))
                result.Groups.Unified++;
            else
                result.Groups.NotUnified++;
        }

        // Sites
        await foreach (var site in client.RootApi.ListAllSitesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Sites.Total++;

            switch (SourceProvider.ClassifySite(site))
            {
                case SourceProvider.SiteCategory.Group:
                    result.Sites.Group++;
                    break;
                case SourceProvider.SiteCategory.Classic:
                    result.Sites.Classic++;
                    break;
                case SourceProvider.SiteCategory.Communication:
                    result.Sites.Communication++;
                    break;
                case SourceProvider.SiteCategory.Personal:
                    result.Sites.Personal++;
                    break;
                default:
                    result.Sites.Other++;
                    break;
            }
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
        public GroupCounts Groups { get; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("sites")]
        public SiteCounts Sites { get; } = new();
    }

    /// <summary>
    /// The user item-count breakdown. <see cref="Licensed"/> and
    /// <see cref="SharedMailboxWithStorage"/> require a license seat; the remainder do not.
    /// </summary>
    private sealed class UserCounts
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public int Total { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("licensed")]
        public int Licensed { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("unlicensed")]
        public int Unlicensed { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sharedMailboxWithStorage")]
        public int SharedMailboxWithStorage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sharedMailboxWithoutStorage")]
        public int SharedMailboxWithoutStorage { get; set; }
    }

    /// <summary>
    /// The group item-count breakdown. Only <see cref="Unified"/> groups require a seat.
    /// </summary>
    private sealed class GroupCounts
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public int Total { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("unified")]
        public int Unified { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("notUnified")]
        public int NotUnified { get; set; }
    }

    /// <summary>
    /// The site item-count breakdown.
    /// </summary>
    private sealed class SiteCounts
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public int Total { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("group")]
        public int Group { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("classic")]
        public int Classic { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("communication")]
        public int Communication { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("personal")]
        public int Personal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("other")]
        public int Other { get; set; }
    }
}
