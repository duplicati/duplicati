//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Service
{
    public class Runner : IDisposable
    {
        private System.Threading.Thread m_thread;
        private volatile bool m_terminate = false;
        private System.Diagnostics.Process m_process;

        private readonly int WAIT_POLL_TIME = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;

        public Runner()
        {
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
            var cmdargs = Environment.CommandLine + " --ping-pong-keepalive=true";

            if (cmdargs.StartsWith(self_exec))
                cmdargs = cmdargs.Substring(self_exec.Length);


            if (!System.IO.File.Exists(exec))
            {
                Console.WriteLine("File not found {0}", exec);
                return;
            }

            while (!m_terminate)
            {
                try
                {
                    var pr = new System.Diagnostics.ProcessStartInfo(exec, cmdargs);
                    pr.UseShellExecute = false;
                    pr.RedirectStandardInput = true;
                    pr.RedirectStandardOutput = true;

                    if (!m_terminate)
                        m_process = System.Diagnostics.Process.Start(pr);

                    while(!m_process.HasExited)
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
                    Console.WriteLine(ex);

                    // Throttle restarts
                    if (!m_terminate)
                        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                }
            }
        }

        private void PingProcess()
        {
            for(var n = 0; n < 5; n++)
            {
                m_process.StandardInput.WriteLine("ping");
                m_process.StandardInput.Flush();

                string msg = null;
                System.Threading.ThreadPool.QueueUserWorkItem((x) =>
                {
                    Console.WriteLine("Reading...");
                    msg = m_process.StandardOutput.ReadLine();
                    Console.WriteLine("Read: {0}", msg);
                });

                var i = 10;
        
                while (string.IsNullOrWhiteSpace(msg) && i-- > 0)
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(6));

                if (!string.IsNullOrWhiteSpace(msg))
                    return;
            }

            throw new Exception("Process timed out!");
        }

        public void Wait()
        {
            m_thread.Join();
        }

        public void Stop()
        {
            m_terminate = true;
            var p = m_process;
            if (p != null)
                p.Kill();
            Wait();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

