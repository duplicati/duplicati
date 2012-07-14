using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization
{
    public interface IProgressEventData
    {
        DuplicatiOperation Operation { get; }
        DuplicatiOperationMode Mode { get; }
        string Message { get; }
        string SubMessage { get; }
        int Progress { get; }
        int SubProgress { get; }
        long LastEventID { get; }
    }
}
