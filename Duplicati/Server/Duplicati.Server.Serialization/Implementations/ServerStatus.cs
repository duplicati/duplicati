using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class ServerStatus : Interface.IServerStatus
    {
        public RunnerState ActiveBackupState { get; set; }
        public long ActiveScheduleId { get; set; }
        public LiveControlState ProgramState { get; set; }
        public IList<long> SchedulerQueueIds { get; set; }
        public bool HasError { get; set; }
        public bool HasWarning { get; set; }
        public SuggestedStatusIcon SuggestedStatusIcon { get; set; }
        public DateTime EstimatedPauseEnd { get; set; }
        public long LastEventID { get; set; }
        public long LastDataUpdateID { get; set; }
    }
}
