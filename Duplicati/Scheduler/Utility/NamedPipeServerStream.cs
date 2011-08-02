using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Utility
{
    public class NamedPipeServerStream : IDisposable
    {
        public System.IO.Pipes.NamedPipeServerStream Server { get; set; }
        public string Name { get; set; }
        public string UserName { get; set; }
        public System.IO.Pipes.PipeDirection Direction { get; set; }
        public NamedPipeServerStream(string aBasePipeName, string aUserName, System.IO.Pipes.PipeDirection aDir)
        {
            UserName = string.IsNullOrEmpty(aUserName) ? Utility.User.UserName : aUserName;
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
            System.IO.Pipes.PipeSecurity ps = new System.IO.Pipes.PipeSecurity();
            System.IO.Pipes.PipeAccessRule par = new System.IO.Pipes.PipeAccessRule("Everyone",
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
