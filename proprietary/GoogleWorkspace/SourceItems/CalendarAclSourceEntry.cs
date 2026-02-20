// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google;
using Google.Apis.Calendar.v3;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarAclSourceEntry(string parentPath, string calendarId, CalendarService service)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "acl.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        try
        {
            var request = service.Acl.List(calendarId);
            var acl = await request.ExecuteAsync(cancellationToken);
            var json = JsonSerializer.Serialize(acl, new JsonSerializerOptions { WriteIndented = true });
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new UserInformationException(
                Strings.CalendarAclSourceEntry.ForbiddenAccessError(
                    OptionsHelper.GOOGLE_AVOID_CALENDAR_ACL_OPTION,
                    CalendarService.Scope.Calendar),
                "GoogleCalendarACLPermissionError",
                ex);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.CalendarEventACL.ToString() },
            { "gsuite:Name", "acl.json" },
            { "gsuite:Id", calendarId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
