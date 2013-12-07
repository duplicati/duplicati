using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public class HttpServerConnection : IDisposable
    {
        private const string CONTROL_SCRIPT = "control.cgi";
        private const string STATUS_WINDOW = "index.html";
        private const string EDIT_WINDOW = "edit-window.html";
        
        private Uri m_controlUri;
        private string m_baseUri;
        //private System.Net.NetworkCredential m_credentials;
        private static readonly System.Text.Encoding ENCODING = System.Text.Encoding.GetEncoding("utf-8");
        public delegate void StatusUpdate(IServerStatus status);
        public event StatusUpdate StatusUpdated;

        private volatile IServerStatus m_status;

        private volatile bool m_shutdown = false;
        private volatile System.Threading.Thread m_requestThread;
        private volatile System.Threading.Thread m_pollThread;
        private System.Threading.AutoResetEvent m_waitLock;

        private readonly Dictionary<string, string> m_updateRequest;

        public IServerStatus Status { get { return m_status; } }

        private object m_lock = new object();
        private Queue<Dictionary<string, string>> m_workQueue = new Queue<Dictionary<string,string>>();

        public HttpServerConnection(Uri server, System.Net.NetworkCredential credentials)
        {
            m_baseUri = server.ToString();
            if (!m_baseUri.EndsWith("/"))
                m_baseUri += "/";
            
            m_controlUri = new Uri(m_baseUri + CONTROL_SCRIPT);
            m_updateRequest = new Dictionary<string, string>();
            m_updateRequest["action"] = "get-current-state";
            m_updateRequest["longpoll"] = "false";
            m_updateRequest["lasteventid"] = "0";

            UpdateStatus();

            //We do the first request without long poll,
            // and all the rest with longpoll
            m_updateRequest["longpoll"] = "true";
            m_updateRequest["duration"] = "5m";
            
            m_waitLock = new System.Threading.AutoResetEvent(false);
            m_requestThread = new System.Threading.Thread(ThreadRunner);
            m_pollThread = new System.Threading.Thread(LongPollRunner);

            m_requestThread.Name = "TrayIcon Request Thread";
            m_pollThread.Name = "TrayIcon Longpoll Thread";

            m_requestThread.Start();
            m_pollThread.Start();
        }

        private void UpdateStatus()
        {
            m_status = PerformRequest<IServerStatus>(m_updateRequest);
            m_updateRequest["lasteventid"] = m_status.LastEventID.ToString();

            if (StatusUpdated != null)
                StatusUpdated(m_status);
        }

        private void LongPollRunner()
        {
            while (!m_shutdown)
            {
                try
                {
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Console.WriteLine("Request error: " + ex.Message);
                }
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
                            break;

                        if (req != null)
                        {
                            any = true;
                            PerformRequest<string>(req);
                        }
                    
                    } while (req != null);
                    
                    if (!(any || m_shutdown))
                        m_waitLock.WaitOne(TimeSpan.FromMinutes(1), true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                    Console.WriteLine("Request error: " + ex.Message);
                }
            }
        }

        public void Close()
        {
            m_shutdown = true;
            m_waitLock.Set();
            m_pollThread.Abort();
            m_pollThread.Join(TimeSpan.FromSeconds(10));
            if (!m_requestThread.Join(TimeSpan.FromSeconds(10)))
            {
                m_requestThread.Abort();
                m_requestThread.Join(TimeSpan.FromSeconds(10));
            }
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

            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(m_controlUri);
            req.Method = "POST";
            req.ContentLength = data.Length;
            req.ContentType = "application/x-www-form-urlencoded ; charset=" + ENCODING.BodyName;
            req.Headers.Add("Accept-Charset", ENCODING.BodyName);
            req.UserAgent = "Duplicati TrayIcon Monitor, v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            
            //Wrap it all in async stuff
            Duplicati.Library.Utility.AsyncHttpRequest areq = new Library.Utility.AsyncHttpRequest(req);

            using (System.IO.Stream s = areq.GetRequestStream())
                s.Write(data, 0, data.Length);

            //Assign the timeout, and add a little processing time as well
            if (queryparams["action"] == "get-current-state" && queryparams.ContainsKey("duration"))
                areq.Timeout = (int)(Duplicati.Library.Utility.Timeparser.ParseTimeSpan(queryparams["duration"]) + TimeSpan.FromSeconds(5)).TotalMilliseconds;

            using(System.Net.HttpWebResponse r = (System.Net.HttpWebResponse)areq.GetResponse())
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
                        return Serializer.Deserialize<T>(sr);
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
  
        public void ClearWarning()
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "clear-warning");
            ExecuteAndNotify(req);
        }

        public void ClearError()
        {
            Dictionary<string, string> req = new Dictionary<string, string>();
            req.Add("action", "send-command");
            req.Add("command", "clear-error");
            ExecuteAndNotify(req);
        }

        public void Dispose()
        {
            Close();
        }
        
        public string StatusWindowURL
        {
            get { return m_baseUri + STATUS_WINDOW; }
        }

        public string EditWindowURL
        {
            get { return m_baseUri + EDIT_WINDOW; }
        }

    }
}
