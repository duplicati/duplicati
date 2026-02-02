// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserMailboxEmailSourceEntry(SourceProvider provider, string path, GraphUser user, GraphMessage email)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, email.Id)), email.CreatedDateTime.FromGraphDateTime(), email.LastModifiedDateTime.FromGraphDateTime())
{
    private readonly Dictionary<string, string?> _minorMetadata = new Dictionary<string, string?> {
                { "o365:v", "1" },
                { "o365:Id", email.Id },
                { "o365:Type", SourceItemType.UserMailboxEmail.ToString() },
                { "o365:Name", email.Subject ?? "" },
                { "o365:Subject", email.Subject ?? "" },
                { "o365:From", email.From?.EmailAddress?.Address ?? "" },
                { "o365:To", string.Join(",", email.ToRecipients?.Select(x => x.EmailAddress?.Address) ?? []) },
                { "o365:CreatedDateTime", email.CreatedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:ReceivedDateTime", email.ReceivedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:SentDateTime", email.SentDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:InternetMessageId", email.InternetMessageId ?? "" },
                { "o365:HasAttachments", email.HasAttachments?.ToString() ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken ct)
        => Task.FromResult(_minorMetadata);

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "content.eml"),
            createdUtc: email.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: email.LastModifiedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.UserEmailApi.GetEmailContentStreamAsync(user.Id, email.Id, ct)
        );

        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "metadata.json"),
            createdUtc: email.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: email.LastModifiedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.UserEmailApi.GetEmailMetadataStreamAsync(user.Id, email.Id, ct)
        );
    }
}
