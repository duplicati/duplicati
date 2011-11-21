using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Server.Serialization;

namespace Duplicati.GUI.TrayIcon
{
    public class HttpServerConnection : IDisposable
    {
        private Uri m_uri;
        private System.Net.NetworkCredential m_credentials;
        private static readonly System.Text.Encoding ENCODING = System.Text.Encoding.GetEncoding("utf-8");
        public delegate void StatusUpdate(ISerializableStatus status);
        public event StatusUpdate StatusUpdated;

        private ISerializableStatus m_status;

        private TimeSpan m_updateIntervalIdle = TimeSpan.FromSeconds(15);
        private TimeSpan m_updateIntervalActive = TimeSpan.FromSeconds(5);
        private TimeSpan m_currentInterval;

        private volatile bool m_shutdown = false;
        private volatile System.Threading.Thread m_thread;
        private System.Threading.AutoResetEvent m_waitLock;

        private Serializer m_serializer;
        private readonly Dictionary<string, string> m_updateRequest;

        public ISerializableStatus Status { get { return m_status; } }

        private object m_lock = new object();
        private Queue<Dictionary<string, string>> m_workQueue = new Queue<Dictionary<string,string>>();

        public HttpServerConnection(Uri server, System.Net.NetworkCredential credentials)
        {
            m_uri = server;
            m_serializer = new Serializer();
            m_updateRequest = new Dictionary<string, string>();
            m_updateRequest.Add("action", "get-current-state");

            UpdateStatus();

            m_waitLock = new System.Threading.AutoResetEvent(false);
            m_thread = new System.Threading.Thread(ThreadRunner);
            m_thread.Start();
        }

        private void UpdateStatus()
        {
            ISerializableStatus old_status = m_status;
            m_status = PerformRequest<ISerializableStatus>(m_updateRequest);

            m_currentInterval = m_status.ProgramState == Server.Serialization.LiveControlState.Paused ? m_updateIntervalIdle :
                    m_status.ActiveScheduleId < 0 ? m_updateIntervalIdle : m_updateIntervalActive;

            bool somethingChanged = false;
            if (old_status != null)
            {
                somethingChanged |=
                    old_status.ActiveBackupState != m_status.ActiveBackupState
                    ||
                    old_status.ActiveScheduleId != m_status.ActiveScheduleId
                    ||
                    old_status.ProgramState != m_status.ProgramState
                    ||
                    old_status.SchedulerQueueIds.Count != m_status.SchedulerQueueIds.Count;

                if (!somethingChanged && old_status.RunningBackupStatus != null && m_status.RunningBackupStatus != null && m_status.ActiveScheduleId >= 0)
                {
                    somethingChanged |=
                        old_status.RunningBackupStatus.Message != m_status.RunningBackupStatus.Message
                        ||
                        old_status.RunningBackupStatus.Mode != m_status.RunningBackupStatus.Mode
                        ||
                        old_status.RunningBackupStatus.Operation != m_status.RunningBackupStatus.Operation
                        ||
                        old_status.RunningBackupStatus.Progress != m_status.RunningBackupStatus.Progress
                        ||
                        old_status.RunningBackupStatus.SubMessage != m_status.RunningBackupStatus.SubMessage
                        ||
                        old_status.RunningBackupStatus.SubProgress != m_status.RunningBackupStatus.SubProgress;
                }

                if (!somethingChanged)
                {
                    for (int i = 0; i < m_status.SchedulerQueueIds.Count; i++)
                        somethingChanged |= old_status.SchedulerQueueIds[i] != m_status.SchedulerQueueIds[i];
                }

                if (somethingChanged && StatusUpdated != null)
                    StatusUpdated(m_status);
            }
        }

        private void ThreadRunner()
        {
            while (!m_shutdown)
            {
                try
                {
                    Dictionary<string, string> req;
                    bool any = false;
                    do
                    {
                        req = null;

                        lock (m_lock)
                            if (m_workQueue.Count > 0)
                                req = m_workQueue.Dequeue();

                        if (m_shutdown)
                            return;

                        if (req != null)
                        {
                            any = true;
                            PerformRequest<string>(req);
                        }
                    
                    } while (req != null);
                    
                    if (!(any || m_shutdown))
                        m_waitLock.WaitOne(m_currentInterval, true);
                    
                    if (m_shutdown)
                        return;

                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Request error: " + ex.Message);
                }
            }
        }

        public void Close()
        {
            m_shutdown = true;
            m_waitLock.Set();
            if (!m_thread.Join(TimeSpan.FromSeconds(10)))
                m_thread.Abort();
            m_thread.Join(TimeSpan.FromSeconds(10));
        }

        private static string EncodeQueryString(Dictionary<string, string> dict)
        {
            return string.Join("&", Array.ConvertAll(dict.Keys.ToArray(), key => string.Format("{0}={1}", Uri.EscapeUriString(key), Uri.EscapeUriString(dict[key]))));
        }

        private T PerformRequest<T>(Dictionary<string, string> queryparams)
        {
            queryparams["format"] = "json";

            string query = EncodeQueryString(queryparams);
            byte[] data = ENCODING.GetBytes(query);

            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(m_uri);
            req.Method = "POST";
            req.ContentLength = data.Length;
            req.ContentType = "application/x-www-form-urlencoded ; charset=" + ENCODING.BodyName;
            req.Headers.Add("Accept-Charset", ENCODING.BodyName);
            
            using (System.IO.Stream s = req.GetRequestStream())
                s.Write(data, 0, data.Length);

            using(System.Net.HttpWebResponse r = (System.Net.HttpWebResponse)req.GetResponse())
            using (System.IO.Stream s = r.GetResponseStream())
                if (typeof(T) == typeof(string))
                {
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    {
                        s.CopyTo(ms);
                        return (T)(object)ENCODING.GetString(ms.ToArray());
                    }
                }
                else
                {
                    using (var sr = new System.IO.StreamReader(s, ENCODING, true))
                        return m_serializer.Deserialize<T>(sr);
                }

        }

        private void ExecuteAndNotify(Dictionary<string, string> req)
        {
            lock (m_lock)
            {
                m_workQueue.Enqueue(req);
                m_waitLock.Set();
            }
        }

        public void Pause(string duration = null)
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "pause");
            if (!string.IsNullOrWhiteSpace(duration))
                req.Add("duration", duration);

            ExecuteAndNotify(req);
        }

        public void Resume()
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "resume");
            ExecuteAndNotify(req);
        }

        public void StopBackup()
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "stop");
            ExecuteAndNotify(req);
        }

        public void AbortBackup()
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "abort");
            ExecuteAndNotify(req);
        }

        public void RunBackup(long id, bool forcefull = false)
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "run-backup");
            req.Add("id", id.ToString());
            if (forcefull)
                req.Add("full", "true");
            ExecuteAndNotify(req);
        }


        public void Dispose()
        {
            Close();
        }
    }
}
