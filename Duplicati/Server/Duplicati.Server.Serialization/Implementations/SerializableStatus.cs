using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class SerializableStatus : ISerializableStatus
    {
        public RunnerState ActiveBackupState { get; set; }
        public long ActiveScheduleId { get; set; }
        public LiveControlState ProgramState { get; set; }
        public IProgressEventData RunningBackupStatus { get; set; }
        public IList<long> SchedulerQueueIds { get; set; }
    }
}
