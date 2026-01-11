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

using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

/// <summary>
/// Endpoint for querying folder backup status.
/// Used by the Windows Shell Extension to show overlay icons on backed up folders.
/// </summary>
public class FolderStatus : IEndpointV1
{
    /// <summary>
    /// Maps the folder status endpoints
    /// </summary>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/folderstatus", (
            [FromServices] IFolderStatusService folderStatusService) =>
                folderStatusService.GetAllFolderStatuses())
            .RequireAuthorization();

        group.MapGet("/folderstatus/{*path}", (
            [FromRoute] string path,
            [FromServices] IFolderStatusService folderStatusService) =>
                folderStatusService.GetFolderStatus(path))
            .RequireAuthorization();
    }
}
