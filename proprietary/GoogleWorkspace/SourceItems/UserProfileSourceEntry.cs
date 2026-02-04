// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class UserProfileSourceEntry(SourceProvider provider, string parentPath, string userId)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "profile.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDirectoryService();
        var request = service.Users.Get(userId);
        var user = await request.ExecuteAsync(cancellationToken);

        var json = JsonSerializer.Serialize(user, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.UserProfile.ToString() },
            { "gsuite:Name", "profile.json" },
            { "gsuite:id", userId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
