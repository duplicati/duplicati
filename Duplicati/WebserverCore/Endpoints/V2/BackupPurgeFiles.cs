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

using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2.Backup;

/// <summary>
/// Endpoint for purging files from a backup.
/// </summary>
public class BackupPurgeFiles : IEndpointV2
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/backup/purge-files", async ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromBody] Dto.V2.PurgeFilesRequestDto input)
            => await ExecutePurgeFilesAsync(queueRunnerService, GetBackup(connection, input.BackupId), input).ConfigureAwait(false))
            .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private static Task<Dto.TaskStartedDto> ExecutePurgeFilesAsync(IQueueRunnerService queueRunnerService, IBackup bk, Dto.V2.PurgeFilesRequestDto input)
    {
        if (input.Filters is null || input.Filters.Length == 0)
            throw new BadRequestException("No paths or filters supplied");

        return Task.FromResult(new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreatePurgeFilesTask(bk, input.Filters, input.Versions))));
    }
}
