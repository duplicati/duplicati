// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserMailboxRulesSourceEntry(SourceProvider provider, GraphUser user, string path)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, "rules")), DateTime.UnixEpoch, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var rule in provider.UserEmailApi.ListMessageRulesAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var ms = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(ms, rule);
            var jsonBytes = ms.ToArray();

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, rule.Id + ".json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: jsonBytes.Length,
                streamFactory: (ct) => Task.FromResult<Stream>(new MemoryStream(jsonBytes)),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", rule.Id },
                    { "o365:Type", SourceItemType.UserMailboxRule.ToString() },
                    { "o365:Name", rule.DisplayName },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Type", "UserMailboxRules" },
            { "o365:Name", "rules" }
        });
}
