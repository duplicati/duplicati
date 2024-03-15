using System.Net.WebSockets;
using Duplicati.WebserverCore.Abstractions.Notifications;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor : IWebsocketAccessor
{
    private List<WebSocket> _connections = new();

    public void AddConnection(WebSocket newConnection)
    {
        _connections.Add(newConnection);
        ClearClosed();
    }

    private void ClearClosed()
    {
        _connections = _connections.Where(c => c.State != WebSocketState.Open).ToList();
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

    //TODO: add Send method and Receive event; or maybe multiple events for differentiating between messages types?  
}