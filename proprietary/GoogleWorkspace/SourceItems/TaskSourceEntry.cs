// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;
using GTask = Google.Apis.Tasks.v1.Data.Task;
using Task = System.Threading.Tasks.Task;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class TaskSourceEntry(string parentPath, GTask task)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, (task.Title ?? task.Id) + ".json"),
        !string.IsNullOrEmpty(task.Updated) ? DateTime.Parse(task.Updated!) : DateTime.UnixEpoch,
        !string.IsNullOrEmpty(task.Updated) ? DateTime.Parse(task.Updated!) : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Task.ToString() },
            { "gsuite:Name", task.Title ?? task.Id },
            { "gsuite:id", task.Id },
            { "gsuite:Status", task.Status },
            { "gsuite:Due", task.Due }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
