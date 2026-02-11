// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GmailSettingsSourceEntry(SourceProvider provider, string userId, string parentPath)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Settings")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(provider, userId, this.Path, "filters.json", GmailSettingType.Filters);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(provider, userId, this.Path, "forwarding.json", GmailSettingType.Forwarding);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(provider, userId, this.Path, "vacation.json", GmailSettingType.Vacation);
        if (cancellationToken.IsCancellationRequested)
            yield break;
        yield return new GmailSettingFileSourceEntry(provider, userId, this.Path, "signatures.json", GmailSettingType.Signatures);
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
