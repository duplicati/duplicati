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
        ListDestinationRestoreTargets
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

        using var client = new SourceProvider(url, "", forwardoptions, true);
        await client.Initialize(cancellationToken);

        var targetEntry = await client.GetEntry((path ?? "").TrimStart('/'), isFolder: true, cancellationToken).ConfigureAwait(false);
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

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}
