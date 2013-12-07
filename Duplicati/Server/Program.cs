using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        public static Duplicati.Library.Utility.WorkerThread<Tuple<long, Duplicati.Server.Serialization.DuplicatiOperation>> WorkThread;

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
        public static WebServer WebServer;
        
        /// <summary>
        /// An event that is set once the server is ready to respond to requests
        /// </summary>
        public static System.Threading.ManualResetEvent ServerStartedEvent = new System.Threading.ManualResetEvent(false);

        /// <summary>
        /// The status event signaler, used to controll long polling of status updates
        /// </summary>
        public static EventPollNotify StatusEventNotifyer = new EventPollNotify();

        /// <summary>
        /// The progress event signaler, used to control long polling of current backup progress
        /// </summary>
        public static EventPollNotify ProgressEventNotifyer = new EventPollNotify();

        /// <summary>
        /// An event ID that increases whenever the database is updated
        /// </summary>
        public static long LastDataUpdateID = 0;

        //TODO: These should be persisted to the database
        public static bool HasError;
        public static bool HasWarning;
        
        public static int ServerPort
        {
            get
            {
                return WebServer.Port;
            }
        }

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
                Environment.SetEnvironmentVariable(DB_KEY_ENV_NAME, ApplicationName + "_Key_42");
            }


            //Find commandline options here for handling special startup cases
            Dictionary<string, string> commandlineOptions = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(new List<string>(args));

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
                if (writeConsole)
                {
                    Console.WriteLine(Strings.Program.HelpDisplayDialog);

                    foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                        Console.WriteLine(Strings.Program.HelpDisplayFormat, arg.Name, arg.LongDescription);

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
                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.StartupFailure, ex.ToString());
                        return;
                    }
                    else
                    {
                        throw new Exception(Strings.Program.StartupFailure, ex);
                    }
                }

                if (!instance.IsFirstInstance)
                {
                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                        return;
                    }
                    else
                    {
                        throw new Exception(Strings.Program.AnotherInstanceDetected);
                    }
                }


                Version sqliteVersion = new Version((string)Duplicati.Library.Utility.SQLiteLoader.SQLiteConnectionType.GetProperty("SQLiteVersion").GetValue(null, null));
                if (sqliteVersion < new Version(3, 6, 3))
                {
                    if (writeConsole)
                    {
                        //The official Mono SQLite provider is also broken with less than 3.6.3
                        Console.WriteLine(Strings.Program.WrongSQLiteVersion, sqliteVersion, "3.6.3");
                        return;
                    }
                    else
                    {
                        throw new Exception(string.Format(Strings.Program.WrongSQLiteVersion, sqliteVersion, "3.6.3"));
                    }
                }

                //Create the connection instance
                System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.Utility.SQLiteLoader.SQLiteConnectionType);

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

                    Duplicati.Library.Utility.DatabaseUpgrader.UpgradeDatabase(con, DatabasePath, typeof(Duplicati.Server.Database.Connection));
                }
                catch (Exception ex)
                {
                    //Unwrap the reflection exceptions
                    if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                        ex = ex.InnerException;

                    if (writeConsole)
                    {
                        Console.WriteLine(Strings.Program.DatabaseOpenError, ex.Message);
                        return;
                    }
                    else
                    {
                        throw new Exception(string.Format(Strings.Program.DatabaseOpenError, ex.Message), ex);
                    }
                }

                DataConnection = new Duplicati.Server.Database.Connection(con);

                ApplicationExitEvent = new System.Threading.ManualResetEvent(false);

                LiveControl = new LiveControls(DataConnection.ApplicationSettings);
                LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
                LiveControl.ThreadPriorityChanged += new EventHandler(LiveControl_ThreadPriorityChanged);
                LiveControl.ThrottleSpeedChanged += new EventHandler(LiveControl_ThrottleSpeedChanged);

                Program.WorkThread = new Duplicati.Library.Utility.WorkerThread<Tuple<long, Server.Serialization.DuplicatiOperation>>(Runner.Run, LiveControl.State == LiveControls.LiveControlState.Paused);
                Program.Scheduler = new Scheduler(WorkThread);

                Program.WorkThread.StartingWork += new EventHandler(SignalNewEvent);
                Program.WorkThread.CompletedWork += new EventHandler(SignalNewEvent);
                Program.WorkThread.WorkQueueChanged += new EventHandler(SignalNewEvent);
                Program.Scheduler.NewSchedule += new EventHandler(SignalNewEvent);

                LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
                LiveControl.ThreadPriorityChanged += new EventHandler(LiveControl_ThreadPriorityChanged);
                LiveControl.ThrottleSpeedChanged += new EventHandler(LiveControl_ThrottleSpeedChanged);

                Program.WebServer = new Server.WebServer(commandlineOptions);

                ServerStartedEvent.Set();
                ApplicationExitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(string.Format(Strings.Program.SeriousError, ex.ToString()));
                if (writeConsole)
                    Console.WriteLine(Strings.Program.SeriousError, ex.ToString());
                else
                    throw new Exception(string.Format(Strings.Program.SeriousError, ex.ToString()), ex);
            }
            finally
            {
                StatusEventNotifyer.SignalNewEvent();
                ProgressEventNotifyer.SignalNewEvent();

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
                    WorkThread.Pause();
                    break;
                case LiveControls.LiveControlState.Running:
                    WorkThread.Resume();
                    break;
            }

            StatusEventNotifyer.SignalNewEvent();
        }

        /// <summary>
        /// Returns a localized name for a task type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string LocalizeTaskType(Server.Serialization.DuplicatiOperation type)
        {
            switch (type)
            {
                case Server.Serialization.DuplicatiOperation.Backup:
                    return Strings.TaskType.FullBackup;
                case Server.Serialization.DuplicatiOperation.List:
                    return Strings.TaskType.IncrementalBackup;
                case Server.Serialization.DuplicatiOperation.Remove:
                    return Strings.TaskType.ListActualFiles;
                case Server.Serialization.DuplicatiOperation.Verify:
                    return Strings.TaskType.ListBackupEntries;
                case Server.Serialization.DuplicatiOperation.Restore:
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
