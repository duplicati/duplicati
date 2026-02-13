// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarAclSourceEntry(SourceProvider provider, string parentPath, string userId, string calendarId)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "acl.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetCalendarService(userId);
        var request = service.Acl.List(calendarId);
        var acl = await request.ExecuteAsync(cancellationToken);

        var json = JsonSerializer.Serialize(acl, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }
}
