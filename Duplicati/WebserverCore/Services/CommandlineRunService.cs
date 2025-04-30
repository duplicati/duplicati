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
using System.Text;
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

public class CommandlineRunService(IWorkerThreadsManager workerThreadsManager) : ICommandlineRunService
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<CommandlineRunService>();

    private class LogWriter : TextWriter
    {
        private readonly ActiveRun m_target;
        private readonly StringBuilder m_sb = new StringBuilder();
        private int m_newlinechars = 0;

        public LogWriter(ActiveRun target)
        {
            m_target = target;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (m_target.Lock)
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

        public override void WriteLine(string? value)
        {
            value ??= string.Empty;
            lock (m_target.Lock)
            {
                m_target.LastAccess = DateTime.Now;

                //Avoid writing the log if it does not exist
                if (m_target.IsLogDisposed)
                {
                    FIXMEGlobal.LogHandler.WriteMessage(new Library.Logging.LogEntry("Attempted to write message after closing: {0}", new object[] { value }, Library.Logging.LogMessageType.Warning, LOGTAG, "CommandLineOutputAfterLogClosed", null));
                    return;
                }

                try
                {
                    if (m_sb.Length != 0)
                    {
                        m_target.Log.Add(m_sb + value);
                        m_sb.Length = 0;
                        m_newlinechars = 0;
                    }
                    else
                    {
                        m_target.Log.Add(value);
                    }
                }
                catch (Exception ex)
                {
                    // This can happen on a very unlucky race where IsLogDisposed is set right after the check
                    FIXMEGlobal.LogHandler.WriteMessage(new Library.Logging.LogEntry("Failed to forward commandline message: {0}", new object[] { value }, Library.Logging.LogMessageType.Warning, LOGTAG, "CommandLineOutputAfterLogClosed", ex));
                }
            }
        }
    }

    private class ActiveRun : ICommandlineRunService.IActiveRun
    {
        public string ID { get; } = Guid.NewGuid().ToString();
        public DateTime LastAccess { get; set; } = DateTime.Now;
        public readonly Library.Utility.FileBackedStringList Log = new Library.Utility.FileBackedStringList();
        public Runner.IRunnerData? Task;
        public LogWriter? Writer;
        public object Lock { get; } = new object();
        public bool Finished { get; set; } = false;
        public bool Started { get; set; } = false;
        public bool IsLogDisposed { get; set; } = false;
        public Thread? Thread;

        public IEnumerable<string> GetLog() => Log;

        public void Abort()
        {
            var tt = this.Task;
            if (tt != null)
                tt.Abort();

            var tr = this.Thread;
            if (tr != null)
                tr.Interrupt();
        }
    }

    private readonly Dictionary<string, ActiveRun> m_activeItems = new Dictionary<string, ActiveRun>();
    private Task? m_cleanupTask;

    public ICommandlineRunService.IActiveRun? GetActiveRun(string id)
    {
        ActiveRun? t;
        lock (m_activeItems)
            m_activeItems.TryGetValue(id, out t);

        if (t != null)
            lock (t.Lock)
                t.LastAccess = DateTime.Now;

        return t;
    }

    public string StartTask(string[] args)
    {
        var k = new ActiveRun();
        k.Writer = new LogWriter(k);

        m_activeItems[k.ID] = k;
        StartCleanupTask();

        k.Task = Runner.CreateCustomTask((sink) =>
        {
            try
            {
                k.Thread = Thread.CurrentThread;
                k.Started = true;

                var code = CommandLine.Program.RunCommandLine(k.Writer, k.Writer, c =>
                {
                    k.Task!.SetController(c);
                    c.AppendSink(sink);
                }, args);
                k.Writer.WriteLine("Return code: {0}", code);
            }
            catch (Exception ex)
            {
                var rx = ex;
                if (rx is System.Reflection.TargetInvocationException)
                    rx = rx.InnerException;

                if (rx is Library.Interface.UserInformationException)
                    k.Log.Add(rx.Message);
                else
                    k.Log.Add(rx?.ToString() ?? "null exception?");

                throw rx ?? new Exception("null exception?");
            }
            finally
            {
                k.Finished = true;
                k.Thread = null;
            }
        });

        workerThreadsManager.AddTask(k.Task);
        return k.ID;
    }

    private void StartCleanupTask()
    {
        if (m_cleanupTask == null || m_cleanupTask.IsCompleted || m_cleanupTask.IsFaulted || m_cleanupTask.IsCanceled)
            m_cleanupTask = RunCleanupAsync();
    }

    private async Task RunCleanupAsync()
    {
        while (m_activeItems.Count > 0)
        {
            var oldest = m_activeItems.Values
                .OrderBy(x => x.LastAccess)
                .FirstOrDefault();

            if (oldest != null)
            {
                // If the task has finished, we just wait a little to allow the UI to pick it up
                var timeout = oldest.Finished ? TimeSpan.FromMinutes(5) : TimeSpan.FromDays(1);
                if (DateTime.Now - oldest.LastAccess > timeout)
                {
                    oldest.IsLogDisposed = true;
                    m_activeItems.Remove(oldest.ID);
                    oldest.Log.Dispose();

                    // Fix all expired, or stop running
                    continue;
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        }
    }
}
