using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        private const string TOOLKIT_RUMPS = "rumps";

        private const string HOSTURL_OPTION = "hosturl";
        private const string NOHOSTEDSERVER_OPTION = "no-hosted-server";
        private const string READCONFIGFROMDB_OPTION = "read-config-from-db";

        private const string BROWSER_COMMAND_OPTION = "browser-command";

        private const string DEFAULT_HOSTURL = "http://localhost:8200";
        
        private static string _browser_command = null;
        public static string BrowserCommand { get { return _browser_command; } }
        public static Server.Database.Connection databaseConnection = null;

        private static string GetDefaultToolKit(bool printwarnings)
        {
            // No longer using Cocoa directly as it fails on 32bit as well            
            if (Duplicati.Library.Utility.Utility.IsClientOSX)
                    return TOOLKIT_RUMPS;

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
                toolkit = GetDefaultToolKit(true);
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
                else if (TOOLKIT_RUMPS.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_RUMPS;
                else
                    toolkit = GetDefaultToolKit(true);
            }

            HostedInstanceKeeper hosted = null;
            var openui = false;
            string password = null;
            var saltedpassword = false;
            var serverURL = new Uri(DEFAULT_HOSTURL);

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
                serverURL = (new UriBuilder(serverURL) { Port = Duplicati.Server.Program.ServerPort }).Uri;
            }
            
            if (Library.Utility.Utility.ParseBoolOption(options, NOHOSTEDSERVER_OPTION) && Library.Utility.Utility.ParseBoolOption(options, READCONFIGFROMDB_OPTION))
                databaseConnection = GetDatabaseConnection(_args);

            string pwd;

            if (options.TryGetValue("webserver-password", out pwd))
            {
                password = pwd;
                saltedpassword = false;
            }

            if (databaseConnection != null)
            {
                password = databaseConnection.ApplicationSettings.WebserverPasswordTrayIcon;
                saltedpassword = false;
            }
            
            if (databaseConnection != null)
                serverURL = (new UriBuilder(serverURL) {Port = databaseConnection.ApplicationSettings.LastWebserverPort}).Uri;

            string url;

            if (options.TryGetValue(HOSTURL_OPTION, out url))
                serverURL = new Uri(url);
            
            using (hosted)
            {
                var reSpawn = 0;

                do
                {
                    try
                    {
                        using (Connection = new HttpServerConnection(serverURL, password, saltedpassword))
                        {
                            using (var tk = RunTrayIcon(toolkit))
                            {
                                if (hosted != null && Server.Program.Instance != null)
                                    Server.Program.Instance.SecondInstanceDetected +=
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
                    catch (WebException ex)
                    {
                        System.Diagnostics.Trace.WriteLine("Request error: " + ex.Message);
                        Console.WriteLine("Request error: " + ex.Message);

                        reSpawn++;
                    }
                } while (reSpawn < 3);
            }
        }

        private static Server.Database.Connection GetDatabaseConnection(string[] args)
        {
            //Find commandline options here for handling special startup cases
            var commandlineOptions = Library.Utility.CommandLineParser.ExtractOptions(new List<string>(args));
            var dbPassword = Environment.GetEnvironmentVariable(Server.Program.DB_KEY_ENV_NAME);

            //If we are on windows we encrypt the database by default
            //We do not encrypt on Linux as most distros use a SQLite library without encryption support,
            //Linux users can use an encrypted home folder, or install a SQLite library with encryption support
            if (!Library.Utility.Utility.IsClientLinux &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Server.Program.DB_KEY_ENV_NAME)))
                    dbPassword = Library.AutoUpdater.AutoUpdateSettings.AppName + "_Key_42";

            if (commandlineOptions.ContainsKey("server-encryption-key"))
                dbPassword = commandlineOptions["server-encryption-key"];

            var serverDataFolder = Environment.GetEnvironmentVariable(Server.Program.DATAFOLDER_ENV_NAME);

            // Allow override of the environment variables from the commandline
            if (commandlineOptions.ContainsKey("server-datafolder"))
                serverDataFolder = commandlineOptions["server-datafolder"];
            
            //Set the %DUPLICATI_HOME% env variable, if it is not already set
            if (string.IsNullOrEmpty(serverDataFolder))
            {
#if DEBUG
                //debug mode uses a lock file located in the app folder
                serverDataFolder = Library.AutoUpdater.UpdaterManager.InstalledBaseDir;
#else
                bool portableMode = commandlineOptions.ContainsKey("portable-mode") ? Library.Utility.Utility.ParseBool(commandlineOptions["portable-mode"], true) : false;

                if (portableMode)
                {
                    //Portable mode uses a data folder in the application home dir
                    serverDataFolder = System.IO.Path.Combine(Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "data");
                    System.IO.Directory.SetCurrentDirectory(Library.AutoUpdater.UpdaterManager.InstalledBaseDir);
                }
                else
                {
                    //Normal release mode uses the systems "Application Data" folder
                    serverDataFolder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName);
                }
#endif
            }
            
            var sqliteVersion = new Version((string)Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType.GetProperty("SQLiteVersion").GetValue(null, null));

            if (sqliteVersion < new Version(3, 6, 3))
            {
                //The official Mono SQLite provider is also broken with less than 3.6.3
                throw new Exception($@"Unsupported version of SQLite detected ({sqliteVersion}), must be 3.6.3 or higher");
            }

            //Create the connection instance
            var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection();

            try
            {
                var databasePath = System.IO.Path.Combine(Library.Utility.Utility.AppendDirSeparator(
                    Library.Utility.Utility.ExpandEnvironmentVariables(serverDataFolder).Trim('"')), "Duplicati-server.sqlite");

                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(databasePath)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(databasePath));
#if DEBUG
                //Default is to not use encryption for debugging
                var useDatabaseEncryption = commandlineOptions.ContainsKey("unencrypted-database") && !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true);
#else
                var useDatabaseEncryption = !commandlineOptions.ContainsKey("unencrypted-database") || !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true);
#endif
                con.ConnectionString = "Data Source=" + databasePath;

                //Attempt to open the database, handling any encryption present
                Server.Program.OpenDatabase(con, useDatabaseEncryption, dbPassword);

                Duplicati.Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, databasePath,
                    typeof(Duplicati.Server.Database.Connection));
            }
            catch (Exception ex)
            {
                //Unwrap the reflection exceptions
                if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                throw new Exception($@"Failed to create, open or upgrade the database. Error message: {ex}");
            }

            return new Duplicati.Server.Database.Connection(con);
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
            else if (toolkit == TOOLKIT_RUMPS)
                return GetRumpsRunnerInstance();
            else 
                throw new UserInformationException(string.Format("The selected toolkit '{0}' is invalid", toolkit));
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
#if __MonoCS__ || __WindowsGTK__
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
#if __MonoCS__ || __WindowsGTK__
            return typeof(AppIndicator.ApplicationIndicator) != null;
#else
            return false;
#endif
        }
        
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TryGetMonoMac()
        {
            return !Environment.Is64BitProcess && typeof(MonoMac.AppKit.NSApplication) != null;
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
                
                return new Duplicati.Library.Interface.ICommandLineArgument[]
                {
                    new Duplicati.Library.Interface.CommandLineArgument(TOOLKIT_OPTION, CommandLineArgument.ArgumentType.Enumeration, "Selects the toolkit to use", "Choose the toolkit used to generate the TrayIcon, note that it will fail if the selected toolkit is not supported on this machine", GetDefaultToolKit(false), null, toolkits.ToArray()),
                    new Duplicati.Library.Interface.CommandLineArgument(HOSTURL_OPTION, CommandLineArgument.ArgumentType.String, "Selects the url to connect to", "Supply the url that the TrayIcon will connect to and show status for", DEFAULT_HOSTURL),
                    new Duplicati.Library.Interface.CommandLineArgument(NOHOSTEDSERVER_OPTION, CommandLineArgument.ArgumentType.String, "Disables local server", "Set this option to not spawn a local service, use if the TrayIcon should connect to a running service"),
                    new Duplicati.Library.Interface.CommandLineArgument(READCONFIGFROMDB_OPTION, CommandLineArgument.ArgumentType.String, "Read server connection info from DB", $"Set this option to read server connection info for running service from its database (only together with {NOHOSTEDSERVER_OPTION})"),               
                    new Duplicati.Library.Interface.CommandLineArgument(BROWSER_COMMAND_OPTION, CommandLineArgument.ArgumentType.String, "Sets the browser comand", "Set this option to override the default browser detection"),
                };
            }
        }
    }
}
