using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Duplicati.Scheduler.Utility
{
    public static class Tools
    {
        /// <summary>
        /// The last encountered security exception
        /// </summary>
        public static Exception SecurityException { get; set; }
        // A little entropy - this makes it hard for anything outside this program to decode protected strings
        private static byte[] USID = System.Text.ASCIIEncoding.ASCII.GetBytes(
            " 064527 062156 073557 020163 050130 050040 067562 062546  071563 067551 060556 020154 071120 066145 060557 020144");
        /// <summary>
        /// Protect some data
        /// </summary>
        /// <param name="data">Data to protect</param>
        /// <returns>Protected string</returns>
        public static byte[] Protect(byte[] data)
        {
            SecurityException = null;
            try
            {
                // Encrypt the data using DataProtectionScope.CurrentUser. The result can be decrypted
                //  only by the same current user.
                return System.Security.Cryptography.ProtectedData.Protect(data, USID, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException e)
            {
                Debug.WriteLine("Data was not encrypted. An error occurred.");
                Debug.WriteLine(e);
                SecurityException = e;
                return new byte[0];
            }
        }
        /// <summary>
        /// Unprotect some data
        /// </summary>
        /// <param name="data">Data to decrypt</param>
        /// <returns>decrypted data</returns>
        public static byte[] Unprotect(byte[] data)
        {
            if (data == null || data.Length == 0) return new byte[0];
            SecurityException = null;
            try
            {
                //Decrypt the data using DataProtectionScope.CurrentUser.
                return ProtectedData.Unprotect(data, USID, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException e)
            {
                Debug.WriteLine("Data was not decrypted. An error occurred.");
                Debug.WriteLine(e);
                SecurityException = e;
                return new byte[0];
            }
        }
        /// <summary>
        /// Unprotects data into a SecureString
        /// </summary>
        /// <param name="aData">Data to decrypt</param>
        /// <returns>SecureString</returns>
        public static System.Security.SecureString SecureUnprotect(byte[] aData)
        {
            if (aData == null || aData.Length == 0) return new System.Security.SecureString();
            System.Security.SecureString Result = new System.Security.SecureString();
            foreach (byte b in Unprotect(aData))
                Result.AppendChar((char)b);
            return Result;
        }
        /// <summary>
        /// Protects data in a SecureString
        /// </summary>
        /// <param name="aValue">SecureString</param>
        /// <returns>Protected data</returns>
        public static byte[] SecureProtect(System.Security.SecureString aValue)
        {
            if (aValue == null || aValue.Length == 0) return new byte[0];
            return Duplicati.Scheduler.Utility.Tools.Protect(System.Text.ASCIIEncoding.ASCII.GetBytes(
                System.Runtime.InteropServices.Marshal.PtrToStringAuto(
                System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aValue))));
        }
        /// <summary>
        /// Compares 2 secure strings and returns result
        /// </summary>
        /// <param name="aS1">One secure string</param>
        /// <param name="aS2">Another secure string</param>
        /// <returns>True if same</returns>
        public static bool GotIt(System.Security.SecureString aS1, System.Security.SecureString aS2)
        {
            return
                System.Runtime.InteropServices.Marshal.PtrToStringAuto(
                System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aS1)).Equals(
                System.Runtime.InteropServices.Marshal.PtrToStringAuto(
                System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aS2)));
        }
        /// <summary>
        /// A Combine that accepts params
        /// </summary>
        /// <param name="aParts">Stuff to combine</param>
        /// <returns>Combined path</returns>
        public static string Combine(params string[] aParts)
        {
            if (aParts.Length == 0) return string.Empty;
            string Double = new string(new char[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar });
            string Separator = System.IO.Path.DirectorySeparatorChar.ToString();
            string Result = aParts[0];
            for (int i = 1; i < aParts.Length; i++)
            {
                string Part = aParts[i];
                while (Part.Contains(Double)) Part = Part.Replace(Double, Separator);
                if (!Result.EndsWith(Separator) && !Part.StartsWith(Separator)) Result += Separator;
                Result += Part;
            }
            return Result;
        }
        ////////////////////////// Tools for the lazy
        /// <summary>
        /// Run something and return true if no exceptions
        /// </summary>
        /// <param name="aAction">What to run</param>
        /// <returns>True if no exceptions</returns>
        public static bool NoException(Action aAction)
        {
            return TryCatch(aAction) == null;
        }
        /// <summary>
        /// Run something and return true if it had an exception
        /// </summary>
        /// <param name="aAction">What to run</param>
        /// <returns>True if it had an exception</returns>
        public static bool HadException(Action aAction)
        {
            return TryCatch(aAction) != null;
        }
        /// <summary>
        /// Run something and return any exception encountered
        /// </summary>
        /// <param name="aAction">What to run</param>
        /// <returns>Exception or null</returns>
        public static Exception TryCatch(Action aAction)
        {
            Exception Result = null;
            try
            {
                aAction();
            }
            catch (Exception Ex)
            {
#if DEBUG
                Debug.WriteLine("BEGIN TC Exception");
                Debug.Indent(); Debug.WriteLine(Ex); Debug.Unindent();
                Debug.WriteLine("END   TC Exception");
#endif
                Result = Ex;
            }
            return Result;
        }
        /// <summary>
        /// Runs action in a BackgroundWorker, waits until complete and calls DoEvents every 100 milliseconds
        /// </summary>
        /// <remarks>
        /// This is handy for running a long-timed-thing and keep the forms alive while it runs.
        /// Be careful, users can do weird things while you are away.
        /// </remarks>
        /// <param name="aAction">What to run</param>
        public static void Background(Action aAction)
        {
            using (System.ComponentModel.BackgroundWorker bgw = new System.ComponentModel.BackgroundWorker())
            {
                bgw.DoWork += new System.ComponentModel.DoWorkEventHandler(
                    delegate(object sender, System.ComponentModel.DoWorkEventArgs e)
                    {
                        aAction();
                    });
                bgw.RunWorkerAsync();
                while (bgw.IsBusy)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
            }
        }
        /// <summary>
        /// Sets a rule on a file
        /// </summary>
        /// <param name="filePath">File to set</param>
        /// <param name="account">Account to impact</param>
        /// <param name="rights">Rights to set</param>
        /// <param name="controlType">Well, its the control type, isn't it?</param>
        public static void SetRule(string filePath, string account, System.Security.AccessControl.FileSystemRights rights, System.Security.AccessControl.AccessControlType controlType)
        {
            System.Security.AccessControl.FileSecurity fSecurity = System.IO.File.GetAccessControl(filePath);
            fSecurity.ResetAccessRule(new System.Security.AccessControl.FileSystemAccessRule(account, rights, controlType));
            System.IO.File.SetAccessControl(filePath, fSecurity);
        }
        [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int WNetGetConnection([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPTStr)] string localName, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPTStr)] StringBuilder remoteName, ref int length);
        /// <summary>
        /// Translates a Windows drive letter into a UNC
        /// </summary>
        /// <param name="aDrive">Drive Letter</param>
        /// <returns>UNC</returns>
        public static string DriveToUNC(char aDrive)
        {
            int Len = 255;
            StringBuilder Stupid = new StringBuilder(Len);
            WNetGetConnection(aDrive.ToString(), Stupid, ref Len);
            return Stupid.ToString();
        }
        /// <summary>
        /// The path to the log files
        /// </summary>
        /// <param name="aPackage">Package name</param>
        /// <returns>Path to log files</returns>
        public static string LogFileDirectory(string aPackage)
        {
            Duplicati.Scheduler.Utility.User.GetApplicationDirectory(aPackage);
            return Duplicati.Scheduler.Utility.User.GetApplicationDirectory(System.IO.Path.Combine(aPackage, "Logs"));
        }
        /// <summary>
        /// A file filter that may be used to find log files
        /// </summary>
        public const string LogFileFilter = "Log-*.txt";
        /// <summary>
        /// The default log name
        /// </summary>
        /// <param name="aPackage">Package name</param>
        /// <returns>Log file name with path</returns>
        public static string LogFileName(string aPackage)
        {
            return System.IO.Path.Combine(LogFileDirectory(aPackage), "Log-" + DateTime.Now.ToString("yyyyMMddHHmm") + "-" +
                System.Diagnostics.Process.GetCurrentProcess().Id.ToString() + ".txt");
        }
        /// <summary>
        /// Some fancy error processing
        /// </summary>
        /// <param name="aPackage">Package name</param>
        /// <param name="aName">Module name</param>
        /// <param name="aEx">Exception</param>
        public static void ProcessError(string aPackage, string aName, Exception aEx)
        {
            ProcessError(aPackage, aName, aEx, true, string.Empty);
        }
        /// <summary>
        /// Some fancy error processing
        /// </summary>
        /// <param name="aPackage">Package name</param>
        /// <param name="aName">Module name</param>
        /// <param name="aEx">Exception</param>
        /// <param name="aShowDialog">Show the sql messagebox</param>
        /// <param name="aLogFile">Send to log file</param>
        public static void ProcessError(string aPackage, string aName, Exception aEx, bool aShowDialog, string aLogFile)
        {
            if (string.IsNullOrEmpty(aLogFile)) 
                aLogFile = LogFileName(aPackage);
            // Use the power of the Con
            bool OK = Duplicati.Scheduler.Utility.Tools.NoException((Action)delegate()
            {
                Console.SetError(System.IO.File.AppendText(aLogFile));
                Console.Error.WriteLine(aEx);
                Console.Error.WriteLine(DateTime.Now.ToString() + '\n' + AssemblyInfo.Info);
                Console.Error.WriteLine(aEx);
            });
            if (aShowDialog)
                // Cool huh?
                new Microsoft.SqlServer.MessageBox.ExceptionMessageBox(
                    new ApplicationException("Details " + (OK ? "written " : "NOT written ") + "to " + aLogFile, aEx) 
                    { Source = aName }) { Beep = true }.Show(null);
        }
    }
}
