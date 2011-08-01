using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Named pipe server
    /// </summary>
    public static class Pipe
    {
        /// <summary>
        /// What to call with a new message [NOT an event]
        /// </summary>
        /// <param name="aNewOne">The message</param>
        public delegate void NewMessageDelegate(string aNewOne);
        /// <summary>
        /// Named pipe server
        /// </summary>
        /// <param name="aNewMessage"></param>
        public static void Server(NewMessageDelegate aNewMessage)
        {
            // Just keep trying to connect (in the background).
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate()
            { Thread(aNewMessage); })) { Name = Program.PipeServerThreadName, IsBackground = true }
                .Start();
        }
        /// <summary>
        /// The server thread
        /// </summary>
        /// <param name="aNewMessage"></param>
        private static void Thread(NewMessageDelegate aNewMessage)
        {
            while (true)
            {
                using (Utility.NamedPipeServerStream PipeStream = new Utility.NamedPipeServerStream(
                    Program.PipeBaseName, Utility.User.UserName, System.IO.Pipes.PipeDirection.In))
                {
                    // Try to connect
                    Debug.WriteLine("Advertising with in-pipe:" + PipeStream.Name);
                    try { PipeStream.Server.WaitForConnection(); }
                    catch (Exception Ex)
                    {
                        Debug.WriteLine(Ex);        // Do something??
                    }
                    Debug.WriteLine("Connected = " + PipeStream.Server.IsConnected.ToString());
                    // Just keep on reading
                    using (System.IO.StreamReader Reader = new System.IO.StreamReader(PipeStream.Server))
                        while (PipeStream.Server.IsConnected)
                        {
                            while (PipeStream.Server.IsConnected)
                            {
                                try
                                {
                                    string In = Reader.ReadLine();
                                    if (In != null && aNewMessage != null) aNewMessage(In);
                                    Debug.WriteIf(In == null, '+');
                                }
                                catch (Exception Ex)
                                {
                                    Debug.WriteLine(Ex);
                                }
                            }
                        }
                    PipeStream.Server.Close();
                    // Close up and wait for next one
                }
            }
        }
    }
}
