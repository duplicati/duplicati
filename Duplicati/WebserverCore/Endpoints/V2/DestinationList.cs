// Copyright (C) 2026, The Duplicati Team
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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2;

public class DestinationList : IEndpointV2
{
    private const int MAX_LIMIT = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/destination/list", ([FromServices] Connection connection, [FromServices] IApplicationSettings applicationSettings, [FromBody] DestinationListRequestDto input, CancellationToken cancelToken)
            => ExecuteTestAsync(connection, applicationSettings, input, cancelToken))
            .RequireAuthorization();
    }

    private static async Task<DestinationListResponseDto> ExecuteTestAsync(Connection connection, IApplicationSettings applicationSettings, DestinationListRequestDto input, CancellationToken cancelToken)
    {
        var destinationType = input.DestinationType ?? RemoteDestinationType.Backend;

        var offset = input.Offset ?? 0;
        var limit = input.Limit ?? MAX_LIMIT;
        if (offset < 0 || offset > int.MaxValue - MAX_LIMIT)
            return DestinationListResponseDto.Failure($"Offset must be greater than or equal to 0 and less than or equal to {int.MaxValue - MAX_LIMIT}");
        if (limit < 0 || limit > MAX_LIMIT)
            return DestinationListResponseDto.Failure($"Limit must be greater than or equal to 0 and less than or equal to {MAX_LIMIT}");


        try
        {
            if (destinationType == RemoteDestinationType.SourceProvider)
            {
                using var wrapper = await SharedRemoteOperation.GetSourceProviderForTestingAsync(connection, applicationSettings, input.DestinationUrl, null, input.BackupId, input.ConnectionStringId ?? -1, input.SourcePrefix, cancelToken);

                // Initialize the source provider, not done with "getForTesting"
                await wrapper.SourceProvider.InitializeAsync(cancelToken);

                // Obtain the list items
                var entry = await wrapper.SourceProvider.GetEntryAsync(input.Path, true, cancelToken);
                if (entry == null || !entry.IsFolder)
                    return DestinationListResponseDto.Failure("Folder does not exist");

                var entries = await entry.Enumerate(cancelToken)
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(cancelToken);

                var items = new List<DestinationListResponseItem>(entries.Count);
                foreach (var e in entries)
                    items.Add(new DestinationListResponseItem
                    {
                        Path = e.Path,
                        Metadata = await e.GetMinorMetadata(cancelToken),
                        Size = e.Size
                    });

                return DestinationListResponseDto.Create(
                    items, offset, hasMore: items.Count() == limit
                );
            }
            else
            {
                string pathCombine(string additionalPath, IFileEntry entry)
                {
                    var name = $"{additionalPath?.TrimEnd('/')}/{entry.Name?.TrimStart('/')}";
                    if (entry.IsFolder)
                        Util.AppendDirSeparator(name, "/");
                    return name;
                }

                using var wrapper = await SharedRemoteOperation.GetBackendAsync(connection, applicationSettings, input.DestinationUrl, input.Path, input.BackupId, input.ConnectionStringId ?? -1, cancelToken);

                using (var b = wrapper.Backend)
                {
                    var items = await b.ListAsync(cancelToken)
                        .Skip(offset)
                        .Take(limit)
                        .Select(x => new DestinationListResponseItem
                        {
                            Path = pathCombine(input.Path, x),
                            Metadata = null,
                            Size = x.Size
                        })
                        .ToListAsync(cancelToken);

                    return DestinationListResponseDto.Create(
                        items, offset, hasMore: items.Count() == limit
                    );
                }
            }
        }
        catch (Exception ex) when (SharedRemoteOperation.GetInnerException<FolderMissingException>(ex) is FolderMissingException)
        {
            return DestinationListResponseDto.Failure(
                "Folder does not exist"
            );
        }
        catch (UserInformationException uex)
        {
            return DestinationListResponseDto.Failure(
                uex.Message
            );
        }
        catch (Exception ex)
        {
            return DestinationListResponseDto.Failure(
                ex.Message
            );
        }
    }
}
