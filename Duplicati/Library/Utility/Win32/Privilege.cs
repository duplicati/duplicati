using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using static Duplicati.Library.Utility.Win32.TokenNativeMethods;

namespace Duplicati.Library.Utility.Win32
{
    public static class Privilege
    {
        #region Privilege names

        public const string
            CreateToken = "SeCreateTokenPrivilege",
            AssignPrimaryToken = "SeAssignPrimaryTokenPrivilege",
            LockMemory = "SeLockMemoryPrivilege",
            IncreaseQuota = "SeIncreaseQuotaPrivilege",
            UnsolicitedInput = "SeUnsolicitedInputPrivilege",
            MachineAccount = "SeMachineAccountPrivilege",
            TrustedComputingBase = "SeTcbPrivilege",
            Security = "SeSecurityPrivilege",
            TakeOwnership = "SeTakeOwnershipPrivilege",
            LoadDriver = "SeLoadDriverPrivilege",
            SystemProfile = "SeSystemProfilePrivilege",
            SystemTime = "SeSystemtimePrivilege",
            ProfileSingleProcess = "SeProfileSingleProcessPrivilege",
            IncreaseBasePriority = "SeIncreaseBasePriorityPrivilege",
            CreatePageFile = "SeCreatePagefilePrivilege",
            CreatePermanent = "SeCreatePermanentPrivilege",
            Backup = "SeBackupPrivilege",
            Restore = "SeRestorePrivilege",
            Shutdown = "SeShutdownPrivilege",
            Debug = "SeDebugPrivilege",
            Audit = "SeAuditPrivilege",
            SystemEnvironment = "SeSystemEnvironmentPrivilege",
            ChangeNotify = "SeChangeNotifyPrivilege",
            RemoteShutdown = "SeRemoteShutdownPrivilege",
            Undock = "SeUndockPrivilege",
            SyncAgent = "SeSyncAgentPrivilege",
            EnableDelegation = "SeEnableDelegationPrivilege",
            ManageVolume = "SeManageVolumePrivilege",
            Impersonate = "SeImpersonatePrivilege",
            CreateGlobal = "SeCreateGlobalPrivilege",
            TrustedCredentialManagerAccess = "SeTrustedCredManAccessPrivilege",
            ReserveProcessor = "SeReserveProcessorPrivilege";

        #endregion

        public static void RunWithPrivileges(Action action, params string[] privs)
        {
            if (privs == null || privs.Length == 0)
            {
                throw new ArgumentNullException(nameof(privs));
            }
            var luids = privs
                .Select(e => new LUID_AND_ATTRIBUTES { Luid = GetLuidForName(e), Attributes = SE_PRIVILEGE_ENABLED })
                .ToArray();

            RuntimeHelpers.PrepareConstrainedRegions();
            try { /* CER */ }
            finally
            {
                using (var threadToken = new ThreadTokenScope())
                using (new ThreadPrivilegeScope(threadToken, luids))
                {
                    action();
                }
            }
        }

        private static LUID_AND_ATTRIBUTES[] AdjustTokenPrivileges2(SafeTokenHandle token, LUID_AND_ATTRIBUTES[] attrs)
        {
            var sizeofAttr = Marshal.SizeOf<LUID_AND_ATTRIBUTES>();
            var pDesired = Marshal.AllocHGlobal(4 /* count */ + attrs.Length * sizeofAttr);
            try
            {
                // Fill pStruct
                {
                    Marshal.WriteInt32(pDesired, attrs.Length);
                    var pAttr = pDesired + 4;
                    for (int i = 0; i < attrs.Length; i++)
                    {
                        Marshal.StructureToPtr(attrs[i], pAttr, false);
                        pAttr += sizeofAttr;
                    }
                }

                // Call Adjust
                const int cbPrevious = 16384 /* some arbitrarily high number */;
                var pPrevious = Marshal.AllocHGlobal(cbPrevious);
                try
                {
                    if (!AdjustTokenPrivileges(token, false, pDesired, cbPrevious, pPrevious, out var retLen))
                    {
                        throw new Win32Exception();
                    }

                    // Parse result
                    {
                        var result = new LUID_AND_ATTRIBUTES[Marshal.ReadInt32(pPrevious)];
                        var pAttr = pPrevious + 4;
                        for (int i = 0; i < result.Length; i++)
                        {
                            result[i] = Marshal.PtrToStructure<LUID_AND_ATTRIBUTES>(pAttr);
                        }
                        return result;
                    }
                }
                finally { Marshal.FreeHGlobal(pPrevious); }
            }
            finally { Marshal.FreeHGlobal(pDesired); }
        }

        private static LUID GetLuidForName(string priv)
        {
            if (!LookupPrivilegeValue(null, priv, out var result))
            {
                throw new Win32Exception();
            }

            return result;
        }

        private class ThreadPrivilegeScope : IDisposable
        {
            private LUID_AND_ATTRIBUTES[] RevertTo;
            private Thread OwnerThread;
            private readonly ThreadTokenScope Token;

            public ThreadPrivilegeScope(ThreadTokenScope token, LUID_AND_ATTRIBUTES[] setTo)
            {
                this.OwnerThread = Thread.CurrentThread;
                this.Token = token ?? throw new ArgumentNullException(nameof(token));

                this.RevertTo = AdjustTokenPrivileges2(token.Handle, setTo);
            }

            public void Dispose()
            {
                if (OwnerThread != Thread.CurrentThread)
                {
                    throw new InvalidOperationException("Wrong thread");
                }
                if (RevertTo == null)
                {
                    return;
                }

                AdjustTokenPrivileges2(Token.Handle, RevertTo);
            }
        }

        private class ThreadTokenScope : IDisposable
        {
            private bool IsImpersonating;
            private readonly Thread OwnerThread;
            public readonly SafeTokenHandle Handle;

            [ThreadStatic]
            private static ThreadTokenScope Current;

            public ThreadTokenScope()
            {
                if (Current != null)
                {
                    throw new InvalidOperationException("Reentrance to ThreadTokenScope");
                }

                this.OwnerThread = Thread.CurrentThread;

                if (!OpenThreadToken(GetCurrentThread(), TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges, true, out var token))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ERROR_NO_TOKEN)
                    {
                        throw new Win32Exception(error);
                    }

                    // No token is on the thread, copy from process
                    if (!OpenProcessToken(GetCurrentProcess(), TokenAccessLevels.Duplicate, out var processToken))
                    {
                        throw new Win32Exception();
                    }

                    if (!DuplicateTokenEx(processToken, TokenAccessLevels.Impersonate | TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges,
                        IntPtr.Zero, SecurityImpersonationLevel.Impersonation, TokenType.Impersonation, out token))
                    {
                        throw new Win32Exception();
                    }

                    if (!SetThreadToken(IntPtr.Zero, token))
                    {
                        throw new Win32Exception();
                    }
                    this.IsImpersonating = true;
                }

                this.Handle = token;
                Current = this;
            }

            public void Dispose()
            {
                if (OwnerThread != Thread.CurrentThread)
                {
                    throw new InvalidOperationException("Wrong thread");
                }
                if (Current != this)
                {
                    throw new ObjectDisposedException(nameof(ThreadTokenScope));
                }
                Current = null;

                if (IsImpersonating)
                {
                    RevertToSelf();
                }
                IsImpersonating = false;
            }
        }
    }
}
