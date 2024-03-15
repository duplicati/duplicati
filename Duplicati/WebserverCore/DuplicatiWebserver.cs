using System.Net.WebSockets;
using Duplicati.WebserverCore.Database;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.EntityFrameworkCore;

namespace Duplicati.WebserverCore;

public class DuplicatiWebserver
{
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public void InitWebServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseRESTHandlers();
        builder.Services.AddControllers()
            // This app gets launched by a different assembly, so we need to tell it to look in this one
            .AddApplicationPart(GetType().Assembly);
        builder.Services.AddHostedService<ApplicationPartsLogger>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var connectionString = builder.Configuration.GetConnectionString("Sqlite");
        builder.Services.AddDbContext<MainDbContext>(o => { o.UseSqlite(connectionString); });

        builder.Services.AddDuplicati();

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;
    }

    public async Task Start()
    {
        App.UseAuthMiddleware();
        App.UseDefaultStaticFiles();

        //TODO: remove
        App.MapControllers();

        App.AddEndpoints();
        App.UseWebSockets(new WebSocketOptions
        {
            //TODO: read it from configuration Kestrel:Endpoints:MyHttpEndpoint:Url 
            AllowedOrigins = { "localhost:8201" }
        });
        
        App.Use(async (context, next) =>
        {
            //TODO: add configuration with this path
            if (context.Request.Path != "/ws")
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

        await App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
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