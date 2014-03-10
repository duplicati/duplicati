using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization.Interface
{
    public interface IProgressEventData
    {
        long BackupID { get; }
        
        string BackendAction  { get; }
        string BackendPath { get; }
        long BackendFileSize { get; }
        long BackendFileProgress { get; }
        long BackendSpeed { get; }
        
        string CurrentFilename { get; }
        long CurrentFilesize { get; }
        long CurrentFileoffset { get; }
        
        string Phase { get; }
        float OverallProgress { get; }
        long ProcessedFileCount { get; }
        long ProcessedFileSize { get; }
        long TotalFileCount { get; }
        long TotalFileSize { get; }
        bool StillCounting { get; }

    }
}
