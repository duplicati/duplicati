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
using Duplicati.Library.Logging;
using Duplicati.Server;

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// DTO entry for reporting a log entry
/// </summary>
public sealed record LogEntry
{
    /// <summary>
    /// The time the message was logged
    /// </summary>
    public required DateTime When { get; init; }

    /// <summary>
    /// The ID assigned to the message
    /// </summary>
    public required long ID { get; init; }

    /// <summary>
    /// The logged message
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The log tag
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// The message ID
    /// </summary>
    public required string MessageID { get; init; }

    /// <summary>
    /// The message ID
    /// </summary>
    public required string ExceptionID { get; init; }

    /// <summary>
    /// The message type
    /// </summary>
    public required LogMessageType Type { get; init; }

    /// <summary>
    /// Exception data attached to the message
    /// </summary>
    public required string? Exception { get; init; }

    /// <summary>
    /// The backup ID, if any
    /// </summary>
    public required string BackupID { get; init; }

    /// <summary>
    /// The task ID, if any
    /// </summary>
    public required string TaskID { get; init; }

    /// <summary>
    /// Convert the internal record to a DTO record
    /// </summary>
    /// <param name="entry">The internal record</param>
    /// <returns>The DTO record</returns>
    public static LogEntry FromInternalEntry(LogWriteHandler.LogEntry entry)
    {
        return new LogEntry
        {
            When = entry.When,
            ID = entry.ID,
            Message = entry.Message,
            Tag = entry.Tag,
            MessageID = entry.MessageID,
            ExceptionID = entry.ExceptionID,
            Type = entry.Type,
            Exception = entry.Exception?.ToString(),
            BackupID = entry.BackupID,
            TaskID = entry.TaskID
        };
    }
}
