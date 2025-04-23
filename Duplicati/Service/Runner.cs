// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Linq;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.Service
{
    public class Runner : IDisposable
    {
        private readonly System.Threading.Thread m_thread;
        private volatile bool m_terminate = false;
        private volatile bool m_softstop = false;
        private System.Diagnostics.Process m_process;
        private readonly Action m_onStartedAction;
        private readonly Action m_onStoppedAction;
        private readonly Action<string, bool> m_reportMessage;

        private readonly object m_writelock = new object();
        private readonly string[] m_cmdargs;
        private readonly PackageHelper.NamedExecutable m_executable;


        private readonly int WAIT_POLL_TIME = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;

        public Runner(PackageHelper.NamedExecutable executable, string[] cmdargs, Action onStartedAction = null, Action onStoppedAction = null, Action<string, bool> logMessage = null)
        {
            m_onStartedAction = onStartedAction;
            m_onStoppedAction = onStoppedAction;
            m_reportMessage = logMessage;
            m_executable = executable;
            if (m_reportMessage == null)
                m_reportMessage = (x, y) => Console.WriteLine(x);

            m_cmdargs = cmdargs;
            m_thread = new System.Threading.Thread(Run);
            m_thread.IsBackground = true;
            m_thread.Name = "Server Runner";
            m_thread.Start();
        }

        private void Run()
        {
            var path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var exec = System.IO.Path.Combine(path, PackageHelper.GetExecutableName(m_executable));
            // Preserve order, but ensure the ping-pong-keepalive is set
            var cmdargs = (m_cmdargs ?? [])
                .Concat(["--ping-pong-keepalive=true"])
                .ToArray();

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

                        m_reportMessage(string.Format("Starting process {0} with cmd args {1}", exec, string.Join(Environment.NewLine, cmdargs)), false);

                        var pr = new System.Diagnostics.ProcessStartInfo(exec, cmdargs)
                        {
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = false,
                            WorkingDirectory = path
                        };

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

                        if (m_process.ExitCode != 0)
                            m_reportMessage(string.Format("Process has exited with code {0}", m_process.ExitCode), true);
                        else if (!m_terminate)
                            m_reportMessage("Process has exited without an error code", true);
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
                    }

                    // Throttle restarts
                    if (!m_terminate)
                        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
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
            for (var n = 0; n < 5; n++)
            {
                lock (m_writelock)
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

