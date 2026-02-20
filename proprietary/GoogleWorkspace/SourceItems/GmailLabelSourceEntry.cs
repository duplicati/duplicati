// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Gmail.v1;
using Google.Apis.Requests;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GmailLabelSourceEntry(string userId, string parentPath, Label label, GmailService service)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, label.Id)), null, null)
{
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
    {
        return EnumerateMessages(cancellationToken);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateMessages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = service.Users.Messages.List(userId);
        request.LabelIds = new[] { label.Id };

        do
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Messages != null)
            {
                var batch = new BatchRequest(service);
                var messages = new List<Message>();

                foreach (var msg in response.Messages)
                {
                    var msgRequest = service.Users.Messages.Get(userId, msg.Id);
                    msgRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    batch.Queue<Message>(msgRequest, (message, error, index, httpResponse) =>
                    {
                        if (error == null)
                        {
                            lock (messages)
                            {
                                messages.Add(message);
                            }
                        }
                    });
                }

                await batch.ExecuteAsync(cancellationToken);

                foreach (var message in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    yield return new GmailMessageSourceEntry(userId, this.Path, message, service);
                }
            }

            request.PageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(request.PageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailLabel.ToString() },
            { "gsuite:Name", label.Name },
            { "gsuite:Id", label.Id },
            { "gsuite:LabelType", label.Type },
            { "gsuite:LabelListVisibility", label.LabelListVisibility },
            { "gsuite:MessageListVisibility", label.MessageListVisibility }
        };

        // Add color information if present
        if (label.Color != null)
        {
            metadata["gsuite:ColorBackground"] = label.Color.BackgroundColor;
            metadata["gsuite:ColorText"] = label.Color.TextColor;
        }

        return Task.FromResult(metadata
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
