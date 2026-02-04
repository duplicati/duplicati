// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SiteSourceEntry(string parentPath, File file)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, file.Name + ".site"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        // Google Sites cannot be exported via Drive API.
        // We serialize the metadata to JSON so we at least have a record of it.
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Site.ToString() },
            { "gsuite:Name", file.Name },
            { "gsuite:id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
