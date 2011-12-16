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

namespace Duplicati.Server
{
    /// <summary>
    /// This class collects all reportable status properties into a single class that can be exported as JSON
    /// </summary>
    public class SerializableStatus : ISerializableStatus
    {
        public LiveControlState ProgramState
        {
            get { return EnumConverter.Convert<LiveControlState>(Program.LiveControl.State); }
        }

        public RunnerState ActiveBackupState
        {
            get { return Program.Runner.CurrentState; }
        }

        public long ActiveScheduleId
        {
            get 
            { 
                IDuplicityTask t = Program.WorkThread.CurrentTask;
                if (t == null || t.Schedule == null)
                    return -1;
                else
                    return t.Schedule.ID;

            }
        }

        public IList<long> SchedulerQueueIds
        {
            get { return Program.Scheduler.Schedule.Select(x => x.ID).ToList(); }
        }

        public IProgressEventData RunningBackupStatus
        {
            get { return Program.Runner.LastEvent; }
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
    }
}

