// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.HangoutsChat.v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatMessageSourceEntry(SourceProvider provider, string parentPath, Message message, string userId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, message.Name.Split('/').Last())),
        message.CreateTimeDateTimeOffset.HasValue ? message.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ChatMessageFileSourceEntry(this.Path, message);

        if (message.Attachment != null)
        {
            foreach (var attachment in message.Attachment)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new ChatAttachmentSourceEntry(provider, this.Path, attachment, userId);
            }
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatMessage.ToString() },
            { "gsuite:Name", CapMessage(message.ArgumentText ?? message.Name) },
            { "gsuite:Id", message.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static string CapMessage(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Length <= 50 ? input : input[..47] + "...";
    }
}
