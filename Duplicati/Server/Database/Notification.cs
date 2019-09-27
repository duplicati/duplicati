﻿//  Copyright (C) 2015, The Duplicati Team

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

namespace Duplicati.Server.Database
{
    public class Notification : Server.Serialization.Interface.INotification
    {
        #region INotification implementation
        public long ID { get; set; }
        public Duplicati.Server.Serialization.NotificationType Type { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public string BackupID { get; set; }
        public string Action { get; set; }
        public DateTime Timestamp  { get; set; }
        public string LogEntryID { get; set; }
        public string MessageID { get; set; }
        public string MessageLogTag { get; set; }
        #endregion
    }
}

