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

