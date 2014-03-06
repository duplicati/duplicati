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
        public const string OPTION_WEBROOT = "webservice-webroot";
        /// <summary>
        /// Option for changing the webservice listen port
        /// </summary>
        public const string OPTION_PORT = "webservice-port";

        /// <summary>
        /// The single webserver instance
        /// </summary>
        private HttpServer.HttpServer m_server;
        
        /// <summary>
        /// The webserver listening port
        /// </summary>
        public readonly int Port;

        /// <summary>
        /// Sets up the webserver and starts it
        /// </summary>
        /// <param name="options">A set of options</param>
        public WebServer(IDictionary<string, string> options)
        {
            int port;
            string portstring;
            IEnumerable<int> ports = null;
            options.TryGetValue(OPTION_PORT, out portstring);
            if (!string.IsNullOrEmpty(portstring))
                ports = 
                    from n in portstring.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                where int.TryParse(n, out port)
                                select int.Parse(n);

            if (ports == null || !ports.Any())
                ports = new int[] { 8080 };                                

            // If we are in hosted mode with no specified port, 
            // then try different ports
            Exception e = null;
            foreach(var p in ports)
                try
                {
                    // Due to the way the server is initialized, 
                    // we cannot try to start it again on another port, 
                    // so we create a new server for each attempt
                
                    var server = CreateServer(options);
                    server.Start(System.Net.IPAddress.Any, p);
                    m_server = server;
                    this.Port = p;
                    return;
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                }
                
            throw new Exception("Unable to open a socket for listening, tried ports: " + string.Join(",", from n in ports select n.ToString()));
        }
        
        private static HttpServer.HttpServer CreateServer(IDictionary<string, string> options)
        {
            HttpServer.HttpServer server = new HttpServer.HttpServer();

            server.Add(new DynamicHandler());

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

            if (Library.Utility.Utility.IsClientOSX)
            {
                string osxTmpWebRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", "..", "..", "..", "..", "..", ".."));
                osxTmpWebRoot = System.IO.Path.Combine(osxTmpWebRoot, "Server");
                if (System.IO.Directory.Exists(System.IO.Path.Combine(osxTmpWebRoot, "webroot")))
                    webroot = osxTmpWebRoot;
            }
#endif

            webroot = System.IO.Path.Combine(webroot, "webroot");

            if (options.ContainsKey(OPTION_WEBROOT))
            {
                string userroot = options[OPTION_WEBROOT];
#if DEBUG
                //In debug mode we do not care where the path points
#else
                //In release mode we check that the user supplied path is located
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
            fh.MimeTypes.Add("map", "application/json");
            server.Add(fh);
            server.Add(new IndexHtmlHandler(System.IO.Path.Combine(webroot, "index.html")));
#if DEBUG
            //For debugging, it is nice to know when we get a 404
            server.Add(new DebugReportHandler());
#endif
            
            return server;
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
            private readonly Dictionary<string, ProcessSub> SUPPORTED_METHODS;

            public DynamicHandler()
            {
                SUPPORTED_METHODS = new Dictionary<string, ProcessSub>(System.StringComparer.InvariantCultureIgnoreCase);
             
                //Make a list of all supported actions
                SUPPORTED_METHODS.Add("supported-actions", ListSupportedActions);
                SUPPORTED_METHODS.Add("system-info", ListSystemInfo);
                SUPPORTED_METHODS.Add("list-backups", ListBackups);
                SUPPORTED_METHODS.Add("get-current-state", GetCurrentState);
                SUPPORTED_METHODS.Add("get-progress-state", GetProgressState);
                SUPPORTED_METHODS.Add("list-application-settings", ListApplicationSettings);
                SUPPORTED_METHODS.Add("list-options", ListCoreOptions);
                SUPPORTED_METHODS.Add("send-command", SendCommand);
                SUPPORTED_METHODS.Add("get-backup-defaults", GetBackupDefaults);
                SUPPORTED_METHODS.Add("get-folder-contents", GetFolderContents);
                SUPPORTED_METHODS.Add("get-backup", GetBackup);
                SUPPORTED_METHODS.Add("add-backup", AddBackup);
                SUPPORTED_METHODS.Add("update-backup", UpdateBackup);
                SUPPORTED_METHODS.Add("delete-backup", DeleteBackup);
                SUPPORTED_METHODS.Add("validate-path", ValidatePath);
                SUPPORTED_METHODS.Add("list-tags", ListTags);
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
                Serializer.SerializeJson(b, o);
            }

            private void ListSystemInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new
                {
                    APIVersion = 1,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    ServerVersionName = License.VersionNumbers.Version,
                    ServerTime = DateTime.Now,
                    OSType = Library.Utility.Utility.IsClientLinux ? (Library.Utility.Utility.IsClientOSX ? "OSX" : "Linux") : "Windows",
                    DirectorySeparator = System.IO.Path.DirectorySeparatorChar,
                    PathSeparator = System.IO.Path.PathSeparator,
                    CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
                    MonoVersion = Duplicati.Library.Utility.Utility.IsMono ? Duplicati.Library.Utility.Utility.MonoVersion.ToString() : null,
                    MachineName = System.Environment.MachineName,
                    NewLine = System.Environment.NewLine,
                    CLRVersion = System.Environment.Version.ToString(),
                    CLROSInfo = new
                    {
                        Platform = System.Environment.OSVersion.Platform.ToString(),
                        ServicePack = System.Environment.OSVersion.ServicePack,
                        Version = System.Environment.OSVersion.Version.ToString(),
                        VersionString = System.Environment.OSVersion.VersionString
                    },
                    CompressionModules =  Serializable.ServerSettings.CompressionModules,
                    EncryptionModules = Serializable.ServerSettings.EncryptionModules,
                    BackendModules = Serializable.ServerSettings.BackendModules,
                    GenericModules = Serializable.ServerSettings.GenericModules
                });
            }

            private void ListSupportedActions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new { Version = 1, Methods = SUPPORTED_METHODS.Keys });
            }

            private void ListBackups (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.Backups);
            }

            private void ListTags(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                var r = 
                    from n in 
                    Serializable.ServerSettings.CompressionModules
                        .Union(Serializable.ServerSettings.EncryptionModules)
                        .Union(Serializable.ServerSettings.BackendModules)
                        .Union(Serializable.ServerSettings.GenericModules)
                        select n.Key.ToLower();
                
                // Append all known tags
                r = r.Union(from n in Program.DataConnection.Backups select n.Tags into p from x in p select x.ToLower());
                OutputObject(bw, r);
            }

            private void ValidatePath(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                if (input["path"] == null || input["path"].Value == null)
                {
                    ReportError(response, bw, "The path parameter was not set");
                    return;
                }
                
                string path = input["path"].Value;
                
                if (path.StartsWith("%") && path.EndsWith("%"))
                {
                    if (SpecialFolders.Nodes.Any(x => x.id == path))
                        OutputObject(bw, null);
                }
                
                if (!path.StartsWith("/"))
                {
                    ReportError(response, bw, "The path parameter must start with a forward-slash");
                    return;
                }
                
                try
                {
                    if (System.IO.Path.IsPathRooted(path) && System.IO.Directory.Exists(path))
                    {
                        OutputObject(bw, null);
                        return;
                    }
                }
                catch
                {
                }
                
                ReportError(response, bw, "File or folder not found");
                return;
            }

            private void GetFolderContents(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                if(input["path"] == null || input["path"].Value == null)
                {
                    ReportError(response, bw, "The path parameter was not set");
                    return;
                }

                bool skipFiles = Library.Utility.Utility.ParseBool(input["onlyfolders"].Value, false);

                string path = input["path"].Value;
                if (!path.StartsWith("/"))
                {
                    ReportError(response, bw, "The path parameter must start with a forward-slash");
                    return;
                }

                try
                {
                    if (!path.EndsWith("/"))
                        path += '/';

                    IEnumerable<Serializable.TreeNode> res;

                    if (!Library.Utility.Utility.IsClientLinux && path.Equals("/"))
                    {
                        res = 
                            from di in System.IO.DriveInfo.GetDrives()
                            where di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable
                            select new Serializable.TreeNode()
                            {
                                id = "/" + di.RootDirectory.FullName.Replace('\\', '/'),
                                text = di.RootDirectory.FullName.Replace('\\', ' ') + "(" + di.DriveType + ")",
                                iconCls = "x-tree-icon-drive"
                            };
                    }
                    else
                    {
                        //Helper function for finding out if a folder has sub elements
                        Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

                        //Helper function for dealing with exceptions when accessing off-limits folders
                        Func<string, bool> isEmptyFolder = (p) =>
                        {
                            try { return !hasSubElements(p); }
                            catch { }
                            return true;
                        };

                        //Helper function for dealing with exceptions when accessing off-limits folders
                        Func<string, bool> canAccess = (p) =>
                        {
                            try { hasSubElements(p); return true; }
                            catch { }
                            return false;
                        };

                        res = 
                            from s in System.IO.Directory.EnumerateFileSystemEntries(Library.Utility.Utility.IsClientLinux ? path : path.Substring(1).Replace('/', '\\'))
                                  
                            let attr = System.IO.File.GetAttributes(s)
                            let isSymlink = (attr & FileAttributes.ReparsePoint) != 0
                            let isFolder = (attr & FileAttributes.Directory) != 0
                            let isFile = !isFolder
                            let isHidden = (attr & FileAttributes.Hidden) != 0

                            let accesible = isFile || canAccess(s)
                            let isLeaf = isFile || !accesible || isEmptyFolder(s) 

                            let rawid = isFolder ? Library.Utility.Utility.AppendDirSeparator(s) : s

                            where !skipFiles || isFolder
                                  
                            select new Serializable.TreeNode()
                            {
                                id = Library.Utility.Utility.IsClientLinux ? rawid : "/" + rawid.Replace('\\', '/'),
                                text = System.IO.Path.GetFileName(s),
                                iconCls = isFolder ? (accesible ? "x-tree-icon-parent" : "x-tree-icon-locked") : "x-tree-icon-leaf",
                                leaf = isLeaf
                            };
                    }

                    if (path.Equals("/")) 
                    {
                        // Prepend special folders
                        res = SpecialFolders.Nodes.Union(res);
                    }

                    OutputObject(bw, res);
                }
                catch (Exception ex)
                {
                    ReportError(response, bw, "Failed to process the path: " + ex.Message);
                }
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
                    id = poller.Wait(lastEventId, (int)ts.TotalMilliseconds);
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
                    /*var ev = Program.Runner.LastEvent;
                    ev.LastEventID = id;
                    OutputObject(bw, ev);*/
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

            private void ListCoreOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new Duplicati.Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);
            }

            private void ListApplicationSettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.ApplicationSettings);
            }
            
            private static void MergeJsonObjects(Newtonsoft.Json.Linq.JObject self, Newtonsoft.Json.Linq.JObject other)
            {
                foreach(var p in other.Properties())
                {
                    var sp = self.Property(p.Name);
                    if (sp == null)
                        self.Add(p);
                    else
                    {
                        switch (p.Type)
                        {
                            // Primitives override
                            case Newtonsoft.Json.Linq.JTokenType.Boolean:
                            case Newtonsoft.Json.Linq.JTokenType.Bytes:
                            case Newtonsoft.Json.Linq.JTokenType.Comment:
                            case Newtonsoft.Json.Linq.JTokenType.Constructor:
                            case Newtonsoft.Json.Linq.JTokenType.Date:
                            case Newtonsoft.Json.Linq.JTokenType.Float:
                            case Newtonsoft.Json.Linq.JTokenType.Guid:
                            case Newtonsoft.Json.Linq.JTokenType.Integer:
                            case Newtonsoft.Json.Linq.JTokenType.String:
                            case Newtonsoft.Json.Linq.JTokenType.TimeSpan:
                            case Newtonsoft.Json.Linq.JTokenType.Uri:
                            case Newtonsoft.Json.Linq.JTokenType.None:
                            case Newtonsoft.Json.Linq.JTokenType.Null:
                            case Newtonsoft.Json.Linq.JTokenType.Undefined:
                                self.Replace(p);
                                break;

                            // Arrays merge
                            case Newtonsoft.Json.Linq.JTokenType.Array:
                                if (sp.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                    sp.Value = new Newtonsoft.Json.Linq.JArray(((Newtonsoft.Json.Linq.JArray)sp.Value).Union((Newtonsoft.Json.Linq.JArray)p.Value));
                                else
                                {
                                    var a = new Newtonsoft.Json.Linq.JArray(sp.Value);
                                    sp.Value = new Newtonsoft.Json.Linq.JArray(a.Union((Newtonsoft.Json.Linq.JArray)p.Value));
                                }
                                
                                break;
                                
                            // Objects merge
                            case Newtonsoft.Json.Linq.JTokenType.Object:
                                if (sp.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                                    MergeJsonObjects((Newtonsoft.Json.Linq.JObject)sp.Value, (Newtonsoft.Json.Linq.JObject)p.Value);
                                else
                                    sp.Value = p.Value;
                                break;
                                
                            // Ignore other stuff                                
                            default:
                                break;
                        }
                    }
                }
            }

            private void GetBackupDefaults(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {   
                // Start with a scratch object
                var o = new Newtonsoft.Json.Linq.JObject();
                
                // Add application wide settings
                o.Add("ApplicationOptions", new Newtonsoft.Json.Linq.JArray(Program.DataConnection.Settings));
                
                try
                {
                    // Add built-in defaults
                    Newtonsoft.Json.Linq.JObject n;
                    using(var s = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".newbackup.json")))
                        n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(s.ReadToEnd());
                    
                    MergeJsonObjects(o, n);
                }
                catch
                {
                }

                try
                {
                    // Add install defaults/overrides, if present
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "newbackup.json");
                    if (System.IO.File.Exists(path))
                    {
                        Newtonsoft.Json.Linq.JObject n;
                        n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(System.IO.File.ReadAllText(path));
                        
                        MergeJsonObjects(o, n);
                    }
                }
                catch
                {
                }

                OutputObject(bw, new
                {
                    success = true,
                    data = o
                });
            }

            private void GetBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                long id;
                if (!long.TryParse(input["id"].Value, out id))
                    ReportError(response, bw, "Invalid or missing backup id");
                else
                {
                    var bk = Program.DataConnection.GetBackup(id);
                    if (bk == null)
                        ReportError(response, bw, "Invalid or missing backup id");
                    else
                    {
                        var scheduleId = Program.DataConnection.GetScheduleIDsFromTags(new string[] { "ID=" + id });
                        var schedule = scheduleId.Any() ? Program.DataConnection.GetSchedule(scheduleId.First()) : null;
                        
                        OutputObject(bw, new
                        {
                            success = true,
                            data = new {
                                Schedule = schedule,
                                Backup = bk,
                            }
                        });
                    }
                }
            }

            private void UpdateBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                string str = request.Form["data"].Value;
                if (string.IsNullOrWhiteSpace(str))
                {
                    ReportError(response, bw, "Missing backup object");
                    return;
                }

                AddOrUpdateBackupData data = null;
                try
                {
                    data = Serializer.Deserialize<AddOrUpdateBackupData>(new StringReader(str));
                    if (data.Backup == null)
                    {
                        ReportError(response, bw, "Data object had no backup entry");
                        return;
                    }
                    
                    if (data.Backup.ID <= 0)
                    {
                        ReportError(response, bw, "Invalid or missing backup id");
                        return;
                    }                    
                    
                    lock(Program.DataConnection.m_lock)
                    {
                        var backup = Program.DataConnection.GetBackup(data.Backup.ID);
                        if (backup == null)
                        {
                            ReportError(response, bw, "Invalid or missing backup id");
                            return;
                        }
    
                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase) && x.ID != data.Backup.ID).Any())
                        {
                            ReportError(response, bw, "There already exists a backup with the name: " + data.Backup.Name);
                            return;
                        }
                        
                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);

                    }
                    
                    OutputObject(bw, new { status = "OK" });
                }
                catch (Exception ex)
                {
                    if (data == null)
                        ReportError(response, bw, string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                    else
                        ReportError(response, bw, string.Format("Unable to save backup or schedule: {0}", ex.Message));
                        
                }
            }
            
            private class AddOrUpdateBackupData
            {
                public Database.Schedule Schedule {get; set;}
                public Database.Backup Backup {get; set;}
            }

            private void AddBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                string str = request.Form["data"].Value;
                if (string.IsNullOrWhiteSpace(str))
                {
                    ReportError(response, bw, "Missing backup object");
                    return;
                }

                AddOrUpdateBackupData data = null;
                try
                {
                    data = Serializer.Deserialize<AddOrUpdateBackupData>(new StringReader(str));
                    if (data.Backup == null)
                    {
                        ReportError(response, bw, "Data object had no backup entry");
                        return;
                    }
                        
                    data.Backup.ID = -1;
                    
                    lock(Program.DataConnection.m_lock)
                    {
                        if (Program.DataConnection.Backups.Where(x => x.Name.Equals(data.Backup.Name, StringComparison.InvariantCultureIgnoreCase)).Any())
                        {
                            ReportError(response, bw, "There already exists a backup with the name: " + data.Backup.Name);
                            return;
                        }
                        
                        Program.DataConnection.AddOrUpdateBackupAndSchedule(data.Backup, data.Schedule);
                    }
                    
                    OutputObject(bw, new { status = "OK" });
                }
                catch (Exception ex)
                {
                    if (data == null)
                        ReportError(response, bw, string.Format("Unable to parse backup or schedule object: {0}", ex.Message));
                    else
                        ReportError(response, bw, string.Format("Unable to save schedule or backup object: {0}", ex.Message));
                }
            }

            private void DeleteBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                long id;
                if (!long.TryParse(input["id"].Value, out id))
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }


                var backup = Program.DataConnection.GetBackup(id);
                if (backup == null)
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }

                if (Program.WorkThread.Active)
                {
                    try
                    {
                        //TODO: It's not safe to access the values like this, 
                        //because the runner thread might interfere
                        if (Program.WorkThread.CurrentTask.Item1 == id)
                        {
                            bool force;
                            if (!bool.TryParse(input["force"].Value, out force))
                                force = false;
                            
                            if (!force)
                            {
                                OutputObject(bw, new { status = "failed", reason = "backup-in-progress" });
                                return;
                            }

                            bool hasPaused = Program.LiveControl.State == LiveControls.LiveControlState.Paused;
                            Program.LiveControl.Pause();

                            try
                            {
                                for (int i = 0; i < 10; i++)
                                    if (Program.WorkThread.Active)
                                    {
                                        var t = Program.WorkThread.CurrentTask;
                                        if (t != null && t.Item1 == id)
                                            System.Threading.Thread.Sleep(1000);
                                        else
                                            break;
                                    }
                                    else
                                        break;
                            }
                            finally
                            {
                            }

                            if (Program.WorkThread.Active)
                            {
                                var t = Program.WorkThread.CurrentTask;
                                if (t == null && t.Item1 == id)
                                {
                                    if (hasPaused)
                                        Program.LiveControl.Resume();
                                    OutputObject(bw, new { status = "failed", reason = "backup-unstoppable" });
                                    return;
                                }
                            }

                            if (hasPaused)
                                Program.LiveControl.Resume();
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputObject(bw, new { status = "error", message = ex.Message });
                        return;
                    }
                }
                
                Program.DataConnection.DeleteBackup(backup);

                //We have fiddled with the schedules
                Program.Scheduler.Reschedule();

                OutputObject(bw, new { status = "OK" });
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
                        break;

                    case "abort":
                        break;

                    case "run":
                    case "run-backup":
                        {
                            Duplicati.Server.Serialization.Interface.IBackup backup = null;
                            if (long.TryParse(input["id"].Value, out id))
                                backup = Program.DataConnection.GetBackup(id);

                            if (backup == null)
                            {
                                ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                                return;
                            }

                            Program.WorkThread.AddTask(new Tuple<long, DuplicatiOperation>(id, DuplicatiOperation.Backup));
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
