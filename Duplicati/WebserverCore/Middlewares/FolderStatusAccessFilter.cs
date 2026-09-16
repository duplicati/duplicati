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

using System.Security.Cryptography;
using System.Text;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Middlewares;

/// <summary>
/// Endpoint filter guarding the folder status endpoints.
/// The Windows shell extension runs inside Explorer and cannot log in, so it
/// presents the access key that the server writes to its data folder when the
/// folder status service is enabled (see <see cref="ServerSettings.SyncFolderStatusAccessKeyFile"/>).
/// Reading that file requires the same access as reading the server database,
/// so the key does not grant anything the caller could not already obtain.
/// Authenticated callers are always allowed.
/// </summary>
/// <param name="connection">The database connection used to read the application settings</param>
public class FolderStatusAccessFilter(Connection connection) : IEndpointFilter
{
    /// <summary>
    /// The header the shell extension sends the access key in
    /// </summary>
    public const string HeaderName = "X-Duplicati-FolderStatus-Key";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;
        var presentedKey = httpContext.Request.Headers.TryGetValue(HeaderName, out var header) ? header.ToString() : null;
        var settings = connection.ApplicationSettings;

        if (!IsAllowed(isAuthenticated, presentedKey, settings.FolderStatusAccessKey, settings.EnableFolderStatusService))
            throw new UnauthorizedException("Folder status requires authentication or a valid folder status access key");

        return await next(context);
    }

    /// <summary>
    /// Decides if a folder status request is allowed
    /// </summary>
    /// <param name="isAuthenticated">True if the caller presented valid credentials</param>
    /// <param name="presentedKey">The access key sent by the caller, or null if none</param>
    /// <param name="expectedKey">The access key stored in the settings, or null if none has been generated</param>
    /// <param name="serviceEnabled">True if the folder status service is enabled in the settings</param>
    /// <returns>True if the request should be served</returns>
    public static bool IsAllowed(bool isAuthenticated, string? presentedKey, string? expectedKey, bool serviceEnabled)
    {
        if (isAuthenticated)
            return true;

        if (!serviceEnabled || string.IsNullOrWhiteSpace(presentedKey) || string.IsNullOrWhiteSpace(expectedKey))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presentedKey.Trim()),
            Encoding.UTF8.GetBytes(expectedKey.Trim()));
    }
}
