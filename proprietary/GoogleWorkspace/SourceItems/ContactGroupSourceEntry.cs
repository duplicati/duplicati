// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.PeopleService.v1.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactGroupSourceEntry(string parentPath, ContactGroup group)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, EntryName(group) + ".json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    /// <summary>
    /// The name of the entry of a contact group: the last segment of its resource name.
    /// The path uses the id, as a group name is neither unique nor safe to use in a path.
    /// </summary>
    /// <param name="group">The contact group</param>
    /// <returns>The entry name</returns>
    internal static string EntryName(ContactGroup group)
        => group.ResourceName.Split('/').Last();

    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(group, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ContactGroup.ToString() },
            { "gsuite:Name", group.FormattedName ?? group.Name },
            { "gsuite:Id", group.ResourceName },
            { "gsuite:Etag", group.ETag }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
