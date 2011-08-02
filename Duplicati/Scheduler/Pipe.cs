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
