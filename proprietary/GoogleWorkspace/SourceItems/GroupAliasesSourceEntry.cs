// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GroupAliasesSourceEntry(SourceProvider provider, string parentPath, string groupEmail)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "aliases.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDirectoryService();
        var request = service.Groups.Get(groupEmail);
        var group = await request.ExecuteAsync(cancellationToken);

        var aliases = new List<string>();
        
        if (group.Aliases != null)
        {
            aliases.AddRange(group.Aliases);
        }
        
        if (group.NonEditableAliases != null)
        {
            aliases.AddRange(group.NonEditableAliases);
        }

        var json = JsonSerializer.Serialize(aliases, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GroupAliases.ToString() },
            { "gsuite:Name", "aliases.json" },
            { "gsuite:Id", groupEmail }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
