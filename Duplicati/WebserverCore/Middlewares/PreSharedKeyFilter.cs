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

namespace Duplicati.WebserverCore.Middlewares;

public class PreSharedKeyFilter : IEndpointFilter
{
    public const string HeaderName = "X-Duplicati-PreSharedKey";
    public static string? PreSharedKey = null;

    /// <summary>
    /// Checks if the request carries the pre-shared key, using a constant-time comparison
    /// </summary>
    /// <param name="request">The request to check</param>
    /// <param name="preSharedKey">The expected pre-shared key</param>
    /// <returns><c>true</c> if the request carries the key; <c>false</c> otherwise</returns>
    private static bool HasValidPreSharedKey(HttpRequest request, string preSharedKey)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
            return false;

        var value = values[0];
        if (string.IsNullOrEmpty(value))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(preSharedKey));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(PreSharedKey))
        {
            context.HttpContext.Response.StatusCode = 500;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("PreSharedKey not set");
            return null;
        }

        if (!HasValidPreSharedKey(context.HttpContext.Request, PreSharedKey))
        {
            context.HttpContext.Response.StatusCode = 403;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("Invalid PreSharedKey");
            return null;
        }

        return await next(context);
    }
}
