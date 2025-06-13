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

#nullable enable

using System;
using System.Threading.Tasks;

namespace Duplicati.Server.Serialization.Interface;

/// <summary>
/// Represents a queued task.
/// </summary>
public interface IQueuedTask
{
    /// <summary>
    /// The task ID.
    /// </summary>
    long TaskID { get; }
    /// <summary>
    /// The backup ID, if applicable.
    /// </summary>
    string? BackupID { get; }
    /// <summary>
    /// The operation type of the task.
    /// </summary>
    DuplicatiOperation Operation { get; }
    /// <summary>
    /// Callback to be executed when the task is starting.
    /// </summary>
    Func<Task>? OnStarting { get; set; }
    /// <summary>
    /// Callback to be executed when the task is finished.
    /// If the task completes successfully, the exception parameter will be null.
    /// </summary>
    Func<Exception?, Task>? OnFinished { get; set; }

    /// <summary>
    /// Updates the throttle speeds for the task.
    /// </summary>
    /// <param name="uploadSpeed">The upload speed to set.</param>
    /// <param name="downloadSpeed">The download speed to set.</param>
    void UpdateThrottleSpeeds(string? uploadSpeed, string? downloadSpeed);
    /// <summary>
    /// The time when the task was starting to execute.
    /// </summary>
    DateTime? TaskStarted { get; set; }
    /// <summary>
    /// The time when the task was finished executing.
    /// </summary>
    DateTime? TaskFinished { get; set; }
    /// <summary>
    /// Stops the task.
    /// </summary>
    void Stop();
    /// <summary>
    /// Aborts the task.
    /// </summary>
    void Abort();
    /// <summary>
    /// Pauses the task.
    /// </summary>
    /// <param name="alsoTransfers">If true, also pauses transfers.</param>
    void Pause(bool alsoTransfers);
    /// <summary>
    /// Resumes the task.
    /// </summary>
    void Resume();
}
