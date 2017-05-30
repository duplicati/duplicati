#region "Disclaimer / License"
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
#endregion
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Server.Serialization;

namespace Duplicati.Server.Serializable
{
    /// <summary>
    /// This class collects all reportable status properties into a single class that can be exported as JSON
    /// </summary>
    public class ServerStatus : Duplicati.Server.Serialization.Interface.IServerStatus
    {
        public LiveControlState ProgramState
        {
            get { return EnumConverter.Convert<LiveControlState>(Program.LiveControl.State); }
        }

        public string UpdatedVersion 
        { 
            get 
            { 
                var u = Program.DataConnection.ApplicationSettings.UpdatedVersion;
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

        public UpdatePollerStates UpdaterState { get { return Program.UpdatePoller.ThreadState; } }

        public bool UpdateReady { get { return Duplicati.Library.AutoUpdater.UpdaterManager.HasUpdateInstalled; } }

        public double UpdateDownloadProgress { get { return Program.UpdatePoller.DownloadProgess; } }


        public Tuple<long, string> ActiveTask
        {
            get 
            { 
                var t = Program.WorkThread.CurrentTask;
                if (t == null)
                    return null;
                else
                    return new Tuple<long, string>(t.TaskID, t.Backup == null ? null : t.Backup.ID);
            }
        }

        public IList<Tuple<long, string>> SchedulerQueueIds
        {
            get { return (from n in Program.Scheduler.WorkerQueue where n.Backup != null select new Tuple<long, string>(n.TaskID, n.Backup.ID)).ToList(); }
        }

        public IList<Tuple<string, DateTime>> ProposedSchedule
        {
            get
            {
                return (
                    from n in Program.Scheduler.Schedule
                                let backupid = (from t in n.Value.Tags
                                                where t != null && t.StartsWith("ID=")
                                                select t.Substring("ID=".Length)).FirstOrDefault()
                                where !string.IsNullOrWhiteSpace(backupid)
                                select new Tuple<string, DateTime>(backupid, n.Key)
                ).ToList();
            }
        }
        
        public bool HasWarning { get { return Program.DataConnection.ApplicationSettings.UnackedWarning; } }
        public bool HasError { get { return Program.DataConnection.ApplicationSettings.UnackedError; } }
        
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
                return Program.LiveControl.EstimatedPauseEnd; 
            }
        }

        private long m_lastEventID = Program.StatusEventNotifyer.EventNo;

        public long LastEventID 
        { 
            get { return m_lastEventID; }
            set { m_lastEventID = value; }
        }

        public long LastDataUpdateID { get { return Program.LastDataUpdateID; } }

        public long LastNotificationUpdateID { get { return Program.LastNotificationUpdateID; } }

    }
}

