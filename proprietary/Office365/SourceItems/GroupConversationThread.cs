// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupConversationThreadSourceEntry(SourceProvider provider, string path, GraphGroup group, GraphConversationThread thread)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, thread.Id)), null, thread.LastDeliveredDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var post in provider.GroupConversationApi.ListThreadPostsAsync(group.Id, thread.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new StreamResourceEntryFunction(
                SystemIO.IO_OS.PathCombine(this.Path, post.Id + ".json"),
                createdUtc: post.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: post.LastModifiedDateTime.FromGraphDateTime(),
                size: -1,
                streamFactory: (ct) => provider.GroupConversationApi.GetThreadPostStreamAsync(group.Id, thread.Id, post.Id, ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>
                {
                    { "o365:v", "1" },
                    { "o365:Id", post.Id },
                    { "o365:Type", SourceItemType.GroupConversationThreadPost.ToString() },
                    { "o365:Name", $"{post.From?.EmailAddress?.Name} - {post.CreatedDateTime.FromGraphDateTime()}" },
                    { "o365:From", post.From?.ToString()  },
                    { "o365:CreatedDateTime", post.CreatedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                    { "o365:LastModifiedDateTime", post.LastModifiedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) }
                }
                .WhereNotNull()
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
                );
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", thread.Id },
                { "o365:Type", SourceItemType.GroupConversationThread.ToString() },
                { "o365:Name", $"Thread: {thread.Topic ?? ""}" },
                { "o365:Topic", thread.Topic },
                { "o365:LastDeliveredDateTime", thread.LastDeliveredDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) }
            }
            .WhereNotNull()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
