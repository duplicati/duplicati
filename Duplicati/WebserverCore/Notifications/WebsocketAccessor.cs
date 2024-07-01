using System.Net.WebSockets;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Extensions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor : IWebsocketAccessor
{
    private List<WebSocket> _connections = new();
    private readonly JsonSerializerSettings m_jsonSettings;

    public WebsocketAccessor(JsonSerializerSettings jsonSettings, EventPollNotify eventPollNotify, IServiceProvider serviceProvider)
    {
        m_jsonSettings = jsonSettings;
        
        eventPollNotify.NewEvent += async (_, _) =>
        {
            using var scope = serviceProvider.CreateScope();
            var statusService = scope.ServiceProvider.GetRequiredService<IStatusService>();
            await Send(statusService.GetStatus());
        };
    }

    public void AddConnection(WebSocket newConnection)
    {
        _connections.Add(newConnection);
        ClearClosed();
    }

    private void ClearClosed()
    {
        _connections = _connections.Where(c => c.State == WebSocketState.Open).ToList();
    }

    public WebSocket[] OpenConnections => Connections.ToArray();

    private IEnumerable<WebSocket> Connections
    {
        get
        {
            ClearClosed();
            return _connections;
        }
    }

    public async Task Send<T>(T data)
    {
        var json = JsonConvert.SerializeObject(data, m_jsonSettings);
        var bytes = json.GetBytes();

        foreach (var webSocket in Connections)
        {
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    public async Task HandleClientMessage(string message)
    {
        //TODO: handle client message
    }
}