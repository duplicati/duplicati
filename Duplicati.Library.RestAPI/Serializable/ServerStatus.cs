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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Server.Serialization;
using Duplicati.Library.RestAPI;

namespace Duplicati.Server.Serializable
{
    /// <summary>
    /// This class collects all reportable status properties into a single class that can be exported as JSON
    /// </summary>
    public class ServerStatus : Duplicati.Server.Serialization.Interface.IServerStatus
    {
        public LiveControlState ProgramState
        {
            get { return EnumConverter.Convert<LiveControlState>(FIXMEGlobal.LiveControl.State); }
        }

        public string UpdatedVersion 
        { 
            get 
            { 
                var u = FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion;
                if (u == null)
                    return null;
                
                Version v;
                if (!Version.TryParse(u.Version, out v))
                    return null;

                if (v <= System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
                    return null;

                return u.Displayname; 
            }
        }

        public string UpdateDownloadLink => FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion?.GetUpdateUrls()?.FirstOrDefault();

        public UpdatePollerStates UpdaterState { get { return FIXMEGlobal.UpdatePoller.ThreadState; } }

        public double UpdateDownloadProgress { get { return FIXMEGlobal.UpdatePoller.DownloadProgess; } }


        public Tuple<long, string> ActiveTask
        {
            get 
            { 
                var t = FIXMEGlobal.WorkThread.CurrentTask;
                if (t == null)
                    return null;
                else
                    return new Tuple<long, string>(t.TaskID, t.Backup == null ? null : t.Backup.ID);
            }
        }

        public IList<Tuple<long, string>> SchedulerQueueIds
        {
            get { return (from n in FIXMEGlobal.Scheduler.WorkerQueue where n.Backup != null select new Tuple<long, string>(n.TaskID, n.Backup.ID)).ToList(); }
        }

        public IList<Tuple<string, DateTime>> ProposedSchedule
        {
            get
            {
                return (
                    from n in FIXMEGlobal.Scheduler.Schedule
                                let backupid = (from t in n.Value.Tags
                                                where t != null && t.StartsWith("ID=", StringComparison.Ordinal)
                                                select t.Substring("ID=".Length)).FirstOrDefault()
                                where !string.IsNullOrWhiteSpace(backupid)
                                select new Tuple<string, DateTime>(backupid, n.Key)
                ).ToList();
            }
        }
        
        public bool HasWarning { get { return FIXMEGlobal.DataConnection.ApplicationSettings.UnackedWarning; } }
        public bool HasError { get { return FIXMEGlobal.DataConnection.ApplicationSettings.UnackedError; } }
        
        public SuggestedStatusIcon SuggestedStatusIcon
        {
            get
            {
                if (this.ActiveTask == null)
                {
                    if (this.ProgramState == LiveControlState.Paused)
                        return SuggestedStatusIcon.Paused;
                    
                    if (this.HasError)
                        return SuggestedStatusIcon.ReadyError;
                    if (this.HasWarning)
                        return SuggestedStatusIcon.ReadyWarning;
                    
                    return SuggestedStatusIcon.Ready;
                }
                else
                {
                    if (this.ProgramState == LiveControlState.Running)
                        return SuggestedStatusIcon.Active;
                    else
                        return SuggestedStatusIcon.ActivePaused;
                }
            }
        }

        public DateTime EstimatedPauseEnd 
        {
            get 
            { 
                return FIXMEGlobal.LiveControl.EstimatedPauseEnd; 
            }
        }

        private long m_lastEventID = FIXMEGlobal.StatusEventNotifyer.EventNo;

        public long LastEventID 
        { 
            get { return m_lastEventID; }
            set { m_lastEventID = value; }
        }

        public long LastDataUpdateID { get { return FIXMEGlobal.PeekLastDataUpdateID(); } }

        public long LastNotificationUpdateID { get { return FIXMEGlobal.PeekLastNotificationUpdateID(); } }

    }
}

