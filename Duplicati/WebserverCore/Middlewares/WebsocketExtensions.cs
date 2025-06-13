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
using System.Net.WebSockets;
using System.Text;
using Duplicati.WebserverCore.Abstractions.Notifications;

namespace Duplicati.WebserverCore.Middlewares;

public static class WebsocketExtensions
{
    public static IApplicationBuilder UseNotifications(this IApplicationBuilder app, string notificationPath)
    {
        var opts = new WebSocketOptions();

        app.UseWebSockets(opts);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path != notificationPath)
            {
                // TODO: Exceptions have a stack trace pointing to here,
                // if the exception is thrown in the next middleware.
                await next(context);
            }
            else
            {
                if (context.User.Identity?.IsAuthenticated == false)
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await context.RequestServices.GetRequiredService<IWebsocketAuthenticator>().AddConnection(webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                    return;
                }

                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await context.RequestServices.GetRequiredService<IWebsocketAccessor>().AddConnection(webSocket, true);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
        });
    }
}