// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Drive.v3;
using Google.Apis.HangoutsChat.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatSourceEntry(string parentPath, HangoutsChatService chatService, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Chat")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Create a folder for each space type to organize the structure
        var spaceTypes = new[] { "SPACE", "GROUP_CHAT", "DIRECT_MESSAGE" };

        foreach (var spaceType in spaceTypes)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new ChatSpaceTypeSourceEntry(this.Path, spaceType, chatService, driveService);
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
