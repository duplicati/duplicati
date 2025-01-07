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

namespace Duplicati.WebserverCore.Middlewares;

public class PreSharedKeyFilter : IEndpointFilter
{
    public const string HeaderName = "X-Duplicati-PreSharedKey";
    public static string? PreSharedKey = null;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(PreSharedKey))
        {
            context.HttpContext.Response.StatusCode = 500;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("PreSharedKey not set");
            return null;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValue) || headerValue != PreSharedKey)
        {
            context.HttpContext.Response.StatusCode = 403;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("Invalid PreSharedKey");
            return null;
        }

        return await next(context);
    }
}
