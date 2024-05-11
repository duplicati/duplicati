// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using System.Net;
using Duplicati.Library.Common;
using Duplicati.Library.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public static class Program
    {
        public enum PasswordSource
        {
            Database,
            HostedServer
        }

        public static HttpServerConnection Connection;
    
        private const string HOSTURL_OPTION = "hosturl";
        private const string NOHOSTEDSERVER_OPTION = "no-hosted-server";
        private const string READCONFIGFROMDB_OPTION = "read-config-from-db";

        private const string DETACHED_PROCESS = "detached-process";
        private const string BROWSER_COMMAND_OPTION = "browser-command";

        private const string DEFAULT_HOSTURL = "http://localhost:8200";
        
        private static string _browser_command = null;
        private static bool disableTrayIconLogin = false;
        private static bool openui = false;
        private static Uri serverURL = new Uri(DEFAULT_HOSTURL);


        public static string BrowserCommand { get { return _browser_command; } }
        public static Server.Database.Connection databaseConnection = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);

            if (Platform.IsClientWindows && !Duplicati.Library.Utility.Utility.ParseBoolOption(options, DETACHED_PROCESS))
                Duplicati.Library.Utility.Win32.AttachConsole(Duplicati.Library.Utility.Win32.ATTACH_PARENT_PROCESS);

            foreach (string s in args)
                if (
                    s.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("usage", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("/usage", StringComparison.OrdinalIgnoreCase))
                    options["help"] = "";

            if (options.ContainsKey("help"))
            {
                Console.WriteLine("Supported commandline arguments:");
                Console.WriteLine();

                foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                {
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);
                }

                Console.WriteLine("Additionally, these server options are also supported:");
                Console.WriteLine();

                foreach (Library.Interface.ICommandLineArgument arg in Duplicati.Server.Program.SupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                return 0;
            }

            options.TryGetValue(BROWSER_COMMAND_OPTION, out _browser_command);

            HostedInstanceKeeper hosted = null;

            string password = null;
            var saltedpassword = false;

            if (!Library.Utility.Utility.ParseBoolOption(options, NOHOSTEDSERVER_OPTION))
            {
                try
                {
                    hosted = new HostedInstanceKeeper(_args);
                }
                catch (Server.SingleInstance.MultipleInstanceException)
                {
                    return 1;
                }

                // We have a hosted server, if this is the first run, 
                // we should open the main page
                openui = Duplicati.Server.Program.IsFirstRun || Duplicati.Server.Program.ServerPortChanged;
                password = Duplicati.Server.Program.DataConnection.ApplicationSettings.WebserverPassword;
                saltedpassword = true;
                // Tell the hosted server it was started by the TrayIcon
                Duplicati.Server.Program.Origin = "Tray icon";

                var cert = Duplicati.Server.Program.DataConnection.ApplicationSettings.ServerSSLCertificate;
                var scheme = "http";

                if (cert != null && cert.HasPrivateKey)
                    scheme = "https";

                serverURL = (new UriBuilder(serverURL)
                {
                    Port = Duplicati.Server.Program.ServerPort,
                    Scheme = scheme
                }).Uri;
            }
            else if (Library.Utility.Utility.ParseBoolOption(options, READCONFIGFROMDB_OPTION))
            {
                databaseConnection = Server.Program.GetDatabaseConnection(options);

                if (databaseConnection != null)
                {
                    disableTrayIconLogin = databaseConnection.ApplicationSettings.DisableTrayIconLogin;
                    password = databaseConnection.ApplicationSettings.WebserverPasswordTrayIcon;
                    saltedpassword = false;

                    var cert = databaseConnection.ApplicationSettings.ServerSSLCertificate;
                    var scheme = "http";

                    if (cert != null && cert.HasPrivateKey)
                        scheme = "https";

                    serverURL = (new UriBuilder(serverURL)
                    {
                        Port = databaseConnection.ApplicationSettings.LastWebserverPort == -1 ? serverURL.Port : databaseConnection.ApplicationSettings.LastWebserverPort,
                        Scheme = scheme
                    }).Uri;
                }
            }

            string pwd;

            if (options.TryGetValue("webserver-password", out pwd))
            {
                password = pwd;
                saltedpassword = false;
            }

            string url;

            if (options.TryGetValue(HOSTURL_OPTION, out url))
                serverURL = new Uri(url);

            StartTray(_args, options, hosted, password, saltedpassword);

            return 0;
        }

        private static void StartTray(string[] _args, Dictionary<string, string> options, HostedInstanceKeeper hosted, string password, bool saltedpassword)
        {
            using (hosted)
            {
                var reSpawn = 0;

                do
                {
                    try
                    {
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
                        using (Connection = new HttpServerConnection(serverURL, password, saltedpassword, databaseConnection != null ? PasswordSource.Database : PasswordSource.HostedServer, disableTrayIconLogin, options))
                        {
                            using (var tk = RunTrayIcon())
                            {
                                if (hosted != null && Server.Program.ApplicationInstance != null)
                                    Server.Program.ApplicationInstance.SecondInstanceDetected +=
                                        new Server.SingleInstance.SecondInstanceDelegate(
                                            x => { tk.ShowUrlInWindow(serverURL.ToString()); });

                                // TODO: If we change to hosted browser this should be a callback
                                if (openui)
                                {
                                    try
                                    {
                                        tk.ShowUrlInWindow(Connection.StatusWindowURL);

                                        Duplicati.Server.Program.IsFirstRun = false;
                                        Duplicati.Server.Program.ServerPortChanged = false;
                                    }
                                    catch
                                    {
                                    }
                                }

                                // If the server shuts down, shut down the tray-icon as well
                                Action shutdownEvent = () =>
                                {
                                    // Make sure we do not start again after 
                                    // a controlled exit
                                    reSpawn = 100;
                                    tk.InvokeExit();
                                };

                                if (hosted != null)
                                    hosted.InstanceShutdown += shutdownEvent;

                                tk.Init(_args);

                                // If the tray-icon quits, stop the server
                                reSpawn = 100;

                                // Make sure that the server shutdown does not access the tray-icon,
                                // as it would be disposed by now
                                if (hosted != null)
                                    hosted.InstanceShutdown -= shutdownEvent;
                            }
                        }
                    }
                    catch (WebException ex)
                    {
                        System.Diagnostics.Trace.WriteLine("Request error: " + ex);
                        Console.WriteLine("Request error: " + ex);

                        reSpawn++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine("Unexpected error: " + ex);
                        Console.WriteLine("Unexpected error: " + ex);
                        return;
                    }
                } while (reSpawn < 3);
            }
        }

        private static TrayIconBase RunTrayIcon()
            => new AvaloniaRunner();

        public static Duplicati.Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                var args = new List<Duplicati.Library.Interface.ICommandLineArgument>()
                {
                    new Duplicati.Library.Interface.CommandLineArgument(HOSTURL_OPTION, CommandLineArgument.ArgumentType.String, "Selects the url to connect to", "Supply the url that the TrayIcon will connect to and show status for", DEFAULT_HOSTURL),
                    new Duplicati.Library.Interface.CommandLineArgument(NOHOSTEDSERVER_OPTION, CommandLineArgument.ArgumentType.String, "Disables local server", "Set this option to not spawn a local service, use if the TrayIcon should connect to a running service"),
                    new Duplicati.Library.Interface.CommandLineArgument(READCONFIGFROMDB_OPTION, CommandLineArgument.ArgumentType.String, "Read server connection info from DB", $"Set this option to read server connection info for running service from its database (only together with {NOHOSTEDSERVER_OPTION})"),               
                    new Duplicati.Library.Interface.CommandLineArgument(BROWSER_COMMAND_OPTION, CommandLineArgument.ArgumentType.String, "Sets the browser command", "Set this option to override the default browser detection"),
                };

                if (Platform.IsClientWindows)
                {
                    args.Add(new Duplicati.Library.Interface.CommandLineArgument(DETACHED_PROCESS, CommandLineArgument.ArgumentType.String, "Runs the tray-icon detached", "This option runs the tray-icon in detached mode, meaning that the process will exit immediately and not send output to the console of the caller"));
                }

                return args.ToArray();
            }
        }
    }
}
