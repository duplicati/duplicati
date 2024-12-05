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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Crashlog;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.WebserverCore;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Duplicati.Server
{
    public class Program
    {

        private static readonly string[] AlternativeHelpStrings = ["help", "/help", "usage", "/usage", "--help"];

        private static readonly string[] ParameterFileOptionStrings = ["parameters-file", "parameterfile"];

        private const string PING_PONG_KEEPALIVE_OPTION = "ping-pong-keepalive";
        private const string WINDOWS_EVENTLOG_OPTION = "windows-eventlog";
        private const string WINDOWS_EVENTLOG_LEVEL_OPTION = "windows-eventlog-level";
        private const string DISABLE_DB_ENCRYPTION_OPTION = "disable-db-encryption";
        private const string REQUIRE_DB_ENCRYPTION_KEY_OPTION = "require-db-encryption-key";
        private const string SETTINGS_ENCRYPTION_KEY_OPTION = "settings-encryption-key";

#if DEBUG
        private const bool DEBUG_MODE = true;
#else
        private const bool DEBUG_MODE = false;
#endif

        /// <summary>
        /// The log tag for messages from this class
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<Program>();
        /// <summary>
        /// The path to the directory that contains the main executable
        /// </summary>
        public static readonly string StartupPath = Duplicati.Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR;

        /// <summary>
        /// Name of the database file
        /// </summary>
        public const string SERVER_DATABASE_FILENAME = "Duplicati-server.sqlite";

        /// <summary>
        /// The environment variable prefix
        /// </summary>
        private static readonly string ENV_NAME_PREFIX = Duplicati.Library.AutoUpdater.AutoUpdateSettings.AppName.ToUpper(CultureInfo.InvariantCulture);

        /// <summary>
        /// The name of the environment variable that holds the path to the data folder used by Duplicati
        /// </summary>
        private static readonly string DATAFOLDER_ENV_NAME = ENV_NAME_PREFIX + "_HOME";

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DataFolder { get => FIXMEGlobal.DataFolder; private set => FIXMEGlobal.DataFolder = value; }

        /// <summary>
        /// The single instance
        /// </summary>
        public static SingleInstance ApplicationInstance = null;

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static Database.Connection DataConnection { get => FIXMEGlobal.DataConnection; set => FIXMEGlobal.DataConnection = value; }

        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static object MainLock { get => FIXMEGlobal.MainLock; }

        /// <summary>
        /// This is the scheduling thread
        /// </summary>
        public static IScheduler Scheduler { get => FIXMEGlobal.Scheduler; }

        /// <summary>
        /// List of completed task results
        /// </summary>
        public static List<KeyValuePair<long, Exception>> TaskResultCache { get => FIXMEGlobal.TaskResultCache; }

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
        public static LiveControls LiveControl { get => DuplicatiWebserver.Provider.GetRequiredService<LiveControls>(); }

        /// <summary>
        /// The application exit event
        /// </summary>
        public static System.Threading.ManualResetEvent ApplicationExitEvent { get => FIXMEGlobal.ApplicationExitEvent; set => FIXMEGlobal.ApplicationExitEvent = value; }

        /// <summary>
        /// Duplicati webserver instance
        /// </summary>
        public static DuplicatiWebserver DuplicatiWebserver { get; set; }

        /// <summary>
        /// Callback to shutdown the modern webserver
        /// </summary>
        private static void ShutdownModernWebserver()
        {
            DuplicatiWebserver.Stop().GetAwaiter().GetResult();
        }

        /// <summary>
        /// The update poll thread.
        /// </summary>
        public static UpdatePollThread UpdatePoller => FIXMEGlobal.UpdatePoller;

        /// <summary>
        /// An event that is set once the server is ready to respond to requests
        /// </summary>
        public static readonly System.Threading.ManualResetEvent ServerStartedEvent = new System.Threading.ManualResetEvent(false);

        /// <summary>
        /// The status event signaler, used to control long polling of status updates
        /// </summary>
        public static EventPollNotify StatusEventNotifyer => FIXMEGlobal.Provider.GetRequiredService<EventPollNotify>();

        /// <summary>
        /// A delegate method for creating a copy of the current progress state
        /// </summary>
        public static Func<Duplicati.Server.Serialization.Interface.IProgressEventData> GenerateProgressState { get => FIXMEGlobal.GenerateProgressState; set => FIXMEGlobal.GenerateProgressState = value; }

        /// <summary>
        /// The log redirect handler
        /// </summary>
        public static LogWriteHandler LogHandler { get => FIXMEGlobal.LogHandler; }

        private static System.Threading.Timer PurgeTempFilesTimer = null;

        public static int ServerPort
        {
            get
            {
                return DuplicatiWebserver.Port;
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

        static Program()
        {
            FIXMEGlobal.StartOrStopUsageReporter = Program.StartOrStopUsageReporter;
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, Library.AutoUpdater.PackageHelper.NamedExecutable.Server, out var preloadDbSettings);

            //If this executable is invoked directly, write to console, otherwise throw exceptions
            var writeToConsoleOnException = FIXMEGlobal.Origin == "Server";

            // Prepared for the future, where we might want to have a silent console mode
            var silentConsole = false;
            var logMessageToConsole = (string message) => { if (!silentConsole) Console.WriteLine(message); };

            //Find commandline options here for handling special startup cases
            var args = new List<string>(_args);
            var optionsWithFilter = Library.Utility.FilterCollector.ExtractOptions(new List<string>(args));
            var commandlineOptions = optionsWithFilter.Item1;
            var filter = optionsWithFilter.Item2;

            if (_args.Select(s => s.ToLower()).Intersect(AlternativeHelpStrings.Select(x => x.ToLower())).Any())
            {
                return ShowHelp(writeToConsoleOnException);
            }

            if (commandlineOptions.ContainsKey("tempdir") && !string.IsNullOrEmpty(commandlineOptions["tempdir"]))
            {
                Library.Utility.SystemContextSettings.DefaultTempPath = commandlineOptions["tempdir"];
            }

            Library.Utility.SystemContextSettings.StartSession();

            ApplyEnvironmentVariables(commandlineOptions);
            ApplySecretProvider(commandlineOptions, CancellationToken.None).Await();

            var parameterFileOption = commandlineOptions.Keys.Select(s => s.ToLower())
                .Intersect(ParameterFileOptionStrings.Select(x => x.ToLower())).FirstOrDefault();

            if (parameterFileOption != null && !string.IsNullOrEmpty(commandlineOptions[parameterFileOption]))
            {
                string filename = commandlineOptions[parameterFileOption];
                commandlineOptions.Remove(parameterFileOption);
                if (!ReadOptionsFromFile(filename, ref filter, args, commandlineOptions))
                    return 100;
            }

            ConfigureLogging(commandlineOptions);

            var crashed = false;
            var terminated = false;
            try
            {
                DataConnection = GetDatabaseConnection(commandlineOptions, silentConsole);

                if (!DataConnection.ApplicationSettings.FixedInvalidBackupId)
                    DataConnection.FixInvalidBackupId();

                DataConnection.ApplicationSettings.UpgradePasswordToKBDF();
                CreateApplicationInstance(writeToConsoleOnException);

                StartOrStopUsageReporter();

                AdjustApplicationSettings(commandlineOptions);

                ApplicationExitEvent = new System.Threading.ManualResetEvent(false);

                Library.AutoUpdater.UpdaterManager.OnError += obj =>
                {
                    DataConnection.LogError(null, "Error in updater", obj);
                };

                DuplicatiWebserver = StartWebServer(commandlineOptions, DataConnection).ConfigureAwait(false).GetAwaiter().GetResult();

                UpdatePoller.Init();

                SetPurgeTempFilesTimer(commandlineOptions);

                SetLiveControls();

                SetWorkerThread();

                if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, PING_PONG_KEEPALIVE_OPTION))
                {
                    PingPongThread = new System.Threading.Thread(PingPongMethod) { IsBackground = true };
                    PingPongThread.Start();
                }

                DataConnection.ReWriteAllFieldsIfEncryptionChanged();
                DataConnection.SetPreloadSettingsIfChanged(preloadDbSettings);

                Library.Logging.Log.WriteInformationMessage(LOGTAG, "ServerStarted", Strings.Program.ServerStarted(DuplicatiWebserver.Port));
                logMessageToConsole(Strings.Program.ServerStarted(DuplicatiWebserver.Port));

                if (FIXMEGlobal.Origin == "Server" && DataConnection.ApplicationSettings.AutogeneratedPassphrase)
                {
                    var signinToken = DuplicatiWebserver.Provider.GetRequiredService<IJWTTokenProvider>().CreateSigninToken("server-cli");
                    var hostname = (DataConnection.ApplicationSettings.AllowedHostnames ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x != "*") ?? "localhost";
                    var scheme = DataConnection.ApplicationSettings.UseHTTPS ? "https" : "http";

                    var url = $"{scheme}://{hostname}:{DuplicatiWebserver.Port}/signin.html?token={signinToken}";
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "ServerStartedSignin", null, Strings.Program.ServerStartedSignin(url));
                    logMessageToConsole(Strings.Program.ServerStartedSignin(url));
                }

                DuplicatiWebserver.TerminationTask.ContinueWith((t) =>
                {
                    if (t.Exception != null)
                    {
                        Library.Logging.Log.WriteWarningMessage(LOGTAG, "ServerCrashed", t.Exception, Strings.Program.ServerCrashed(t.Exception.Message));
                        logMessageToConsole(Strings.Program.ServerStartedSignin(Strings.Program.ServerCrashed(t.Exception.ToString())));
                    }

                    terminated = true;
                    ApplicationExitEvent.Set();
                });

                ServerStartedEvent.Set();
                ApplicationExitEvent.WaitOne();
            }
            catch (SingleInstance.MultipleInstanceException mex)
            {
                crashed = true;
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                if (!writeToConsoleOnException) throw;

                Console.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                return 100;
            }
            catch (Exception ex)
            {
                crashed = true;
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                if (writeToConsoleOnException)
                {
                    Console.WriteLine(Strings.Program.SeriousError(ex.ToString()));
                    return 100;
                }
                else
                    throw new Exception(Strings.Program.SeriousError(ex.ToString()), ex);
            }
            finally
            {
                var steps = new Action[] {
                    () => StatusEventNotifyer.SignalNewEvent(),
                    () => { if (ShutdownModernWebserver != null) ShutdownModernWebserver(); },
                    () => UpdatePoller?.Terminate(),
                    () => Scheduler?.Terminate(true),
                    () => FIXMEGlobal.WorkThread?.Terminate(true),
                    () => ApplicationInstance?.Dispose(),
                    () => PurgeTempFilesTimer?.Dispose(),
                    () => Library.UsageReporter.Reporter.ShutDown(),
                    () => PingPongThread?.Interrupt(),
                    () => LogHandler?.Dispose()
                };

                foreach (var teardownStep in steps)
                {
                    try
                    {
                        teardownStep();
                    }
                    catch (Exception ex)
                    {
                        // If the server is already crashed, that is the main error
                        // If the server crashes during teardown, we log that as an error
                        if (!(crashed || terminated))
                        {
                            System.Diagnostics.Trace.WriteLine(Strings.Program.TearDownError(ex.ToString()));
                            logMessageToConsole(Strings.Program.TearDownError(ex.ToString()));
                        }
                    }
                }
            }

            return 0;
        }

        private static async Task<DuplicatiWebserver> StartWebServer(IReadOnlyDictionary<string, string> options, Connection connection)
        {
            var server = await WebServerLoader.TryRunServer(options, connection, async parsedOptions =>
            {
                var mappedSettings = new DuplicatiWebserver.InitSettings(
                    parsedOptions.WebRoot,
                    parsedOptions.Port,
                    parsedOptions.Interface,
                    parsedOptions.Certificate,
                    parsedOptions.Servername,
                    parsedOptions.AllowedHostnames,
                    parsedOptions.DisableStaticFiles,
                    parsedOptions.SPAPaths
                );

                var server = new DuplicatiWebserver();

                server.InitWebServer(mappedSettings, connection);

                // Start the server, but catch any configuration issues
                var task = server.Start(mappedSettings);
                await Task.WhenAny(task, Task.Delay(500));
                if (task.IsCompleted)
                    await task;

                return server;
            }).ConfigureAwait(false);

            FIXMEGlobal.Provider = server.Provider;
            ServerPortChanged |= server.Port != DataConnection.ApplicationSettings.LastWebserverPort;
            DataConnection.ApplicationSettings.LastWebserverPort = server.Port;

            return server;
        }

        private static void SetWorkerThread()
        {
            FIXMEGlobal.WorkerThreadsManager.Spawn(x => { Runner.Run(x, true); });
            FIXMEGlobal.WorkThread.StartingWork += (worker, task) => { SignalNewEvent(null, null); };
            FIXMEGlobal.WorkThread.CompletedWork += (worker, task) => { SignalNewEvent(null, null); };
            FIXMEGlobal.WorkThread.WorkQueueChanged += (worker) => { SignalNewEvent(null, null); };
            FIXMEGlobal.Scheduler.SubScribeToNewSchedule(() => SignalNewEvent(null, null));
            FIXMEGlobal.WorkThread.OnError += (worker, task, exception) =>
            {
                Program.DataConnection.LogError(task?.BackupID, "Error in worker", exception);
            };

            var lastScheduleId = FIXMEGlobal.NotificationUpdateService.LastDataUpdateId;
            Program.StatusEventNotifyer.NewEvent += (sender, e) =>
            {
                if (lastScheduleId == FIXMEGlobal.NotificationUpdateService.LastDataUpdateId) return;
                lastScheduleId = FIXMEGlobal.NotificationUpdateService.LastDataUpdateId;
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

            FIXMEGlobal.WorkThread.CompletedWork += (worker, task) => { RegisterTaskResult(task.TaskID, null); };
            FIXMEGlobal.WorkThread.OnError += (worker, task, exception) => { RegisterTaskResult(task.TaskID, exception); };
        }

        private static void SetLiveControls()
        {
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
                    if (Math.Abs((DateTime.Now - lastPurge).TotalHours) < (DEBUG_MODE ? 1 : 23))
                    {
                        return;
                    }

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

            PurgeTempFilesTimer =
                new System.Threading.Timer(purgeTempFilesCallback, null,
                    DEBUG_MODE ? TimeSpan.FromSeconds(10) : TimeSpan.FromHours(1),
                    DEBUG_MODE ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1));
        }

        private static void AdjustApplicationSettings(Dictionary<string, string> commandlineOptions)
        {
            // This clears the JWT config, and a new will be generated, invalidating all existing tokens
            if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, WebServerLoader.OPTION_WEBSERVICE_RESET_JWT_CONFIG))
            {
                DataConnection.ApplicationSettings.JWTConfig = null;
                // Clean up stored tokens as they are now invalid
                DataConnection.ExecuteWithCommand((con) => con.ExecuteNonQuery("DELETE FROM TokenFamily"));
            }

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_DISABLE_VISUAL_CAPTCHA))
                DataConnection.ApplicationSettings.DisableVisualCaptcha = Library.Utility.Utility.ParseBool(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_DISABLE_VISUAL_CAPTCHA], true);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS))
                DataConnection.ApplicationSettings.DisableSigninTokens = Library.Utility.Utility.ParseBool(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS], true);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_PASSWORD))
                DataConnection.ApplicationSettings.SetWebserverPassword(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_PASSWORD]);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES))
                DataConnection.ApplicationSettings.SetAllowedHostnames(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES]);
            else if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT))
                DataConnection.ApplicationSettings.SetAllowedHostnames(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT]);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_TIMEZONE) && !string.IsNullOrEmpty(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]))
                try
                {
                    DataConnection.ApplicationSettings.Timezone = TimeZoneHelper.FindTimeZone(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]);
                }
                catch (Exception ex)
                {
                    throw new UserInformationException(Strings.Program.InvalidTimezone(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]), "InvalidTimeZone", ex);
                }

            // The database has recorded a new version
            if (DataConnection.ApplicationSettings.UpdatedVersion != null)
            {
                // Check if the running version is newer than the recorded version
                if (UpdaterManager.TryParseVersion(DataConnection.ApplicationSettings.UpdatedVersion.Version) <= UpdaterManager.TryParseVersion(UpdaterManager.SelfVersion.Version))
                {
                    // Clean up lingering update notifications
                    var updateNotifications = DataConnection.GetNotifications().Where(x => x.Action == "update:new").ToList();
                    foreach (var n in updateNotifications)
                        DataConnection.DismissNotification(n.ID);

                    // Clear up the recorded version
                    DataConnection.ApplicationSettings.UpdatedVersion = null;
                }
            }

        }

        private static void CreateApplicationInstance(bool writeToConsoleOnExceptionw)
        {
            try
            {
                //This will also create DATAFOLDER if it does not exist
                ApplicationInstance = new SingleInstance(DataFolder);
            }
            catch (Exception ex)
            {
                if (writeToConsoleOnExceptionw)
                {
                    Console.WriteLine(Strings.Program.StartupFailure(ex));
                    Environment.Exit(200);
                }

                throw new Exception(Strings.Program.StartupFailure(ex));
            }

            if (!ApplicationInstance.IsFirstInstance)
            {
                if (writeToConsoleOnExceptionw)
                {
                    Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                    Environment.Exit(200);
                }

                throw new SingleInstance.MultipleInstanceException(Strings.Program.AnotherInstanceDetected);
            }
        }

        private static void ApplyEnvironmentVariables(Dictionary<string, string> commandlineOptions)
        {
            foreach (var key in SupportedCommands.SelectMany(x => (x.Aliases ?? []).Prepend(x.Name)).Distinct())
            {
                // Commandline options take precedence
                if (commandlineOptions.ContainsKey(key))
                    continue;

                var envkey = $"{ENV_NAME_PREFIX}__{key.Replace('-', '_').ToUpperInvariant()}";
                var envval = Environment.GetEnvironmentVariable(envkey);
                if (!string.IsNullOrWhiteSpace(envval))
                    commandlineOptions[key] = envval;
            }

            // Set the encryption key from the environment variable
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME)) && string.IsNullOrWhiteSpace(commandlineOptions.GetValueOrDefault(SETTINGS_ENCRYPTION_KEY_OPTION)))
                commandlineOptions[SETTINGS_ENCRYPTION_KEY_OPTION] = Environment.GetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME);
        }

        private static async Task ApplySecretProvider(Dictionary<string, string> commandlineOptions, CancellationToken cancellationToken)
            => FIXMEGlobal.SecretProvider = await SecretProviderHelper.ApplySecretProviderAsync([], [], commandlineOptions, TempFolder.SystemTempPath, FIXMEGlobal.SecretProvider, cancellationToken).ConfigureAwait(false);

        private static void ConfigureLogging(Dictionary<string, string> commandlineOptions)
        {
            //Log various information in the logfile
            if (DEBUG_MODE && !commandlineOptions.ContainsKey("log-file"))
            {
                var prefix = System.Reflection.Assembly.GetEntryAssembly().GetName().Name.StartsWith("Duplicati.Server") ? "server" : "trayicon";
                commandlineOptions["log-file"] = System.IO.Path.Combine(StartupPath, $"Duplicati-{prefix}.debug.log");
                commandlineOptions["log-level"] = Duplicati.Library.Logging.LogMessageType.Profiling.ToString();
                if (System.IO.File.Exists(commandlineOptions["log-file"]))
                {
                    System.IO.File.Delete(commandlineOptions["log-file"]);
                }
            }

            // Setup the log redirect
            Library.Logging.Log.StartScope(LogHandler, null);

            if (commandlineOptions.ContainsKey("log-file"))
            {
                var loglevel = Library.Logging.LogMessageType.Warning;
                if (commandlineOptions.ContainsKey("log-level"))
                    Enum.TryParse(commandlineOptions["log-level"], true, out loglevel);

                LogHandler.SetServerFile(commandlineOptions["log-file"], loglevel);
            }

            if (commandlineOptions.TryGetValue(WINDOWS_EVENTLOG_OPTION, out var source) && !string.IsNullOrEmpty(source))
            {
                if (!OperatingSystem.IsWindows())
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "WindowsLogNotSupported", null, Strings.Program.WindowsEventLogNotSupported);
                }
                else if (!WindowsEventLogSource.SourceExists(source))
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "WindowsLogMissing", null, Strings.Program.WindowsEventLogSourceNotFound(source));
                }
                else
                {
                    var loglevel = Library.Logging.LogMessageType.Information;
                    if (commandlineOptions.ContainsKey(WINDOWS_EVENTLOG_LEVEL_OPTION))
                        Enum.TryParse(commandlineOptions[WINDOWS_EVENTLOG_LEVEL_OPTION], true, out loglevel);

                    LogHandler.AppendLogDestination(new WindowsEventLogSource(source), loglevel);
                }
            }

            CrashlogHelper.OnUnobservedTaskException += (ex) => LogHandler.WriteMessage(new Library.Logging.LogEntry(ex.Message, null, Library.Logging.LogMessageType.Error, LOGTAG, "UnobservedTaskException", ex));
        }

        private static int ShowHelp(bool writeToConsoleOnExceptionw)
        {
            if (writeToConsoleOnExceptionw)
            {
                Console.WriteLine(Strings.Program.HelpDisplayDialog);

                foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                    Console.WriteLine(Strings.Program.HelpDisplayFormat(arg.Name, arg.LongDescription));

                return 0;
            }

            throw new Exception("Server invoked with --help");
        }

        public static string GetDataFolderPath(Dictionary<string, string> commandlineOptions)
        {
            var serverDataFolder = Environment.GetEnvironmentVariable(DATAFOLDER_ENV_NAME);
            if (commandlineOptions.ContainsKey("server-datafolder"))
                serverDataFolder = commandlineOptions["server-datafolder"];

            if (string.IsNullOrEmpty(serverDataFolder))
            {
                bool portableMode = commandlineOptions.ContainsKey("portable-mode")
                    ? Library.Utility.Utility.ParseBool(commandlineOptions["portable-mode"], true)
                    : (DEBUG_MODE ? true : false); // Default to portable mode in debug mode

                if (DEBUG_MODE && portableMode)
                {
                    //debug mode uses a lock file located in the app folder
                    return StartupPath;
                }
                else if (portableMode)
                {
                    //Portable mode uses a data folder in the application home dir
                    System.IO.Directory.SetCurrentDirectory(StartupPath);
                    return System.IO.Path.Combine(StartupPath, "data");
                }
                else
                {
                    //Normal release mode uses the systems "(Local) Application Data" folder
                    // %LOCALAPPDATA% on Windows, ~/.config on Linux, ~/Library/Application\ Support on MacOS
                    return DatabaseLocator.GetDefaultStorageFolder(SERVER_DATABASE_FILENAME, Library.AutoUpdater.AutoUpdateSettings.AppName);
                }
            }
            else
                return Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(serverDataFolder).Trim('"'));
        }

        public static Database.Connection GetDatabaseConnection(Dictionary<string, string> commandlineOptions, bool silentConsole)
        {
            DataFolder = GetDataFolderPath(commandlineOptions);
            CrashlogHelper.DefaultLogDir = DataFolder;

            var sqliteVersion = new Version(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteVersion);
            if (sqliteVersion < new Version(3, 6, 3))
            {
                //The official Mono SQLite provider is also broken with less than 3.6.3
                throw new Exception(Strings.Program.WrongSQLiteVersion(sqliteVersion, "3.6.3"));
            }

            //Create the connection instance
            var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection();

            try
            {
                DatabasePath = System.IO.Path.Combine(DataFolder, SERVER_DATABASE_FILENAME);

                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(DatabasePath)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath));

                // Attempt to open the database, removing any encryption present
                Duplicati.Library.SQLiteHelper.SQLiteLoader.OpenDatabase(con, DatabasePath, Library.SQLiteHelper.SQLiteRC4Decrypter.GetEncryptionPassword(commandlineOptions));

                Duplicati.Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, DatabasePath, typeof(Duplicati.Library.RestAPI.Database.DatabaseConnectionSchemaMarker));
            }
            catch (Exception ex)
            {
                //Unwrap the reflection exceptions
                if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                throw new Exception(Strings.Program.DatabaseOpenError(ex.Message), ex);
            }

            var disableDbEncryption = Library.Utility.Utility.ParseBoolOption(commandlineOptions, DISABLE_DB_ENCRYPTION_OPTION);
            var requireDbEncryptionKey = Library.Utility.Utility.ParseBoolOption(commandlineOptions, REQUIRE_DB_ENCRYPTION_KEY_OPTION);
            var encKey = EncryptedFieldHelper.KeyInstance.CreateKeyIfValid(commandlineOptions.GetValueOrDefault(SETTINGS_ENCRYPTION_KEY_OPTION));
            var usingBlacklistedKey = encKey?.IsBlacklisted ?? false;
            var hasValidEncryptionKey = encKey != null;

            FIXMEGlobal.SettingsEncryptionKeyProvidedExternally = hasValidEncryptionKey;

            if (requireDbEncryptionKey && !(hasValidEncryptionKey || disableDbEncryption))
                throw new UserInformationException(Strings.Program.DatabaseEncryptionKeyRequired(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION), "RequireDbEncryptionKey");

            if (!hasValidEncryptionKey)
            {
                try
                {
                    var hasEncryptedFields = false;
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText = @$"SELECT ""Value"" FROM ""Option"" WHERE ""Name"" = '{Database.ServerSettings.CONST.ENCRYPTED_FIELDS}' AND ""BackupID"" = {Connection.SERVER_SETTINGS_ID}";
                        hasEncryptedFields = Library.Utility.Utility.ParseBool(cmd.ExecuteScalar()?.ToString(), false);
                    }

                    if (hasEncryptedFields)
                    {
                        Library.Logging.Log.WriteWarningMessage(LOGTAG, "EncryptionKeyMissing", null, Strings.Program.EncryptionKeyMissing(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME));
                        if (!silentConsole)
                            Console.WriteLine(Strings.Program.EncryptionKeyMissing(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME));
                    }
                }
                catch
                {
                    // Ignore errors here, as we are just checking for a potential issue
                    // Only negative effect is that we do not show a potentially helpful warning
                }
            }

            if (!hasValidEncryptionKey && !disableDbEncryption)
            {
                disableDbEncryption = true;
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "MissingEncryptionKey", null, Strings.Program.NoEncryptionKeySpecified(Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
                if (!silentConsole)
                    Console.WriteLine(Strings.Program.NoEncryptionKeySpecified(Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
            }

            if (usingBlacklistedKey && !disableDbEncryption)
            {
                disableDbEncryption = true;
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "BlacklistedEncryptionKey", null, Strings.Program.BlacklistedEncryptionKey(Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
                if (!silentConsole)
                    Console.WriteLine(Strings.Program.BlacklistedEncryptionKey(Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
            }

            return new Database.Connection(con, disableDbEncryption, encKey);
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
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void LiveControl_StateChanged(object sender, EventArgs e)
        {
            var worker = FIXMEGlobal.WorkThread;
            switch (LiveControl.State)
            {
                case LiveControls.LiveControlState.Paused:
                    {
                        worker.Pause();
                        var t = worker.CurrentTask;
                        t?.Pause();
                        break;
                    }
                case LiveControls.LiveControlState.Running:
                    {
                        worker.Resume();
                        var t = worker.CurrentTask;
                        t?.Resume();
                        break;
                    }
                default:
                    throw new InvalidOperationException($"State of {nameof(LiveControl)} was not recognized!");
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
        /// The options related to the secret provider
        /// </summary>
        private static readonly IReadOnlyList<ICommandLineArgument> SECRET_PROVIDER_OPTIONS = new Options(new Dictionary<string, string>()).SupportedCommands.Where(x => x.Name.StartsWith("secret-provider")).ToList();

        /// <summary>
        /// Gets a list of all supported commandline options
        /// </summary>
        public static Library.Interface.ICommandLineArgument[] SupportedCommands
            => (OperatingSystem.IsWindows()
                ? new[] {
                    new Duplicati.Library.Interface.CommandLineArgument(WINDOWS_EVENTLOG_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.LogwindowseventlogShort, Strings.Program.LogwindowseventlogLong),
                    new Duplicati.Library.Interface.CommandLineArgument(WINDOWS_EVENTLOG_LEVEL_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LogwindowseventloglevelShort, Strings.Program.LogwindowseventloglevelLong, Library.Logging.LogMessageType.Information.ToString(), null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                }
                : []
            )
            .Concat([
                new Duplicati.Library.Interface.CommandLineArgument("tempdir", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.TempdirShort, Strings.Program.TempdirLong, System.IO.Path.GetTempPath()),
                new Duplicati.Library.Interface.CommandLineArgument("help", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.HelpCommandDescription, Strings.Program.HelpCommandDescription),
                new Duplicati.Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong2, "", ParameterFileOptionStrings),
                new Duplicati.Library.Interface.CommandLineArgument("portable-mode", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PortablemodeCommandDescription, Strings.Program.PortablemodeCommandDescription),
                new Duplicati.Library.Interface.CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.LogfileCommandDescription, Strings.Program.LogfileCommandDescription),
                new Duplicati.Library.Interface.CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LoglevelCommandDescription, Strings.Program.LoglevelCommandDescription, Library.Logging.LogMessageType.Warning.ToString(), null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBROOT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverWebrootDescription, Strings.Program.WebserverWebrootDescription, WebServerLoader.DEFAULT_OPTION_WEBROOT),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_PORT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverPortDescription, Strings.Program.WebserverPortDescription, WebServerLoader.DEFAULT_OPTION_PORT.ToString()),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLEHTTPS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverDisableHTTPSDescription, Strings.Program.WebserverDisableHTTPSDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_REMOVESSLCERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverRemoveCertificateDescription, Strings.Program.WebserverRemoveCertificateDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SSLCERTIFICATEFILE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificateFileDescription, Strings.Program.WebserverCertificateFileDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SSLCERTIFICATEFILEPASSWORD, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificatePasswordDescription, Strings.Program.WebserverCertificatePasswordDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_INTERFACE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverInterfaceDescription, Strings.Program.WebserverInterfaceDescription, WebServerLoader.DEFAULT_OPTION_INTERFACE),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_PASSWORD, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.WebserverPasswordDescription, Strings.Program.WebserverPasswordDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverAllowedhostnamesDescription, Strings.Program.WebserverAllowedhostnamesDescription, null, [WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT]),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_RESET_JWT_CONFIG, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverResetJwtConfigDescription, Strings.Program.WebserverResetJwtConfigDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLE_VISUAL_CAPTCHA, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverDisableVisualCaptchaDescription, Strings.Program.WebserverDisableVisualCaptchaDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_API_ONLY, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverApiOnlyDescription, Strings.Program.WebserverApiOnlyDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverDisableSigninTokensDescription, Strings.Program.WebserverDisableSigninTokensDescription),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SPAPATHS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverSpaPathsDescription, Strings.Program.WebserverSpaPathsDescription, WebServerLoader.DEFAULT_OPTION_SPAPATHS),
                new Duplicati.Library.Interface.CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_TIMEZONE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.WebserverTimezoneDescription, Strings.Program.WebserverTimezoneDescription, TimeZoneHelper.GetLocalTimeZone(), null, TimeZoneHelper.GetTimeZones().Select(x => x.Id).ToArray()),
                new Duplicati.Library.Interface.CommandLineArgument(PING_PONG_KEEPALIVE_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.PingpongkeepaliveShort, Strings.Program.PingpongkeepaliveLong),
                new Duplicati.Library.Interface.CommandLineArgument("log-retention", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.Program.LogretentionShort, Strings.Program.LogretentionLong, DEFAULT_LOG_RETENTION),
                new Duplicati.Library.Interface.CommandLineArgument("server-datafolder", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ServerdatafolderShort, Strings.Program.ServerdatafolderLong(DATAFOLDER_ENV_NAME), System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName)),
                new Duplicati.Library.Interface.CommandLineArgument(DISABLE_DB_ENCRYPTION_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.DisabledbencryptionShort, Strings.Program.DisabledbencryptionLong),
                new Duplicati.Library.Interface.CommandLineArgument(REQUIRE_DB_ENCRYPTION_KEY_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.RequiredbencryptionShort, Strings.Program.RequiredbencryptionLong),
                new Duplicati.Library.Interface.CommandLineArgument(SETTINGS_ENCRYPTION_KEY_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Password, Strings.Program.SettingsencryptionkeyShort, Strings.Program.SettingsencryptionkeyLong(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME)),
            ])
            .Concat(SECRET_PROVIDER_OPTIONS)
            .ToArray();

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

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs, (key, value) =>
                {
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
