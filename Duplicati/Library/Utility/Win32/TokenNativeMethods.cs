using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Duplicati.Library.Utility.Win32
{
    internal static class TokenNativeMethods
    {
        const string Advapi32 = "advapi32.dll";
        const string Kernel32 = "kernel32.dll";
        const string Wtsapi32 = "wtsapi32.dll";
        const string Userenv = "userenv.dll";

        #region constants

        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const uint SE_PRIVILEGE_DISABLED = 0x00000000;
        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        public const int ERROR_SUCCESS = 0x0;
        public const int ERROR_ACCESS_DENIED = 0x5;
        public const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
        public const int ERROR_NO_TOKEN = 0x3f0;
        public const int ERROR_NOT_ALL_ASSIGNED = 0x514;
        public const int ERROR_NO_SUCH_PRIVILEGE = 0x521;
        public const int ERROR_CANT_OPEN_ANONYMOUS = 0x543;
        public const uint STANDARD_RIGHTS_REQUIRED = 0x000F0000;
        public const uint STANDARD_RIGHTS_READ = 0x00020000;
        public const uint NORMAL_PRIORITY_CLASS = 0x0020;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint MAX_PATH = 260;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const uint INFINITE = 0xFFFFFFFF;

        #endregion

        #region Advapi32

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool AdjustTokenPrivileges(SafeTokenHandle TokenHandle, bool DisableAllPrivileges,
            IntPtr NewState, uint BufferLength, IntPtr PreviousState, out uint ReturnLength);

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool RevertToSelf();

        [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID Luid);

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool OpenProcessToken(IntPtr ProcessToken, TokenAccessLevels DesiredAccess, out SafeTokenHandle TokenHandle);

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool OpenThreadToken(IntPtr ThreadToken, TokenAccessLevels DesiredAccess, bool OpenAsSelf, out SafeTokenHandle TokenHandle);

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool DuplicateTokenEx(SafeTokenHandle ExistingToken, TokenAccessLevels DesiredAccess,
            IntPtr TokenAttributes, SecurityImpersonationLevel ImpersonationLevel, TokenType TokenType, out SafeTokenHandle NewToken);

        [DllImport(Advapi32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool SetThreadToken(IntPtr Thread, SafeTokenHandle Token);

        [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessAsUser(SafeTokenHandle hToken,
            StringBuilder appExeName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint dwCreationFlags,
            EnvironmentBlockSafeHandle environment, string currentDirectory, ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION startupInformation);

        [DllImport(Advapi32, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle,
            TokenInformationClass TokenInformationClass, out int TokenInformation,
            uint TokenInformationLength, out uint ReturnLength);

        #endregion

        #region Kernel32

        [DllImport(Kernel32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport(Kernel32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport(Kernel32, ExactSpelling = true, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern IntPtr GetCurrentThread();

        [DllImport(Kernel32, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern bool SetInformationJobObject(SafeJobHandle hJob, JobObjectInfoType infoType,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength);

        [DllImport(Kernel32, SetLastError = true)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

        #endregion

        #region Wtsapi

        [DllImport(Wtsapi32, ExactSpelling = true, SetLastError = true)]
        public static extern bool WTSQueryUserToken(int sessionid, out SafeTokenHandle handle);

        #endregion

        #region Userenv

        [DllImport(Userenv, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out EnvironmentBlockSafeHandle lpEnvironment, SafeTokenHandle hToken, bool bInherit);

        [DllImport(Userenv, ExactSpelling = true, SetLastError = true)]
        public extern static bool DestroyEnvironmentBlock(IntPtr hEnvironment);

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public uint HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGE
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege;
        }

        #endregion

        #region Enums

        public enum JobObjectInfoType
        {
            AssociateCompletionPortInformation = 7,
            BasicLimitInformation = 2,
            BasicUIRestrictions = 4,
            EndOfJobTimeInformation = 6,
            ExtendedLimitInformation = 9,
            SecurityLimitInformation = 5,
            GroupInformation = 11
        }

        public enum SecurityImpersonationLevel
        {
            Anonymous = 0,
            Identification = 1,
            Impersonation = 2,
            Delegation = 3,
        }

        public enum TokenType
        {
            Primary = 1,
            Impersonation = 2,
        }

        public enum TokenInformationClass
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin,
            TokenElevationType,
            TokenLinkedToken,
            TokenElevation,
            TokenHasRestrictions,
            TokenAccessInformation,
            TokenVirtualizationAllowed,
            TokenVirtualizationEnabled,
            TokenIntegrityLevel,
            TokenUIAccess,
            TokenMandatoryPolicy,
            TokenLogonSid,
            TokenIsAppContainer,
            TokenCapabilities,
            TokenAppContainerSid,
            TokenAppContainerNumber,
            TokenUserClaimAttributes,
            TokenDeviceClaimAttributes,
            TokenRestrictedUserClaimAttributes,
            TokenRestrictedDeviceClaimAttributes,
            TokenDeviceGroups,
            TokenRestrictedDeviceGroups,
            // MaxTokenInfoClass should always be the last enum
            MaxTokenInfoClass
        }

        #endregion

        #region SafeHandles

        public sealed class EnvironmentBlockSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public EnvironmentBlockSafeHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return DestroyEnvironmentBlock(handle);
            }
        }

        public sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeTokenHandle()
                : base(true)
            {
            }

            override protected bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }

        internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeJobHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return CloseHandle(this.handle);
            }
        }

        #endregion
    }
}
