// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.Drive.v3;
using System.Runtime.CompilerServices;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A file in a drive, listed as a folder of its metadata, permissions, content, comments and
/// revisions
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="stateKey">The change tracking key of the drive, so a failed request below the file holds the stored token back, or <c>null</c> if changes are not tracked</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="file">The file</param>
/// <param name="userIsInactive">Whether the user the drive is listed through is suspended or archived</param>
/// <param name="driveService">The Drive service</param>
internal class DriveFileSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, bool userIsInactive, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, file.Id)), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFileSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFileMetadataSourceEntry(this.Path, file);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFilePermissionsSourceEntry(provider, stateKey, this.Path, file, driveService);

        // A Google Site has no content Drive can hand out: it is not binary, so it cannot be
        // downloaded, and it is not a Docs Editors file, so it cannot be exported either. Its
        // metadata, permissions and comments are all Drive knows about it
        if (!GoogleMimeTypes.IsGoogleSite(file.MimeType))
        {
            // The owner can turn off download for viewers and commenters, and Drive flags some
            // files; the listing says so, and asking for the content would only be refused
            if (file.Capabilities?.CanDownload == false)
            {
                Log.WriteVerboseMessage(LOGTAG, "FileNotDownloadable", Strings.FileNotDownloadable(file.Id));
            }
            else
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new DriveFileContentSourceEntry(provider, stateKey, this.Path, file, driveService);
            }
        }

        // A shortcut is a pointer to another file and has no comments of its own; the
        // comments endpoint answers 404 for it. The target file carries the comments.
        if (!GoogleMimeTypes.IsShortcut(file.MimeType))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new DriveFileCommentsSourceEntry(provider, stateKey, this.Path, file, userIsInactive, driveService);
        }

        // Skip revisions for Google Workspace files (Docs, Sheets, Slides, Sites, Shortcuts) as they cannot be exported
        if (!GoogleMimeTypes.IsGoogleSite(file.MimeType) && !GoogleMimeTypes.IsShortcut(file.MimeType) && !GoogleMimeTypes.IsGoogleDoc(file.MimeType))
        {
            // Drive tells readers of a file that they may not read its revisions; asking would
            // only be refused, so the revisions folder is left out for them
            if (file.Capabilities?.CanReadRevisions == false)
            {
                Log.WriteVerboseMessage(LOGTAG, "RevisionsNotReadable", Strings.RevisionsNotReadable(file.Id));
            }
            else
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new DriveFileRevisionsSourceEntry(provider, stateKey, this.Path, file, driveService);
            }
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFile.ToString() },
            { "gsuite:Name", file.Name },
            { "gsuite:Id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
