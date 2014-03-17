using System;
using System.Collections.Generic;

namespace Duplicati.Server.Serialization.Interface
{
    public interface IServerStatus
    {
        Tuple<long, string> ActiveTask { get; }
        LiveControlState ProgramState { get; }
        System.Collections.Generic.IList<Tuple<long,string>> SchedulerQueueIds { get; }
        bool HasWarning { get; }
        bool HasError { get; }
        SuggestedStatusIcon SuggestedStatusIcon { get; }
        DateTime EstimatedPauseEnd { get; }
        long LastEventID { get; }
        long LastDataUpdateID { get;  }
    }
}
