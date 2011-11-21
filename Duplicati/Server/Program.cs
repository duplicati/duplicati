using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.LightDatamodel;

namespace Duplicati.Server
{
    public class Program
    {
        /// <summary>
        /// The name of the application, change this to re-brand it
        /// </summary>
        public const string ApplicationName = "Duplicati";

        /// <summary>
        /// The path to the directory that contains the main executable
        /// </summary>
        public static readonly string StartupPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        /// <summary>
        /// The name of the environment variable that holds the path to the data folder used by Duplicati
        /// </summary>
        public static readonly string DATAFOLDER_ENV_NAME = ApplicationName.ToUpper() + "_HOME";

        /// <summary>
        /// The environment variable that holdes the database key used to encrypt the SQLite database
        /// </summary>
        public static readonly string DB_KEY_ENV_NAME = ApplicationName.ToUpper() + "_DB_KEY";

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DATAFOLDER { get { return Library.Utility.Utility.AppendDirSeparator(Environment.ExpandEnvironmentVariables("%" + DATAFOLDER_ENV_NAME + "%").TrimStart('"').TrimEnd('"')); } }

        /// <summary>
        /// A flag indicating if database encryption is in use
        /// </summary>
        public static bool UseDatabaseEncryption;

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static IDataFetcherWithRelations DataConnection;

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
        public static Duplicati.Library.Utility.WorkerThread<IDuplicityTask> WorkThread;

        /// <summary>
        /// The path to the file that contains the current database
        /// </summary>
        public static string DatabasePath;

        /// <summary>
        /// The actual runner, do not call directly. Only used for events.
        /// </summary>
        public static DuplicatiRunner Runner;

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
        public static WebServer WebServer;

        /// <summary>
        /// The main entry point for the application.
        /// <param name="args">Commandline arguments</param>
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
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
                Environment.SetEnvironmentVariable(DB_KEY_ENV_NAME, ApplicationName + "_Key_42");
            }


            //Find commandline options here for handling special startup cases
            Dictionary<string, string> commandlineOptions = CommandLine.CommandLineParser.ExtractOptions(new List<string>(args));

            foreach (string s in args)
                if (
                    s.Equals("help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/help", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("usage", StringComparison.InvariantCultureIgnoreCase) ||
                    s.Equals("/usage", StringComparison.InvariantCultureIgnoreCase))
                    commandlineOptions["help"] = "";

            //If the commandline issues --help, just stop here
            if (commandlineOptions.ContainsKey("help"))
            {
                Console.WriteLine(Strings.Program.HelpDisplayDialog);

                foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                    Console.WriteLine(Strings.Program.HelpDisplayFormat, arg.Name, arg.LongDescription);

                return;
            }

#if DEBUG
            //Log various information in the logfile
            if (!commandlineOptions.ContainsKey("log-file"))
            {
                commandlineOptions["log-file"] = System.IO.Path.Combine(StartupPath, "Duplicati.debug.log");
                commandlineOptions["log-level"] = Duplicati.Library.Logging.LogMessageType.Profiling.ToString();
            }
#endif

            if (commandlineOptions.ContainsKey("log-level"))
                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(commandlineOptions["log-level"].Trim(), StringComparison.InvariantCultureIgnoreCase))
                        Duplicati.Library.Logging.Log.LogLevel = (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

            if (commandlineOptions.ContainsKey("log-file"))
            {
                if (System.IO.File.Exists(commandlineOptions["log-file"]))
                    System.IO.File.Delete(commandlineOptions["log-file"]);
                Duplicati.Library.Logging.Log.CurrentLog = new Duplicati.Library.Logging.StreamLog(commandlineOptions["log-file"]);
            }
            
            //Set the %DUPLICATI_HOME% env variable, if it is not already set
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DATAFOLDER_ENV_NAME)))
            {
#if DEBUG
                //debug mode uses a lock file located in the app folder
                Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
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
                    Environment.SetEnvironmentVariable(DATAFOLDER_ENV_NAME, System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName));
                }
#endif
            }

            SingleInstance instance = null;

            try
            {
                try
                {
                    //This will also create Program.DATAFOLDER if it does not exist
                    instance = new SingleInstance(ApplicationName, Program.DATAFOLDER);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(Strings.Program.StartupFailure, ex.ToString());
                    return;
                }

                if (!instance.IsFirstInstance)
                {
                    Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                    return;
                }

                Version sqliteVersion = new Version((string)SQLiteLoader.SQLiteConnectionType.GetProperty("SQLiteVersion").GetValue(null, null));
                if (sqliteVersion < new Version(3, 6, 3))
                {
                    //The official Mono SQLite provider is also broken with less than 3.6.3
                    Console.WriteLine(Strings.Program.WrongSQLiteVersion, sqliteVersion, "3.6.3");
                    return;
                }

                //Create the connection instance
                System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteLoader.SQLiteConnectionType);

                try
                {
                    DatabasePath = System.IO.Path.Combine(Program.DATAFOLDER, "Duplicati.sqlite");
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

                    DatabaseUpgrader.UpgradeDatabase(con, DatabasePath);
                }
                catch (Exception ex)
                {
                    //Unwrap the reflection exceptions
                    if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                        ex = ex.InnerException;

                    Console.WriteLine(Strings.Program.DatabaseOpenError, ex.Message);
                    return;
                }

                DataConnection = new DataFetcherWithRelations(new SQLiteDataProvider(con));

                ApplicationExitEvent = new System.Threading.ManualResetEvent(false);

                LiveControl = new LiveControls(new Duplicati.Datamodel.ApplicationSettings(DataConnection));
                LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
                LiveControl.ThreadPriorityChanged += new EventHandler(LiveControl_ThreadPriorityChanged);
                LiveControl.ThrottleSpeedChanged += new EventHandler(LiveControl_ThrottleSpeedChanged);

                Runner = new DuplicatiRunner();
                WorkThread = new Duplicati.Library.Utility.WorkerThread<IDuplicityTask>(new Duplicati.Library.Utility.WorkerThread<IDuplicityTask>.ProcessItemDelegate(Runner.ExecuteTask), LiveControl.State == LiveControls.LiveControlState.Paused);
                Scheduler = new Scheduler(DataConnection, WorkThread, MainLock);

                /*WorkThread.StartingWork += new EventHandler(Events.WorkThread_StartingWork);
                WorkThread.CompletedWork += new EventHandler(Events.WorkThread_CompletedWork);
                WorkThread.WorkQueueChanged += new EventHandler(Events.WorkThread_WorkQueueChanged);
                Scheduler.NewSchedule += new EventHandler(Events.Scheduler_NewSchedule);
                Runner.ProgressEvent += new DuplicatiRunner.ProgressEventDelegate(Events.Runner_DuplicatiProgress);
                DataConnection.AfterDataConnection += new System.Data.LightDatamodel.DataConnectionEventHandler(Events.DataConnection_AfterDataConnection);
                
                LiveControl.StateChanged += new EventHandler(Events.LiveControl_StateChanged);
                LiveControl.ThreadPriorityChanged += new EventHandler(Events.LiveControl_ThreadPriorityChanged);
                LiveControl.ThrottleSpeedChanged += new EventHandler(Events.LiveControl_ThrottleSpeedChanged);*/


                Program.WebServer = new Server.WebServer(8080);

                DataConnection.AfterDataConnection += new DataConnectionEventHandler(DataConnection_AfterDataConnection);

                ApplicationExitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                Console.WriteLine(Strings.Program.SeriousError, ex.ToString());
            }


            if (Scheduler != null)
                Scheduler.Terminate(true);
            if (WorkThread != null)
                WorkThread.Terminate(true);
            if (instance != null)
                instance.Dispose();

#if DEBUG
            //Flush the file
            using (Duplicati.Library.Logging.Log.CurrentLog as Duplicati.Library.Logging.StreamLog)
                Duplicati.Library.Logging.Log.CurrentLog = null;
#endif
        }

        /// <summary>
        /// Handles a change in the LiveControl and updates the Runner
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void LiveControl_ThreadPriorityChanged(object sender, EventArgs e)
        {
            if (LiveControl.ThreadPriority == null)
                Runner.UnsetThreadPriority();
            else
                Runner.SetThreadPriority(LiveControl.ThreadPriority.Value);
        }

        /// <summary>
        /// Handles a change in the LiveControl and updates the Runner
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void LiveControl_ThrottleSpeedChanged(object sender, EventArgs e)
        {
            if (LiveControl.DownloadLimit == null)
                Runner.SetDownloadLimit(null);
            else
                Runner.SetDownloadLimit(LiveControl.DownloadLimit.Value.ToString() + "b");

            if (LiveControl.UploadLimit == null)
                Runner.SetUploadLimit(null);
            else
                Runner.SetUploadLimit(LiveControl.UploadLimit.Value.ToString() + "b");
        }

        /// <summary>
        /// This event handler updates the trayicon menu with the current state of the runner.
        /// </summary>
        static void LiveControl_StateChanged(object sender, EventArgs e)
        {
            switch (LiveControl.State)
            {
                case LiveControls.LiveControlState.Paused:
                    WorkThread.Pause();
                    Runner.Pause();
                    break;
                case LiveControls.LiveControlState.Running:
                    WorkThread.Resume();
                    Runner.Resume();
                    break;
            }
        }


        private static void DataConnection_AfterDataConnection(object sender, DataActions action)
        {
            if (action == DataActions.Insert || action == DataActions.Update)
                Scheduler.Reschedule();
        }

        /// <summary>
        /// Returns a localized name for a task type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string LocalizeTaskType(DuplicityTaskType type)
        {
            switch (type)
            {
                case DuplicityTaskType.FullBackup:
                    return Strings.TaskType.FullBackup;
                case DuplicityTaskType.IncrementalBackup:
                    return Strings.TaskType.IncrementalBackup;
                case DuplicityTaskType.ListActualFiles:
                    return Strings.TaskType.ListActualFiles;
                case DuplicityTaskType.ListBackupEntries:
                    return Strings.TaskType.ListBackupEntries;
                case DuplicityTaskType.ListBackups:
                    return Strings.TaskType.ListBackups;
                case DuplicityTaskType.ListFiles:
                    return Strings.TaskType.ListFiles;
                case DuplicityTaskType.RemoveAllButNFull:
                    return Strings.TaskType.RemoveAllButNFull;
                case DuplicityTaskType.RemoveOlderThan:
                    return Strings.TaskType.RemoveOlderThan;
                case DuplicityTaskType.Restore:
                    return Strings.TaskType.Restore;
                case DuplicityTaskType.RestoreSetup:
                    return Strings.TaskType.RestoreSetup;
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

                    setPwdMethod.Invoke(con, new object[] { attemptedPassword });
                    con.Open();
                }
                catch
                {
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
        /// Gets a list of all supported commandline options
        /// </summary>
        private static Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                return new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument("help", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.HelpCommandDescription, Strings.Program.HelpCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("unencrypted-database", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.UnencrypteddatabaseCommandDescription, Strings.Program.UnencrypteddatabaseCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("portable-mode", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PortablemodeCommandDescription, Strings.Program.PortablemodeCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.LogfileCommandDescription, Strings.Program.LogfileCommandDescription),
                    new Duplicati.Library.Interface.CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LoglevelCommandDescription, Strings.Program.LoglevelCommandDescription, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                };
            }
        }
    }
}
