using System.Net.WebSockets;

namespace Duplicati.WebserverCore.Abstractions.Notifications;

public interface IWebsocketAccessor
{
    Task AddConnection(WebSocket newConnection);
    Task Send<T>(T data);
    Task HandleClientMessage(string message);
}