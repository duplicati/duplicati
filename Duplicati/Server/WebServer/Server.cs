using System;
using System.Collections.Generic;
using System.Linq;
using HttpServer.HttpModules;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Common.IO;

namespace Duplicati.Server.WebServer
{
    public class Server
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Server>();

        /// <summary>
        /// Option for changing the webroot folder
        /// </summary>
        public const string OPTION_WEBROOT = "webservice-webroot";

        /// <summary>
        /// Option for changing the webservice listen port
        /// </summary>
        public const string OPTION_PORT = "webservice-port";

        /// <summary>
        /// Option for changing the webservice listen interface
        /// </summary>
        public const string OPTION_INTERFACE = "webservice-interface";

        /// <summary>
        /// The default path to the web root
        /// </summary>
        public const string DEFAULT_OPTION_WEBROOT = "webroot";

        /// <summary>
        /// The default listening port
        /// </summary>
        public const int DEFAULT_OPTION_PORT = 8200;

        /// <summary>
        /// Option for setting the webservice SSL certificate
        /// </summary>
        public const string OPTION_SSLCERTIFICATEFILE = "webservice-sslcertificatefile";

        /// <summary>
        /// Option for setting the webservice SSL certificate key
        /// </summary>
        public const string OPTION_SSLCERTIFICATEFILEPASSWORD = "webservice-sslcertificatepassword";

        /// <summary>
        /// The default listening interface
        /// </summary>
        public const string DEFAULT_OPTION_INTERFACE = "loopback";

        /// <summary>
        /// The single webserver instance
        /// </summary>
        private readonly HttpServer.HttpServer m_server;
        
        /// <summary>
        /// The webserver listening port
        /// </summary>
        public readonly int Port;
        
        /// <summary>
        /// A string that is sent out instead of password values
        /// </summary>
        public const string PASSWORD_PLACEHOLDER = "**********";

        /// <summary>
        /// Sets up the webserver and starts it
        /// </summary>
        /// <param name="options">A set of options</param>
        public Server(IDictionary<string, string> options)
        {
            string portstring;
            IEnumerable<int> ports = null;
            options.TryGetValue(OPTION_PORT, out portstring);
            if (!string.IsNullOrEmpty(portstring))
                ports = 
                    from n in portstring.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                where int.TryParse(n, out _)
                                select int.Parse(n);

            if (ports == null || !ports.Any())
                ports = new int[] { DEFAULT_OPTION_PORT };

            string interfacestring;
            System.Net.IPAddress listenInterface;
            options.TryGetValue(OPTION_INTERFACE, out interfacestring);

            if (string.IsNullOrWhiteSpace(interfacestring))
                interfacestring = Program.DataConnection.ApplicationSettings.ServerListenInterface;
            if (string.IsNullOrWhiteSpace(interfacestring))
                interfacestring = DEFAULT_OPTION_INTERFACE;

            if (interfacestring.Trim() == "*" || interfacestring.Trim().Equals("any", StringComparison.OrdinalIgnoreCase) || interfacestring.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                listenInterface = System.Net.IPAddress.Any;
            else if (interfacestring.Trim() == "loopback")
                listenInterface = System.Net.IPAddress.Loopback;
            else
                listenInterface = System.Net.IPAddress.Parse(interfacestring);

            string certificateFile;
            options.TryGetValue(OPTION_SSLCERTIFICATEFILE, out certificateFile);

            string certificateFilePassword;
            options.TryGetValue(OPTION_SSLCERTIFICATEFILEPASSWORD, out certificateFilePassword);

            X509Certificate2 cert = null;
            bool certValid = false;

            if (certificateFile == null)
            {
                try
                {
                    cert = Program.DataConnection.ApplicationSettings.ServerSSLCertificate;

                    if (cert != null)
                        certValid = cert.HasPrivateKey;
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "DefectStoredSSLCert", ex, Strings.Server.DefectSSLCertInDatabase);
                }
            }
            else if (certificateFile.Length == 0)
            {
                Program.DataConnection.ApplicationSettings.ServerSSLCertificate = null;
            }
            else
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(certificateFilePassword))
                        cert = new X509Certificate2(certificateFile, "", X509KeyStorageFlags.Exportable);
                    else
                        cert = new X509Certificate2(certificateFile, certificateFilePassword, X509KeyStorageFlags.Exportable);

                    certValid = cert.HasPrivateKey;
                }
                catch (Exception ex)
                {
                    throw new Exception(Strings.Server.SSLCertificateFailure(ex.Message), ex);
                }
            }

            // If we are in hosted mode with no specified port, 
            // then try different ports
            foreach (var p in ports)
                try
                {
                    // Due to the way the server is initialized, 
                    // we cannot try to start it again on another port, 
                    // so we create a new server for each attempt
                
                    var server = CreateServer(options);
                    
                    if (!certValid)
                        server.Start(listenInterface, p);
                    else
                        server.Start(listenInterface, p, cert, System.Security.Authentication.SslProtocols.Tls11 | System.Security.Authentication.SslProtocols.Tls12, null, false);

                    m_server = server;
                    m_server.ServerName = string.Format("{0} v{1}", Library.AutoUpdater.AutoUpdateSettings.AppName, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                    this.Port = p;

                    if (interfacestring !=  Program.DataConnection.ApplicationSettings.ServerListenInterface)
                        Program.DataConnection.ApplicationSettings.ServerListenInterface = interfacestring;
                    
                    if (certValid && !cert.Equals(Program.DataConnection.ApplicationSettings.ServerSSLCertificate))
                        Program.DataConnection.ApplicationSettings.ServerSSLCertificate = cert;

                    Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "ServerListening", Strings.Server.StartedServer(listenInterface.ToString(), p));
                    
                    return;
                }
                catch (System.Net.Sockets.SocketException)
                {
                }
                
            throw new Exception(Strings.Server.ServerStartFailure(ports));
        }

        private static void AddMimeTypes(FileModule fm)
        {
            fm.AddDefaultMimeTypes();
            fm.MimeTypes["htc"] = "text/x-component";
            fm.MimeTypes["json"] = "application/json";
            fm.MimeTypes["map"] = "application/json";
            fm.MimeTypes["htm"] = "text/html; charset=utf-8";
            fm.MimeTypes["html"] = "text/html; charset=utf-8";
            fm.MimeTypes["hbs"] = "application/x-handlebars-template";
            fm.MimeTypes["woff"] = "application/font-woff";
            fm.MimeTypes["woff2"] = "application/font-woff";
        }
            
        private static HttpServer.HttpServer CreateServer(IDictionary<string, string> options)
        {
            HttpServer.HttpServer server = new HttpServer.HttpServer();

            server.Add(new HostHeaderChecker());

            if (string.Equals(Environment.GetEnvironmentVariable("SYNO_DSM_AUTH") ?? string.Empty, "1"))
                server.Add(new SynologyAuthenticationHandler());

            server.Add(new AuthenticationHandler());

            server.Add(new RESTHandler());

            string webroot = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string install_webroot = System.IO.Path.Combine(Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "webroot");

#if DEBUG
            // Easy test for extensions while debugging
            install_webroot = Library.AutoUpdater.UpdaterManager.InstalledBaseDir;

            if (!System.IO.Directory.Exists(System.IO.Path.Combine(webroot, "webroot")))
            {
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
                        userroot.StartsWith(Util.AppendDirSeparator(System.Reflection.Assembly.GetExecutingAssembly().Location), Library.Utility.Utility.ClientFilenameStringComparison)
                        ||
                        userroot.StartsWith(Util.AppendDirSeparator(Program.StartupPath), Library.Utility.Utility.ClientFilenameStringComparison)
                    )
                )
#endif
                {
                    webroot = userroot;
                    install_webroot = webroot;
                }
            }

            if (install_webroot != webroot && System.IO.Directory.Exists(System.IO.Path.Combine(install_webroot, "customized")))
            {
                var customized_files = new CacheControlFileHandler("/customized/", System.IO.Path.Combine(install_webroot, "customized"));
                AddMimeTypes(customized_files);
                server.Add(customized_files);
            }

            if (install_webroot != webroot && System.IO.Directory.Exists(System.IO.Path.Combine(install_webroot, "oem")))
            {
                var oem_files = new CacheControlFileHandler("/oem/", System.IO.Path.Combine(install_webroot, "oem"));
                AddMimeTypes(oem_files);
                server.Add(oem_files);
            }

            if (install_webroot != webroot && System.IO.Directory.Exists(System.IO.Path.Combine(install_webroot, "package")))
            {
                var proxy_files = new CacheControlFileHandler("/proxy/", System.IO.Path.Combine(install_webroot, "package"));
                AddMimeTypes(proxy_files);
                server.Add(proxy_files);
            }

            var fh = new CacheControlFileHandler("/", webroot, true);
            AddMimeTypes(fh);
            server.Add(fh);

            server.Add(new IndexHtmlHandler(webroot));
#if DEBUG
            //For debugging, it is nice to know when we get a 404
            server.Add(new DebugReportHandler());
#endif
            return server;
        }

        private class DebugReportHandler : HttpModule
        {
            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                System.Diagnostics.Trace.WriteLine(string.Format("Rejecting request for {0}", request.Uri));
                return false;
            }
        }

        private class CacheControlFileHandler : FileModule
        {
            public CacheControlFileHandler(string baseUri, string basePath, bool useLastModifiedHeader = false)
                : base(baseUri, basePath, useLastModifiedHeader)
            {

            }

            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                if (!this.CanHandle(request.Uri))
                    return false;

                if (request.Uri.AbsolutePath.EndsWith("index.html", StringComparison.Ordinal) || request.Uri.AbsolutePath.EndsWith("index.htm", StringComparison.Ordinal))
                    response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                else
                    response.AddHeader("Cache-Control", "max-age=" + (60 * 60 * 24));
                return base.Process(request, response, session);
            }
        }

        /// <summary>
        /// Module for injecting host header verification
        /// </summary>
        private class HostHeaderChecker : HttpModule
        {
            /// <summary>
            /// The hostnames that we allow
            /// </summary>
            private string[] m_lastSplitNames;

            /// <summary>
            /// The string used to generate m_lastSplitNames;
            /// </summary>
            private string m_lastAllowed;

            /// <summary>
            /// A regex to detect potential IPv4 addresses.
            /// Note that this also detects things that are not valid IPv4.
            /// </summary>
            private static readonly System.Text.RegularExpressions.Regex IPV4 = new System.Text.RegularExpressions.Regex(@"((\d){1,3}\.){3}(\d){1,3}");
            /// <summary>
            /// A regex to detect potential IPv6 addresses.
            /// Note that this also detects things that are not valid IPv6.
            /// </summary>
            private static readonly System.Text.RegularExpressions.Regex IPV6 = new System.Text.RegularExpressions.Regex(@"(\:)?(\:?[A-Fa-f0-9]{1,4}\:?){1,8}(\:)?");

            /// <summary>
            /// The hostnames that are always allowed
            /// </summary>
            private static readonly string[] DEFAULT_ALLOWED = new string[] { "localhost", "127.0.0.1", "::1", "localhost.localdomain" };

            /// <summary>
            /// Process the received request
            /// </summary>
            /// <returns>A flag indicating if the request is handled.</returns>
            /// <param name="request">The received request.</param>
            /// <param name="response">The response object.</param>
            /// <param name="session">The session state.</param>
            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                string[] h = null;
                var hstring = Program.DataConnection.ApplicationSettings.AllowedHostnames;

                if (!string.IsNullOrWhiteSpace(hstring))
                {
                    h = m_lastSplitNames;
                    if (hstring != m_lastAllowed)
                    {
                        m_lastAllowed = hstring;
                        h = m_lastSplitNames = (hstring ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    if (h == null || h.Length == 0)
                        h = null;
                }

                // For some reason, the web server strips out the host header
                var host = request.Headers["Host"];
                if (string.IsNullOrWhiteSpace(host))
                    host = request.Uri.Host;

                // This should not happen
                if (string.IsNullOrWhiteSpace(host))
                {
                    response.Reason = "Invalid request, missing host header";
                    response.Status = System.Net.HttpStatusCode.Forbidden;
                    var msg = System.Text.Encoding.ASCII.GetBytes(response.Reason);
                    response.ContentType = "text/plain";
                    response.ContentLength = msg.Length;
                    response.Body.Write(msg, 0, msg.Length);
                    response.Send();
                    return true;
                }

                // Check the hostnames we always allow
                if (Array.IndexOf(DEFAULT_ALLOWED, host) >= 0)
                    return false;

                // Then the user specified ones
                if (h != null && Array.IndexOf(h, host) >= 0)
                    return false;

                // Disable checks if we have an asterisk
                if (h != null && Array.IndexOf(h, "*") >= 0)
                    return false;

                // Finally, check if we have a potential IP address
                var v4 = IPV4.Match(host);
                var v6 = IPV6.Match(host);

                if ((v4.Success && v4.Length == host.Length) || (v6.Success && v6.Length == host.Length))
                {
                    try
                    {
                        // Verify that the hostname is indeed a valid IP address
                        System.Net.IPAddress.Parse(host);
                        return false;
                    }
                    catch
                    { }
                }

                // Failed to find a valid header
                response.Reason = $"The host header sent by the client is not allowed";
                response.Status = System.Net.HttpStatusCode.Forbidden;
                var txt = System.Text.Encoding.ASCII.GetBytes(response.Reason);
                response.ContentType = "text/plain";
                response.ContentLength = txt.Length;
                response.Body.Write(txt, 0, txt.Length);
                response.Send();
                return true;

            }
        }
    }
}
