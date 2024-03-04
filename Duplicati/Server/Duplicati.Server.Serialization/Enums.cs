// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server.Serialization
{
    public enum RunnerState
    {
        Started,
        Suspended,
        Running,
        Stopped,
    }

    public enum RunnerResult
    {
        OK,
        Partial,
        Warning,
        Error
    }

    public enum CloseReason
    {
        None,
        ApplicationExitCall,
        TaskManagerClosing,
        WindowsShutDown,
        UserClosing
    }

    public enum LiveControlState
    {
        Running,
        Paused
    }


    public enum DuplicatiOperation
    {
        Backup,
        Restore,
        List,
        Remove,
        Repair,
        RepairUpdate,
        Verify,
        Compact,
        CreateReport,
        ListRemote,
        Delete,
        Vacuum,
        CustomRunner,
    }

    public enum SuggestedStatusIcon
    {
        Ready,
        ReadyWarning,
        ReadyError,
        Paused,
        Active,
        ActivePaused
    }


    public enum UpdatePollerStates
    {
        Waiting,
        Checking,
        Downloading
    }

    public enum NotificationType
    {
        Information,
        Warning,
        Error
    }

    public static class EnumConverter
    {
        public static T Convert<T>(Enum o)
        {
            return (T)Enum.Parse(typeof(T), o.ToString(), false);
        }
    }
}
