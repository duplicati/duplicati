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
using System.Runtime.InteropServices;

namespace Duplicati.Library.Utility
{
    //The signatures in this file are from http://pinvoke.net

    /// <summary>
    /// Various Windows specific calls 
    /// </summary>
    public static class Win32
    {

        #region Consts
        internal const int SE_PRIVILEGE_DISABLED = 0x00000000;
        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const int TOKEN_QUERY = 0x00000008;
        internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;

        public const int ATTACH_PARENT_PROCESS = -1;

        internal const string SE_BACKUP_NAME = "SeBackupPrivilege";

        #endregion

        #region Enums

        [FlagsAttribute]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }


        [Flags]
        public enum PROCESS_PRIORITY_CLASS : int
        {
            /// <summary>
            /// Process that has priority above <see cref="NORMAL_PRIORITY_CLASS" /> but below <see cref="HIGH_PRIORITY_CLASS" />.
            /// </summary>
            ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000,
            /// <summary>
            /// Process that has priority above <see cref="IDLE_PRIORITY_CLASS" /> but below <see cref="NORMAL_PRIORITY_CLASS" />.
            /// </summary>
            BELOW_NORMAL_PRIORITY_CLASS = 0x00004000,
            /// <summary>
            /// Process that performs time-critical tasks that must be executed immediately. The threads of the process preempt the threads of normal or idle priority class processes. An example is the Task List, which must respond quickly when called by the user, regardless of the load on the operating system. Use extreme care when using the high-priority class, because a high-priority class application can use nearly all available CPU time.
            /// </summary>
            HIGH_PRIORITY_CLASS = 0x00000080,
            /// <summary>
            /// Process whose threads run only when the system is idle. The threads of the process are preempted by the threads of any process running in a higher priority class. An example is a screen saver. The idle-priority class is inherited by child processes.
            /// </summary>
            IDLE_PRIORITY_CLASS = 0x00000040,
            /// <summary>
            /// Process with no special scheduling needs.
            /// </summary>
            NORMAL_PRIORITY_CLASS = 0x00000020,
            /// <summary>
            /// Begin background processing mode. The system lowers the resource scheduling priorities of the process (and its threads) so that it can perform background work without significantly affecting activity in the foreground.
            /// </summary>
            PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000,
            /// <summary>
            /// End background processing mode. The system restores the resource scheduling priorities of the process (and its threads) as they were before the process entered background processing mode.
            /// </summary>
            PROCESS_MODE_BACKGROUND_END = 0x00200000,
            /// <summary>
            /// Process that has the highest possible priority. The threads of the process preempt the threads of all other processes, including operating system processes performing important tasks. For example, a real-time process that executes for more than a very brief interval can cause disk caches not to flush or cause the mouse to be unresponsive.
            /// </summary>
            REALTIME_PRIORITY_CLASS = 0x00000100
        }


        /// <summary>
        /// The IO priority settings
        /// </summary>
        public enum IO_PRIORITY_HINT : int
        {
            /// <summary>
            /// Defragging, content indexing and other background I/Os.
            /// </summary>
            IoPriorityVeryLow = 0,
            /// <summary>
            /// Prefetching for applications.
            /// </summary>
            IoPriorityLow,
            /// <summary>
            /// Normal I/Os.
            /// </summary>
            IoPriorityNormal,
            /// <summary>
            /// Used by filesystems for checkpoint I/O.
            /// </summary>
            IoPriorityHigh,
            /// <summary>
            /// Used by memory manager. Not available for applications.
            /// </summary>
            IoPriorityCritical,
        }


        public enum PROCESS_INFORMATION_CLASS : int
        {
            ProcessBasicInformation = 0,
            ProcessQuotaLimits,
            ProcessIoCounters,
            ProcessVmCounters,
            ProcessTimes,
            ProcessBasePriority,
            ProcessRaisePriority,
            ProcessDebugPort,
            ProcessExceptionPort,
            ProcessAccessToken,
            ProcessLdtInformation,
            ProcessLdtSize,
            ProcessDefaultHardErrorMode,
            ProcessIoPortHandlers,
            ProcessPooledUsageAndLimits,
            ProcessWorkingSetWatch,
            ProcessUserModeIOPL,
            ProcessEnableAlignmentFaultFixup,
            ProcessPriorityClass,
            ProcessWx86Information,
            ProcessHandleCount,
            ProcessAffinityMask,
            ProcessPriorityBoost,
            ProcessDeviceMap,
            ProcessSessionInformation,
            ProcessForegroundInformation,
            ProcessWow64Information,
            ProcessImageFileName,
            ProcessLUIDDeviceMapsEnabled,
            ProcessBreakOnTermination,
            ProcessDebugObjectHandle,
            ProcessDebugFlags,
            ProcessHandleTracing,
            ProcessIoPriority,
            ProcessExecuteFlags,
            ProcessResourceManagement,
            ProcessCookie,
            ProcessImageInformation,
            ProcessCycleTime,
            ProcessPagePriority,
            ProcessInstrumentationCallback,
            ProcessThreadStackAllocation,
            ProcessWorkingSetWatchEx,
            ProcessImageFileNameWin32,
            ProcessImageFileMapping,
            ProcessAffinityUpdateMode,
            ProcessMemoryAllocationMode,
            MaxProcessInfoClass
        }

        /// <summary>
        /// Placeholder Compatibility Mode values
        /// </summary>
        public enum PHCM_VALUES : sbyte
        {
            PHCM_APPLICATION_DEFAULT = 0,
            PHCM_DISGUISE_PLACEHOLDER = 1,
            PHCM_EXPOSE_PLACEHOLDERS = 2,
            PHCM_MAX = 2,
            PHCM_ERROR_INVALID_PARAMETER = -1,
            PHCM_ERROR_NO_TEB = -2,
        }

        #endregion

        #region Structs

        /// <summary>
        /// The TOKEN_PRIVILEGES structure contains information about a set of privileges for an access token.
        /// </summary>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-token_privileges.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TOKEN_PRIVILEGES
        {
            public int Count;
            public long Luid;
            public int Attr;
        }

        #endregion

        #region Function calls

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        /// <summary>
        /// Attaches the calling process to the console of the specified process.
        /// </summary>
        /// <param name="dwProcessId">[in] Identifier of the process, usually will be ATTACH_PARENT_PROCESS</param>
        /// <returns>If the function succeeds, the return value is nonzero.
        /// If the function fails, the return value is zero.
        /// To get extended error information, call Marshal.GetLastWin32Error.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// Sets the process information parameters
        /// </summary>
        /// <returns>The set information process.</returns>
        /// <param name="hProcess">The process to use.</param>
        /// <param name="processInformationClass">The process information class.</param>
        /// <param name="processInformation">The process information.</param>
        /// <param name="processInformationLength">The process information length.</param>
        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtSetInformationProcess(IntPtr hProcess, PROCESS_INFORMATION_CLASS processInformationClass, ref IO_PRIORITY_HINT processInformation, int processInformationLength);


        /// <summary>
        /// Sets the process information parameters
        /// </summary>
        /// <returns>The query information.</returns>
        /// <param name="hProcess">The process to use.</param>
        /// <param name="processInformationClass">The process information class.</param>
        /// <param name="processInformation">The process information.</param>
        /// <param name="processInformationLength">Process information length.</param>
        /// <param name="returnLength">The size of the result.</param>
        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtQueryInformationProcess(IntPtr hProcess, PROCESS_INFORMATION_CLASS processInformationClass, ref IO_PRIORITY_HINT processInformation, int processInformationLength, IntPtr returnLength);

        /// <summary>
        /// Sets the priority class for the specified process. This value together with the priority value of each thread of the process determines each thread's base priority level.
        /// </summary>
        /// <returns><c>true</c>, if priority class was set, <c>false</c> otherwise.</returns>
        /// <param name="handle">A handle to the process. The handle must have the PROCESS_SET_INFORMATION access right.</param>
        /// <param name="priorityClass">The priority class for the process.</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr handle, PROCESS_PRIORITY_CLASS priorityClass);

        /// <summary>
        /// Gets the priority class for the specified process. This value together with the priority value of each thread of the process determines each thread's base priority level.
        /// </summary>
        /// <returns>The <see cref="PROCESS_PRIORITY_CLASS"/> value.</returns>
        /// <param name="handle">A handle to the process. The handle must have the PROCESS_SET_INFORMATION access right.</param>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern PROCESS_PRIORITY_CLASS GetPriorityClass(IntPtr handle);

        /// <summary>
        /// Sets the current placeholder compatibility mode for the process.
        /// </summary>
        /// <returns>The previous <see cref="PHCM_VALUES"/> value.</returns>
        /// <param name="pcm">New value to set.</param>
        [DllImport("ntdll.dll")]
        public static extern PHCM_VALUES RtlSetProcessPlaceholderCompatibilityMode(PHCM_VALUES pcm);



        /// <summary>
        /// The AdjustTokenPrivileges function enables or disables privileges in the specified access token.
        /// Enabling or disabling privileges in an access token requires TOKEN_ADJUST_PRIVILEGES access.
        /// </summary>
        /// <param name="htok"></param>
        /// <param name="disall"></param>
        /// <param name="newst"></param>
        /// <param name="len"></param>
        /// <param name="prev"></param>
        /// <param name="relen"></param>
        /// <returns></returns>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges.
        /// </remarks>
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall,
        ref TOKEN_PRIVILEGES newst, int len, IntPtr prev, IntPtr relen);

        /// <summary>
        /// Retrieves a pseudo handle for the current process.
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocess
        /// </remarks>
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// The OpenProcessToken function opens the access token associated with a process.
        /// </summary>
        /// <param name="h"></param>
        /// <param name="acc"></param>
        /// <param name="phtok"></param>
        /// <returns></returns>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken.
        /// </remarks>
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr
        phtok);

        /// <summary>
        /// The LookupPrivilegeValue function retrieves the locally unique identifier (LUID) used on a specified system to locally represent the specified privilege name.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="name"></param>
        /// <param name="pluid"></param>
        /// <returns></returns>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupprivilegevaluea.
        /// </remarks>
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string host, string name,
        ref long pluid);
        #endregion
    }
}
