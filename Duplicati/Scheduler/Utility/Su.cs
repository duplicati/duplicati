// This sample demonstrates the use of the WindowsIdentity class to impersonate a user.
// IMPORTANT NOTES:
// This sample can be run only on Windows XP.  The default Windows 2000 security policy
// prevents this sample from executing properly, and changing the policy to allow
// proper execution presents a security risk.
// This sample requests the user to enter a password on the console screen.
// Because the console window does not support methods allowing the password to be masked,
// it will be visible to anyone viewing the screen.
// The sample is intended to be executed in a .NET Framework 1.1 environment.  To execute
// this code in a 1.0 environment you will need to use a duplicate token in the call to the
// WindowsIdentity constructor. See KB article Q319615 for more information.
// http://msdn.microsoft.com/en-us/library/system.security.principal.windowsidentity.impersonate%28v=vs.71%29.aspx

using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Permissions;
using System.Windows.Forms;
using System.Diagnostics;

[assembly: SecurityPermissionAttribute(SecurityAction.RequestMinimum, UnmanagedCode = true)]
[assembly: PermissionSetAttribute(SecurityAction.RequestMinimum, Name = "FullTrust")]
namespace Duplicati.Scheduler.Utility
{
    public class Su
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(String lpszUsername, String lpszDomain, String lpszPassword,
            int dwLogonType, int dwLogonProvider, ref IntPtr phToken);

        //[DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        //private unsafe static extern int FormatMessage(int dwFlags, ref IntPtr lpSource,
        //    int dwMessageId, int dwLanguageId, ref String lpBuffer, int nSize, IntPtr* Arguments);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public extern static bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static bool DuplicateToken(IntPtr ExistingTokenHandle,
            int SECURITY_IMPERSONATION_LEVEL, ref IntPtr DuplicateTokenHandle);
        private static WindowsImpersonationContext itsImpersonatedUser = null;
        public static Exception Impersonate(string aUserName)
        {
            return Impersonate(aUserName, Duplicati.Scheduler.Utility.User.UserDomain);
        }
        public static Exception Impersonate(string aUserName, string aDomainName, string aPass)
        {
            System.Security.SecureString s = new System.Security.SecureString();
            foreach (char c in aPass) s.AppendChar(c);
            return Impersonate(aUserName, aDomainName, s);
        }
        public static Exception Impersonate(string aUserName, string aDomainName)
        {
            GetCheck gc = new GetCheck();
            gc.Prompt = "Enter password for " + aUserName + ":";
            if (gc.ShowDialog() == DialogResult.Cancel) return new Exception("User canceled");
            return Impersonate(aUserName, aDomainName, gc.Result);
        }
        // If you incorporate this code into a DLL, be sure to demand FullTrust.
        [PermissionSetAttribute(SecurityAction.Demand, Name = "FullTrust")]
        public static Exception Impersonate(string aUserName, string aDomainName, System.Security.SecureString aPass)
        {
            Exception Result = null;
            IntPtr tokenHandle = new IntPtr(0);
            IntPtr dupeTokenHandle = new IntPtr(0);
            try
            {
                string userName = aUserName;
                string domainName = string.IsNullOrEmpty( aDomainName ) ? Duplicati.Scheduler.Utility.User.UserDomain : aDomainName;

                const int LOGON32_PROVIDER_DEFAULT = 0;
                //This parameter causes LogonUser to create a primary token.
                const int LOGON32_LOGON_INTERACTIVE = 2;

                tokenHandle = IntPtr.Zero;
                bool returnValue = false;
                IntPtr StrPtr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aPass);

                // Call LogonUser to obtain a handle to an access token.
                returnValue = LogonUser(userName, domainName, System.Runtime.InteropServices.Marshal.PtrToStringAuto(StrPtr),
                    LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT,
                    ref tokenHandle);
                System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(StrPtr);
                Debug.WriteLine("LogonUser called.");

                if (false == returnValue)
                {
                    int ret = Marshal.GetLastWin32Error();
                    Debug.WriteLine("LogonUser failed with error code : " + ret.ToString());
                    return new System.ComponentModel.Win32Exception(ret);
                }

                Debug.WriteLine("Did LogonUser Succeed? " + (returnValue ? "Yes" : "No"));
                Debug.WriteLine("Value of Windows NT token: " + tokenHandle);

                // Check the identity.
                Debug.WriteLine("Before impersonation: "
                    + WindowsIdentity.GetCurrent().Name);
                // Use the token handle returned by LogonUser.
                WindowsIdentity newId = new WindowsIdentity(tokenHandle);
                itsImpersonatedUser = newId.Impersonate();

                // Check the identity.
                Debug.WriteLine("After impersonation: "
                    + WindowsIdentity.GetCurrent().Name);

                // Free the tokens.
                if (tokenHandle != IntPtr.Zero)
                    CloseHandle(tokenHandle);

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception occurred. " + ex.Message);
                Result = ex;
            }
            return Result;
        }
        [PermissionSetAttribute(SecurityAction.Demand, Name = "FullTrust")]
        public static void UnImpersonate()
        {
            if (itsImpersonatedUser != null)
            {
                // Stop impersonating the user.
                itsImpersonatedUser.Undo();
                itsImpersonatedUser = null;
                // Check the identity.
                Debug.WriteLine("After Undo: " + WindowsIdentity.GetCurrent().Name);
            }
        }
    }


}