// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class OrganizationalUnitsSourceEntry(SourceProvider provider, string parentPath)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "organizational_units.json"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDirectoryService();
        var request = service.Orgunits.List("my_customer");
        request.Type = Google.Apis.Admin.Directory.directory_v1.OrgunitsResource.ListRequest.TypeEnum.All;
        var orgUnits = await request.ExecuteAsync(cancellationToken);

        var json = JsonSerializer.Serialize(orgUnits, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.OrganizationalUnit.ToString() },
            { "gsuite:Name", "organizational_units.json" },
            { "gsuite:id", "organizational_units" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
