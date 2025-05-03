// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Interface;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2.Backup;

public class BackupListing : IEndpointV2
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/backup/list-filesets", ([FromServices] Connection connection, [FromServices] EventPollNotify eventPollNotify, [FromBody] Dto.V2.ListFilesetsRequestDto input)
            => ExecuteGetFilesets(connection, eventPollNotify, GetBackup(connection, input.BackupId)))
            .RequireAuthorization();

        group.MapPost("/backup/list-folder", ([FromServices] Connection connection, [FromServices] EventPollNotify eventPollNotify, [FromBody] Dto.V2.ListFolderContentRequestDto input)
            => ExecuteListFolder(connection, eventPollNotify, GetBackup(connection, input.BackupId), input))
            .RequireAuthorization();

        group.MapPost("/backup/list-versions", ([FromServices] Connection connection, [FromServices] EventPollNotify eventPollNotify, [FromBody] Dto.V2.ListFileVersionsRequestDto input)
            => ExecuteListVersions(connection, eventPollNotify, GetBackup(connection, input.BackupId), input))
            .RequireAuthorization();

        group.MapPost("/backup/search", ([FromServices] Connection connection, [FromServices] EventPollNotify eventPollNotify, [FromBody] Dto.V2.SearchEntriesRequestDto input)
            => ExecuteSearch(connection, eventPollNotify, GetBackup(connection, input.BackupId), input))
            .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private static Dto.V2.ListFolderContentResponseDto ExecuteListFolder(Connection connection, EventPollNotify eventPollNotify, IBackup bk, Dto.V2.ListFolderContentRequestDto input)
    {
        var time = string.IsNullOrWhiteSpace(input.Time)
            ? new DateTime(0)
            : Library.Utility.Timeparser.ParseTimeInterval(input.Time, DateTime.Now);

        var r = Runner.Run(connection, eventPollNotify, Runner.CreateListFolderContents(bk, input.Paths, time, input.PageSize ?? 1000, input.Page ?? 0), false) as IListFolderResults;
        if (r == null)
            throw new ServerErrorException("No result from list operation");

        return Dto.V2.ListFolderContentResponseDto.Create(
            r.Entries.Items
                .Select(x => new Dto.V2.ListFolderContentItemDto()
                {
                    Path = x.Path,
                    IsDirectory = x.IsDirectory,
                    Size = x.Size,
                    LastModified = x.LastModified,
                    IsSymlink = x.IsSymlink
                }),
                r.Entries.Page,
                r.Entries.PageSize,
                r.Entries.TotalCount);
    }

    private static Dto.V2.ListFilesetsResponseDto ExecuteGetFilesets(Connection connection, EventPollNotify eventPollNotify, IBackup bk)
    {
        var extra = new Dictionary<string, string?>();

        // Retries will hang the http request
        extra["number-of-retries"] = "0";

        try
        {
            var r = Runner.Run(connection, eventPollNotify, Runner.CreateListFilesetsTask(bk, extra), false) as IListFilesetResults;
            if (r == null)
                throw new ServerErrorException("No result from list operation");

            if (r.EncryptedFiles.HasValue && r.EncryptedFiles.Value && bk.Settings.Any(x => string.Equals("--no-encryption", x.Name, StringComparison.OrdinalIgnoreCase)))
                throw new UserInformationException("The remote storage is encrypted, but no passphrase is given", "EncryptedStorageNoPassphrase");

            return Dto.V2.ListFilesetsResponseDto.Create(
                r.Filesets
                    .Select(x => new Dto.V2.ListFilesetsResponseItem()
                    {
                        Version = x.Version,
                        Time = x.Time,
                        IsFullBackup = x.IsFullBackup,
                        FileCount = x.FileCount,
                        FileSizes = x.FileSizes
                    })
            );
        }
        catch (FolderMissingException fex)
        {
            throw new UserInformationException("Remote folder is missing", "FolderMissing", fex);
        }
    }

    private static Dto.V2.ListFileVersionsOutputDto ExecuteListVersions(Connection connection, EventPollNotify eventPollNotify, IBackup bk, Dto.V2.ListFileVersionsRequestDto input)
    {
        var r = Runner.Run(connection, eventPollNotify, Runner.ListFileVersionsTask(bk, input.Paths, input.PageSize ?? 1000, input.Page ?? 0), false) as IListFileVersionsResults;
        if (r == null)
            throw new ServerErrorException("No result from list operation");

        return Dto.V2.ListFileVersionsOutputDto.Create(
            r.FileVersions.Items
                .Select(x => new Dto.V2.ListFileVersionsItemDto
                {
                    Version = x.Version,
                    Time = x.Time,
                    Path = x.Path,
                    IsDirectory = x.IsDirectory,
                    Size = x.Size,
                    LastModified = x.LastModified,
                    IsSymlink = x.IsSymlink
                }),
                r.FileVersions.Page,
                r.FileVersions.PageSize,
                r.FileVersions.TotalCount);
    }

    private static Dto.V2.SearchEntriesResponseDto ExecuteSearch(Connection connection, EventPollNotify eventPollNotify, IBackup bk, Dto.V2.SearchEntriesRequestDto input)
    {
        var time = string.IsNullOrWhiteSpace(input.Time)
            ? new DateTime(0)
            : Library.Utility.Timeparser.ParseTimeInterval(input.Time, DateTime.Now);

        var r = Runner.Run(connection, eventPollNotify, Runner.CreateSearchEntriesTask(bk, input.Filters, input.Paths, time, input.PageSize ?? 1000, input.Page ?? 0), false) as ISearchFilesResults;
        if (r == null)
            throw new ServerErrorException("No result from list operation");

        return Dto.V2.SearchEntriesResponseDto.Create(
            r.FileVersions.Items
                .Select(x => new Dto.V2.SearchEntriesItemDto
                {
                    Version = x.Version,
                    Time = x.Time,
                    Path = x.Path,
                    IsDirectory = x.IsDirectory,
                    Size = x.Size,
                    LastModified = x.LastModified,
                    IsSymlink = x.IsSymlink
                }),
                r.FileVersions.Page,
                r.FileVersions.PageSize,
                r.FileVersions.TotalCount);
    }
}
