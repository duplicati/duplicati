using System.Net.WebSockets;
using Duplicati.WebserverCore.Options;

namespace Duplicati.WebserverCore.Middlewares;

public static class WebsocketExtensions
{
    public static IApplicationBuilder UseNotifications(this IApplicationBuilder app, IConfiguration configuration)
    {
        var kestrelUrl = configuration["Kestrel:Endpoints:MyHttpEndpoint:Url"];
        kestrelUrl = kestrelUrl != null ? new Uri(kestrelUrl).Authority : "localhost:8201";

        var options = configuration.GetRequiredSection(NotificationsOptions.SectionName).Get<NotificationsOptions>()!;

        app.UseWebSockets(new WebSocketOptions
        {
            AllowedOrigins = { kestrelUrl }
        });

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path != options.WebsocketPath)
            {
                await next(context);
            }
            else
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await Echo(webSocket);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
        });
    }

    private static async Task Echo(WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];
        var receiveResult = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer), CancellationToken.None);

        while (!receiveResult.CloseStatus.HasValue)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                receiveResult.MessageType,
                receiveResult.EndOfMessage,
                CancellationToken.None);

            receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
        }

        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }
}