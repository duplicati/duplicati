using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Implementations
{
    internal class ProgressEventData : IProgressEventData
    {
        public DuplicatiOperation Operation { get; set; }

        public DuplicatiOperationMode Mode { get; set; }

        public string Message { get; set; }

        public string SubMessage { get; set; }

        public int Progress { get; set; }

        public int SubProgress { get; set; }
    }
}
