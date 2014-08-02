using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Logging;

namespace Duplicati.Server
{
    public class LogWriteHandler : ILog
    {
        public void WriteMessage(string message, LogMessageType type, Exception exception)
        {
            
        }

        internal void SetServerFile(string p, LogMessageType loglevel)
        {
            
        }

        internal void SetOperationFile(string file, LogMessageType level)
        {
            
        }

        internal void Dispose()
        {
            
        }

        internal void RemoveOperationFile()
        {
            
        }

        internal object AfterID(long id, LogMessageType level)
        {
            return null;
        }
    }
}
