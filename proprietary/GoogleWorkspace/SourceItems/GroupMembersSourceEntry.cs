// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GroupMembersSourceEntry(SourceProvider provider, string parentPath, string groupEmail)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "members.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDirectoryServiceForGroups();
        var request = service.Members.List(groupEmail);
        var members = new List<Google.Apis.Admin.Directory.directory_v1.Data.Member>();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) break;
            request.PageToken = nextPageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.MembersValue != null)
            {
                members.AddRange(response.MembersValue);
            }
            nextPageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

        var json = JsonSerializer.Serialize(members, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GroupMember.ToString() },
            { "gsuite:Name", "members.json" },
            { "gsuite:Id", groupEmail }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
