//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;

namespace Duplicati.Service
{
    public class Runner : IDisposable
    {
        private System.Threading.Thread m_thread;
        private volatile bool m_terminate = false;
        private volatile bool m_softstop = false;
        private System.Diagnostics.Process m_process;
        private Action m_onStartedAction;
        private Action m_onStoppedAction;
        private Action<string, bool> m_reportMessage;

        private readonly object m_writelock = new object();
        private readonly string[] m_cmdargs;


        private readonly int WAIT_POLL_TIME = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;

        public Runner(string[] cmdargs, Action onStartedAction = null, Action onStoppedAction = null, Action<string, bool> logMessage = null)
        {
            m_onStartedAction = onStartedAction;
            m_onStoppedAction = onStoppedAction;
            m_reportMessage = logMessage;
            if (m_reportMessage == null)
                m_reportMessage = (x,y) => Console.WriteLine(x);

            m_cmdargs = cmdargs;
            m_thread = new System.Threading.Thread(Run);
            m_thread.IsBackground = true;
            m_thread.Name = "Server Runner";
            m_thread.Start();
        }

        private void Run()
        {
            var self_exec = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var exec = System.IO.Path.Combine(path, "Duplicati.Server.exe");
            var cmdargs = "--ping-pong-keepalive=true";
            if (m_cmdargs != null && m_cmdargs.Length > 0)
                cmdargs = Duplicati.Library.Utility.Utility.WrapAsCommandLine(new string[] { cmdargs }.Concat(m_cmdargs));

            var firstRun = true;
            var startAttempts = 0;

            try
            {
                while (!m_terminate && !m_softstop)
                {
                    if (!System.IO.File.Exists(exec))
                    {
                        m_reportMessage(string.Format("File not found {0}", exec), true);
                        return;
                    }

                    try
                    {
                        if (!firstRun)
                            m_reportMessage(string.Format("Attempting to restart server process: {0}", exec), true);

                        m_reportMessage(string.Format("Starting process {0} with cmd args {1}", exec, cmdargs), false);

                        var pr = new System.Diagnostics.ProcessStartInfo(exec, cmdargs);
                        pr.UseShellExecute = false;
                        pr.RedirectStandardInput = true;
                        pr.RedirectStandardOutput = true;
                        pr.WorkingDirectory = path;

                        if (!m_terminate)
                            m_process = System.Diagnostics.Process.Start(pr);

                        if (firstRun && m_onStartedAction != null)
                        {
                            PingProcess();
                            m_onStartedAction();
                        }
                        firstRun = false;

                        while (!m_process.HasExited)
                        {
                            m_process.WaitForExit(WAIT_POLL_TIME);
                            if (!m_process.HasExited)
                            {
                                if (m_terminate)
                                    m_process.Kill();
                                else
                                    PingProcess();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_reportMessage(string.Format("Process has failed with error message: {0}", ex), true);

                        if (firstRun)
                        {
                            startAttempts++;
                            if (startAttempts > 5)
                            {
                                m_reportMessage("Too many startup attempts, giving up", true);
                                m_terminate = true;
                            }
                        }

                        // Throttle restarts
                        if (!m_terminate)
                            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                    }
                }
            }
            finally
            {
                if (m_onStoppedAction != null)
                    m_onStoppedAction();
            }
        }

        private void PingProcess()
        {
            for(var n = 0; n < 5; n++)
            {
                lock(m_writelock)
                {
                    m_process.StandardInput.WriteLine("ping");
                    m_process.StandardInput.Flush();
                }

                using (var t = m_process.StandardOutput.ReadLineAsync())
                {
                    t.Wait(TimeSpan.FromMinutes(1));

                    if (t.IsCompleted && !t.IsFaulted && !t.IsCanceled)
                        return;
                }
            }

            // Not responding, stop it
            m_process.Kill();
            throw new Exception("Process timed out!");
        }

        public void Wait()
        {
            m_thread.Join();
        }

        public void Stop(bool force = true)
        {
            if (force)
            {
                m_terminate = true;
                var p = m_process;
                if (p != null)
                    p.Kill();
            }
            else
            {
                m_softstop = true;
                lock (m_writelock)
                {
                    if (m_process != null)
                    {
                        m_process.StandardInput.WriteLine("shutdown");
                        m_process.StandardInput.Flush();
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

