using System.Net.WebSockets;
using System.Text;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Options;

namespace Duplicati.WebserverCore.Middlewares;

public static class WebsocketExtensions
{
    public static IApplicationBuilder UseNotifications(this IApplicationBuilder app, IConfiguration configuration)
    {
        var kestrelUrl = configuration["Kestrel:Endpoints:MyHttpEndpoint:Url"];
        if (kestrelUrl != null)
        {
            var uri = new Uri(kestrelUrl);
            kestrelUrl = uri.Scheme + "://" + uri.Authority;
        }
        else
        {
            kestrelUrl = "http://localhost:8201";
        }

        var options = configuration.GetRequiredSection(NotificationsOptions.SectionName).Get<NotificationsOptions>()!;

        app.UseWebSockets(new WebSocketOptions
        {
            AllowedOrigins = { kestrelUrl }
        });

        return app.Use(async (context, next) =>
        {
            var websocketAccessor = context.RequestServices.GetRequiredService<IWebsocketAccessor>();
            if (context.Request.Path != options.WebsocketPath)
            {
                await next(context);
            }
            else
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    websocketAccessor.AddConnection(webSocket);
                    await HandleClientData(webSocket, websocketAccessor);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
        });
    }

    private static async Task HandleClientData(WebSocket webSocket, IWebsocketAccessor websocketAccessor, CancellationToken cancellationToken = default)
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