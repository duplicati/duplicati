#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An exception indicating that the requested folder is missing
    /// </summary>
    [Serializable]
    public class FolderMissingException : Exception
    {
        public FolderMissingException()
            : base(Strings.Common.FolderMissingError)
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
    [Serializable]
    public class FolderAreadyExistedException : Exception
    {
        public FolderAreadyExistedException()
            : base(Strings.Common.FolderAlreadyExistsError)
        { }

        public FolderAreadyExistedException(string message)
            : base(message)
        { }

        public FolderAreadyExistedException(Exception innerException)
            : base(Strings.Common.FolderAlreadyExistsError, innerException)
        { }

        public FolderAreadyExistedException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the user has cancelled the action
    /// </summary>
    [Serializable]
    public class CancelException : Exception
    {
        public CancelException()
            : base()
        { }

        public CancelException(string message)
            : base(message)
        { }

        public CancelException(Exception innerException)
            : base(Strings.Common.CancelExceptionError, innerException)
        { }

        public CancelException(string message, Exception innerException)
            : base(message, innerException)
        { }
}
}
