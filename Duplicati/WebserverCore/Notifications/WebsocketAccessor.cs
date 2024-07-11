using System.Net.WebSockets;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Extensions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor : IWebsocketAccessor
{
    private object _lock = new object();
    private List<WebSocket> _connections = new();
    private readonly JsonSerializerSettings m_jsonSettings;
    private readonly IServiceProvider m_serviceProvider;

    public WebsocketAccessor(JsonSerializerSettings jsonSettings, EventPollNotify eventPollNotify, IServiceProvider serviceProvider)
    {
        m_jsonSettings = jsonSettings;
        m_serviceProvider = serviceProvider;

        eventPollNotify.NewEvent += async (_, _) =>
        {
            await Send(StatusService.GetStatus());
        };
    }

    private IStatusService StatusService => m_serviceProvider.GetService<IStatusService>() ?? throw new Exception("StatusService not found");

    public async Task AddConnection(WebSocket newConnection)
    {
        await SendInitialStatus(newConnection);
        lock (_lock)
            _connections.Add(newConnection);
        ClearClosed();
    }

    private async Task SendInitialStatus(WebSocket connection)
        => await Send(StatusService.GetStatus(), [connection]);

    private void ClearClosed()
    {
        lock (_lock)
            _connections = _connections.Where(c => c.State == WebSocketState.Open).ToList();
    }

    private IEnumerable<WebSocket> Connections
    {
        get
        {
            ClearClosed();
            return _connections;
        }
    }

    private Task Send<T>(T data, IEnumerable<WebSocket> connections)
    {
        var json = JsonConvert.SerializeObject(data, m_jsonSettings);
        var bytes = json.GetBytes();

        lock (_lock)
            connections = connections.ToList();

        return Task.WhenAll(
            connections
            .Where(c => c.State == WebSocketState.Open)
            .Select(c => c.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None))
        );
    }


    public Task Send<T>(T data)
        => Send(data, Connections);

    public Task HandleClientMessage(string message)
    {
        //TODO: handle client message
        return Task.CompletedTask;
    }
}