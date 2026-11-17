// Copyright (C) 2026, The Duplicati Team
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

namespace Duplicati.Server.Serialization
{
    public enum LiveControlState
    {
        Running,
        Paused
    }


    public enum DuplicatiOperation
    {
        BackupOrSync,
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
        RestoreControlFiles,
        CustomRunner,
        ListFilesets,
        ListFolderContents,
        ListFileVersions,
        SearchEntries,
        DeleteVersions,
        ListBrokenFiles,
        PurgeBrokenFiles,
        PurgeFiles,
        SetVersionLabel,
        RestoreTest,
    }

    public enum SuggestedStatusIcon
    {
        Ready,
        ReadyWarning,
        ReadyError,
        Paused,
        Active,
        ActivePaused,
        Disconnected
    }

    /// <summary>
    /// The outcome a backup run must have before the backups that run after it are started.
    /// </summary>
    public enum RunAfterCondition
    {
        /// <summary>
        /// The run completed with a success or warning result, and was not interrupted (the default).
        /// </summary>
        Success,
        /// <summary>
        /// The run finished in any way, including with errors or by being stopped.
        /// </summary>
        Always
    }

    /// <summary>
    /// What the server does to the computer once the task queue has run empty.
    /// </summary>
    public enum QueueCompletionAction
    {
        /// <summary>
        /// Nothing is done (the default).
        /// </summary>
        None,
        /// <summary>
        /// The computer is shut down.
        /// </summary>
        Shutdown,
        /// <summary>
        /// The computer is restarted.
        /// </summary>
        Reboot,
        /// <summary>
        /// The script from the "post-queue-processing-script" server setting is run.
        /// </summary>
        RunScript
    }

    /// <summary>
    /// The kind of operation a backup configuration performs when it runs.
    /// </summary>
    public enum OperationType
    {
        /// <summary>
        /// Run a regular encrypted/deduplicating backup (the default).
        /// </summary>
        Backup,
        /// <summary>
        /// Run the sync handler, mirroring files to the destination
        /// </summary>
        Sync
    }


    public enum UpdatePollerStates
    {
        Waiting,
        Checking,
        Downloading,
        Verifying,
        LaunchingHelper,
        Installing
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
