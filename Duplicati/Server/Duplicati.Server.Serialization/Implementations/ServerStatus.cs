using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        public string UpdatedVersion { get; set; }
        public UpdatePollerStates UpdaterState { get; set; }
        public bool UpdateReady { get; set; }
        public double UpdateDownloadProgress { get; set; }
    }
}
