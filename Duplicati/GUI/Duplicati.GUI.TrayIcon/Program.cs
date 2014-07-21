using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Duplicati.Library.Interface;

namespace Duplicati.GUI.TrayIcon
{
    public static class Program
    {
        public static HttpServerConnection Connection;
    
        private const string TOOLKIT_OPTION = "toolkit";
        private const string TOOLKIT_WINDOWS_FORMS = "winforms";
        private const string TOOLKIT_GTK = "gtk";
        private const string TOOLKIT_GTK_APP_INDICATOR = "gtk-appindicator";
        private const string TOOLKIT_COCOA = "cocoa";

        private const string HOSTURL_OPTION = "hosturl";
        private const string NOHOSTEDSERVER_OPTION = "no-hosted-server";
        
        private const string BROWSER_COMMAND_OPTION = "browser-command";

        private const string DEFAULT_HOSTURL = "http://localhost:8080";
        
        private static string _browser_command = null;
        public static string BrowserCommand { get { return _browser_command; } }
        
        
        private static string GetDefaultToolKit()
        {
            if (Duplicati.Library.Utility.Utility.IsClientOSX)
                return TOOLKIT_COCOA;

#if __MonoCS__ || __WindowsGTK__            
            if (Duplicati.Library.Utility.Utility.IsClientLinux)
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

            foreach (string s in args)
                if (
                    s.Equals("help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("usage", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/usage", StringComparison.InvariantCultureIgnoreCase))
                    options["help"] = "";

            if (options.ContainsKey("help"))
            {
                Console.WriteLine("Supported commandline arguments:");
                Console.WriteLine();

                foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                Console.WriteLine("Additionally, these server options are also supported:");
                Console.WriteLine();

                foreach (Library.Interface.ICommandLineArgument arg in Duplicati.Server.Program.SupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                return;
            }            
            
            options.TryGetValue(BROWSER_COMMAND_OPTION, out _browser_command);
            
            string toolkit;
            if (!options.TryGetValue(TOOLKIT_OPTION, out toolkit))
                toolkit = GetDefaultToolKit();
            else 
            {
                if (TOOLKIT_WINDOWS_FORMS.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_WINDOWS_FORMS;
#if __MonoCS__ || __WindowsGTK__                
                else if (TOOLKIT_GTK.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_GTK;
                else if (TOOLKIT_GTK_APP_INDICATOR.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_GTK_APP_INDICATOR;
#endif
                else if (TOOLKIT_COCOA.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_COCOA;
                else
                    toolkit = GetDefaultToolKit();
            }

            HostedInstanceKeeper hosted = null;
            bool openui = false;
            string password = null;
            bool saltedpassword = false;
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
            }

            using (hosted)
            {
                string url;
                if (!options.TryGetValue(HOSTURL_OPTION, out url))
                {
                    if (hosted == null)
                    {
                        url = DEFAULT_HOSTURL;
                    }
                    else
                    {
                        int port = Duplicati.Server.Program.ServerPort;
                        url = "http://127.0.0.1:" + port;
                    }
                }

                string pwd;
                if (options.TryGetValue("webserver-password", out pwd))
                {
                    password = pwd;
                    saltedpassword = false;
                }

                using (Connection = new HttpServerConnection(new Uri(url), password, saltedpassword))
                {
                    using(var tk = RunTrayIcon(toolkit))
                    {
                        if (hosted != null && Server.Program.Instance != null)
                            Server.Program.Instance.SecondInstanceDetected += new Server.SingleInstance.SecondInstanceDelegate(x => { tk.ShowUrlInWindow(url); });
                        
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
                            tk.InvokeExit();
                        };

                        if (hosted != null)
                            hosted.InstanceShutdown += shutdownEvent;

                        tk.Init(_args);

                        // Make sure that the server shutdown does not access the tray-icon,
                        // as it would be disposed by now
                        if (hosted != null)
                            hosted.InstanceShutdown -= shutdownEvent;
                    }
                }
            }
        }
  
        private static TrayIconBase RunTrayIcon(string toolkit)
        {
            if (toolkit == TOOLKIT_WINDOWS_FORMS)
                return GetWinformsInstance();
#if __MonoCS__ || __WindowsGTK__
            else if (toolkit == TOOLKIT_GTK)
                return GetGtkInstance();
            else if (toolkit == TOOLKIT_GTK_APP_INDICATOR)
                return GetAppIndicatorInstance();
#endif
            else if (toolkit == TOOLKIT_COCOA)
                return GetCocoaRunnerInstance();
            else 
                throw new Exception(string.Format("The selected toolkit '{0}' is invalid", toolkit));
        }
        
        //We keep these in functions to avoid attempting to load the instance,
        // because the required assemblies may not exist on the machine 
        //
        //They must be non-inlined even if they are prime candidates,
        // as the inlining will pollute the type system and possibly
        // attempt to load non-existing assemblies

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetWinformsInstance() { return new Windows.WinFormsRunner(); }
#if __MonoCS__ || __WindowsGTK__
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetGtkInstance() { return new GtkRunner(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetAppIndicatorInstance() { return new AppIndicatorRunner(); }
#endif
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static TrayIconBase GetCocoaRunnerInstance() { return new CocoaRunner(); } 

        //The functions below simply load the requested type,
        // and if the type is not present, calling the function will result in an exception.
        //This seems to be more reliable than attempting to load the assembly,
        // as there are too many complex rules for when an updated assembly is also
        // acceptable. This is fairly error proof, as it is just asks the runtime
        // to load the required types
        private static bool TryGetGtk()
        {
#if __MonoCS__ || __WindowsGTK__
            return typeof(Gtk.StatusIcon) != null && typeof(Gdk.Image) != null;
#else
            return false;
#endif
        }

        private static bool TryGetWinforms()
        {
            return typeof(System.Windows.Forms.NotifyIcon) != null;
        }

        private static bool TryGetAppIndicator()
        {
#if __MonoCS__ || __WindowsGTK__
            return typeof(AppIndicator.ApplicationIndicator) != null;
#else
            return false;
#endif
        }
        
        private static bool TryGetMonoMac()
        {
            return typeof(MonoMac.AppKit.NSApplication) != null;
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

        private static bool SupportsCocoaStatusIcon
        {
            get 
            {
                try { return TryGetMonoMac(); }
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
                if (SupportsCocoaStatusIcon)
                    toolkits.Add(TOOLKIT_COCOA);
                
                return new Duplicati.Library.Interface.ICommandLineArgument[]
                {
                    new Duplicati.Library.Interface.CommandLineArgument(TOOLKIT_OPTION, CommandLineArgument.ArgumentType.Enumeration, "Selects the toolkit to use", "Choose the toolkit used to generate the TrayIcon, note that it will fail if the selected toolkit is not supported on this machine", GetDefaultToolKit(), null, toolkits.ToArray()),
                    new Duplicati.Library.Interface.CommandLineArgument(HOSTURL_OPTION, CommandLineArgument.ArgumentType.String, "Selects the url to connect to", "Supply the url that the TrayIcon will connect to and show status for", DEFAULT_HOSTURL),
                    new Duplicati.Library.Interface.CommandLineArgument(NOHOSTEDSERVER_OPTION, CommandLineArgument.ArgumentType.String, "Disables local server", "Set this option to not spawn a local service, use if the TrayIcon should connect to a running service"),
                    new Duplicati.Library.Interface.CommandLineArgument(BROWSER_COMMAND_OPTION, CommandLineArgument.ArgumentType.String, "Sets the browser comand", "Set this option to override the default browser detection"),
                };
            }
        }
    }
}
