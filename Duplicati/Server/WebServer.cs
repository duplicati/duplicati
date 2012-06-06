using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpServer.MVC;
using HttpServer.HttpModules;
using System.IO;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    public class WebServer
    {
        /// <summary>
        /// Option for changing the webroot folder
        /// </summary>
        private const string OPTION_WEBROOT = "webservice-webroot";
        /// <summary>
        /// Option for changing the webservice listen port
        /// </summary>
        private const string OPTION_PORT = "webservice-port";

        /// <summary>
        /// The single webserver instance
        /// </summary>
        private HttpServer.HttpServer m_server;
             
        public WebServer(IDictionary<string, string> options)
        {
            m_server = new HttpServer.HttpServer();

            m_server.Add(new DynamicHandler());

            string webroot = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#if DEBUG
            //For debug we go "../../../.." to get out of "GUI/Duplicati.GUI.TrayIcon/bin/debug"
            string tmpwebroot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", "..", "..", ".."));
            tmpwebroot = System.IO.Path.Combine(tmpwebroot, "Server");
            if (System.IO.Directory.Exists(System.IO.Path.Combine(tmpwebroot, "webroot")))
                webroot = tmpwebroot;
            else
            {
                //If we are running the server standalone, we only need to exit "bin/Debug"
                tmpwebroot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", ".."));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(tmpwebroot, "webroot")))
                    webroot = tmpwebroot;
            }

#endif
            webroot = System.IO.Path.Combine(webroot, "webroot");

            if (options.ContainsKey(OPTION_WEBROOT))
            {
                string userroot = options[OPTION_WEBROOT];
#if DEBUG
                //In debug mode we do not care where the path points
#else
                //In release mode we check that the usersupplied path is located
                // in the same folders as the running application, to avoid users
                // that inadvertently expose top level folders
                if (!string.IsNullOrWhiteSpace(userroot)
                    &&
                    (
                        userroot.StartsWith(Library.Utility.Utility.AppendDirSeparator(System.Reflection.Assembly.GetExecutingAssembly().Location), Library.Utility.Utility.ClientFilenameStringComparision)
                        ||
                        userroot.StartsWith(Library.Utility.Utility.AppendDirSeparator(Program.StartupPath), Library.Utility.Utility.ClientFilenameStringComparision)
                    )
                )
#endif
                {
                    webroot = userroot;
                }
            }

            FileModule fh = new FileModule("/", webroot);
            fh.AddDefaultMimeTypes();
            fh.MimeTypes.Add("htc", "text/x-component");
            fh.MimeTypes.Add("json", "application/json");
            m_server.Add(fh);
            m_server.Add(new IndexHtmlHandler(System.IO.Path.Combine(webroot, "status-window.html")));
#if DEBUG
            //For debugging, it is nice to know when we get a 404
            m_server.Add(new DebugReportHandler());
#endif

            int port;
            string portstring;
            if (!options.TryGetValue(OPTION_PORT, out portstring) || !int.TryParse(portstring, out port))
                port = 8080;

            m_server.Start(System.Net.IPAddress.Any, port);
        }

        private class BodyWriter : System.IO.StreamWriter, IDisposable
        {
            private HttpServer.IHttpResponse m_resp;

            // We override the format provider so all JSON output uses US format
            public override IFormatProvider FormatProvider
            {
                get { return System.Globalization.CultureInfo.InvariantCulture; }
            }

            public BodyWriter(HttpServer.IHttpResponse resp)
                : base(resp.Body,  resp.Encoding)
            {
                m_resp = resp;
            }

            protected override void Dispose (bool disposing)
            {
                if (!m_resp.HeadersSent)
                {
                    base.Flush();
                    m_resp.ContentLength = base.BaseStream.Length;
                    m_resp.Send();
                }
                base.Dispose(disposing);
            }
        }

        private class IndexHtmlHandler : HttpModule
        {
            private string m_defaultdoc;

            public IndexHtmlHandler(string defaultdoc) { m_defaultdoc = defaultdoc; }

            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                if ((request.Uri.AbsolutePath == "/" || request.Uri.AbsolutePath == "/index.html" || request.Uri.AbsolutePath == "/index.htm") && System.IO.File.Exists(m_defaultdoc))
                {
                    response.Status = System.Net.HttpStatusCode.OK;
                    response.Reason = "OK";
                    response.ContentType = "text/html";

                    using (var fs = System.IO.File.OpenRead(m_defaultdoc))
                    {
                        response.ContentLength = fs.Length;
                        response.Body = fs;
                        response.Send();
                    }

                    return true;
                }

                return false;
            }
        }

        private class DebugReportHandler : HttpModule
        {
            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                System.Diagnostics.Trace.WriteLine(string.Format("Rejecting request for {0}", request.Uri));
                return false;
            }
        }

        private class DynamicHandler : HttpModule
        {
            private delegate void ProcessSub(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter writer);
            private Server.Serialization.Serializer m_serializer;
            private readonly Dictionary<string, ProcessSub> SUPPORTED_METHODS;

            public DynamicHandler()
            {
                m_serializer = new Serializer();
                SUPPORTED_METHODS = new Dictionary<string, ProcessSub>(System.StringComparer.InvariantCultureIgnoreCase);
             
                //Make a list of all supported actions
                SUPPORTED_METHODS.Add("supported-actions", ListSupportedActions);
                SUPPORTED_METHODS.Add("list-schedules", ListSchedules);
                SUPPORTED_METHODS.Add("get-current-state", GetCurrentState);
                SUPPORTED_METHODS.Add("get-progress-state", GetProgressState);
                SUPPORTED_METHODS.Add("list-application-settings", ListApplicationSettings);
                SUPPORTED_METHODS.Add("list-installed-backends", GetInstalledBackends);
                SUPPORTED_METHODS.Add("list-installed-encryption-modules", ListInstalledEncryptionModules);
                SUPPORTED_METHODS.Add("list-installed-compression-modules", ListInstalledCompressionModules);
                SUPPORTED_METHODS.Add("list-installed-generic-modules", ListInstalledGenericModules);
                SUPPORTED_METHODS.Add("list-options", ListCoreOptions);
                SUPPORTED_METHODS.Add("list-recent-completed", ListRecentCompleted);
                SUPPORTED_METHODS.Add("get-recent-log-details", GetLogBlob);
                SUPPORTED_METHODS.Add("send-command", SendCommand);
                SUPPORTED_METHODS.Add("get-backup-defaults", GetBackupDefaults);
            }

            public override bool Process (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                //We use the fake entry point /control.cgi to listen for requests
                //This ensures that the rest of the webserver can just serve plain files
                if (!request.Uri.AbsolutePath.Equals("/control.cgi", StringComparison.InvariantCultureIgnoreCase))
                    return false;

                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                string action = input["action"].Value ?? "";
             
                //Lookup the actual handler method
                ProcessSub method;
                SUPPORTED_METHODS.TryGetValue(action, out method);

                if (method == null) {
                    response.Status = System.Net.HttpStatusCode.NotImplemented;
                    response.Reason = "Unsupported action: " + (action == null ? "<null>" : "");
                    response.Send();
                } else {
                    //Default setup
                    response.Status = System.Net.HttpStatusCode.OK;
                    response.Reason = "OK";
#if DEBUG
                    response.ContentType = "text/plain";
#else
                    response.ContentType = "text/json";
#endif
                    using (BodyWriter bw = new BodyWriter(response))
                    {
                        try
                        {
                            method(request, response, session, bw);
                        }
                        catch (Exception ex)
                        {
                            if (!response.HeadersSent)
                            {
                                response.Status = System.Net.HttpStatusCode.InternalServerError;
                                response.Reason = ex.Message;
                                response.ContentType = "text/plain";
                                bw.WriteLine("Internal error");
#if DEBUG
                                bw.Write("Stacktrace: " + ex.ToString());
#endif
                                bw.Flush();
                            }
                        }
                    }
                }

                return true;
            }

            private void ReportError(HttpServer.IHttpResponse response, BodyWriter bw, string message)
            {
                response.Status = System.Net.HttpStatusCode.InternalServerError;
                response.Reason = message;

                OutputObject(bw, new { Error = message });
            }
            
            private void OutputObject (BodyWriter b, object o)
            {
                m_serializer.SerializeJson(b, o);
            }

            private void ListSupportedActions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new { Version = 1, Methods = SUPPORTED_METHODS.Keys });
            }

            private void ListSchedules (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.GetObjects<Datamodel.Schedule>());
            }

            private bool LongPollCheck(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, BodyWriter bw, EventPollNotify poller, ref long id, out bool isError)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                if (Library.Utility.Utility.ParseBool(input["longpoll"].Value, false))
                {
                    long lastEventId;
                    if (!long.TryParse(input["lasteventid"].Value, out lastEventId))
                    {
                        ReportError(response, bw, "When activating long poll, the request must include the last event id");
                        isError = true;
                        return false;
                    }

                    TimeSpan ts;
                    try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                    catch (Exception ex)
                    {
                        ReportError(response, bw, "Invalid duration: " + ex.Message);
                        isError = true;
                        return false;
                    }

                    if (ts <= TimeSpan.FromSeconds(10) || ts.TotalMilliseconds > int.MaxValue)
                    {
                        ReportError(response, bw, "Invalid duration, must be at least 10 seconds, and less than " + int.MaxValue + " milliseconds");
                        isError = true;
                        return false;
                    }

                    isError = false;
                    id = Program.StatusEventNotifyer.Wait(lastEventId, (int)ts.TotalMilliseconds);
                    return true;
                }

                isError = false;
                return false;
            }

            private void GetProgressState(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                bool isError;
                long id = 0;
                if (LongPollCheck(request, response, bw, Program.ProgressEventNotifyer, ref id, out isError) || !isError)
                {
                    //TODO: Don't block if the backup is completed when entering the wait state
                    OutputObject(bw, Program.Runner.LastEvent);
                }
            }

            private void GetCurrentState (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                bool isError;
                long id = 0;
                if (LongPollCheck(request, response, bw, Program.StatusEventNotifyer, ref id, out isError))
                {
                    //Make sure we do not report a higher number than the eventnotifyer says
                    var st = new Serializable.ServerStatus();
                    st.LastEventID = id;
                    OutputObject(bw, st);
                }
                else if (!isError)
                {
                    OutputObject(bw, new Serializable.ServerStatus());
                }
            }

            private void GetInstalledBackends(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Duplicati.Library.DynamicLoader.BackendLoader.Backends);
            }

            private void ListInstalledEncryptionModules(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Duplicati.Library.DynamicLoader.EncryptionLoader.Modules);
            }

            private void ListInstalledCompressionModules(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Duplicati.Library.DynamicLoader.CompressionLoader.Modules);
            }

            private void ListInstalledGenericModules(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Duplicati.Library.DynamicLoader.GenericLoader.Modules);
            }

            private void ListCoreOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new Duplicati.Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);
            }

            private void ListRecentCompleted(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.GetObjects<Datamodel.Log>("EndTime > ? AND SubAction LIKE ? ORDER BY EndTime DESC", Library.Utility.Timeparser.ParseTimeInterval(new Datamodel.ApplicationSettings(Program.DataConnection).RecentBackupDuration, DateTime.Now, true), "Primary"));
            }

            private void GetLogBlob(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                long id;
                if (!long.TryParse(input["id"].Value, out id))
                    ReportError(response, bw, "Invalid or missing log id");
                else
                {
                    Datamodel.LogBlob lb = Program.DataConnection.GetObject<Datamodel.LogBlob>("LogId = ?", id);
                    OutputObject(bw, lb);
                }
            }

            private void ListApplicationSettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new Datamodel.ApplicationSettings(Program.DataConnection));
            }

            private void GetBackupDefaults(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new Serializable.JobSettings());
            }


            private void SendCommand(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                string command = input["command"].Value ?? "";
                long id;

                switch (command.ToLowerInvariant())
                {
                    case "pause":
                        if (input.Contains("duration") && !string.IsNullOrWhiteSpace(input["duration"].Value))
                        {
                            TimeSpan ts;
                            try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                            catch (Exception ex)
                            {
                                ReportError(response, bw, ex.Message);
                                return;
                            }
                            if (ts.TotalMilliseconds > 0)
                                Program.LiveControl.Pause(ts);
                            else
                                Program.LiveControl.Pause();
                        }
                        else
                        {
                            Program.LiveControl.Pause();
                        }

                        break;
                    case "resume":
                        Program.LiveControl.Resume();
                        break;

                    case "stop":
                        if (!Program.Runner.IsStopRequested)
                            Program.Runner.Stop(CloseReason.UserClosing);
                        break;

                    case "abort":
                        if (!Program.Runner.IsStopRequested)
                        {
                            Program.Runner.Stop(CloseReason.UserClosing);
                            Program.Runner.Stop(CloseReason.UserClosing);
                        }
                        break;

                    case "run":
                    case "run-backup":
                        {
                            Datamodel.Schedule schedule = null;
                            if (long.TryParse(input["id"].Value, out id))
                                schedule = Program.DataConnection.GetObjectById<Datamodel.Schedule>(id);

                            if (schedule == null)
                            {
                                ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                                return;
                            }

                            if (Library.Utility.Utility.ParseBoolOption(input.ToDictionary(x => x.Name, x => x.Value), "full"))
                                Program.WorkThread.AddTask(new FullBackupTask(schedule));
                            else
                                Program.WorkThread.AddTask(new IncrementalBackupTask(schedule));
                        }
                        break;
                    case "clear-warning":
                        Program.HasWarning = false;
                        break;
                    case "clear-error":
                        Program.HasError = false;
                        break;
                    
                    default:
                        ReportError(response, bw, string.Format("Unsupported command {0}", command));
                        break;
                }

                OutputObject(bw, new { Status = "OK" });
            }
        }
    }
}
