// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class ServerStatus : Interface.IServerStatus
    {
        public Tuple<long, string> ActiveTask { get; set; }
        public LiveControlState ProgramState { get; set; }
        public IList<Tuple<long, string>> SchedulerQueueIds { get; set; }
        public bool HasError { get; set; }
        public bool HasWarning { get; set; }
        public SuggestedStatusIcon SuggestedStatusIcon { get; set; }
        public DateTime EstimatedPauseEnd { get; set; }
        public long LastEventID { get; set; }
        public long LastDataUpdateID { get; set; }
        public long LastNotificationUpdateID { get; set; }

        public string UpdatedVersion { get; set; }
        public string UpdateDownloadLink { get; set; }
        public UpdatePollerStates UpdaterState { get; set; }
        public double UpdateDownloadProgress { get; set; }
    }
}
