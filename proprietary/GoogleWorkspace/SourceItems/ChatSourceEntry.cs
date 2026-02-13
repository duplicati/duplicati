// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatSourceEntry(SourceProvider provider, string parentPath, string userId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Chat")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Create a folder for each space type to organize the structure
        var spaceTypes = new[] { "SPACE", "GROUP_CHAT", "DIRECT_MESSAGE" };

        foreach (var spaceType in spaceTypes)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new ChatSpaceTypeSourceEntry(provider, this.Path, spaceType, userId);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.UserChat.ToString() },
            { "gsuite:Name", "Chat" },
            { "gsuite:Id", "Chat" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
