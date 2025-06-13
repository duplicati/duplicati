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

using System;

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// Describes a notification
    /// </summary>
    public interface INotification
    {
        /// <summary>
        /// The notification unique ID
        /// </summary>
        long ID { get; }
        /// <summary>
        /// The notification type
        /// </summary>
        NotificationType Type { get; }
        /// <summary>
        /// The notification title
        /// </summary>
        string Title { get; }
        /// <summary>
        /// The notification message
        /// </summary>
        string Message { get; }
        /// <summary>
        /// The serialized exception data, if any
        /// </summary>
        string Exception { get; }
        /// <summary>
        /// The ID of the backup that the notification belongs to
        /// </summary>
        string BackupID { get; }
        /// <summary>
        /// The action for the notification
        /// </summary>
        string Action { get; }
        /// <summary>
        /// When the notification was emitted
        /// </summary>
        DateTime Timestamp { get; }
        /// <summary>
        /// The ID of the log entry that relates to this message, if any
        /// </summary>
        /// <value>The log entry identifier.</value>
        string LogEntryID { get; }
        /// <summary>
        /// The ID of the event that triggered this notification
        /// </summary>
        string MessageID { get; }
        /// <summary>
        /// The logtag of the error or event that triggered this notification
        /// </summary>
        string MessageLogTag { get; }
    }
}

