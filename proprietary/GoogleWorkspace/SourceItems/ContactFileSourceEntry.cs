// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.PeopleService.v1.Data;
using System.Text;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactFileSourceEntry(string parentPath, Person person)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "contact.vcf"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCARD");
        sb.AppendLine("VERSION:3.0");

        if (person.Names != null)
        {
            foreach (var name in person.Names)
            {
                sb.AppendLine($"FN:{name.DisplayName}");
                sb.AppendLine($"N:{name.FamilyName};{name.GivenName};{name.MiddleName};;");
            }
        }

        if (person.EmailAddresses != null)
        {
            foreach (var email in person.EmailAddresses)
            {
                sb.AppendLine($"EMAIL;TYPE={email.Type}:{email.Value}");
            }
        }

        if (person.PhoneNumbers != null)
        {
            foreach (var phone in person.PhoneNumbers)
            {
                sb.AppendLine($"TEL;TYPE={phone.Type}:{phone.Value}");
            }
        }

        sb.AppendLine("END:VCARD");

        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Contact.ToString() },
            { "gsuite:Name", person.Names?.FirstOrDefault()?.DisplayName ?? person.ResourceName },
            { "gsuite:id", person.ResourceName },
            { "gsuite:Etag", person.ETag }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
