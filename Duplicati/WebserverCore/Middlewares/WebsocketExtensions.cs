using System.Net.WebSockets;
using System.Text;
using Duplicati.WebserverCore.Abstractions.Notifications;

namespace Duplicati.WebserverCore.Middlewares;

public static class WebsocketExtensions
{
    public static IApplicationBuilder UseNotifications(this IApplicationBuilder app,
        string[] allowedHostnames, string notificationPath)
    {
        var opts = new WebSocketOptions();
        if (allowedHostnames.All(x => x != "*"))
            foreach (var allowedHostname in allowedHostnames)
                opts.AllowedOrigins.Add(allowedHostname);

        app.UseWebSockets(opts);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path != notificationPath)
            {
                await next(context);
            }
            else
            {
                if (context.User.Identity?.IsAuthenticated == false)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await webSocket.CloseAsync((WebSocketCloseStatus)4401, "User is not authenticated!",
                        CancellationToken.None);
                    return;
                }

                var websocketAccessor = context.RequestServices.GetRequiredService<IWebsocketAccessor>();
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await websocketAccessor.AddConnection(webSocket);
                    await HandleClientData(webSocket, websocketAccessor);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
        });
    }

    private static async Task HandleClientData(WebSocket webSocket, IWebsocketAccessor websocketAccessor,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1024 * 4];

        var result = await ReceiveAsync();

        while (!result.CloseStatus.HasValue)
        {
            result = await ReceiveAsync();
        }

        await webSocket.CloseAsync(
            result.CloseStatus.Value,
            result.CloseStatusDescription,
            CancellationToken.None);

        return;

        async Task<WebSocketReceiveResult> ReceiveAsync()
        {
            var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            var message = Encoding.Default.GetString(buffer[..receiveResult.Count]);
            await websocketAccessor.HandleClientMessage(message);

            return receiveResult;
        }
    }
}