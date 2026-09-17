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

using System.Net;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Middlewares;

/// <summary>
/// Endpoint filter guarding the folder status endpoints.
/// The Windows shell extension runs inside Explorer and cannot log in, so
/// requests from the local machine are served without authentication once the
/// folder status service has been explicitly enabled. The endpoints only expose
/// backup metadata (source paths, backup names and status), never credentials.
/// Authenticated callers are always allowed.
/// </summary>
/// <param name="connection">The database connection used to read the application settings</param>
public class FolderStatusAccessFilter(Connection connection) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;

        if (!IsAllowed(isAuthenticated, httpContext.Connection.RemoteIpAddress, connection.ApplicationSettings.EnableFolderStatusService))
            throw new UnauthorizedException("Folder status requires authentication, or a local connection with the folder status service enabled");

        return await next(context);
    }

    /// <summary>
    /// Decides if a folder status request is allowed
    /// </summary>
    /// <param name="isAuthenticated">True if the caller presented valid credentials</param>
    /// <param name="remoteAddress">The address the request originated from, or null if unknown</param>
    /// <param name="serviceEnabled">True if the folder status service is enabled in the settings</param>
    /// <returns>True if the request should be served</returns>
    public static bool IsAllowed(bool isAuthenticated, IPAddress? remoteAddress, bool serviceEnabled)
    {
        if (isAuthenticated)
            return true;

        if (!serviceEnabled || remoteAddress == null)
            return false;

        if (remoteAddress.IsIPv4MappedToIPv6)
            remoteAddress = remoteAddress.MapToIPv4();

        return IPAddress.IsLoopback(remoteAddress);
    }
}
