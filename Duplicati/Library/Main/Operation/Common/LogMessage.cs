//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;

namespace Duplicati.Library.Main.Operation.Common
{
    public struct LogMessage
    {
        public Logging.LogMessageType Level;
        public string Message;
        public Exception Exception;
        public bool IsVerbose;
        public bool IsDryRun;

        public static LogMessage Warning(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Warning,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Error(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Error,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Profiling(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Profiling,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Information(string message, Exception ex = null)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Verbose(string message, params object[] args)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsVerbose = true
            };
        }

        public static LogMessage DryRun(string message, params object[] args)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsDryRun = true
            };
        }

    }
}

