// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.HangoutsChat.v1;
using Google.Apis.Drive.v3;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A message in a space, listed as a folder of its content and its attachments
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="parentPath">The path of the space</param>
/// <param name="message">The message</param>
/// <param name="userChatName">The Chat resource name, <c>users/{id}</c>, of the user whose spaces are walked</param>
/// <param name="chatService">The Chat service</param>
/// <param name="driveService">The Drive service</param>
internal class ChatMessageSourceEntry(SourceProvider provider, string parentPath, Message message, string userChatName, HangoutsChatService chatService, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, message.Name.Split('/').Last())),
        message.CreateTimeDateTimeOffset.HasValue ? message.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        DateTime.UnixEpoch)
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ChatMessageSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ChatMessageContentSourceEntry(this.Path, message);

        if (message.Attachment != null)
        {
            foreach (var attachment in message.Attachment)
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                // Google serves an attachment uploaded to Chat only to the user who uploaded it
                // and refuses everyone else, so it is backed up with the uploader and left out
                // of the other members' walks. An attachment kept in Drive is shared like any
                // Drive file and is read from every member
                if (attachment.AttachmentDataRef != null && !string.Equals(message.Sender?.Name, userChatName, StringComparison.Ordinal))
                {
                    Log.WriteVerboseMessage(LOGTAG, "ChatAttachmentUploadedByOther", "Leaving out attachment {0} of message {1}: it was uploaded by {2}, and Google serves it only to the uploader, whose backup holds it", attachment.ContentName ?? attachment.Name, message.Name, message.Sender?.Name ?? "an unknown user");
                    continue;
                }

                yield return new ChatAttachmentSourceEntry(provider, this.Path, attachment, chatService, driveService);
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
            { "gsuite:SpaceName", message.Space.Name },
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
