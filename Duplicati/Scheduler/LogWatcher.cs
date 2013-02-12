using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler
{
    public class LogWatcher : System.IO.FileSystemWatcher 
    {
        public LogWatcher(string aLogDir)
            : base(aLogDir, "*.txt")
        {
            this.NotifyFilter = System.IO.NotifyFilters.Attributes;
        }

    }
}
