using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler.Utility
{
    public class NamedPipeServerStream : IDisposable
    {
        public System.IO.Pipes.NamedPipeServerStream Server { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public System.IO.Pipes.PipeDirection Direction { get; set; }
        public NamedPipeServerStream(string aBasePipeName, string aUserName, System.IO.Pipes.PipeDirection aDir)
        {
            UserName = string.IsNullOrEmpty(aUserName) ? Duplicati.Scheduler.Utility.User.UserName : aUserName;
            Name = MakePipeName(aBasePipeName, UserName, aDir);
            Direction = aDir;
            Server = GetPipeServer(Name, aDir);
        }
        public static void PrintServers()
        {
            foreach (string S in System.IO.Directory.GetFiles(@"\\.\pipe\"))
                System.Diagnostics.Debug.WriteLine(S);
        }
        public const string PipeDirectory = @"\\.\pipe\";
        public static bool ServerIsUp(string aServerName)
        {
            return System.IO.Directory.GetFiles(PipeDirectory).Contains(PipeDirectory + aServerName);
        }
        public static string MakePipeName(string aBaseName, string aUserName, System.IO.Pipes.PipeDirection aDir)
        {
            return (aBaseName + '.' + aUserName + '.' + aDir.ToString()).Replace('\\', '.');
        }
        private static System.IO.Pipes.NamedPipeServerStream GetPipeServer(string aPipeName, System.IO.Pipes.PipeDirection aDir)
        {
            //Get "Everyone" for localized OS: http://social.msdn.microsoft.com/forums/en-US/netfxbcl/thread/0737f978-a998-453d-9a6a-c348285d7ea3/
            System.Security.Principal.SecurityIdentifier sid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            System.Security.Principal.NTAccount acct = sid.Translate(typeof(System.Security.Principal.NTAccount)) as System.Security.Principal.NTAccount;

            System.IO.Pipes.PipeSecurity ps = new System.IO.Pipes.PipeSecurity();
            System.IO.Pipes.PipeAccessRule par = new System.IO.Pipes.PipeAccessRule(acct,
                System.IO.Pipes.PipeAccessRights.ReadWrite,
                System.Security.AccessControl.AccessControlType.Allow);
            ps.AddAccessRule(par);
            return new System.IO.Pipes.NamedPipeServerStream(aPipeName, aDir, 1,
                System.IO.Pipes.PipeTransmissionMode.Message,
                System.IO.Pipes.PipeOptions.None, 4096, 4096, ps);
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (Server != null) Server.Dispose();
        }

        #endregion
    }
}
