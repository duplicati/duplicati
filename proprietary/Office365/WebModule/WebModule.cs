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
        ListDestinationRestoreTargets
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

        if (op != Operation.ListDestination && op != Operation.ListDestinationRestoreTargets)
            throw new UserInformationException($"Unsupported operation: {op}", "UnsupportedOperation");

        if (string.IsNullOrWhiteSpace(url))
            throw new UserInformationException($"Missing URL", "MissingURL");

        var forwardoptions = new Dictionary<string, string?>()
        {
            { "store-metadata-content-in-database", "true" }
        };

        var uri = new Library.Utility.Uri(url);
        foreach (var key in uri.QueryParameters.AllKeys)
            forwardoptions[key!] = uri.QueryParameters[key];

        using var client = new SourceProvider(url, "", forwardoptions);
        await client.Initialize(cancellationToken);

        var targetEntry = await client.GetEntry(path ?? "", isFolder: true, cancellationToken).ConfigureAwait(false);
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

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}
