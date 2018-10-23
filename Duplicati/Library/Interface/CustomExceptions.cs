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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// A special exception that gives the user information on how to proceed.
    /// Use this exception if the message is directed at the end user, and supplies
    /// some guidance or explanation for the error. Exceptions of this type will
    /// suppress the stack trace by default, on commandline output
    /// </summary>
    [Serializable]
    public class UserInformationException : Exception
    {
        /// <summary>
        /// The help ID for the exception
        /// </summary>
        public readonly string HelpID;

        public UserInformationException(string message, string helpId)
            : base(message)
        {
            HelpID = helpId;
        }

        public UserInformationException(string message, string helpId, Exception innerException)
            : base(message, innerException)
        {
            HelpID = helpId;
        }
    }

    /// <summary>
    /// An exception indicating that the requested folder is missing
    /// </summary>
    [Serializable]
    public class FolderMissingException : UserInformationException
    {
        public FolderMissingException()
            : base(Strings.Common.FolderMissingError, "FolderMissing")
        { }

        public FolderMissingException(string message)
            : base(message, "FolderMissing")
        { }

        public FolderMissingException(Exception innerException)
            : base(Strings.Common.FolderMissingError, "FolderMissing", innerException)
        { }

        public FolderMissingException(string message, Exception innerException)
            : base(message, "FolderMissing", innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the requested folder is missing
    /// </summary>
    [Serializable]
    public class FileMissingException : UserInformationException
    {
        public FileMissingException()
            : base(LC.L("The requested file does not exist"), "FileMissing")
        { }

        public FileMissingException(string message)
            : base(message, "FileMissing")
        { }

        public FileMissingException(Exception innerException)
            : base(LC.L("The requested file does not exist"), "FileMissing", innerException)
        { }

        public FileMissingException(string message, Exception innerException)
            : base(message, "FileMissing", innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the requested folder already existed
    /// </summary>
    [Serializable]
    public class FolderAreadyExistedException : UserInformationException
    {
        public FolderAreadyExistedException()
            : base(Strings.Common.FolderAlreadyExistsError, "FolderAlreadyExists")
        { }

        public FolderAreadyExistedException(string message)
            : base(message, "FolderAlreadyExists")
        { }

        public FolderAreadyExistedException(Exception innerException)
            : base(Strings.Common.FolderAlreadyExistsError, "FolderAlreadyExists", innerException)
        { }

        public FolderAreadyExistedException(string message, Exception innerException)
            : base(message, "FolderAlreadyExists", innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the user has cancelled the action
    /// </summary>
    [Serializable]
    public class CancelException : UserInformationException
    {
        public CancelException()
            : base(Strings.Common.CancelExceptionError, "Cancelled")
        { }

        public CancelException(string message)
            : base(message, "Cancelled")
        { }

        public CancelException(Exception innerException)
            : base(Strings.Common.CancelExceptionError, "Cancelled", innerException)
        { }

        public CancelException(string message, Exception innerException)
            : base(message, "Cancelled", innerException)
        { }
    }

    /// <summary>
    /// The reason why an operation is aborted
    /// </summary>
    public enum OperationAbortReason
    {
        /// <summary>
        /// The operation is aborted, but this is considered a normal operation
        /// </summary>
        Normal,
        /// <summary>
        /// The operation is aborted and this should give a warning
        /// </summary>
        Warning,
        /// <summary>
        /// The operation is aborted and this is an error
        /// </summary>
        Error
    }

    /// <summary>
    /// A class that signals the operation should be aborted
    /// </summary>
    [Serializable]
    public class OperationAbortException : UserInformationException
    {
        /// <summary>
        /// The reason for the abort operation
        /// </summary>
        public readonly OperationAbortReason AbortReason;

        public OperationAbortException(OperationAbortReason reason, string message)
            : base(message, "OperationAborted")
        {
            AbortReason = reason;
        }

        public OperationAbortException(OperationAbortReason reason, string message, Exception innerException)
            : base(message, "OperationAborted", innerException)
        {
            AbortReason = reason;
        }
    }
}
