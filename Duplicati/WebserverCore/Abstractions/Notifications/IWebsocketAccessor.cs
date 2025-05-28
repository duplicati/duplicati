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
using System.Net.WebSockets;

namespace Duplicati.WebserverCore.Abstractions.Notifications;

/// <summary>
/// The services that can be subscribed to via the websocket connection.
/// </summary>
public enum SubscriptionService
{
    /// <summary>
    /// The legacy status of the server, including active tasks.
    /// </summary>
    LegacyStatus,
    /// <summary>
    /// Data has been updated or changed, such as backup configurations
    /// </summary>
    BackupList,
    /// <summary>
    /// Server settings updates
    /// </summary>
    ServerSettings,
    /// <summary>
    /// Progress updates for ongoing tasks, such as backups or restores.
    /// </summary>
    Progress,
    /// <summary>
    /// The task queue updates, when tasks are added, removed, or changed in the queue.
    /// </summary>
    TaskQueue,
    /// <summary>
    /// Notification messages
    /// </summary>
    Notifications,
    /// <summary>
    /// Scheduler updates, such as when scheduled tasks are added, removed, or changed.
    /// </summary>
    Scheduler
}


/// <summary>
/// Interface for managing WebSocket connections and handling messages.
/// </summary>
public interface IWebsocketAccessor
{
    /// <summary>
    /// Adds a new WebSocket connection to the list of active connections.
    /// </summary>
    /// <param name="newConnection">The new WebSocket connection to add.</param>
    /// <param name="subscribeToLegacyStatus">If true, the connection will subscribe to legacy status updates.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddConnection(WebSocket newConnection, bool subscribeToLegacyStatus);
}