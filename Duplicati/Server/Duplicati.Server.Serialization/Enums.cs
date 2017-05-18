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
        CustomRunner
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
