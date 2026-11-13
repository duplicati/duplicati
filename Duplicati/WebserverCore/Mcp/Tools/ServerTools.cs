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

using System.ComponentModel;
using Duplicati.Library.Interface;
using Duplicati.Library.IO;
using Duplicati.WebserverCore.Abstractions;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp.Tools;

/// <summary>
/// MCP tools reporting the state of the server
/// </summary>
/// <param name="taskQueueService">The task queue</param>
/// <param name="liveControls">The pause state</param>
/// <param name="scope">What the caller is allowed to do</param>
[McpServerToolType]
public class ServerTools(ITaskQueueService taskQueueService, ILiveControls liveControls, McpScope scope)
{
    [McpServerTool(Name = "get_server_status", Title = "Get server status", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reports the Duplicati server version, whether it is paused, and the running and waiting tasks. Tasks run one at a time, in queue order.")]
    public Task<McpServerStatus> GetServerStatus()
        => McpHelpers.RunAsync("get_server_status", null, scope, McpTokenLevel.Read, () => Task.FromResult(new McpServerStatus(
            Library.AutoUpdater.UpdaterManager.SelfVersion.Displayname ?? "Unknown",
            liveControls.IsPaused,
            taskQueueService.GetTaskQueueDetails()
                // A task of a backup outside the scope of the caller is shown, as it holds up the queue, but not named
                .Select(x => scope.CanAccessBackup(x.BackupID)
                    ? new McpQueueItem(x.TaskID, x.BackupID, x.BackupName, McpHelpers.FormatOperation(x.Operation), x.Status)
                    : new McpQueueItem(x.TaskID, null, null, "Other", x.Status))
                .ToList())));
}
