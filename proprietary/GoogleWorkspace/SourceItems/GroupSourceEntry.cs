// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Admin.Directory.directory_v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GroupSourceEntry(SourceProvider provider, string parentPath, Group group)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, group.Name ?? group.Email)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new GroupSettingsSourceEntry(provider, this.Path, group.Email);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new GroupMembersSourceEntry(provider, this.Path, group.Email);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new GroupAliasesSourceEntry(provider, this.Path, group.Email);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new GroupConversationsSourceEntry(provider, this.Path, group.Email);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Group.ToString() },
            { "gsuite:Name", group.Name ?? group.Email },
            { "gsuite:Id", group.Id },
            { "gsuite:Email", group.Email }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
