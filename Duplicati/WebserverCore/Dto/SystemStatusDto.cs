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
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// A snapshot of the system status, sent periodically to subscribers of the system status service.
/// The server does not keep any history; clients that want to show trends must collect the snapshots themselves.
/// </summary>
/// <param name="Timestamp">The time the snapshot was taken, in UTC</param>
/// <param name="IntervalMs">The interval between snapshots, in milliseconds</param>
/// <param name="Machine">Information about the machine running the server</param>
/// <param name="Processes">Resource usage of the server process and any controller processes it runs</param>
/// <param name="Queue">The current task queue, with the running task first</param>
/// <param name="Progress">The progress of the running task, or null if no task is running</param>
public sealed record SystemStatusDto(
    DateTime Timestamp,
    int IntervalMs,
    SystemStatusMachineDto Machine,
    IEnumerable<SystemStatusProcessDto> Processes,
    IEnumerable<SystemStatusQueueItemDto> Queue,
    IProgressEventData? Progress
);

/// <summary>
/// Information about the machine running the server.
/// Values that cannot be determined on the current platform are null.
/// </summary>
/// <param name="ProcessorCount">The number of logical processors</param>
/// <param name="CpuUsage">The machine-wide CPU usage since the previous snapshot, as a fraction between 0 and 1</param>
/// <param name="TotalMemory">The total memory available, in bytes</param>
/// <param name="AvailableMemory">The memory that is currently available, in bytes</param>
public sealed record SystemStatusMachineDto(
    int ProcessorCount,
    double? CpuUsage,
    long? TotalMemory,
    long? AvailableMemory
);

/// <summary>
/// Resource usage for a single process.
/// </summary>
/// <param name="Role">The role of the process, either "server" or "controller"</param>
/// <param name="ProcessId">The process ID</param>
/// <param name="StartTime">The time the process started, in UTC</param>
/// <param name="CpuUsage">The CPU usage since the previous snapshot, as a fraction of all processors between 0 and 1, or null on the first snapshot</param>
/// <param name="WorkingSet">The working set, in bytes</param>
/// <param name="PrivateMemory">The private memory, in bytes</param>
/// <param name="ManagedHeapSize">The size of the managed heap, in bytes, if known</param>
/// <param name="ThreadCount">The number of threads</param>
public sealed record SystemStatusProcessDto(
    string Role,
    int ProcessId,
    DateTime? StartTime,
    double? CpuUsage,
    long WorkingSet,
    long PrivateMemory,
    long? ManagedHeapSize,
    int ThreadCount
);

/// <summary>
/// An entry in the task queue.
/// </summary>
/// <param name="TaskID">The task ID</param>
/// <param name="BackupID">The backup ID, if the task is for a backup</param>
/// <param name="BackupName">The backup name, if the task is for a backup</param>
/// <param name="Operation">The operation the task performs</param>
/// <param name="Status">The status of the task, either "Running" or "Waiting"</param>
/// <param name="TaskStarted">When the task started, if it has started</param>
public sealed record SystemStatusQueueItemDto(
    long TaskID,
    string? BackupID,
    string? BackupName,
    string Operation,
    string Status,
    DateTime? TaskStarted
);
