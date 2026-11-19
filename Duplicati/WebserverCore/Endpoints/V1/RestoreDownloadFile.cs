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
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Duplicati.WebserverCore.Endpoints.V1;

/// <summary>
/// Downloads the files of a restore to the browser
/// </summary>
public class RestoreDownloadFile : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/restore-download/{id}", async ([FromServices] Connection connection, [FromServices] IHttpContextAccessor httpContextAccessor, [FromServices] IJWTTokenProvider jWTTokenProvider, [FromRoute] long id, [FromQuery] string token, CancellationToken ct) =>
        {
            // The browser cannot send the authorization header for a download, so it sends a single operation token
            var singleOperationToken = jWTTokenProvider.ReadSingleOperationToken(token);
            if (singleOperationToken.Operation != RestoreDownload.TokenOperation)
                throw new UnauthorizedException("Invalid operation");

            if (connection.ApplicationSettings.DisableRestoreDownload)
                throw new ForbiddenException("Restore to the browser is disabled on this server");

            // Only a file registered by a restore for download, not other temp files such as a bug report
            var tf = connection.GetTempFiles().FirstOrDefault(x => x.ID == id);
            var name = RestoreDownload.GetDownloadName(tf?.Origin);
            if (tf == null || name == null)
                throw new NotFoundException("Invalid or missing download id");

            if (!File.Exists(tf.Path))
                throw new NotFoundException("File is missing");

            var response = httpContextAccessor.HttpContext!.Response;
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(name);
            using var fs = File.OpenRead(tf.Path);
            response.ContentLength = fs.Length;
            response.ContentType = "application/octet-stream";
            response.Headers.ContentDisposition = disposition.ToString();
            await fs.CopyToAsync(response.Body, ct).ConfigureAwait(false);
        });
    }
}
