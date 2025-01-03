using System.Collections.Concurrent;
using System.Net.WebSockets;
using Duplicati.Library.Logging;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Extensions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor : IWebsocketAccessor
{
    private static readonly string LOGTAG = Log.LogTagFromType<WebsocketAccessor>();
    private readonly ConcurrentBag<WebSocket> _connections = new();
    private readonly JsonSerializerSettings m_jsonSettings;
    private readonly IStatusService _statusService;

    public WebsocketAccessor(JsonSerializerSettings jsonSettings, EventPollNotify eventPollNotify,
        IStatusService statusService)
    {
        m_jsonSettings = jsonSettings;
        _statusService = statusService;

        eventPollNotify.NewEvent += async (_, _) => { await Send(_statusService.GetStatus()); };
    }

    public async Task AddConnection(WebSocket newConnection)
    {
        await SendInitialStatus(newConnection);
        _connections.Add(newConnection);
        ClearClosed();
    }

    private async Task SendInitialStatus(WebSocket connection)
        => await Send(_statusService.GetStatus(), [connection]);

    private void ClearClosed()
    {
        var openConnections = _connections.Where(c => c.State == WebSocketState.Open).ToArray();
        _connections.Clear();
        foreach (var connection in openConnections)
        {
            if (!_connections.Contains(connection))
            {
                _connections.Add(connection);
            }
        }
    }

    private IEnumerable<WebSocket> Connections
    {
        get
        {
            ClearClosed();
            return _connections;
        }
    }

    private async Task Send<T>(T data, IEnumerable<WebSocket> connections)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data, m_jsonSettings);
            var bytes = json.GetBytes();

            await Task.WhenAll(
                connections
                    .Where(c => c.State == WebSocketState.Open)
                    .Select(c => c.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None))
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "WebsockSendFailure", ex, $"Failed to send websocket message: {ex.Message}");
        }
    }

    public Task Send<T>(T data) => Send(data, Connections);

    public Task HandleClientMessage(string message)
    {
        //TODO: handle client message
        return Task.CompletedTask;
    }
}