// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Notifications;

public class WebsocketAccessor : IWebsocketAccessor
{
    private static readonly string LOGTAG = Log.LogTagFromType<WebsocketAccessor>();
    private readonly ConcurrentDictionary<WebSocket, ConcurrentDictionary<SubscriptionService, string>> _subscribers = new();
    private readonly ConcurrentBag<WebSocket> _connections = new();
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly IStatusService _statusService;
    private readonly ITaskQueueService _taskQueueService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IBackupListService _backupListService;
    private readonly IRemoteController _remoteController;
    private readonly IRemoteControllerRegistration _remoteControllerRegistration;

    private const int APIVersion = 1;

    private record WebSocketRequest(int Version, string Id, string Action, string? Service);
    private record WebSocketRequest<T>(int Version, string Id, string Action, string? Service, T? Data) where T : class;
    private record WebSocketReply(int Version, string? Id, string? Service, string Message, bool Success, object? Data = null)
    {
        // For type detection on the client
        public string Type => "reply";
    }

    private sealed record WebsocketEventMessage<T>(string Type, int ApiVersion, T? Data);

    public WebsocketAccessor(
        JsonSerializerSettings jsonSettings,
        EventPollNotify eventPollNotify,
        IStatusService statusService,
        ITaskQueueService taskQueueService,
        ISettingsService settingsService,
        INotificationService notificationService,
        IBackupListService backupListService,
        IRemoteController remoteController,
        IRemoteControllerRegistration remoteControllerRegistration)
    {
        _jsonSettings = jsonSettings;
        _statusService = statusService;
        _backupListService = backupListService;
        _taskQueueService = taskQueueService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _remoteController = remoteController;
        _remoteControllerRegistration = remoteControllerRegistration;

        eventPollNotify.NewEvent += async (_, _) => { await SendAsync(SubscriptionService.LegacyStatus); };
        eventPollNotify.ServerSettingsUpdate += async (_, _) => { await SendAsync(SubscriptionService.ServerSettings); };
        eventPollNotify.BackupListUpdate += async (_, _) => { await SendAsync(SubscriptionService.BackupList); };
        eventPollNotify.NotificationsUpdated += async (_, _) => { await SendAsync(SubscriptionService.Notifications); };
        eventPollNotify.TaskQueueUpdate += async (_, _) => { await SendAsync(SubscriptionService.TaskQueue); };
        eventPollNotify.TaskCompleted += async (_, taskId) => { await SendTaskCompletedAsync(taskId, GetConnections()); };
        eventPollNotify.RemoteControlUpdate += async (_, _) => { await SendAsync(SubscriptionService.RemoteControl); };
        eventPollNotify.ProgressUpdate += async (_, progress) =>
        {
            if (progress == null)
                return;

            // Avoid generating data for subscriptions that are not active
            if (!_subscribers.Any(c => c.Value.ContainsKey(SubscriptionService.Progress)))
                return;

            await SendDataAsync(SubscriptionService.Progress, progress(), GetConnections());
        };
    }

    public async Task AddConnectionAsync(WebSocket newConnection, bool subscribeToLegacyStatus)
    {
        var subscribed = new ConcurrentDictionary<SubscriptionService, string>();
        if (subscribeToLegacyStatus)
            subscribed.TryAdd(SubscriptionService.LegacyStatus, "");
        _subscribers.TryAdd(newConnection, subscribed);

        _connections.Add(newConnection);
        if (subscribeToLegacyStatus)
            await SendAsync(SubscriptionService.LegacyStatus, [newConnection]);
        ClearClosed();
        await HandleClientDataAsync(newConnection);
    }

    private async Task HandleClientDataAsync(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1024 * 4];
        var result = await ReceiveAsync();

        while (!result?.CloseStatus.HasValue == true)
            result = await ReceiveAsync();

        if (result?.CloseStatus is not null)
            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);

        return;

        async Task<WebSocketReceiveResult?> ReceiveAsync()
        {
            WebSocketReceiveResult? receiveResult;
            try
            {
                receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (WebSocketException e)
                when (e is { WebSocketErrorCode: WebSocketError.ConnectionClosedPrematurely })
            {
                receiveResult = null;
            }

            if (receiveResult is not null && receiveResult.CloseStatus is null)
            {
                var message = Encoding.UTF8.GetString(buffer[..receiveResult.Count]);
                await HandleClientMessageAsync(webSocket, message);
            }

            return receiveResult;
        }
    }

    private void ClearClosed()
    {
        var openConnections = _connections.Where(c => c.State == WebSocketState.Open).ToHashSet();

        // No closed connections, nothing to do
        if (openConnections.Count == _connections.Count)
            return;

        _connections.Clear();
        foreach (var connection in openConnections)
            if (!_connections.Contains(connection))
                _connections.Add(connection);

        var substate = _subscribers.Keys.ToHashSet();
        foreach (var c in substate)
            if (!openConnections.Contains(c))
                _subscribers.TryRemove(c, out _);
    }

    private IEnumerable<WebSocket> GetConnections()
    {
        ClearClosed();
        return _connections;
    }

    private Dto.RemoteControlStatusOutput GetRemoteControlStatus()
    {
        var registrationTask = _remoteControllerRegistration.IsRegistering
            ? _remoteControllerRegistration.WaitForRegistrationAsync()
            : null;

        return new Dto.RemoteControlStatusOutput(
            CanEnable: _remoteController.CanEnable,
            IsEnabled: _remoteController.IsEnabled,
            IsConnected: _remoteController.Connected,
            IsRegistering: _remoteControllerRegistration.IsRegistering,
            IsRegisteringFaulted: registrationTask?.IsFaulted ?? false,
            IsRegisteringCompleted: registrationTask?.IsCompleted ?? false,
            RegistrationUrl: _remoteControllerRegistration.RegistrationUrl
        );
    }

    private ArraySegment<byte> GetBytes<T>(T Data)
    {
        var json = JsonConvert.SerializeObject(Data, _jsonSettings);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return new ArraySegment<byte>(bytes);
    }

    private Task SendRequestReplyAsync<T>(WebSocket socket, string id, string? service, string message, bool success, T? data = default)
        => socket.SendAsync(GetBytes(new WebSocketReply(APIVersion, id, service, message, success, data)), WebSocketMessageType.Text, true, CancellationToken.None);

    private Task SendRequestSuccessReplyAsync(WebSocket socket, WebSocketRequest req, string message = "OK")
        => SendRequestSuccessReplyAsync<object?>(socket, req, message, null);

    private Task SendRequestSuccessReplyAsync<T>(WebSocket socket, WebSocketRequest req, string message = "OK", T? data = default)
        => socket.SendAsync(GetBytes(new WebSocketReply(APIVersion, req.Id, req.Service, message, true, data)), WebSocketMessageType.Text, true, CancellationToken.None);

    private Task SendRequestFailureReplyAsync(WebSocket socket, WebSocketRequest req, string message)
        => SendRequestFailureReplyAsync<object>(socket, req, message, null);

    private Task SendRequestFailureReplyAsync<T>(WebSocket socket, WebSocketRequest req, string message, T? data = default)
        => socket.SendAsync(GetBytes(new WebSocketReply(APIVersion, req.Id, req.Service, message, false, data)), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task SendTaskCompletedAsync(long taskId, IEnumerable<WebSocket> connections)
    {
        var task = _taskQueueService.GetTaskInfo(taskId);
        if (task == null)
        {
            Log.WriteWarningMessage(LOGTAG, "WebsocketTaskNotFound", null, $"Task with ID {taskId} not found for completion notification.");
            return;
        }
        await SendDataAsync(SubscriptionService.TaskCompleted, task, connections);
    }


    private async Task SendAsync(SubscriptionService key, IEnumerable<WebSocket> connections)
    {
        // Avoid generating data for subscriptions that are not active
        if (!_subscribers.Any(c => c.Value.ContainsKey(key)))
            return;

        switch (key)
        {
            case SubscriptionService.LegacyStatus:
                await SendDataAsync(SubscriptionService.LegacyStatus, _statusService.GetStatus(), connections);
                break;
            case SubscriptionService.ServerSettings:
                await SendDataAsync(SubscriptionService.ServerSettings, _settingsService.GetSettingsMasked(), connections);
                break;
            case SubscriptionService.BackupList:
                var targets = connections.ToHashSet();
                await Task.WhenAll(
                    _subscribers
                        .Where(c => targets.Contains(c.Key))
                        .Select(c =>
                        {
                            var found = c.Value.TryGetValue(SubscriptionService.BackupList, out var order);
                            return (c.Key, found, order);
                        })
                        .Where(c => c.found)
                        .GroupBy(c => c.order)
                        .Select(c => SendDataAsync(SubscriptionService.BackupList, _backupListService.List(c.Key), c.Select(x => x.Key)))
                );
                break;
            case SubscriptionService.Notifications:
                await SendDataAsync(SubscriptionService.Notifications, _notificationService.GetNotifications(), connections);
                break;
            case SubscriptionService.TaskQueue:
                await SendDataAsync(SubscriptionService.TaskQueue, _taskQueueService.GetTaskQueue(), connections);
                break;
            case SubscriptionService.TaskCompleted:
                // This event is sent when a task completes, so we do not send initial data
                break;
            case SubscriptionService.Progress:
                // Progress updates are sent via the event system, so we cannot send information in advance
                break;
            case SubscriptionService.RemoteControl:
                await SendDataAsync(SubscriptionService.RemoteControl, GetRemoteControlStatus(), connections);
                break;
            default:
                Log.WriteWarningMessage(LOGTAG, "WebsocketUnknownSubscription", null, $"Unknown subscription service: {key}");
                break;
        }
    }

    private async Task SendDataAsync<T>(SubscriptionService key, T? data, IEnumerable<WebSocket> connections)
    {
        try
        {
            // Legacy clients expect the status to be sent as a simple string, not wrapped in an event message
            var bytes = key == SubscriptionService.LegacyStatus
                ? GetBytes(data)
                : GetBytes(new WebsocketEventMessage<T>(key.ToString().ToLowerInvariant(), APIVersion, data));

            await Task.WhenAll(
                connections
                    .Where(c => c.State == WebSocketState.Open)
                    .Where(c => _subscribers.TryGetValue(c, out var subscribed) && subscribed.ContainsKey(key))
                    .Select(c => c.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None))
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "WebsockSendFailure", ex, $"Failed to send websocket message");
        }
    }

    public Task SendAsync(SubscriptionService key) => SendAsync(key, GetConnections());

    public async Task HandleClientMessageAsync(WebSocket socket, string messagestr)
    {
        WebSocketRequest? message;
        try
        {
            message = JsonConvert.DeserializeObject<WebSocketRequest>(messagestr, _jsonSettings);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "WebsocketDeserializationError", ex, $"Failed to deserialize websocket message");
            await SendRequestReplyAsync<object>(socket, "", null, "Invalid message format", false);
            return;
        }

        if (message == null)
            return;

        if (message.Version != APIVersion)
        {
            await SendRequestFailureReplyAsync(socket, message, "Unsupported API version");
            return;
        }

        switch (message.Action)
        {
            case "status":
                await SendRequestReplyAsync(socket, message.Id, message.Service, "Status request received", true, _statusService.GetStatus());
                return;
            case "ping":
                await SendRequestSuccessReplyAsync(socket, message, "pong");
                return;
            case "auth":
                await SendRequestSuccessReplyAsync(socket, message, "Already authenticated");
                return;
            case "sub":
            case "unsub":
                {
                    if (!Enum.TryParse<SubscriptionService>(message.Service, true, out var serviceEnum))
                    {
                        await SendRequestFailureReplyAsync(socket, message, "Unknown subscription service");
                        return;
                    }

                    _subscribers.TryAdd(socket, new ConcurrentDictionary<SubscriptionService, string>());
                    if (_subscribers.TryGetValue(socket, out var subscribed))
                    {
                        if (message.Action == "sub")
                        {
                            var config = "";
                            try
                            {
                                var msg = JsonConvert.DeserializeObject<WebSocketRequest<string>>(messagestr, _jsonSettings);
                                config = msg?.Data ?? "";
                            }
                            catch
                            {
                            }

                            subscribed.AddOrUpdate(serviceEnum, config, (key, oldValue) => config);
                            await SendRequestSuccessReplyAsync<object>(socket, message, "Subscribed successfully");
                            await SendAsync(serviceEnum, [socket]);
                        }
                        else if (message.Action == "unsub")
                        {
                            subscribed.TryRemove(serviceEnum, out _);
                            await SendRequestSuccessReplyAsync<object>(socket, message, "Unsubscribed successfully");
                        }
                    }

                    return;
                }

            default:
                {
                    Log.WriteWarningMessage(LOGTAG, "WebsocketUnknownAction", null, $"Unknown websocket action: {message.Action}");
                    await SendRequestFailureReplyAsync(socket, message, "Unknown action");
                    return;
                }
        }
    }
}