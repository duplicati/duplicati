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
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Handles logging from the server, 
/// and provides an entry point for the runner
/// to redirect log output to a file
/// </summary>
public interface ILogWriteHandler : ILogDestination, IDisposable
{
    /// <summary>
    /// Represents a single log event
    /// </summary>
    public struct LiveLogEntry
    {
        /// <summary>
        /// The context key used for conveying the backup ID
        /// </summary>
        public const string LOG_EXTRA_BACKUPID = "BackupID";
        /// <summary>
        /// The context key used for conveying the task ID
        /// </summary>
        public const string LOG_EXTRA_TASKID = "TaskID";

        /// <summary>
        /// A unique ID that sequentially increments
        /// </summary>
        private static long _id;

        /// <summary>
        /// The time the message was logged
        /// </summary>
        public readonly DateTime When;

        /// <summary>
        /// The ID assigned to the message
        /// </summary>
        public readonly long ID;

        /// <summary>
        /// The logged message
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// The log tag
        /// </summary>
        public readonly string Tag;

        /// <summary>
        /// The message ID
        /// </summary>
        public readonly string MessageID;

        /// <summary>
        /// The message ID
        /// </summary>
        public readonly string? ExceptionID;

        /// <summary>
        /// The message type
        /// </summary>
        public readonly LogMessageType Type;

        /// <summary>
        /// Exception data attached to the message
        /// </summary>
        public readonly Exception? Exception;

        /// <summary>
        /// The backup ID, if any
        /// </summary>
        public readonly string? BackupID;

        /// <summary>
        /// The task ID, if any
        /// </summary>
        public readonly string TaskID;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveLogEntry"/> struct.
        /// </summary>
        /// <param name="entry">The log entry to store</param>
        public LiveLogEntry(LogEntry entry)
        {
            this.ID = System.Threading.Interlocked.Increment(ref _id);
            this.When = entry.When;
            this.Message = entry.FormattedMessage;
            this.Type = entry.Level;
            this.Exception = entry.Exception;
            this.Tag = entry.FilterTag;
            this.MessageID = entry.Id;
            this.BackupID = entry[LOG_EXTRA_BACKUPID];
            this.TaskID = entry[LOG_EXTRA_TASKID];

            if (entry.Exception == null)
                this.ExceptionID = null;
            else if (entry.Exception is UserInformationException exception)
                this.ExceptionID = exception.HelpID;
            else
                this.ExceptionID = entry.Exception.GetType().FullName;

        }
    }


    void RenewTimeout(LogMessageType type);

    void SetServerFile(string path, LogMessageType level);

    void AppendLogDestination(ILogDestination destination, LogMessageType level);

    LiveLogEntry[] AfterTime(DateTime offset, LogMessageType level);

    LiveLogEntry[] AfterID(long id, LogMessageType level, int pagesize);
}

