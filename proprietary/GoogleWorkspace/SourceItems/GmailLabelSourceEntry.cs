// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Gmail.v1;
using Google.Apis.Requests;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GmailLabelSourceEntry(SourceProvider provider, string userId, string parentPath, Label label)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, label.Name)), null, null)
{
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
    {
        return EnumerateMessages(cancellationToken);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateMessages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetGmailService(userId);
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
                    msgRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal;
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

                    yield return new GmailMessageSourceEntry(provider, userId, this.Path, message);
                }
            }

            request.PageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(request.PageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailLabel.ToString() },
            { "gsuite:Name", label.Name },
            { "gsuite:id", label.Id },
            { "gsuite:LabelType", label.Type }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
