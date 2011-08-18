using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Duplicati.Scheduler.Utility
{
    public class PipeClient
    {
        private class Pipes
        {
            public System.IO.Pipes.NamedPipeClientStream In { get; set; }
            public System.IO.Pipes.NamedPipeClientStream Out { get; set; }
            public bool IsConnected
            {
                get { return In != null && In.IsConnected && Out != null && Out.IsConnected; }
            }
            public Pipes(System.IO.Pipes.NamedPipeClientStream aIn, System.IO.Pipes.NamedPipeClientStream aOut)
            {
                In = aIn;
                Out = aOut;
            }
            public void Close()
            {
                if (In != null) In.Close();
                if (Out != null) Out.Close();
            }
        }
        private Pipes itsPipes;
        private System.IO.StreamWriter itsWriter = null;
        private volatile bool Running;
        public delegate IAsyncResult CommandReceivedDelegate(string aCommand);
        public delegate void ConnectedChangedDelegate(bool aConnected);
        public event ConnectedChangedDelegate ConnectedChanged;
        public static  DateTime LastRun { get; private set; }
        public void Run(object aCommandReceived)
        {
            LastRun = DateTime.Now;
            CommandReceivedDelegate CommandReceived = (CommandReceivedDelegate) aCommandReceived;
            for (Running = true; Running; )
            {
                itsPipes = new Pipes(
                    new System.IO.Pipes.NamedPipeClientStream(".", "Duplicati.Pipe.Out", System.IO.Pipes.PipeDirection.In),
                    new System.IO.Pipes.NamedPipeClientStream(".", "Duplicati.Pipe.In", System.IO.Pipes.PipeDirection.Out));
                while (Running && !itsPipes.Out.IsConnected)
                {
                    try { itsPipes.Out.Connect(10000); } //1000); }
                    catch { }
                }
                itsPipes.In.Connect();
                itsWriter = new System.IO.StreamWriter(itsPipes.Out);
                itsWriter.AutoFlush = true; // !!
                Debug.WriteLine("CONNECTED");
                if (ConnectedChanged != null) ConnectedChanged(IsConnected);
                System.Media.SystemSounds.Beep.Play();
                using (System.Timers.Timer KeepAlive = new System.Timers.Timer(1000))
                using (System.IO.StreamReader Reader = new System.IO.StreamReader(itsPipes.In))
                {
                    KeepAlive.Elapsed += new System.Timers.ElapsedEventHandler(
                        delegate(object sender, System.Timers.ElapsedEventArgs e)
                        {
                            SendCommand(string.Empty);  // Send empty keepalive
                        });
                    while (Running && itsPipes.In.IsConnected)
                    {
                        try
                        {
                            string Line = Reader.ReadLine();
                            if (!string.IsNullOrEmpty(Line)) // Keepalive is empty
                                CommandReceived(Line);
                        }
                        catch (Exception Ex)
                        {
                            Debug.WriteLine(Ex);
                        }
                    }
                    itsPipes.Close();
                }
                Debug.WriteLine("Exiting pipe thread");
                if (ConnectedChanged != null) ConnectedChanged(IsConnected);
            }
        }
        public void Stop()
        {
            Running = false;
        }
        public bool IsConnected { get { return itsPipes.IsConnected; } }
        private static System.Threading.Thread itsThread;
        private static PipeClient itsClient;
        public static PipeClient RunThread(CommandReceivedDelegate aCommandReceived, ConnectedChangedDelegate aConnectionChanged)
        {
            if (LastRun != null && (DateTime.Now - LastRun).TotalSeconds < 10)
                return null;

            if (itsThread != null && itsThread.IsAlive) StopThread();
            itsClient = new PipeClient();
            itsClient.ConnectedChanged += aConnectionChanged;
            itsThread = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(itsClient.Run));
            itsThread.IsBackground = true;  // Die with main thread.
            itsThread.Start(aCommandReceived);
            return itsClient;
        }
        public static void StopThread()
        {
            if (itsThread == null || !itsThread.IsAlive) return;
            itsClient.Stop();
            itsThread.Join(2000);
            if (itsThread.IsAlive) itsThread.Abort();
        }
        public bool SendCommand(string aCommand)
        {
            bool Result = false;
            if (itsWriter != null)
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
