using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// A helper exception to indicate a file-change error
    /// </summary>
    public class FileHasChangedException : System.IO.IOException
    {
        public FileHasChangedException(string message)
            : base(message)
        {
        }

        public FileHasChangedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
