using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// An exception indicating that the requested folder is missing
    /// </summary>
    public class FolderMissingException : Exception
    {
        public FolderMissingException()
            : base()
        { }

        public FolderMissingException(string message)
            : base(message)
        { }

        public FolderMissingException(Exception innerException)
            : base(Strings.Common.FolderMissingError, innerException)
        { }

        public FolderMissingException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the requested folder already existed
    /// </summary>
    public class FolderAreadyExistedExcpetion : Exception
    {
        public FolderAreadyExistedExcpetion()
            : base()
        { }

        public FolderAreadyExistedExcpetion(string message)
            : base(message)
        { }

        public FolderAreadyExistedExcpetion(Exception innerException)
            : base(Strings.Common.FolderAlreadyExistsError, innerException)
        { }

        public FolderAreadyExistedExcpetion(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
