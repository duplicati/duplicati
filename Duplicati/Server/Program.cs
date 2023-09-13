using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;

namespace Duplicati.Server
{
    public class Program
    {

        private static readonly List<string> AlternativeHelpStrings = new List<string> { "help", "/help", "usage", "/usage", "--help" };

        private static readonly List<string> ParameterFileOptionStrings = new List<string> { "parameters-file", "parameterfile" };

        /// <summary>
        /// The log tag for messages from this class
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<Program>();
        /// <summary>
        /// The path to the directory that contains the main executable
        /// </summary>
        public static readonly string StartupPath = Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir;

        /// <summary>
        /// The name of the environment variable that holds the path to the data folder used by Duplicati
        /// </summary>
        private static readonly string DATAFOLDER_ENV_NAME = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName.ToUpper(CultureInfo.InvariantCulture) + "_HOME";

        /// <summary>
        /// The environment variable that holds the database key used to encrypt the SQLite database
        /// </summary>
        private static readonly string DB_KEY_ENV_NAME = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName.ToUpper(CultureInfo.InvariantCulture) + "_DB_KEY";

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DataFolder { get; private set; }

        /// <summary>
        /// The single instance
        /// </summary>
        public static SingleInstance ApplicationInstance = null;

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static Database.Connection DataConnection;

        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static readonly object MainLock = new object();

        /// <summary>
        /// This is the scheduling thread
        /// </summary>
        public static Scheduler Scheduler;

        /// <summary>
        /// This is the working thread
        /// </summary>
        public static Duplicati.Library.Utility.WorkerThread<Runner.IRunnerData> WorkThread;

        /// <summary>
        /// List of completed task results
        /// </summary>
        public static readonly List<KeyValuePair<long, Exception>> TaskResultCache = new List<KeyValuePair<long, Exception>>();

        /// <summary>
        /// The maximum number of completed task results to keep in memory
        /// </summary>
        private static readonly int MAX_TASK_RESULT_CACHE_SIZE = 100;

        /// <summary>
        /// The thread running the ping-pong handler
        /// </summary>
        private static System.Threading.Thread PingPongThread;

        /// <summary>
        /// The path to the file that contains the current database
        /// </summary>
        private static string DatabasePath;

        /// <summary>
        /// The controller interface for pause/resume and throttle options
        /// </summary>
        public static LiveControls LiveControl;

        /// <summary>
        /// The application exit event
        /// </summary>
        public static System.Threading.ManualResetEvent ApplicationExitEvent;

        /// <summary>
        /// The webserver instance
        /// </summary>
        private static WebServer.Server WebServer;

        /// <summary>
        /// The update poll thread.
        /// </summary>
        public static UpdatePollThread UpdatePoller;

        /// <summary>
        /// An event that is set once the server is ready to respond to requests
        /// </summary>
        public static readonly System.Threading.ManualResetEvent ServerStartedEvent = new System.Threading.ManualResetEvent(false);

        /// <summary>
        /// The status event signaler, used to control long polling of status updates
        /// </summary>
        public static readonly EventPollNotify StatusEventNotifyer = new EventPollNotify();

        /// <summary>
        /// A delegate method for creating a copy of the current progress state
        /// </summary>
        public static Func<Duplicati.Server.Serialization.Interface.IProgressEventData> GenerateProgressState;

        /// <summary>
        /// An event ID that increases whenever the database is updated
        /// </summary>
        public static long LastDataUpdateID = 0;

        /// <summary>
        /// An event ID that increases whenever a notification is updated
        /// </summary>
        public static long LastNotificationUpdateID = 0;

        /// <summary>
        /// The log redirect handler
        /// </summary>
        public static readonly LogWriteHandler LogHandler = new LogWriteHandler();

        /// <summary>
        /// Used to check the origin of the web server (e.g. Tray icon or a stand alone Server)
        /// </summary>
        public static string Origin = "Server";

        private static System.Threading.Timer PurgeTempFilesTimer = null;

        public static int ServerPort
        {
            get
            {
                return WebServer.Port;
            }
        }

        public static bool IsFirstRun
        {
            get { return DataConnection.ApplicationSettings.IsFirstRun; }
            set { DataConnection.ApplicationSettings.IsFirstRun = value; }
        }

        public static string StartedBy
        {
            get { return Origin; }
            set { Origin = value; }
        }

        public static bool ServerPortChanged
        {
            get { return DataConnection.ApplicationSettings.ServerPortChanged; }
            set { DataConnection.ApplicationSettings.ServerPortChanged = value; }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args, Duplicati.Library.AutoUpdater.AutoUpdateStrategy.Never);
        }

        public static int RealMain(string[] _args)
        {
            // This is a just a test edit

            //If we are on Windows, append the bundled "win-tools" programs to the search path
            //We add it last, to allow the user to override with other versions
            if (Platform.IsClientWindows)
            {
                Environment.SetEnvironmentVariable("PATH",
                    Environment.GetEnvironmentVariable("PATH") +
                    System.IO.Path.PathSeparator.ToString() +
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "win-tools")
                );
            }

            //If this executable is invoked directly, write to console, otherwise throw exceptions
            var writeToConsole = System.Reflection.Assembly.GetEntryAssembly() == System.Reflection.Assembly.GetExecutingAssembly();

            //Find commandline options here for handling special startup cases
            var args = new List<string>(_args);
            var optionsWithFilter = Library.Utility.FilterCollector.ExtractOptions(new List<string>(args));
            var commandlineOptions = optionsWithFilter.Item1;
            var filter = optionsWithFilter.Item2;

            if (_args.Select(s => s.ToLower()).Intersect(AlternativeHelpStrings.ConvertAll(x => x.ToLower())).Any())
            {
                return ShowHelp(writeToConsole);
            }

            if (commandlineOptions.ContainsKey("tempdir") && !string.IsNullOrEmpty(commandlineOptions["tempdir"]))
            {
                Library.Utility.SystemContextSettings.DefaultTempPath = commandlineOptions["tempdir"];
            }

            Library.Utility.SystemContextSettings.StartSession();

            var parameterFileOption = commandlineOptions.Keys.Select(s => s.ToLower())
                .Intersect(ParameterFileOptionStrings.ConvertAll(x => x.ToLower())).FirstOrDefault();

            if (parameterFileOption != null && !string.IsNullOrEmpty(commandlineOptions[parameterFileOption]))
            {
                string filename = commandlineOptions[parameterFileOption];
                commandlineOptions.Remove(parameterFileOption);
                if (!ReadOptionsFromFile(filename, ref filter, args, commandlineOptions))
                    return 100;
            }

            ConfigureLogging(commandlineOptions);

            try
            {

                DataConnection = GetDatabaseConnection(commandlineOptions);

                if (!DataConnection.ApplicationSettings.FixedInvalidBackupId)
                    DataConnection.FixInvalidBackupId();

                CreateApplicationInstance(writeToConsole);

                StartOrStopUsageReporter();

                AdjustApplicationSettings(commandlineOptions);

                ApplicationExitEvent = new System.Threading.ManualResetEvent(false);

                Library.AutoUpdater.UpdaterManager.OnError += (Exception obj) =>
                {
                    DataConnection.LogError(null, "Error in updater", obj);
                };
                
                UpdatePoller = new UpdatePollThread();

                SetPurgeTempFilesTimer(commandlineOptions);

                SetLiveControls();

                SetWorkerThread();

                StartWebServer(commandlineOptions);

                if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, "ping-pong-keepalive"))
                {
                    PingPongThread = new System.Threading.Thread(PingPongMethod) {IsBackground = true};
                    PingPongThread.Start();
                }

                ServerStartedEvent.Set();
                ApplicationExitEvent.WaitOne();
            }
            catch (SingleInstance.MultipleInstanceException mex)
            {
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                if (!writeToConsole) throw;
                
                Console.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                return 100;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                if (writeToConsole)
                {
                    Console.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                    return 100;
                }
                else
                    throw new Exception(Strings.Program.SeriousError(ex.ToString()), ex);
            }
            finally
            {
                StatusEventNotifyer.SignalNewEvent();

                UpdatePoller?.Terminate();
                Scheduler?.Terminate(true);
                WorkThread?.Terminate(true);
                ApplicationInstance?.Dispose();
                PurgeTempFilesTimer?.Dispose();

                Library.UsageReporter.Reporter.ShutDown();

                try { PingPongThread?.Abort(); }
                catch { }

                LogHandler?.Dispose();
            }

            if (UpdatePoller != null && UpdatePoller.IsUpdateRequested)
                return Library.AutoUpdater.UpdaterManager.MAGIC_EXIT_CODE;

            return 0;
        }

        private static void StartWebServer(Dictionary<string, string> commandlineOptions)
        {
            WebServer = new WebServer.Server(commandlineOptions);

            ServerPortChanged |= WebServer.Port != DataConnection.ApplicationSettings.LastWebserverPort;
            DataConnection.ApplicationSettings.LastWebserverPort = WebServer.Port;
        }

        private static void SetWorkerThread()
        {
            WorkThread = new Duplicati.Library.Utility.WorkerThread<Runner.IRunnerData>((x) => { Runner.Run(x, true); },
                LiveControl.State == LiveControls.LiveControlState.Paused);
            Scheduler = new Scheduler(WorkThread);

            WorkThread.StartingWork += (worker, task) => { SignalNewEvent(null, null); };
            WorkThread.CompletedWork += (worker, task) => { SignalNewEvent(null, null); };
            WorkThread.WorkQueueChanged += (worker) => { SignalNewEvent(null, null); };
            Scheduler.NewSchedule += new EventHandler(SignalNewEvent);
            WorkThread.OnError += (worker, task, exception) =>
            {
                Program.DataConnection.LogError(task?.BackupID, "Error in worker", exception);
            };

            var lastScheduleId = LastDataUpdateID;
            Program.StatusEventNotifyer.NewEvent += (sender, e) =>
            {
                if (lastScheduleId == LastDataUpdateID) return;
                lastScheduleId = LastDataUpdateID;
                Program.Scheduler.Reschedule();
            };

            void RegisterTaskResult(long id, Exception ex)
            {
                lock (MainLock)
                {
                    // If the new results says it crashed, we store that instead of success
                    if (Program.TaskResultCache.Count > 0 && Program.TaskResultCache.Last().Key == id)
                    {
                        if (ex != null && Program.TaskResultCache.Last().Value == null)
                            Program.TaskResultCache.RemoveAt(Program.TaskResultCache.Count - 1);
                        else
                            return;
                    }

                    Program.TaskResultCache.Add(new KeyValuePair<long, Exception>(id, ex));
                    while (Program.TaskResultCache.Count > MAX_TASK_RESULT_CACHE_SIZE)
                        Program.TaskResultCache.RemoveAt(0);
                }
            }

            Program.WorkThread.CompletedWork += (worker, task) => { RegisterTaskResult(task.TaskID, null); };
            Program.WorkThread.OnError += (worker, task, exception) => { RegisterTaskResult(task.TaskID, exception); };
        }

        private static void SetLiveControls()
        {
            LiveControl = new LiveControls(DataConnection.ApplicationSettings);
            LiveControl.StateChanged += LiveControl_StateChanged;
            LiveControl.ThreadPriorityChanged += LiveControl_ThreadPriorityChanged;
            LiveControl.ThrottleSpeedChanged += LiveControl_ThrottleSpeedChanged;
        }

        private static void SetPurgeTempFilesTimer(Dictionary<string, string> commandlineOptions)
        {
            var lastPurge = new DateTime(0);

            System.Threading.TimerCallback purgeTempFilesCallback = (x) =>
            {
                try
                {
#if DEBUG
                    if (Math.Abs((DateTime.Now - lastPurge).TotalHours) < 1)
                    {
                        return;
                    }
#else
                    if (Math.Abs((DateTime.Now - lastPurge).TotalHours) < 23)
                    {
                        return;
                    }
#endif

                    lastPurge = DateTime.Now;

                    foreach (var e in DataConnection.GetTempFiles().Where((f) => f.Expires < DateTime.Now))
                    {
                        try
                        {
                            if (System.IO.File.Exists(e.Path))
                                System.IO.File.Delete(e.Path);
                        }
                        catch (Exception ex)
                        {
                            DataConnection.LogError(null, $"Failed to delete temp file: {e.Path}", ex);
                        }

                        DataConnection.DeleteTempFile(e.ID);
                    }


                    Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) =>
                    {
                        DataConnection.LogError(null, $"Failed to delete temp file: {path}", ex);
                    });

                    if (!commandlineOptions.TryGetValue("log-retention", out string pts))
                    {
                        pts = DEFAULT_LOG_RETENTION;
                    }

                    DataConnection.PurgeLogData(Library.Utility.Timeparser.ParseTimeInterval(pts, DateTime.Now, true));
                }
                catch (Exception ex)
                {
                    DataConnection.LogError(null, "Failed during temp file cleanup", ex);
                }
            };

            try
            {
#if DEBUG
                PurgeTempFilesTimer =
                    new System.Threading.Timer(purgeTempFilesCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromHours(1));
#else
                PurgeTempFilesTimer =
                    new System.Threading.Timer(purgeTempFilesCallback, null, TimeSpan.FromHours(1), TimeSpan.FromDays(1));
#endif
            }
            catch (ArgumentOutOfRangeException)
            {
                //Bugfix for older Mono, slightly more resources used to avoid large values in the period field
                PurgeTempFilesTimer =
                    new System.Threading.Timer(purgeTempFilesCallback, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            }
        }

        private static void AdjustApplicationSettings(Dictionary<string, string> commandlineOptions)
        {
            if (commandlineOptions.ContainsKey("webservice-password"))
            {
                DataConnection.ApplicationSettings.SetWebserverPassword(commandlineOptions["webservice-password"]);
            }

            DataConnection.ApplicationSettings.GenerateWebserverPasswordTrayIcon();

            if (commandlineOptions.ContainsKey("webservice-allowed-hostnames"))
            {
                DataConnection.ApplicationSettings.SetAllowedHostnames(commandlineOptions["webservice-allowed-hostnames"]);
            }
        }

        private static void CreateApplicationInstance(bool writeConsole)
        {
            try
            {
                //This will also create DATAFOLDER if it does not exist
                ApplicationInstance = new SingleInstance(DataFolder);
            }
            catch (Exception ex)
            {
                if (writeConsole)
                {
                    Console.WriteLine(Strings.Program.StartupFailure(ex));
                    Environment.Exit(200);
                }

                throw new Exception(Strings.Program.StartupFailure(ex));
            }

            if (!ApplicationInstance.IsFirstInstance)
            {
                if (writeConsole)
                {
                    Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                    Environment.Exit(200);
                }

                throw new SingleInstance.MultipleInstanceException(Strings.Program.AnotherInstanceDetected);
            }
        }

        private static void ConfigureLogging(Dictionary<string, string> commandlineOptions)
        {

#if DEBUG
            //Log various information in the logfile
            if (!commandlineOptions.ContainsKey("log-file"))
            {
                commandlineOptions["log-file"] = System.IO.Path.Combine(StartupPath, "Duplicati.debug.log");
                commandlineOptions["log-level"] = Duplicati.Library.Logging.LogMessageType.Profiling.ToString();
                if (System.IO.File.Exists(commandlineOptions["log-file"]))
                {
                    System.IO.File.Delete(commandlineOptions["log-file"]);
                }
            }
#endif

            // Setup the log redirect
            Library.Logging.Log.StartScope(LogHandler, null);

            if (commandlineOptions.ContainsKey("log-file"))
            {
                var loglevel = Library.Logging.LogMessageType.Error;

                if (commandlineOptions.ContainsKey("log-level"))
                    Enum.TryParse(commandlineOptions["log-level"], true, out loglevel);

                LogHandler.SetServerFile(commandlineOptions["log-file"], loglevel);
            }
        }

        private static int ShowHelp(bool writeConsole)
        {
            if (writeConsole)
            {
                Console.WriteLine(Strings.Program.HelpDisplayDialog);

                foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                    Console.WriteLine(Strings.Program.HelpDisplayFormat(arg.Name, arg.LongDescription));

                return 0;
            }

            throw new Exception("Server invoked with --help");
        }

        public static Database.Connection GetDatabaseConnection(Dictionary<string, string> commandlineOptions)
        {
            var dbPassword = Environment.GetEnvironmentVariable(DB_KEY_ENV_NAME);

            //If we are on windows we encrypt the database by default
            //We do not encrypt on Linux as most distros use a SQLite library without encryption support,
            //Linux users can use an encrypted home folder, or install a SQLite library with encryption support

            //Note that the password here is a default password and public knowledge
            //
            //The purpose of this is to prevent casual read of the database, as well
            // as protect from harddisk string scans, not to protect from determined
            // attacks.
            //
            //If you desire better security, start Duplicati once with the commandline option
            // --unencrypted-database to decrypt the database.
            //Then set the environment variable DUPLICATI_DB_KEY to the desired key,
            // and run Duplicati again without the --unencrypted-database option
            // to re-encrypt it with the new key
            //
            //If you change the key, please note that you need to supply the same
            // key when restoring the setup, as the setup being backed up will
            // be encrypted as well.
            if (!Platform.IsClientPosix && string.IsNullOrEmpty(dbPassword))
                dbPassword = Library.AutoUpdater.AutoUpdateSettings.AppName + "_Key_42";

            // Allow override of the environment variables from the commandline
            if (commandlineOptions.ContainsKey("server-encryption-key"))
                dbPassword = commandlineOptions["server-encryption-key"];

            var serverDataFolder = Environment.GetEnvironmentVariable(DATAFOLDER_ENV_NAME);
            if (commandlineOptions.ContainsKey("server-datafolder"))
                serverDataFolder = commandlineOptions["server-datafolder"];

            if (string.IsNullOrEmpty(serverDataFolder))
            {
#if DEBUG
                //debug mode uses a lock file located in the app folder
                DataFolder = StartupPath;
#else
                bool portableMode = commandlineOptions.ContainsKey("portable-mode") ? Library.Utility.Utility.ParseBool(commandlineOptions["portable-mode"], true) : false;

                if (portableMode)
                {
                    //Portable mode uses a data folder in the application home dir
                    DataFolder = System.IO.Path.Combine(StartupPath, "data");
                    System.IO.Directory.SetCurrentDirectory(StartupPath);
                }
                else
                {
                    //Normal release mode uses the systems "(Local) Application Data" folder
                    // %LOCALAPPDATA% on Windows, ~/.config on Linux

                    // Special handling for Windows:
                    //   - Older versions use %APPDATA%
                    //   - but new versions use %LOCALAPPDATA%
                    //
                    //  If we find a new version, lets use that
                    //    otherwise use the older location
                    //

                    serverDataFolder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName);
                    if (Platform.IsClientWindows)
                    {
                        var localappdata = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName);

                        var prefile = System.IO.Path.Combine(serverDataFolder, "Duplicati-server.sqlite");
                        var curfile = System.IO.Path.Combine(localappdata, "Duplicati-server.sqlite");

                        // If the new file exists, we use that
                        // If the new file does not exist, and the old file exists we use the old
                        // Otherwise we use the new location
                        if (System.IO.File.Exists(curfile) || !System.IO.File.Exists(prefile))
                            serverDataFolder = localappdata;
                    }

                    DataFolder = serverDataFolder;
                }
#endif
            }
            else
                DataFolder = Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(serverDataFolder).Trim('"'));

            var sqliteVersion = new Version((string)Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType.GetProperty("SQLiteVersion").GetValue(null, null));

            if (sqliteVersion < new Version(3, 6, 3))
            {
                //The official Mono SQLite provider is also broken with less than 3.6.3
                throw new Exception(Strings.Program.WrongSQLiteVersion(sqliteVersion, "3.6.3"));
            }

            //Create the connection instance
            var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection();

            try
            {
                DatabasePath = System.IO.Path.Combine(DataFolder, "Duplicati-server.sqlite");

                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(DatabasePath)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath));
#if DEBUG
                //Default is to not use encryption for debugging
                var useDatabaseEncryption = commandlineOptions.ContainsKey("unencrypted-database") && !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true);
#else
                var useDatabaseEncryption = !commandlineOptions.ContainsKey("unencrypted-database") || !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true);
#endif

                //Attempt to open the database, handling any encryption present
                Duplicati.Library.SQLiteHelper.SQLiteLoader.OpenDatabase(con, DatabasePath, useDatabaseEncryption, dbPassword);

                Duplicati.Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, DatabasePath, typeof(Database.Connection));
            }
            catch (Exception ex)
            {
                //Unwrap the reflection exceptions
                if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                throw new Exception(Strings.Program.DatabaseOpenError(ex.Message));
            }

            return new Database.Connection(con);
        }

        public static void StartOrStopUsageReporter()
        {
            var disableUsageReporter =
                string.Equals(DataConnection.ApplicationSettings.UsageReporterLevel, "none", StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(DataConnection.ApplicationSettings.UsageReporterLevel, "disabled", StringComparison.OrdinalIgnoreCase);

            Library.UsageReporter.ReportType reportLevel;
            if (!Enum.TryParse<Library.UsageReporter.ReportType>(DataConnection.ApplicationSettings.UsageReporterLevel, true, out reportLevel))
                Library.UsageReporter.Reporter.SetReportLevel(null, disableUsageReporter);
            else
                Library.UsageReporter.Reporter.SetReportLevel(reportLevel, disableUsageReporter);
        }

        public static void UpdateThrottleSpeeds()
        {
            if (Program.WorkThread == null)
                return;

            var cur = Program.WorkThread.CurrentTask;
            if (cur != null)
                cur.UpdateThrottleSpeed();
        }

        private static void SignalNewEvent(object sender, EventArgs e)
        {
            StatusEventNotifyer.SignalNewEvent();
        }


        /// <summary>
        /// Handles a change in the LiveControl and updates the Runner
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void LiveControl_ThreadPriorityChanged(object sender, EventArgs e)
        {
            StatusEventNotifyer.SignalNewEvent();
        }

        /// <summary>
        /// Handles a change in the LiveControl and updates the Runner
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void LiveControl_ThrottleSpeedChanged(object sender, EventArgs e)
        {
            StatusEventNotifyer.SignalNewEvent();
        }

        /// <summary>
        /// This event handler updates the trayicon menu with the current state of the runner.
        /// </summary>
        static void LiveControl_StateChanged(object sender, EventArgs e)
        {
            switch (LiveControl.State)
            {
                case LiveControls.LiveControlState.Paused:
                    {
                        WorkThread.Pause();
                        var t = WorkThread.CurrentTask;
                        t?.Pause();
                        break;
                    }
                case LiveControls.LiveControlState.Running:
                    {
                        WorkThread.Resume();
                        var t = WorkThread.CurrentTask;
                        t?.Resume();
                        break;
                    }
            }

            StatusEventNotifyer.SignalNewEvent();
        }
               
        /// <summary>
        /// Simple method for tracking if the server has crashed
        /// </summary>
        private static void PingPongMethod()
        {
            var rd = new System.IO.StreamReader(Console.OpenStandardInput());
            var wr = new System.IO.StreamWriter(Console.OpenStandardOutput());
            string line;
            while ((line = rd.ReadLine()) != null)
            {
                if (string.Equals("shutdown", line, StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: All calls to ApplicationExitEvent and TrayIcon->Quit
                    // should check if we are running something
                    ApplicationExitEvent.Set();
                }
                else
                {
                    wr.WriteLine("pong");
                    wr.Flush();
                }
            }
        }

        /// <summary>
        /// The default log retention
        /// </summary>
        private static readonly string DEFAULT_LOG_RETENTION = "30D";

        /// <summary>
        /// Gets a list of all supported commandline options
        /// </summary>
        public static Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                var lst = new List<Duplicati.Library.Interface.ICommandLineArgument> (new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument("tempdir", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.TempdirShort, Strings.Program.TempdirLong, System.IO.Path.GetTempPath()),
                    new Duplicati.Library.Interface.CommandLineArgument("help", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.HelpCommandDescription, Strings.Program.HelpCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong2, "", new string[] {"parameter-file", "parameterfile"}),
                    new Duplicati.Library.Interface.CommandLineArgument("unencrypted-database", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.UnencrypteddatabaseCommandDescription, Strings.Program.UnencrypteddatabaseCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("portable-mode", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PortablemodeCommandDescription, Strings.Program.PortablemodeCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.LogfileCommandDescription, Strings.Program.LogfileCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LoglevelCommandDescription, Strings.Program.LoglevelCommandDescription, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_WEBROOT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverWebrootDescription, Strings.Program.WebserverWebrootDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_WEBROOT),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_PORT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverPortDescription, Strings.Program.WebserverPortDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_PORT.ToString()),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_SSLCERTIFICATEFILE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificateFileDescription, Strings.Program.WebserverCertificateFileDescription, Duplicati.Server.WebServer.Server.OPTION_SSLCERTIFICATEFILE),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_SSLCERTIFICATEFILEPASSWORD, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificatePasswordDescription, Strings.Program.WebserverCertificatePasswordDescription, Duplicati.Server.WebServer.Server.OPTION_SSLCERTIFICATEFILEPASSWORD),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_INTERFACE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverInterfaceDescription, Strings.Program.WebserverInterfaceDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_INTERFACE),
                    new Duplicati.Library.Interface.CommandLineArgument("webservice-password", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.WebserverPasswordDescription, Strings.Program.WebserverPasswordDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("webservice-allowed-hostnames", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverAllowedhostnamesDescription, Strings.Program.WebserverAllowedhostnamesDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("ping-pong-keepalive", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PingpongkeepaliveShort, Strings.Program.PingpongkeepaliveLong),
                    new Duplicati.Library.Interface.CommandLineArgument("log-retention", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.Program.LogretentionShort, Strings.Program.LogretentionLong, DEFAULT_LOG_RETENTION),
                    new Duplicati.Library.Interface.CommandLineArgument("server-datafolder", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ServerdatafolderShort, Strings.Program.ServerdatafolderLong(DATAFOLDER_ENV_NAME), System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName)),

                });

                if (!Platform.IsClientPosix)
                    lst.Add(new Duplicati.Library.Interface.CommandLineArgument("server-encryption-key", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.ServerencryptionkeyShort, Strings.Program.ServerencryptionkeyLong(DB_KEY_ENV_NAME, "unencrypted-database"), Library.AutoUpdater.AutoUpdateSettings.AppName + "_Key_42"));

                return lst.ToArray();
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref Library.Utility.IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Environment.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var newsource = new List<string>();
                string newtarget = null;
                string prependfilter = null;
                string appendfilter = null;
                string replacefilter = null;

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs, (key, value) => {
                    if (key.Equals("source", StringComparison.OrdinalIgnoreCase))
                    {
                        newsource.Add(value);
                        return false;
                    }
                    else if (key.Equals("target", StringComparison.OrdinalIgnoreCase))
                    {
                        newtarget = value;
                        return false;
                    }
                    else if (key.Equals("append-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        appendfilter = value;
                        return false;
                    }
                    else if (key.Equals("prepend-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        prependfilter = value;
                        return false;
                    }
                    else if (key.Equals("replace-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        replacefilter = value;
                        return false;
                    }

                    return true;
                });

                var opt = tmpparsed.Item1;
                var newfilter = tmpparsed.Item2;

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!filter.Empty && !newfilter.Empty)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.Program.FiltersCannotBeUsedWithFileError2, "FiltersCannotBeUsedOnCommandLineAndInParameterFile");

                if (!newfilter.Empty)
                    filter = newfilter;

                if (!string.IsNullOrWhiteSpace(prependfilter))
                    filter = Library.Utility.FilterExpression.Combine(Library.Utility.FilterExpression.Deserialize(prependfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)), filter);

                if (!string.IsNullOrWhiteSpace(appendfilter))
                    filter = Library.Utility.FilterExpression.Combine(filter, Library.Utility.FilterExpression.Deserialize(appendfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)));

                if (!string.IsNullOrWhiteSpace(replacefilter))
                    filter = Library.Utility.FilterExpression.Deserialize(replacefilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));

                foreach (KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;

                if (!string.IsNullOrEmpty(newtarget))
                {
                    if (cargs.Count <= 1)
                        cargs.Add(newtarget);
                    else
                        cargs[1] = newtarget;
                }

                if (cargs.Count >= 1 && cargs[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
                    cargs.AddRange(newsource);
                else if (newsource.Count > 0)
                    Library.Logging.Log.WriteVerboseMessage(LOGTAG, "NotUsingBackupSources", Strings.Program.SkippingSourceArgumentsOnNonBackupOperation);

                return true;
            }
            catch (Exception e)
            {
                throw new Exception(Strings.Program.FailedToParseParametersFileError(filename, e.Message));
            }
        }
    }
}
