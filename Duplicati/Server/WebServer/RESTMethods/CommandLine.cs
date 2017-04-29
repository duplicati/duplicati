//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class CommandLine : IRESTMethodGET, IRESTMethodPOST
    {
        private class LogWriter : System.IO.TextWriter
        {
            private readonly ActiveRun m_target;
            private readonly StringBuilder m_sb = new StringBuilder();
            private int m_newlinechars = 0;

            public LogWriter(ActiveRun target)
            {
                m_target = target;
            }

            public override Encoding Encoding { get { return System.Text.Encoding.UTF8; } }

            public override void Write(char value)
            {
                lock(m_target.Lock)
                {
                    m_sb.Append(value);
                    if (NewLine[m_newlinechars] == value)
                    {
                        m_newlinechars++;
                        if (m_newlinechars == NewLine.Length)
                            WriteLine(string.Empty);
                    }
                    else
                        m_newlinechars = 0;
                }
            }

            public override void WriteLine(string value)
            {
                value = value ?? string.Empty;
                lock(m_target.Lock)
                {
                    m_target.LastAccess = DateTime.Now;

                    if (m_sb.Length != 0)
                    {
                        m_target.Log.Add(m_sb.ToString() + value);
                        m_sb.Length = 0;
                        m_newlinechars = 0;
                    }
                    else
                    {
                        m_target.Log.Add(value);
                    }
                }
            }
        }

        private class ActiveRun
        {
            public readonly string ID = Guid.NewGuid().ToString();
            public DateTime LastAccess = DateTime.Now;
            public Library.Utility.FileBackedStringList Log = new Library.Utility.FileBackedStringList();
            public Runner.IRunnerData Task;
            public LogWriter Writer;
            public readonly object Lock = new object();
            public bool Finished = false;
            public bool Started = false;
            public System.Threading.Thread Thread;
        }

        private readonly Dictionary<string, ActiveRun> m_activeItems = new Dictionary<string, ActiveRun>();
        private System.Threading.Tasks.Task m_cleanupTask;

        public void POST(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                string[] args;
                using (var sr = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8, true))
                    args = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(sr.ReadToEnd());

                var k = new ActiveRun();
                k.Writer = new LogWriter(k);

                m_activeItems[k.ID] = k;
                StartCleanupTask();

                k.Task = Runner.CreateCustomTask((sink) =>
                {
                    try
                    {
                        k.Thread = System.Threading.Thread.CurrentThread;
                        k.Started = true;

                        var code = Duplicati.CommandLine.Program.RunCommandLine(k.Writer, k.Writer, c => { 
                            k.Task.SetController(c);
                            c.AppendSink(sink);
                        }, args);
                        k.Writer.WriteLine(string.Format("Return code: {0}", code));
                    }
                    catch (Exception ex)
                    {
                        var rx = ex;
                        if (rx is System.Reflection.TargetInvocationException)
                            rx = rx.InnerException;

                        if (rx is Library.Interface.UserInformationException)
                            k.Log.Add(rx.Message);
                        else
                            k.Log.Add(rx.ToString());

                        throw rx;
                    }
                    finally
                    {
                        k.Finished = true;
                        k.Thread = null;
                    }
                });

                Program.WorkThread.AddTask(k.Task);

                info.OutputOK(new
                {
                    ID = k.ID
                });
            }
            else
            {
                if (!key.EndsWith("/abort", StringComparison.InvariantCultureIgnoreCase))
                {
                    info.ReportClientError("Only abort commands are allowed");
                    return;
                }

                key = key.Substring(0, key.Length - "/abort".Length);
                if (string.IsNullOrWhiteSpace(key))
                {
                    info.ReportClientError("No task key found");
                    return;
                }

                ActiveRun t;
                if (!m_activeItems.TryGetValue(key, out t))
                {
                    info.OutputError(code: System.Net.HttpStatusCode.NotFound);
                    return;
                }

                var tt = t.Task;
                if (tt != null)
                    tt.Abort();

                var tr = t.Thread;
                if (tr != null)
                    tr.Abort();

                info.OutputOK();
            }
        }

        private void StartCleanupTask()
        {
            if (m_cleanupTask == null || m_cleanupTask.IsCompleted || m_cleanupTask.IsFaulted || m_cleanupTask.IsCanceled)
                m_cleanupTask = RunCleanupAsync();
        }

        private async System.Threading.Tasks.Task RunCleanupAsync()
        {
            while (m_activeItems.Count > 0)
            {
                var oldest = m_activeItems.Values.OrderBy(x => x.LastAccess).FirstOrDefault();
                if (oldest != null && DateTime.Now - oldest.LastAccess > TimeSpan.FromMinutes(5))
                {
                    m_activeItems.Remove(oldest.ID);
                    oldest.Log.Dispose();

                    // Fix all expired, or stop running
                    continue;
                }

                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(60));
            }
        }

        public void GET(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.OutputOK(
                    Duplicati.CommandLine.Program.SupportedCommands
                );
            }
            else
            {
                ActiveRun t;
                if (!m_activeItems.TryGetValue(key, out t))
                {
                    info.OutputError(code: System.Net.HttpStatusCode.NotFound);
                    return;
                }

                int pagesize;
                int offset;

                int.TryParse(info.Request.QueryString["pagesize"].Value, out pagesize);
                int.TryParse(info.Request.QueryString["offset"].Value, out offset);
                pagesize = Math.Max(10, Math.Min(500, pagesize));
                offset = Math.Max(0, offset);
                var items = new List<string>();
                long count;
                bool started;
                bool finished;

                lock(t.Lock)
                {
                    t.LastAccess = DateTime.Now;
                    count = t.Log.Count;
                    offset = Math.Min((int)count, offset);
                    items.AddRange(t.Log.Skip(offset).Take(pagesize));
                    finished = t.Finished;
                    started = t.Started;
                }

                info.OutputOK(new
                {
                    Pagesize = pagesize,
                    Offset = offset,
                    Count = count,
                    Items = items,
                    Finished = finished,
                    Started = started
                });
            }                
        }
    }
}
