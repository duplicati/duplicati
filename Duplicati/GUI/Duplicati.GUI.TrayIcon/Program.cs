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
    
        private const string TOOLKIT_OPTION = "toolkit";
        private const string TOOLKIT_WINDOWS_FORMS = "winforms";
        private const string TOOLKIT_GTK = "gtk";
        private const string TOOLKIT_GTK_APP_INDICATOR = "gtk-appindicator";
        private const string TOOLKIT_COCOA = "cocoa";
        private const string TOOLKIT_RUMPS = "rumps";

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

        private static string GetDefaultToolKit()
        {
            if (Platform.IsClientOSX)
                return TOOLKIT_COCOA;

#if __WindowsGTK__ || ENABLE_GTK
            if (Platform.IsClientPosix)
            {
                if (SupportsAppIndicator)
                    return TOOLKIT_GTK_APP_INDICATOR;
                else
                    return TOOLKIT_GTK;
            }
            else
#endif
            {
                //Windows users expect a WinForms element
                return TOOLKIT_WINDOWS_FORMS;
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            Duplicati.Library.AutoUpdater.UpdaterManager.RequiresRespawn = true;
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args, Duplicati.Library.AutoUpdater.AutoUpdateStrategy.Never);
        }
        
        public static void RealMain(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);

            if (Platform.IsClientWindows && (Duplicati.Library.AutoUpdater.UpdaterManager.IsRunningInUpdateEnvironment || !Duplicati.Library.Utility.Utility.ParseBoolOption(options, DETACHED_PROCESS)))
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
                    if (arg.Name == TOOLKIT_OPTION)
                        Console.WriteLine("    Supported toolkits: {0}{1}", string.Join(", ", arg.ValidValues), Environment.NewLine);
                }

                Console.WriteLine("Additionally, these server options are also supported:");
                Console.WriteLine();

                foreach (Library.Interface.ICommandLineArgument arg in Duplicati.Server.Program.SupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                return;
            }

            options.TryGetValue(BROWSER_COMMAND_OPTION, out _browser_command);

            string toolkit;
            if (!options.TryGetValue(TOOLKIT_OPTION, out toolkit))
            {
#if !(__WindowsGTK__ || ENABLE_GTK)
                if (Platform.IsClientPosix && !Platform.IsClientOSX)
                    Console.WriteLine("Warning: this build does not support GTK, rebuild with ENABLE_GTK defined");
#endif
                toolkit = GetDefaultToolKit();
            }
            else
            {
                if (TOOLKIT_WINDOWS_FORMS.Equals(toolkit, StringComparison.OrdinalIgnoreCase))
                    toolkit = TOOLKIT_WINDOWS_FORMS;
#if __WindowsGTK__ || ENABLE_GTK
                else if (TOOLKIT_GTK.Equals(toolkit, StringComparison.OrdinalIgnoreCase))
                    toolkit = TOOLKIT_GTK;
                else if (TOOLKIT_GTK_APP_INDICATOR.Equals(toolkit, StringComparison.OrdinalIgnoreCase))
                    toolkit = TOOLKIT_GTK_APP_INDICATOR;
#endif
                else if (TOOLKIT_COCOA.Equals(toolkit, StringComparison.OrdinalIgnoreCase))
                    toolkit = TOOLKIT_COCOA;
                else if (TOOLKIT_RUMPS.Equals(toolkit, StringComparison.OrdinalIgnoreCase))
                    toolkit = TOOLKIT_RUMPS;
                else
                    toolkit = GetDefaultToolKit();
            }

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
                    return;
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

            StartTray(_args, options, toolkit, hosted, password, saltedpassword);
        }

        private static void StartTray(string[] _args, Dictionary<string, string> options, string toolkit, HostedInstanceKeeper hosted, string password, bool saltedpassword)
        {
            using (hosted)
            {
                var reSpawn = 0;

                do
                {
                    try
                    {
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                        try
                        {
                            //try TLS 1.3 (type not available on .NET < 4.8)
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                        }
                        catch (NotSupportedException)
                        {
                        }

                        using (Connection = new HttpServerConnection(serverURL, password, saltedpassword, databaseConnection != null ? PasswordSource.Database : PasswordSource.HostedServer, disableTrayIconLogin, options))
                        {
                            using (var tk = RunTrayIcon(toolkit))
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

        private static TrayIconBase RunTrayIcon(string toolkit)
        {
            if (toolkit == TOOLKIT_WINDOWS_FORMS)
                return GetWinformsInstance();
#if __WindowsGTK__ || ENABLE_GTK
            else if (toolkit == TOOLKIT_GTK)
                return GetGtkInstance();
            else if (toolkit == TOOLKIT_GTK_APP_INDICATOR)
                return GetAppIndicatorInstance();
#endif
            else if (toolkit == TOOLKIT_COCOA)
                return GetCocoaRunnerInstance();
            else if (toolkit == TOOLKIT_RUMPS)
                return GetRumpsRunnerInstance();
            else 
                throw new UserInformationException(string.Format("The selected toolkit '{0}' is invalid", toolkit), "TrayIconInvalidToolKit");
        }
        
        //We keep these in functions to avoid attempting to load the instance,
        // because the required assemblies may not exist on the machine 
        //
        //They must be non-inlined even if they are prime candidates,
        // as the inlining will pollute the type system and possibly
        // attempt to load non-existing assemblies

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetWinformsInstance() { return new Windows.WinFormsRunner(); }
#if __WindowsGTK__ || ENABLE_GTK
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetGtkInstance() { return new GtkRunner(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetAppIndicatorInstance() { return new AppIndicatorRunner(); }
#endif
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetCocoaRunnerInstance() { return new CocoaRunner(); } 

        private static TrayIconBase GetRumpsRunnerInstance() { return new RumpsRunner(); } 

        //The functions below simply load the requested type,
        // and if the type is not present, calling the function will result in an exception.
        //This seems to be more reliable than attempting to load the assembly,
        // as there are too many complex rules for when an updated assembly is also
        // acceptable. This is fairly error proof, as it is just asks the runtime
        // to load the required types
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryGetGtk()
        {
#if __WindowsGTK__ || ENABLE_GTK
            return typeof(Gtk.StatusIcon) != null && typeof(Gdk.Image) != null;
#else
            return false;
#endif
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryGetWinforms()
        {
            return typeof(System.Windows.Forms.NotifyIcon) != null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryGetAppIndicator()
        {
#if __WindowsGTK__ || ENABLE_GTK
            return typeof(AppIndicator.ApplicationIndicator) != null;
#else
            return false;
#endif
        }
        
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryGetMonoMac()
        {
            return typeof(AppKit.NSApplication) != null;
        }
  
        //The functions below here, simply wrap the call to the above functions,
        // converting the exception to a simple boolean value, so the calling
        // code can be kept free of error handling
        private static bool SupportsGtk
        {
            get 
            {
                try { return TryGetGtk(); }
                catch {}
                
                return false;
            }
        }

        private static bool SupportsAppIndicator
        {
            get 
            {
                try { return TryGetAppIndicator(); }
                catch {}
                
                return false;
            }
        }

        private static bool SupportsCocoa
        {
            get 
            {
                try { return TryGetMonoMac(); }
                catch {}
                
                return false;
            }
        }
        
        private static bool SupportsRumps
        {
            get 
            {
                try { return RumpsRunner.CanRun(); }
                catch {}

                return false;
            }
        }


        private static bool SupportsWinForms
        {
            get 
            {
                try { return TryGetWinforms(); }
                catch {}
                
                return false;
            }
        }

        public static Duplicati.Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                var toolkits = new List<string>();
                if (SupportsWinForms)
                    toolkits.Add(TOOLKIT_WINDOWS_FORMS);
                if (SupportsGtk)
                    toolkits.Add(TOOLKIT_GTK);
                if (SupportsAppIndicator)
                    toolkits.Add(TOOLKIT_GTK_APP_INDICATOR);
                if (SupportsCocoa)
                    toolkits.Add(TOOLKIT_COCOA);
                if (SupportsRumps)
                    toolkits.Add(TOOLKIT_RUMPS);
                
                var args = new List<Duplicati.Library.Interface.ICommandLineArgument>()
                {
                    new Duplicati.Library.Interface.CommandLineArgument(TOOLKIT_OPTION, CommandLineArgument.ArgumentType.Enumeration, "Selects the toolkit to use", "Choose the toolkit used to generate the TrayIcon, note that it will fail if the selected toolkit is not supported on this machine", GetDefaultToolKit(), null, toolkits.ToArray()),
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
