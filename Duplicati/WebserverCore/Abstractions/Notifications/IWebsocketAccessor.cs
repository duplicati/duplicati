using System.Net.WebSockets;

namespace Duplicati.WebserverCore.Abstractions.Notifications;

public interface IWebsocketAccessor
{
    void AddConnection(WebSocket newConnection);
    WebSocket[] OpenConnections { get; }
}