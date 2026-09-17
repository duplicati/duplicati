// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using System.Text.Json;
using File = Google.Apis.Drive.v3.Data.File;
using Google;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileCommentsSourceEntry(string parentPath, File file, bool userIsInactive, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "comments.json"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFileCommentsSourceEntry>();

    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var request = driveService.Comments.List(file.Id);
        request.Fields = "*";

        CommentList comments;
        try
        {
            comments = await request.ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException ex) when (userIsInactive && ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // A suspended or archived account keeps read access to the file itself, but Google
            // no longer lets it read the comments of files it does not own. The comments are
            // not reachable through this account, so record an empty list instead of a warning.
            Log.WriteVerboseMessage(LOGTAG, "CommentsNotReadableForInactiveUser", Strings.CommentsNotReadableForInactiveUser(file.Id));
            comments = new CommentList { Comments = [] };
        }

        var json = JsonSerializer.Serialize(comments, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileComment.ToString() },
            { "gsuite:Name", "comments.json" },
            { "gsuite:Id", file.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
