#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// A special exception that gives the user information on how to proceed.
    /// Use this execption if the message is directed at the end user, and supplies
    /// some guidance or explanation for the error. Exceptions of this type will
    /// suppress the stack trace by default, on commandline output
    /// </summary>
    [Serializable]
    public class UserInformationException : Exception
    {
        public UserInformationException(string message)
            : base(message)
        {
        }

        public UserInformationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// An exception indicating that the requested folder is missing
    /// </summary>
    [Serializable]
    public class FolderMissingException : UserInformationException
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
    /// An exception indicating that the requested folder is missing
    /// </summary>
    [Serializable]
    public class FileMissingException : UserInformationException
    {
        public FileMissingException()
            : base(LC.L("The requested file does not exist"))
        { }

        public FileMissingException(string message)
            : base(message)
        { }

        public FileMissingException(Exception innerException)
            : base(LC.L("The requested file does not exist"), innerException)
        { }

        public FileMissingException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the requested folder already existed
    /// </summary>
    [Serializable]
    public class FolderAreadyExistedException : UserInformationException
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
    public class CancelException : UserInformationException
    {
        public CancelException()
            : base(Strings.Common.CancelExceptionError)
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
