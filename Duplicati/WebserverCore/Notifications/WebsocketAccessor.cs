// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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