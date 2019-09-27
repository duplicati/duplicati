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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Duplicati.Library.UsageReporter
{
    /// <summary>
    /// The item being sent to the server
    /// </summary>
    internal class ReportItem
    {
        [JsonProperty("timestamp")]
        public long TimeStamp { get; set; }
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ReportType Type { get; set; }
        [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)]
        public long? Count { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string EventName { get; set; }
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        public ReportItem()
        {
            this.TimeStamp = (long)(DateTime.UtcNow - Library.Utility.Utility.EPOCH).TotalSeconds;
        }

        public ReportItem(ReportType type, long? count, string eventname, string data)
            : this()
        {
            this.Type = type;
            this.Count = count;
            this.EventName = eventname;
            this.Data = data;
        }
    }
}

