// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Keep.v1.Data;
using Google.Apis.Keep.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class KeepNoteSourceEntry(string parentPath, Note note, KeepService keepService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, note.Title ?? note.Name)),
        note.CreateTimeDateTimeOffset.HasValue ? note.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        note.UpdateTimeDateTimeOffset.HasValue ? note.UpdateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new KeepNoteFileSourceEntry(this.Path, note);

        if (note.Attachments != null)
        {
            foreach (var attachment in note.Attachments)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new KeepNoteAttachmentSourceEntry(this.Path, attachment, keepService);
            }
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.KeepNote.ToString() },
            { "gsuite:Name", note.Title ?? note.Name },
            { "gsuite:Id", note.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
