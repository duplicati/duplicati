using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Duplicati.Scheduler.Utility
{
    public class PipeServer
    {
        private class Pipes
        {
            public System.IO.Pipes.NamedPipeServerStream In { get; set; }
            public System.IO.Pipes.NamedPipeServerStream Out { get; set; }
            public bool IsConnected
            {
                get { return In != null && In.IsConnected && Out != null && Out.IsConnected; }
            }
            public Pipes()
            {
                In = GetPipeServer(System.IO.Pipes.PipeDirection.In);
                Out = GetPipeServer(System.IO.Pipes.PipeDirection.Out);
            }
            public void Close()
            {
                if (In != null) In.Close();
                if (Out != null) Out.Close();
            }
            private System.IO.Pipes.NamedPipeServerStream GetPipeServer(System.IO.Pipes.PipeDirection aDir)
            {
                System.IO.Pipes.PipeSecurity ps = new System.IO.Pipes.PipeSecurity();
                System.IO.Pipes.PipeAccessRule par = new System.IO.Pipes.PipeAccessRule("Everyone",
                    System.IO.Pipes.PipeAccessRights.ReadWrite,
                    System.Security.AccessControl.AccessControlType.Allow);
                ps.AddAccessRule(par);

                return new System.IO.Pipes.NamedPipeServerStream("Duplicati.Pipe." + aDir.ToString(),
                    aDir, 1,
                    System.IO.Pipes.PipeTransmissionMode.Message,
                    System.IO.Pipes.PipeOptions.Asynchronous, 4096, 4096, ps);
            }
        }
        private Pipes itsPipes;
        private System.IO.StreamWriter itsWriter = null;
        public delegate void ConnectedDelegate(object sender, EventArgs e);
        public event ConnectedDelegate ConnectedEvent;
        public delegate void NewMessageDelegate(string aMessage);
        public event NewMessageDelegate NewMessage;
        public delegate void LostConnectionDelegate(object sender, Exception e);
        public event LostConnectionDelegate LostConnection;
        public void Run()
        {
            Debug.WriteLine("New Pipeserver");
            BeginConnect();
            System.Media.SystemSounds.Exclamation.Play();
        }
        private System.Threading.Thread itsReadThread;
        private void BeginConnect()
        {
            itsPipes = new Pipes();
            itsPipes.Out.BeginWaitForConnection(new AsyncCallback(Connected), null);
        }
        private void Connected(IAsyncResult aResult)
        {
            try
            {
                itsPipes.In.EndWaitForConnection(aResult);
                itsPipes.Out.WaitForConnection();
                itsWriter = new System.IO.StreamWriter(itsPipes.Out);
                itsWriter.AutoFlush = true;
                if (ConnectedEvent != null) ConnectedEvent(this, new EventArgs());
                if (itsReadThread != null)  // Should never happen
                {
                    itsReadThread.Abort();
                    itsReadThread.Join();
                }
                itsReadThread = new System.Threading.Thread(new System.Threading.ThreadStart(Reader));
                itsReadThread.Start();
            }
            catch (Exception Ex) // ObjectDisposedException
            {
                Debug.WriteLine(Ex);
            }
        }
        private volatile bool RunThread = true;
        private void Reader()
        {
            using (System.Timers.Timer KeepAlive = new System.Timers.Timer(1000))
            using (System.IO.StreamReader sr = new System.IO.StreamReader(itsPipes.In))
            {
                KeepAlive.Elapsed += new System.Timers.ElapsedEventHandler(
                    delegate(object sender, System.Timers.ElapsedEventArgs e)
                    {
                        if (IsConnected) SendCommand(string.Empty);  // Send empty keepalive
                    });
                KeepAlive.Start();
                try
                {
                    for (RunThread = true; IsConnected && RunThread;)
                    {
                        string s = sr.ReadLine();
                        if (!string.IsNullOrEmpty(s) && NewMessage != null) NewMessage(s);
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    // That's OK
                }
                catch (Exception Ex)
                {
                    Debug.WriteLine(Ex);
                }
            }
            Debug.WriteLine("Read thread exiting");
            if (LostConnection != null) LostConnection(this, new Exception("Lost client connection"));
        }
        public bool IsConnected { get { return itsPipes.IsConnected; } }
        public void Close()
        {
            // The close will abort the thread and interrupt the blocked reads
            //
            RunThread = false; // Chances are small this will stop the thread, but the closes will
            if (itsWriter != null) Duplicati.Scheduler.Utility.Tools.TryCatch((Action)delegate() { itsWriter.Close(); });
            if (itsPipes != null) Duplicati.Scheduler.Utility.Tools.TryCatch((Action)delegate() { itsPipes.Close(); }); 
        }
        public bool SendCommand(string aCommand)
        {
            bool Result = false;
            if (IsConnected)
            {
                try
                {
                    itsWriter.WriteLine(aCommand);
                    Result = true;
                }
                catch (Exception Ex)
                {
                    Debug.WriteLine(Ex);
                }
            }
            return Result;
        }
        public bool SendCommand(string aCommand, params string[] aArgs)
        {
            return SendCommand(aCommand + "<=>" + string.Join("<,>", aArgs));
        }
    }
}
