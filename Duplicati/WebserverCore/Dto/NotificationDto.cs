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
namespace Duplicati.WebserverCore.Dto
{
    /// <summary>
    /// The notification DTO
    /// </summary>
    public sealed record NotificationDto
    {
        /// <summary>
        /// Gets or sets the ID of the notification.
        /// </summary>
        public required long ID { get; set; }

        /// <summary>
        /// Gets or sets the type of the notification.
        /// </summary>
        public required Duplicati.Server.Serialization.NotificationType Type { get; set; }

        /// <summary>
        /// Gets or sets the title of the notification.
        /// </summary>
        public required string Title { get; set; }

        /// <summary>
        /// Gets or sets the message of the notification.
        /// </summary>
        public required string Message { get; set; }

        /// <summary>
        /// Gets or sets the exception of the notification.
        /// </summary>
        public required string Exception { get; set; }

        /// <summary>
        /// Gets or sets the backup ID of the notification.
        /// </summary>
        public required string BackupID { get; set; }

        /// <summary>
        /// Gets or sets the action of the notification.
        /// </summary>
        public required string Action { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the notification.
        /// </summary>
        public required DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the log entry ID of the notification.
        /// </summary>
        public required string LogEntryID { get; set; }

        /// <summary>
        /// Gets or sets the message ID of the notification.
        /// </summary>
        public required string MessageID { get; set; }

        /// <summary>
        /// Gets or sets the message log tag of the notification.
        /// </summary>
        public required string MessageLogTag { get; set; }

    }
}