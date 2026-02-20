// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Google.Apis.Gmail.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal enum GmailSettingType
{
    Filters,
    Forwarding,
    Vacation,
    Signatures
}

internal class GmailSettingFileSourceEntry(string userId, string parentPath, string filename, GmailSettingType type, GmailService service)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, filename), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        object? data = null;

        switch (type)
        {
            case GmailSettingType.Filters:
                var filtersReq = service.Users.Settings.Filters.List(userId);
                data = await filtersReq.ExecuteAsync(cancellationToken);
                break;
            case GmailSettingType.Forwarding:
                var forwardingReq = service.Users.Settings.ForwardingAddresses.List(userId);
                var forwarding = await forwardingReq.ExecuteAsync(cancellationToken);
                var autoForwardingReq = service.Users.Settings.GetAutoForwarding(userId);
                var autoForwarding = await autoForwardingReq.ExecuteAsync(cancellationToken);
                data = new { ForwardingAddresses = forwarding, AutoForwarding = autoForwarding };
                break;
            case GmailSettingType.Vacation:
                var vacationReq = service.Users.Settings.GetVacation(userId);
                data = await vacationReq.ExecuteAsync(cancellationToken);
                break;
            case GmailSettingType.Signatures:
                var sendAsReq = service.Users.Settings.SendAs.List(userId);
                data = await sendAsReq.ExecuteAsync(cancellationToken);
                break;
        }

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", type.ToString() },
            { "gsuite:Name", filename },
            { "gsuite:Id", filename }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
