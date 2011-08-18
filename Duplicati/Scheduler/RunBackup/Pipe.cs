#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Duplicati.Scheduler.RunBackup
{
    public static class Pipe
    {
        // Use ETX as a seperator in the pipe, makes for easy parsing
        public const char Separator = (char)3;
        // This lock is probably not necessary...  Locks the pipe
        private static object Locker = new object();
        // The Pipe
        private static System.IO.Pipes.NamedPipeClientStream itsPipe = null;
        // The Pipe's stream
        private static System.IO.StreamWriter itsPipeWriter = null;
        // Just pack up a PROGRESS command all ready for the service and send it out stdout.
        // Try to connect to the named pipe server, if not, don't try again for 5 seconds.
        public static void OperationProgress(Duplicati.Library.Main.Interface aCaller, 
            Duplicati.Library.Main.DuplicatiOperation aOperation, 
            Duplicati.Library.Main.DuplicatiOperationMode aSpecificOperation, 
            int aProgress, int aSubprogress, string aMessage, string aSubmessage)
        {
            // Progress has come, make sure the pipe is ready
            if (itsPipe != null && itsPipeWriter != null && itsPipe.IsConnected)
            {
                try
                {
                    lock (Locker)
                    {
                        // Package the event up into a null-terminated list for easy disassembly later
                        itsPipeWriter.WriteLine(string.Join(Separator.ToString(), new string[] 
                        { 
                            "PROGRESS", Program.Job, aOperation.ToString(), aSpecificOperation.ToString(), 
                            aProgress.ToString(), aMessage, aSubprogress.ToString(), aSubmessage 
                        }));
                    }
                }
                catch (Exception Ex)
                {
                    Debug.WriteLine(Ex);
                }
            }
        }
        // A handy parser class
        public class ProgressArguments
        {
            public bool OK = false;
            public string Job { get; set; }
            public string Operation { get; set; }
            public string SpecificOperation { get; set; }
            public string Caller { get; set; }
            public int Progress { get; set; }
            public int SubProgress { get; set; }
            public string Message { get; set; }
            public string SubMessage { get; set; }
            public ProgressArguments(string aCommand)
            {
                string[] Parts = aCommand.Split(Separator);
                if (Parts.Length != 8 && Parts[0] != "PROGRESS") return;
                Job = Parts[1];
                Operation = Parts[2];
                SpecificOperation = Parts[3];
                Progress = int.Parse(Parts[4]);
                Message = Parts[5];
                SubProgress = int.Parse(Parts[6]);
                SubMessage = Parts[7];
                OK = true;
            }
        }
        // Just keep trying to connect (in the background).
        public static void Connecter()
        {
            // I guess that makes this a "pipe thread".
            new System.Threading.Thread(new System.Threading.ThreadStart(Thread)) { Name = Program.ClientThreadName, IsBackground = true }
                .Start();
        }
        private static void Thread()
        {
            string PipeName = Duplicati.Scheduler.Utility.NamedPipeServerStream.MakePipeName(Program.PipeBaseName, Duplicati.Scheduler.Utility.User.UserName, System.IO.Pipes.PipeDirection.In);
            while (true)
            {
                lock (Locker) itsPipe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out);
                Debug.WriteLine("Looking for out pipe: " + PipeName);
                using (itsPipe)
                {
                    try { itsPipe.Connect(5000); }
                    catch (Exception Ex)
                    {
                        Debug.WriteLine(Ex);
                    }
                    Debug.WriteLine("Connected = " + itsPipe.IsConnected.ToString());
                    if (itsPipe.IsConnected)
                    {
                        using (itsPipeWriter = new System.IO.StreamWriter(itsPipe) { AutoFlush = true })
                        {
                            try
                            {
                                while (itsPipe.IsConnected)
                                    System.Threading.Thread.Sleep(1000);
                            }
                            catch { }    // It's a broken pipe...
                        }
                    }
                    // itsPipe.Close();
                }
            }
        }
    }
}
