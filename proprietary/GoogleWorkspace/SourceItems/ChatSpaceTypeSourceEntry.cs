// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Drive.v3;
using Google.Apis.HangoutsChat.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// Represents a folder containing all spaces of a specific type (SPACE, GROUP_CHAT, or DIRECT_MESSAGE).
/// </summary>
internal class ChatSpaceTypeSourceEntry(string parentPath, string spaceType, HangoutsChatService chatService, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, spaceType)), null, null)
{
    private static string GetDisplayName(string spaceType) => spaceType switch
    {
        "SPACE" => "Spaces",
        "GROUP_CHAT" => "Group Chats",
        "DIRECT_MESSAGE" => "Direct Messages",
        _ => spaceType
    };

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = chatService.Spaces.List();
        request.Filter = $"space_type = \"{spaceType}\"";

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var spaces = await request.ExecuteAsync(cancellationToken);

            if (spaces.Spaces != null)
            {
                foreach (var space in spaces.Spaces)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new ChatSpaceSourceEntry(this.Path, space, chatService, driveService);
                }
            }
            nextPageToken = spaces.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatSpaceType.ToString() },
            { "gsuite:Name", GetDisplayName(spaceType) },
            { "gsuite:Id", spaceType },
            { "gsuite:SpaceType", spaceType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
