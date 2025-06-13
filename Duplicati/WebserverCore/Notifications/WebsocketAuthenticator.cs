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
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Services;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAuthenticator(
    JsonSerializerSettings jsonSettings,
    IWebsocketAccessor websocketAccessor,
    IJWTTokenProvider jWTTokenProvider,
    PreAuthTokenConfig config) : IWebsocketAuthenticator
{
    private const int APIVersion = 1;
    /// <summary>
    /// The maximum time a client has to authenticate before the connection is closed.
    /// </summary>
    private static readonly TimeSpan MaxAuthTime = TimeSpan.FromMinutes(1);
    private static readonly string LOGTAG = Log.LogTagFromType<WebsocketAuthenticator>();
    private readonly ConcurrentDictionary<WebSocket, DateTime> _connections = new();
    private record WebSocketAuthRequest(int Version, string Token);
    private record WebSocketAuthReply(int Version, string Message, bool Success)
    {
        // For type detection on the client
        public string Type => "auth";
    }

    public async Task AddConnection(WebSocket newConnection)
    {
        _connections.TryAdd(newConnection, DateTime.UtcNow);
        // Set up a task to clear closed connections after the maximum authentication time plus a small buffer
        var _ = Task.Run(async () =>
        {
            await Task.Delay(MaxAuthTime.Add(TimeSpan.FromSeconds(5)));
            await ClearClosed();
        });

        await HandleClientData(newConnection);
    }

    private async Task HandleClientData(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1024 * 4];
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
        if (result is not null && result.CloseStatus is null)
        {
            var message = Encoding.UTF8.GetString(buffer[..result.Count]);
            await HandleClientMessage(webSocket, message);
        }
    }

    private async Task ClearClosed()
    {
        // Snapshot the connections to avoid modifying the collection while iterating
        var connections = _connections.ToList();

        foreach (var connection in connections)
        {
            if (connection.Key.State != WebSocketState.Open)
            {
                _connections.TryRemove(connection.Key, out _);
                continue;
            }

            if (connection.Value + MaxAuthTime < DateTime.UtcNow)
            {
                Log.WriteVerboseMessage(LOGTAG, "WebsocketConnectionTimeout", $"WebSocket connection timed out after {MaxAuthTime.TotalSeconds} seconds.");
                await connection.Key.CloseAsync((WebSocketCloseStatus)4401, "User is not authenticated!", CancellationToken.None);
                _connections.TryRemove(connection.Key, out _);
                continue;
            }
        }
    }

    private ArraySegment<byte> GetBytes<T>(T Data)
    {
        var json = JsonConvert.SerializeObject(Data, jsonSettings);
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ArraySegment<byte>(bytes);
    }
    private async Task SendRequestReply(WebSocket socket, string message, bool success)
    {
        var reply = new WebSocketAuthReply(APIVersion, message, success);
        var bytes = GetBytes(reply);
        if (!success)
            await socket.CloseAsync((WebSocketCloseStatus)4401, message, CancellationToken.None);
        else
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        _connections.TryRemove(socket, out _);
    }

    public async Task HandleClientMessage(WebSocket socket, string messagestr)
    {
        WebSocketAuthRequest? message = null;
        try
        {
            message = JsonConvert.DeserializeObject<WebSocketAuthRequest>(messagestr, jsonSettings);
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LOGTAG, "WebsocketDeserializationError", ex, $"Failed to deserialize websocket message: {ex.Message}");
        }

        if (message == null)
        {
            await SendRequestReply(socket, "Invalid message format", false);
            return;
        }

        if (message.Version != APIVersion)
        {
            await SendRequestReply(socket, "Unsupported API version", false);
            return;
        }

        var isValid = false;
        Exception? exception = null;
        try
        {
            // Validate the token against the allowed tokens
            isValid = config.AllowedTokens.Contains(message.Token) ||
                      jWTTokenProvider.ReadAccessToken(message.Token) != null;
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        if (!isValid)
        {
            Log.WriteVerboseMessage(LOGTAG, "WebsocketInvalidToken", exception, $"WebSocket connection with invalid token");
            await SendRequestReply(socket, "Invalid token", false);
            return;
        }

        Log.WriteVerboseMessage(LOGTAG, "WebsocketAuthenticated", $"WebSocket connection authenticated with token");
        await SendRequestReply(socket, "Authenticated successfully", true);
        await websocketAccessor.AddConnection(socket, false);

    }
}