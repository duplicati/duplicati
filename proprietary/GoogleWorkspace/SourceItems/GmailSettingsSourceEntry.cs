// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Gmail.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GmailSettingsSourceEntry(string userId, string parentPath, GmailService service)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Settings")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(userId, this.Path, "filters.json", GmailSettingType.Filters, service);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(userId, this.Path, "forwarding.json", GmailSettingType.Forwarding, service);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(userId, this.Path, "vacation.json", GmailSettingType.Vacation, service);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(userId, this.Path, "signatures.json", GmailSettingType.Signatures, service);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailSettings.ToString() },
            { "gsuite:Name", "Settings" },
            { "gsuite:Id", "Settings" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
