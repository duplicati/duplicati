using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server
{
    public class Program
    {
        /// <summary>
        /// The path to the directory that contains the main executable
        /// </summary>
        public static readonly string StartupPath = Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir;

        /// <summary>
        /// The name of the environment variable that holds the path to the data folder used by Duplicati
        /// </summary>
        public static readonly string DATAFOLDER_ENV_NAME = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName.ToUpper() + "_HOME";

        /// <summary>
        /// The environment variable that holdes the database key used to encrypt the SQLite database
        /// </summary>
        public static readonly string DB_KEY_ENV_NAME = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName.ToUpper() + "_DB_KEY";

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DATAFOLDER { get { return Library.Utility.Utility.AppendDirSeparator(Library.Utility.Utility.ExpandEnvironmentVariables("%" + DATAFOLDER_ENV_NAME + "%").TrimStart('"').TrimEnd('"')); } }

        /// <summary>
        /// The single instance
        /// </summary>
        public static SingleInstance Instance = null;

        /// <summary>
        /// A flag indicating if database encryption is in use
        /// </summary>
        public static bool UseDatabaseEncryption;

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static Database.Connection DataConnection;

        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static object MainLock = new object();

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
        public static List<KeyValuePair<long, Exception>> TaskResultCache = new List<KeyValuePair<long, Exception>>();

        /// <summary>
        /// The maximum number of completed task results to keep in memory
        /// </summary>
        private static int MAX_TASK_RESULT_CACHE_SIZE = 100;

        /// <summary>
        /// The thread running the ping-pong handler
        /// </summary>
        public static System.Threading.Thread PingPongThread;

        /// <summary>
        /// The path to the file that contains the current database
        /// </summary>
        public static string DatabasePath;

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
        public static WebServer.Server WebServer;

        /// <summary>
        /// The update poll thread.
        /// </summary>
        public static UpdatePollThread UpdatePoller;
        
        /// <summary>
        /// An event that is set once the server is ready to respond to requests
        /// </summary>
        public static System.Threading.ManualResetEvent ServerStartedEvent = new System.Threading.ManualResetEvent(false);

        /// <summary>
        /// The status event signaler, used to controll long polling of status updates
        /// </summary>
        public static EventPollNotify StatusEventNotifyer = new EventPollNotify();
        
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
        public static LogWriteHandler LogHandler = new LogWriteHandler();

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

        public static void RealMain(string[] args)
        {
            //If we are on Windows, append the bundled "win-tools" programs to the search path
            //We add it last, to allow the user to override with other versions
            if (!Library.Utility.Utility.IsClientLinux)
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
            bool writeConsole = System.Reflection.Assembly.GetEntryAssembly() == System.Reflection.Assembly.GetExecutingAssembly();

            //If we are on windows we encrypt the database by default
            //We do not encrypt on Linux as most distros use a SQLite library without encryption support,
            //Linux users can use an encrypted home folder, or install a SQLite library with encryption support
            if (!Library.Utility.Utility.IsClientLinux && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DB_KEY_ENV_NAME)))
            {
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
                Environment.SetEnvironmentVariable(DB_KEY_ENV_NAME, Library.AutoUpdater.AutoUpdateSettings.AppName + "_Key_42");
            }


            //Find commandline options here for handling special startup cases
            Dictionary<string, string> commandlineOptions = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(new List<string>(args));

            foreach(string s in args)
                if (
                    s.Equals("help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("usage", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/usage", StringComparison.InvariantCultureIgnoreCase))
                    commandlineOptions["help"] = "";

            //If the commandline issues --help, just stop here
            if (commandlineOptions.ContainsKey("help"))
            {
                if (writeConsole)
                {
                    Console.WriteLine(Strings.Program.HelpDisplayDialog);

                    foreach(Library.Interface.ICommandLineArgument arg in SupportedCommands)
                        Console.WriteLine(Strings.Program.HelpDisplayFormat(arg.Name, arg.LongDescription));

                    return;
                }
                else
                {
                    throw new Exception("Server invoked with --help");
                }

            }

#if DEBUG
            //Log various information in the logfile
            if (!commandlineOptions.ContainsKey("log-file"))
            {
                commandlineOptions["log-file"] = System.IO.Path.Combine(StartupPath, "Duplicati.debug.log");
                commandlineOptions["log-level"] = Duplicati.Library.Logging.LogMessageType.Profiling.ToString();
            }
#endif
            // Allow override of the environment variables from the commandline
            if (commandlineOptions.ContainsKey("server-datafolder"))
                Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, commandlineOptions["server-datafolder"]);
            if (commandlineOptions.ContainsKey("server-encryption-key"))
                Environment.SetEnvironmentVariable(DB_KEY_ENV_NAME, commandlineOptions["server-encryption-key"]);

            //Set the %DUPLICATI_HOME% env variable, if it is not already set
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DATAFOLDER_ENV_NAME)))
            {
#if DEBUG
                //debug mode uses a lock file located in the app folder
                Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, StartupPath);
#else
                bool portableMode = commandlineOptions.ContainsKey("portable-mode") ? Library.Utility.Utility.ParseBool(commandlineOptions["portable-mode"], true) : false;

                if (portableMode)
                {
                    //Portable mode uses a data folder in the application home dir
                    Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, System.IO.Path.Combine(StartupPath, "data"));
                }
                else
                {
                    //Normal release mode uses the systems "Application Data" folder
                    Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName));
                }
#endif
            }

            try
            {
                try
                {
                    //This will also create Program.DATAFOLDER if it does not exist
                    Instance = new SingleInstance(Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName, Program.DATAFOLDER);
                }
                catch (Exception ex)
                {
                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.StartupFailure(ex));
                        return;
                    }
                    else
                    {
                        throw new Exception(Strings.Program.StartupFailure(ex));
                    }
                }

                if (!Instance.IsFirstInstance)
                {
                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                        return;
                    }
                    else
                    {
                        throw new SingleInstance.MultipleInstanceException(Strings.Program.AnotherInstanceDetected);
                    }
                }

                // Setup the log redirect
                Duplicati.Library.Logging.Log.CurrentLog = Program.LogHandler;

                if (commandlineOptions.ContainsKey("log-file"))
                {
                    if (System.IO.File.Exists(commandlineOptions["log-file"]))
                        System.IO.File.Delete(commandlineOptions["log-file"]);

                    var loglevel = Duplicati.Library.Logging.LogMessageType.Error;

                    if (commandlineOptions.ContainsKey("log-level"))
                        Enum.TryParse<Duplicati.Library.Logging.LogMessageType>(commandlineOptions["log-level"], true, out loglevel);

                    Program.LogHandler.SetServerFile(commandlineOptions["log-file"], loglevel); 
                }

                Version sqliteVersion = new Version((string)Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType.GetProperty("SQLiteVersion").GetValue(null, null));
                if (sqliteVersion < new Version(3, 6, 3))
                {
                    if (writeConsole)
                    {
                        //The official Mono SQLite provider is also broken with less than 3.6.3
                        Console.WriteLine(Strings.Program.WrongSQLiteVersion(sqliteVersion, "3.6.3"));
                        return;
                    }
                    else
                    {
                        throw new Exception(Strings.Program.WrongSQLiteVersion(sqliteVersion, "3.6.3"));
                    }
                }

                //Create the connection instance
                System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType);

                try
                {
                    DatabasePath = System.IO.Path.Combine(Program.DATAFOLDER, "Duplicati-server.sqlite");
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(DatabasePath)))
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath));

#if DEBUG
                    //Default is to not use encryption for debugging
                    Program.UseDatabaseEncryption = commandlineOptions.ContainsKey("unencrypted-database") ? !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true) : false;
#else
                    Program.UseDatabaseEncryption = commandlineOptions.ContainsKey("unencrypted-database") ? !Library.Utility.Utility.ParseBool(commandlineOptions["unencrypted-database"], true) : true;
#endif
                    con.ConnectionString = "Data Source=" + DatabasePath;

                    //Attempt to open the database, handling any encryption present
                    OpenDatabase(con);

                    Duplicati.Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, DatabasePath, typeof(Duplicati.Server.Database.Connection));
                }
                catch (Exception ex)
                {
                    //Unwrap the reflection exceptions
                    if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                        ex = ex.InnerException;

                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.DatabaseOpenError(ex.Message));
                        return;
                    }
                    else
                    {
                        throw new Exception(Strings.Program.DatabaseOpenError(ex.Message), ex);
                    }
                }

                DataConnection = new Duplicati.Server.Database.Connection(con);

                if (!DataConnection.ApplicationSettings.FixedInvalidBackupId)
                    DataConnection.FixInvalidBackupId();

                StartOrStopUsageReporter();

                if (commandlineOptions.ContainsKey("webservice-password"))
                    Program.DataConnection.ApplicationSettings.SetWebserverPassword(commandlineOptions["webservice-password"]);

                ApplicationExitEvent = new System.Threading.ManualResetEvent(false);
                    
                Duplicati.Library.AutoUpdater.UpdaterManager.OnError += (Exception obj) =>
                {
                    Program.DataConnection.LogError(null, "Error in updater", obj);
                };


                UpdatePoller = new UpdatePollThread();
                DateTime lastPurge = new DateTime(0);

                System.Threading.TimerCallback purgeTempFilesCallback = (x) => {
                    try
                    {
                        if (Math.Abs((DateTime.Now - lastPurge).TotalHours) < 23)
                            return;
                        
                        lastPurge = DateTime.Now;

                        foreach(var e in Program.DataConnection.GetTempFiles().Where((f) => f.Expires < DateTime.Now))
                        {
                            try 
                            { 
                                if (System.IO.File.Exists(e.Path))
                                    System.IO.File.Delete(e.Path);
                            }
                            catch (Exception ex)
                            {
                                Program.DataConnection.LogError(null, string.Format("Failed to delete temp file: {0}", e.Path), ex); 
                            }

                            Program.DataConnection.DeleteTempFile(e.ID);                                
                        }


                        Duplicati.Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) => {
                            Program.DataConnection.LogError(null, string.Format("Failed to delete temp file: {0}", path), ex); 
                        });

                        string pts;
                        if (!commandlineOptions.TryGetValue("log-retention", out pts))
                            pts = DEFAULT_LOG_RETENTION;

                        Program.DataConnection.PurgeLogData(Library.Utility.Timeparser.ParseTimeInterval(pts, DateTime.Now, true));
                    }
                    catch (Exception ex)
                    {
                        Program.DataConnection.LogError(null, "Failed during temp file cleanup", ex); 
                    }
                };

                try 
                {
                    PurgeTempFilesTimer = new System.Threading.Timer(purgeTempFilesCallback, null, TimeSpan.FromHours(1), TimeSpan.FromDays(1));
                } 
                catch (ArgumentOutOfRangeException)
                {
                    //Bugfix for older Mono, slightly more resources used to avoid large values in the period field
                    PurgeTempFilesTimer = new System.Threading.Timer(purgeTempFilesCallback, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
                }
                    
                LiveControl = new LiveControls(DataConnection.ApplicationSettings);
                LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
                LiveControl.ThreadPriorityChanged += new EventHandler(LiveControl_ThreadPriorityChanged);
                LiveControl.ThrottleSpeedChanged += new EventHandler(LiveControl_ThrottleSpeedChanged);

                Program.WorkThread = new Duplicati.Library.Utility.WorkerThread<Runner.IRunnerData>((x) =>
                {
                    Runner.Run(x, true);
                }, LiveControl.State == LiveControls.LiveControlState.Paused);
                Program.Scheduler = new Scheduler(WorkThread);

                Program.WorkThread.StartingWork += (worker, task) => { SignalNewEvent(null, null); };
                Program.WorkThread.CompletedWork += (worker, task) => { SignalNewEvent(null, null); };
                Program.WorkThread.WorkQueueChanged += (worker) => { SignalNewEvent(null, null); };
                Program.Scheduler.NewSchedule += new EventHandler(SignalNewEvent);
                Program.WorkThread.OnError += (worker, task, exception) => { Program.DataConnection.LogError(task == null ? null : task.BackupID, "Error in worker", exception); };

                Action<long, Exception> registerTaskResult = (id, ex) => {
                    lock(Program.MainLock) {
                        
                        // If the new results says it crashed, we store that instead of success
                        if (Program.TaskResultCache.Count > 0 && Program.TaskResultCache.Last().Key == id)
                        {
                            if (ex != null && Program.TaskResultCache.Last().Value == null)
                                Program.TaskResultCache.RemoveAt(Program.TaskResultCache.Count - 1);
                            else
                                return;
                        }
                        
                        Program.TaskResultCache.Add(new KeyValuePair<long, Exception>(id, ex));
                        while(Program.TaskResultCache.Count > MAX_TASK_RESULT_CACHE_SIZE)
                            Program.TaskResultCache.RemoveAt(0);
                    }
                };

                Program.WorkThread.CompletedWork += (worker, task) => { registerTaskResult(task.TaskID, null); };
                Program.WorkThread.OnError += (worker, task, exception) => { registerTaskResult(task.TaskID, exception); };


                Program.WebServer = new WebServer.Server(commandlineOptions);

                if (Program.WebServer.Port != DataConnection.ApplicationSettings.LastWebserverPort)
                    ServerPortChanged = true;
                DataConnection.ApplicationSettings.LastWebserverPort = Program.WebServer.Port;

                if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, "ping-pong-keepalive"))
                {
                    Program.PingPongThread = new System.Threading.Thread(PingPongMethod);
                    Program.PingPongThread.IsBackground = true;
                    Program.PingPongThread.Start();
                }

                ServerStartedEvent.Set();
                ApplicationExitEvent.WaitOne();
            }
            catch (SingleInstance.MultipleInstanceException mex)
            {
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                if (writeConsole)
                    Console.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                else
                    throw mex;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                if (writeConsole)
                    Console.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                else
                    throw new Exception(Strings.Program.SeriousError(ex.ToString()), ex);
            }
            finally
            {
                StatusEventNotifyer.SignalNewEvent();

                if (UpdatePoller != null)
                    UpdatePoller.Terminate();
                if (Scheduler != null)
                    Scheduler.Terminate(true);
                if (WorkThread != null)
                    WorkThread.Terminate(true);
                if (Instance != null)
                    Instance.Dispose();
                if (PurgeTempFilesTimer != null)
                    PurgeTempFilesTimer.Dispose();

                if (PingPongThread != null)
                    try { PingPongThread.Abort(); }
                    catch { }

                if (LogHandler != null)
                    LogHandler.Dispose();

            }
        }

        public static void StartOrStopUsageReporter()
        {
            var disableUsageReporter = 
                string.Equals(DataConnection.ApplicationSettings.UsageReporterLevel, "none", StringComparison.InvariantCultureIgnoreCase)
                ||
                string.Equals(DataConnection.ApplicationSettings.UsageReporterLevel, "disabled", StringComparison.InvariantCultureIgnoreCase);

            Library.UsageReporter.ReportType reportLevel;
            if (!Enum.TryParse<Library.UsageReporter.ReportType>(DataConnection.ApplicationSettings.UsageReporterLevel, true, out reportLevel))                    
                Library.UsageReporter.Reporter.SetReportLevel(null, disableUsageReporter);
            else
                Library.UsageReporter.Reporter.SetReportLevel(reportLevel, disableUsageReporter);
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
                        if (t != null)
                            t.Pause();
                        break;
                    }
                case LiveControls.LiveControlState.Running:
                    {
                        WorkThread.Resume();
                        var t = WorkThread.CurrentTask;
                        if (t != null)
                            t.Resume();
                        break;
                    }
            }

            StatusEventNotifyer.SignalNewEvent();
        }

        /// <summary>
        /// Returns a localized name for a task type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string LocalizeTaskType(Duplicati.Server.Serialization.DuplicatiOperation type)
        {
            switch (type)
            {
                case Duplicati.Server.Serialization.DuplicatiOperation.Backup:
                    return Strings.TaskType.FullBackup;
                case Duplicati.Server.Serialization.DuplicatiOperation.List:
                    return Strings.TaskType.IncrementalBackup;
                case Duplicati.Server.Serialization.DuplicatiOperation.Remove:
                    return Strings.TaskType.ListActualFiles;
                case Duplicati.Server.Serialization.DuplicatiOperation.Verify:
                    return Strings.TaskType.ListBackupEntries;
                case Duplicati.Server.Serialization.DuplicatiOperation.Restore:
                    return Strings.TaskType.ListBackups;
                default:
                    return type.ToString();
            }
        }

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format
        /// </summary>
        /// <param name="con">The SQLite connection object</param>
        internal static void OpenDatabase(System.Data.IDbConnection con)
        {
            bool noEncryption = !Program.UseDatabaseEncryption;
            string password = Environment.GetEnvironmentVariable(DB_KEY_ENV_NAME);

            System.Reflection.MethodInfo setPwdMethod = con.GetType().GetMethod("SetPassword", new Type[] { typeof(string) });
            string attemptedPassword;

            if (noEncryption || string.IsNullOrEmpty(password))
                attemptedPassword = null; //No encryption specified, attempt to open without
            else
                attemptedPassword = password; //Encryption specified, attempt to open with

            if (setPwdMethod != null)
                setPwdMethod.Invoke(con, new object[] { attemptedPassword });

            try
            {
                //Attempt to open in preferred state
                con.Open();

                // Do a dummy query to make sure we have a working db
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER";
                    cmd.ExecuteScalar();
                }
            }
            catch
            {
                try
                {
                    //We can't try anything else without a password
                    if (string.IsNullOrEmpty(password))
                        throw;

                    //Open failed, now try the reverse
                    if (attemptedPassword == null)
                        attemptedPassword = password;
                    else
                        attemptedPassword = null;

                    con.Close();
                    setPwdMethod.Invoke(con, new object[] { attemptedPassword });
                    con.Open();

                    // Do a dummy query to make sure we have a working db
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER";
                        cmd.ExecuteScalar();
                    }
                }
                catch
                {
                    try { con.Close(); }
                    catch { }
                }

                //If the db is not open now, it won't open
                if (con.State != System.Data.ConnectionState.Open)
                    throw; //Report original error

                //The open method succeeded with the non-default method, now change the password
                System.Reflection.MethodInfo changePwdMethod = con.GetType().GetMethod("ChangePassword", new Type[] { typeof(string) });
                changePwdMethod.Invoke(con, new object[] { noEncryption ? null : password });
            }
        }

        /// <summary>
        /// Simple method for tracking if the server has crashed
        /// </summary>
        private static void PingPongMethod()
        {
            var rd = new System.IO.StreamReader(Console.OpenStandardInput());
            var wr = new System.IO.StreamWriter(Console.OpenStandardOutput());
            while (rd.ReadLine() != null)
            {
                wr.WriteLine("pong");
                wr.Flush();
            }
        }

        /// <summary>
        /// The default log retention
        /// </summary>
        private static string DEFAULT_LOG_RETENTION = "30D";

        /// <summary>
        /// Gets a list of all supported commandline options
        /// </summary>
        public static Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                var lst = new List<Duplicati.Library.Interface.ICommandLineArgument> (new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument("help", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.HelpCommandDescription, Strings.Program.HelpCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("unencrypted-database", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.UnencrypteddatabaseCommandDescription, Strings.Program.UnencrypteddatabaseCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("portable-mode", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PortablemodeCommandDescription, Strings.Program.PortablemodeCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.LogfileCommandDescription, Strings.Program.LogfileCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LoglevelCommandDescription, Strings.Program.LoglevelCommandDescription, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_WEBROOT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverWebrootDescription, Strings.Program.WebserverWebrootDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_WEBROOT),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_PORT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverPortDescription, Strings.Program.WebserverPortDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_PORT.ToString()),
                    new Duplicati.Library.Interface.CommandLineArgument(Duplicati.Server.WebServer.Server.OPTION_INTERFACE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverInterfaceDescription, Strings.Program.WebserverInterfaceDescription, Duplicati.Server.WebServer.Server.DEFAULT_OPTION_INTERFACE),
                    new Duplicati.Library.Interface.CommandLineArgument("webservice-password", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.WebserverPasswordDescription, Strings.Program.WebserverPasswordDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("ping-pong-keepalive", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PingpongkeepaliveShort, Strings.Program.PingpongkeepaliveLong),
                    new Duplicati.Library.Interface.CommandLineArgument("log-retention", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.Program.LogretentionShort, Strings.Program.LogretentionLong, DEFAULT_LOG_RETENTION),
                    new Duplicati.Library.Interface.CommandLineArgument("server-datafolder", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ServerdatafolderShort, Strings.Program.ServerdatafolderLong(DATAFOLDER_ENV_NAME), System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName)),
                });

                if (!Duplicati.Library.Utility.Utility.IsClientLinux)
                    lst.Add(new Duplicati.Library.Interface.CommandLineArgument("server-encryption-key", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.ServerencryptionkeyShort, Strings.Program.ServerencryptionkeyLong(DB_KEY_ENV_NAME, "unencrypted-database"), Library.AutoUpdater.AutoUpdateSettings.AppName + "_Key_42"));

                return lst.ToArray();
            }
        }
    }
}
