using System;
using System.Collections.Generic;

namespace Duplicati.Server.Serialization
{
    public interface IServerStatus
    {
        RunnerState ActiveBackupState { get; }
        long ActiveScheduleId { get; }
        LiveControlState ProgramState { get; }
        System.Collections.Generic.IList<long> SchedulerQueueIds { get; }
        bool HasWarning { get; }
        bool HasError { get; }
        SuggestedStatusIcon SuggestedStatusIcon { get; }
        DateTime LastLogUpdate { get; }
        DateTime EstimatedPauseEnd { get; }
        long LastEventID { get; }
        long LastDataUpdateID { get;  }
    }
}
