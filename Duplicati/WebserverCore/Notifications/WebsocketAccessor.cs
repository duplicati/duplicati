using System.Net.WebSockets;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Extensions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor(JsonSerializerSettings jsonSettings) : IWebsocketAccessor
{
    private List<WebSocket> _connections = new();

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
        var json = JsonConvert.SerializeObject(data, jsonSettings);
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