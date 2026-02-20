// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.PeopleService.v1.Data;
using System.Text;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactDetailsSourceEntry(string parentPath, Person person)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(person))));

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ContactDetails.ToString() },
            { "gsuite:Name", person.Names?.FirstOrDefault()?.DisplayName ?? person.ResourceName },
            { "gsuite:Id", person.ResourceName },
            { "gsuite:Etag", person.ETag }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
