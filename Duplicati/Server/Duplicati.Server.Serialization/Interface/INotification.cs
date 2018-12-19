//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
        long ID { get; set; }
        /// <summary>
        /// The notification type
        /// </summary>
        NotificationType Type { get; set; }
        /// <summary>
        /// The notification title
        /// </summary>
        string Title { get; set; }
        /// <summary>
        /// The notification message
        /// </summary>
        string Message { get; set; }
        /// <summary>
        /// The serialized exception data, if any
        /// </summary>
        string Exception { get; set; }
        /// <summary>
        /// The ID of the backup that the notification belongs to
        /// </summary>
        string BackupID { get; set; }
        /// <summary>
        /// The action for the notification
        /// </summary>
        string Action { get; set; }
        /// <summary>
        /// When the notification was emitted
        /// </summary>
        DateTime Timestamp { get; set; }
        /// <summary>
        /// The ID of the log entry that relates to this message, if any
        /// </summary>
        /// <value>The log entry identifier.</value>
        string LogEntryID { get; set; }
        /// <summary>
        /// The ID of the event that triggered this notification
        /// </summary>
        string MessageID { get; set; }
        /// <summary>
        /// The logtag of the error or event that triggered this notification
        /// </summary>
        string MessageLogTag { get; set; }
    }
}

