#region "Disclaimer / License"
//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

        public RunnerState ActiveBackupState
        {
            get { /*return Program.Runner.CurrentState;*/ return RunnerState.Stopped; }
        }

        public long ActiveScheduleId
        {
            get 
            { 
                var t = Program.WorkThread.CurrentTask;
                if (t == null || t.Item2 != DuplicatiOperation.Backup)
                    return -1;
                else
                    return t.Item1;

            }
        }

        public IList<long> SchedulerQueueIds
        {
            get { return (from n in Program.Scheduler.WorkerQueue where n.Item2 == DuplicatiOperation.Backup select n.Item1).ToList(); }
        }
        
        public bool HasWarning { get { return Program.HasWarning; } }
        public bool HasError { get { return Program.HasError; } }
        
        public SuggestedStatusIcon SuggestedStatusIcon
        {
            get
            {
                if (this.ActiveScheduleId < 0)
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
    }
}

