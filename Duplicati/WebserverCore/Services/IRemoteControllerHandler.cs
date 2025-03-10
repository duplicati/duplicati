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

using Duplicati.Library.RemoteControl;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Interface for handling remote control messages.
/// </summary>
public interface IRemoteControllerHandler
{
    /// <summary>
    /// Prepares metadata for a remote control connection.
    /// </summary>
    /// <param name="metadata">The initial metadata</param>
    /// <returns>The metadata to use</returns>
    Task<Dictionary<string, string?>> OnConnect(Dictionary<string, string?> metadata);

    /// <summary>
    /// Re-keys the remote control connection.
    /// </summary>
    /// <param name="data">The data to re-key with</param>
    /// <returns>An awaitable task</returns>
    Task ReKey(ClaimedClientData data);

    /// <summary>
    /// Handles a control message.
    /// </summary>
    /// <param name="message">The control message to handle</param>
    /// <returns>An awaitable task</returns>
    Task OnControl(KeepRemoteConnection.ControlMessage message);

    /// <summary>
    /// Handles a command message.
    /// </summary>
    /// <param name="commandMessage">The command message to handle</param>
    /// <returns>An awaitable task</returns>
    Task OnMessage(KeepRemoteConnection.CommandMessage commandMessage);
}