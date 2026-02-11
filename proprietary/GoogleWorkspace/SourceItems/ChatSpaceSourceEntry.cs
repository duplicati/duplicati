// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.HangoutsChat.v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatSpaceSourceEntry(SourceProvider provider, string parentPath, Space space)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, space.DisplayName ?? space.Name)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetChatService();
        var request = service.Spaces.Messages.List(space.Name);

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var messages = await request.ExecuteAsync(cancellationToken);

            if (messages.Messages != null)
            {
                foreach (var message in messages.Messages)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new ChatMessageSourceEntry(this.Path, message);
                }
            }
            nextPageToken = messages.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatSpace.ToString() },
            { "gsuite:Name", space.DisplayName ?? space.Name },
            { "gsuite:Id", space.Name },
            { "gsuite:SpaceType", space.Type }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
