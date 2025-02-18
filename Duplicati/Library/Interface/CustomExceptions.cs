// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Net.Http.Headers;
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

    /// <summary>
    /// An exception indicating that verification of uploaded volumes has failed
    /// due to extra, missing, or duplicate files.
    /// </summary>
    [Serializable]
    public class RemoteListVerificationException : UserInformationException
    {
        public RemoteListVerificationException(string message, string helpId)
            : base(message, helpId)
        { }

        public RemoteListVerificationException(string message, string helpId, Exception innerException)
            : base(message, helpId, innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the current encryption key does not match the key
    /// used to encrypt the settings.
    /// </summary>
    [Serializable]
    public class SettingsEncryptionKeyMismatchException : UserInformationException
    {
        public SettingsEncryptionKeyMismatchException()
            : base(Strings.Common.SettingsKeyMismatchExceptionError, "SettingsKeyMismatch")
        { }
    }

    /// <summary>
    /// An exception indicating that the current encryption key does not match the key
    /// used to encrypt the settings.
    /// </summary>
    [Serializable]
    public class SettingsEncryptionKeyMissingException : UserInformationException
    {
        public SettingsEncryptionKeyMissingException()
            : base(Strings.Common.SettingsKeyMissingExceptionError, "SettingsKeyMissing")
        { }
    }
    
    /// <summary>
    /// An exception for carrying the Retry-After header value on 429 Exceptions
    /// </summary>
    [Serializable]
    public class TooManyRequestException(RetryConditionHeaderValue retryAfter) : Exception
    {
        public readonly RetryConditionHeaderValue RetryAfter = retryAfter;
    }
}
