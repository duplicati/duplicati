// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Crashlog;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.WebserverCore;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Duplicati.Server
{
    public class Program
    {
        private const string PARAMETERS_FILE_OPTION = "parameters-file";
        private static readonly string[] PARAMETERS_FILE_OPTION_EXTRAS = ["parameterfile"];
        private const string PING_PONG_KEEPALIVE_OPTION = "ping-pong-keepalive";
        private const string WINDOWS_EVENTLOG_OPTION = "windows-eventlog";
        private const string WINDOWS_EVENTLOG_LEVEL_OPTION = "windows-eventlog-level";
        private const string DISABLE_DB_ENCRYPTION_OPTION = "disable-db-encryption";
        private const string REQUIRE_DB_ENCRYPTION_KEY_OPTION = "require-db-encryption-key";
        private const string SETTINGS_ENCRYPTION_KEY_OPTION = "settings-encryption-key";
        private const string DISABLE_UPDATE_CHECK_OPTION = "disable-update-check";
        private const string REGISTER_REMOTE_CONTROL_OPTION = "register-remote-control";
        private const string REGISTER_REMOTE_CONTROL_REREGISTER_OPTION = "register-remote-control-force";
        private const string LOG_FILE_OPTION = "log-file";
        private const string LOG_LEVEL_OPTION = "log-level";
        private const string LOG_CONSOLE_OPTION = "log-console";
        private const string TEMPDIR_OPTION = "tempdir";
        private const string LOG_RETENTION_OPTION = "log-retention";
        private const string HELP_OPTION = "help";


#if DEBUG
        private const bool DEBUG_MODE = true;
#else
        private const bool DEBUG_MODE = false;
#endif

        /// <summary>
        /// Options to be ignored when validating the command line
        /// </summary>
        public static readonly HashSet<string> ValidationIgnoredOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The log tag for messages from this class
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<Program>();
        /// <summary>
        /// The path to the directory that contains the main executable
        /// </summary>
        public static readonly string StartupPath = Duplicati.Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR;

        /// <summary>
        /// The environment variable prefix
        /// </summary>
        private static readonly string ENV_NAME_PREFIX = AutoUpdateSettings.AppName.ToUpperInvariant();

        /// <summary>
        /// The single instance
        /// </summary>
        public static SingleInstance ApplicationInstance = null;

        /// <summary>
        /// The thread running the ping-pong handler
        /// </summary>
        private static Thread PingPongThread;

        /// <summary>
        /// The controller interface for pause/resume and throttle options
        /// </summary>
        public static LiveControls LiveControl { get => DuplicatiWebserver.Provider.GetRequiredService<LiveControls>(); }

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
        /// An event that is set once the server is ready to respond to requests
        /// </summary>
        public static readonly ManualResetEvent ServerStartedEvent = new ManualResetEvent(false);

        /// <summary>
        /// Timer for purging temp files and log data
        /// </summary>
        private static System.Threading.Timer PurgeTempFilesTimer = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="_args"> The command line arguments</param>
        [STAThread]
        public static int Main(string[] _args)
            => Main(null, _args);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="applicationSettings"> The application settings</param>
        /// <param name="_args"> The command line arguments</param>
        [STAThread]
        public static int Main(IApplicationSettings applicationSettings, string[] _args)
        {
            PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, PackageHelper.NamedExecutable.Server, out var preloadDbSettings);

            applicationSettings ??= new ApplicationSettings();

            //If this executable is invoked directly, write to console, otherwise throw exceptions
            var writeToConsoleOnException = applicationSettings.Origin == "Server";

            // Prepared for the future, where we might want to have a silent console mode
            var silentConsole = false;
            var logMessageToConsole = (string message) => { if (!silentConsole) Console.WriteLine(message); };

            //Find commandline options here for handling special startup cases
            var args = new List<string>(_args);
            var optionsWithFilter = FilterCollector.ExtractOptions(new List<string>(args));
            var commandlineOptions = optionsWithFilter.Item1;
            var filter = optionsWithFilter.Item2;

            if (HelpOptionExtensions.IsArgumentAnyHelpString(args))
            {
                return ShowHelp(writeToConsoleOnException);
            }

            if (commandlineOptions.ContainsKey("tempdir") && !string.IsNullOrEmpty(commandlineOptions["tempdir"]))
            {
                SystemContextSettings.DefaultTempPath = commandlineOptions["tempdir"];
            }

            SystemContextSettings.StartSession();

            ApplyEnvironmentVariables(commandlineOptions);
            ApplySecretProvider(applicationSettings, commandlineOptions, CancellationToken.None).Await();

            var parameterFileOption = PARAMETERS_FILE_OPTION_EXTRAS.Prepend(PARAMETERS_FILE_OPTION)
                .FirstOrDefault(x => commandlineOptions.ContainsKey(x));

            if (parameterFileOption != null && !string.IsNullOrEmpty(commandlineOptions[parameterFileOption]))
            {
                string filename = commandlineOptions[parameterFileOption];
                commandlineOptions.Remove(parameterFileOption);
                if (!ReadOptionsFromFile(filename, ref filter, args, commandlineOptions))
                    return 100;
            }

            var logHandler = new LogWriteHandler();
            using var logScope = ConfigureLogging(logHandler, commandlineOptions);

            // Validate after logging is configured
            CommandLineArgumentValidator.ValidateArguments(SupportedCommands, commandlineOptions, KnownDuplicateOptions, ValidationIgnoredOptions);

            var crashed = false;
            var terminated = false;
            IQueueRunnerService queueRunner = null;
            UpdatePollThread updatePollThread = null;
            EventPollNotify eventPollNotify = null;
            ISchedulerService scheduler = null;
            try
            {
                var connection = GetDatabaseConnection(applicationSettings, commandlineOptions, silentConsole);

                if (!connection.ApplicationSettings.FixedInvalidBackupId)
                    connection.FixInvalidBackupId();

                connection.ApplicationSettings.UpgradePasswordToKBDF();
                CreateApplicationInstance(applicationSettings.DataFolder, writeToConsoleOnException);

                applicationSettings.StartOrStopUsageReporter = () => StartOrStopUsageReporter(connection);
                applicationSettings.StartOrStopUsageReporter?.Invoke();

                AdjustApplicationSettings(connection, commandlineOptions);

                UpdaterManager.OnError += obj =>
                {
                    connection.LogError(null, "Error in updater", obj);
                };

                DuplicatiWebserver = StartWebServer(commandlineOptions, connection, logHandler, applicationSettings).Await();

                connection.SetServiceProvider(DuplicatiWebserver.Provider);
                queueRunner = DuplicatiWebserver.Provider.GetRequiredService<IQueueRunnerService>();
                updatePollThread = DuplicatiWebserver.Provider.GetRequiredService<UpdatePollThread>();
                eventPollNotify = DuplicatiWebserver.Provider.GetRequiredService<EventPollNotify>();
                scheduler = DuplicatiWebserver.Provider.GetRequiredService<ISchedulerService>();

                updatePollThread.Init(Library.Utility.Utility.ParseBoolOption(commandlineOptions, DISABLE_UPDATE_CHECK_OPTION));

                SetPurgeTempFilesTimer(connection, commandlineOptions);

                LiveControl.StateChanged = (e) => { LiveControl_StateChanged(queueRunner, connection, eventPollNotify, e); };

                if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, PING_PONG_KEEPALIVE_OPTION))
                {
                    PingPongThread = new Thread(() => PingPongMethod(applicationSettings)) { IsBackground = true };
                    PingPongThread.Start();
                }

                connection.ReWriteAllFieldsIfEncryptionChanged();
                connection.SetPreloadSettingsIfChanged(preloadDbSettings);
                EmitWarningsForConfigurationIssues(connection, applicationSettings, commandlineOptions);

                Log.WriteInformationMessage(LOGTAG, "ServerStarted", Strings.Program.ServerStarted(DuplicatiWebserver.Interface, DuplicatiWebserver.Port));
                logMessageToConsole(Strings.Program.ServerStarted(DuplicatiWebserver.Interface, DuplicatiWebserver.Port));

                var remoteControlUrl = commandlineOptions.GetValueOrDefault(REGISTER_REMOTE_CONTROL_OPTION);
                if (!string.IsNullOrWhiteSpace(remoteControlUrl))
                    RegisterForRemoteControl(DuplicatiWebserver.Provider.GetRequiredService<IRemoteControllerRegistration>(), DuplicatiWebserver.Provider.GetRequiredService<IRemoteController>(), remoteControlUrl, Library.Utility.Utility.ParseBoolOption(commandlineOptions, REGISTER_REMOTE_CONTROL_REREGISTER_OPTION), logMessageToConsole);

                if (applicationSettings.Origin == "Server" && connection.ApplicationSettings.AutogeneratedPassphrase)
                {
                    var signinToken = DuplicatiWebserver.Provider.GetRequiredService<IJWTTokenProvider>().CreateSigninToken("server-cli");
                    var hostname = (connection.ApplicationSettings.AllowedHostnames ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x != "*") ?? "localhost";
                    var scheme = connection.ApplicationSettings.UseHTTPS ? "https" : "http";

                    var url = $"{scheme}://{hostname}:{DuplicatiWebserver.Port}/signin.html?token={signinToken}";
                    Log.WriteWarningMessage(LOGTAG, "ServerStartedSignin", null, Strings.Program.ServerStartedSignin(url));
                    logMessageToConsole(Strings.Program.ServerStartedSignin(url));
                }

                DuplicatiWebserver.TerminationTask.ContinueWith((t) =>
                {
                    if (t.Exception != null)
                    {
                        Log.WriteWarningMessage(LOGTAG, "ServerCrashed", t.Exception, Strings.Program.ServerCrashed(t.Exception.Message));
                        logMessageToConsole(Strings.Program.ServerStartedSignin(Strings.Program.ServerCrashed(t.Exception.ToString())));
                    }

                    terminated = true;
                    applicationSettings.ApplicationExitEvent.Set();
                });

                var stopCounter = 0;
                Console.CancelKeyPress += (sender, e) =>
                {
                    if (Interlocked.Increment(ref stopCounter) <= 1)
                    {
                        Log.WriteInformationMessage(LOGTAG, "CancelKeyPressed", "Cancel key pressed, stopping server");
                        Task.Run(() => DuplicatiWebserver?.Stop());
                    }
                    else
                    {
                        Log.WriteWarningMessage(LOGTAG, "CancelKeyPressed", null, "Cancel key pressed twice, terminating now");
                        if (OperatingSystem.IsWindows())
                            Environment.FailFast("Cancel key pressed twice, terminating now");
                        else
                            Environment.Exit(0);
                    }
                };

                ServerStartedEvent.Set();
                applicationSettings.ApplicationExitEvent.WaitOne();
            }
            catch (SingleInstance.MultipleInstanceException mex)
            {
                crashed = true;
                Log.WriteErrorMessage(LOGTAG, "MultipleInstanceError", mex, Strings.Program.ServerCrashed(mex.Message));
                System.Diagnostics.Trace.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                if (!writeToConsoleOnException) throw;

                Console.WriteLine(Strings.Program.SeriousError(mex.ToString()));
                return 100;
            }
            catch (Exception ex)
            {
                crashed = true;
                Log.WriteErrorMessage(LOGTAG, "ServerCrashed", ex, Strings.Program.ServerCrashed(ex.Message));
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
                Log.WriteInformationMessage(LOGTAG, "ServerStopping", Strings.Program.ServerStopping);

                var steps = new Action[] {
                    () => eventPollNotify?.SignalNewEvent(),
                    () => ShutdownModernWebserver(),
                    () => updatePollThread?.Terminate(),
                    () => scheduler?.Terminate(true),
                    () => queueRunner?.Terminate(true),
                    () => ApplicationInstance?.Dispose(),
                    () => PurgeTempFilesTimer?.Dispose(),
                    () => Library.UsageReporter.Reporter.ShutDown(),
                    () => PingPongThread?.Interrupt(),
                    () =>
                    {
                        Log.WriteInformationMessage(LOGTAG, "ServerStopped", Strings.Program.ServerStopped);
                        logHandler?.Dispose();
                    }
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

        private static async Task<DuplicatiWebserver> StartWebServer(IReadOnlyDictionary<string, string> options, Connection connection, ILogWriteHandler logWriteHandler, IApplicationSettings applicationSettings)
        {
            var server = await WebServerLoader.TryRunServer(options, connection, async parsedOptions =>
            {
                var mappedSettings = new DuplicatiWebserver.InitSettings(
                    parsedOptions.WebRoot,
                    parsedOptions.Port,
                    parsedOptions.Interface,
                    parsedOptions.Certificate,
                    parsedOptions.AllowedHostnames,
                    parsedOptions.DisableStaticFiles,
                    parsedOptions.TokenLifetimeInMinutes,
                    parsedOptions.SPAPaths,
                    parsedOptions.CorsOrigins,
                    parsedOptions.PreAuthTokens
                );

                var server = DuplicatiWebserver.CreateWebServer(mappedSettings, connection, logWriteHandler, applicationSettings);

                // Start the server, but catch any configuration issues
                var task = server.Start();
                await Task.WhenAny(task, Task.Delay(500));
                if (task.IsCompleted)
                    await task;

                return server;
            }).ConfigureAwait(false);

            connection.ApplicationSettings.ServerPortChanged |= server.Port != connection.ApplicationSettings.LastWebserverPort;
            connection.ApplicationSettings.LastWebserverPort = server.Port;

            return server;
        }

        private static void SetPurgeTempFilesTimer(Connection connection, Dictionary<string, string> commandlineOptions)
        {
            var lastPurge = new DateTime(0);

            TimerCallback purgeTempFilesCallback = (x) =>
            {
                try
                {
                    if (Math.Abs((DateTime.Now - lastPurge).TotalHours) < (DEBUG_MODE ? 1 : 23))
                    {
                        return;
                    }

                    lastPurge = DateTime.Now;

                    foreach (var e in connection.GetTempFiles().Where((f) => f.Expires < DateTime.Now))
                    {
                        try
                        {
                            if (System.IO.File.Exists(e.Path))
                                System.IO.File.Delete(e.Path);
                        }
                        catch (Exception ex)
                        {
                            connection.LogError(null, $"Failed to delete temp file: {e.Path}", ex);
                        }

                        connection.DeleteTempFile(e.ID);
                    }


                    Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) =>
                    {
                        connection.LogError(null, $"Failed to delete temp file: {path}", ex);
                    });

                    if (!commandlineOptions.TryGetValue("log-retention", out string pts))
                    {
                        pts = DEFAULT_LOG_RETENTION;
                    }

                    connection.PurgeLogData(Library.Utility.Timeparser.ParseTimeInterval(pts, DateTime.Now, true));
                }
                catch (Exception ex)
                {
                    connection.LogError(null, "Failed during temp file cleanup", ex);
                }
            };

            PurgeTempFilesTimer =
                new System.Threading.Timer(purgeTempFilesCallback, null,
                    DEBUG_MODE ? TimeSpan.FromSeconds(10) : TimeSpan.FromHours(1),
                    DEBUG_MODE ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1));
        }

        private static void RegisterForRemoteControl(IRemoteControllerRegistration remoteControllerRegistration, IRemoteController remoteController, string remoteControlUrl, bool reRegister, Action<string> logMessageToConsole)
        {
            if (remoteController.CanEnable)
            {
                if (!reRegister)
                {
                    Log.WriteInformationMessage(LOGTAG, "RemoteControlAlreadyRegistered", Strings.Program.RemoteControlAlreadyRegistered);
                    return;
                }

                remoteController.DeleteRegistration();
            }

            Task.Run(async () =>
            {
                try
                {
                    var regTask = remoteControllerRegistration.RegisterMachine(remoteControlUrl);
                    while (!regTask.IsCompleted)
                    {
                        // Interface does not have events, so poll it every second
                        await Task.WhenAny(Task.Delay(1000), regTask).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(remoteControllerRegistration.RegistrationUrl))
                        {
                            Log.WriteWarningMessage(LOGTAG, "RemoteControlRegistrationUrl", null, Strings.Program.RemoteControlRegistrationUrl(remoteControllerRegistration.RegistrationUrl));
                            logMessageToConsole(Strings.Program.RemoteControlRegistrationUrl(remoteControllerRegistration.RegistrationUrl));
                            break;
                        }
                    }

                    // Await the registration task and ensure any errors are logged
                    await regTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RemoteControlRegistrationFailed", ex, Strings.Program.RemoteControlRegistrationFailed(ex.Message));
                }
            });
        }

        private static void AdjustApplicationSettings(Connection connection, Dictionary<string, string> commandlineOptions)
        {
            // This clears the JWT config, and a new will be generated, invalidating all existing tokens
            if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, WebServerLoader.OPTION_WEBSERVICE_RESET_JWT_CONFIG))
            {
                connection.ApplicationSettings.JWTConfig = null;
                // Clean up stored tokens as they are now invalid
                connection.ExecuteWithCommand((con) => con.ExecuteNonQuery("DELETE FROM TokenFamily"));
            }

            if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, WebServerLoader.OPTION_WEBSERVICE_ENABLE_FOREVER_TOKEN))
                connection.ApplicationSettings.EnableForeverTokens();

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS))
                connection.ApplicationSettings.DisableSigninTokens = Library.Utility.Utility.ParseBool(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS], true);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_DISABLEAPIEXTENSIONS))
                connection.ApplicationSettings.DisabledAPIExtensions = commandlineOptions.GetValueOrDefault(WebServerLoader.OPTION_WEBSERVICE_DISABLEAPIEXTENSIONS)?
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_PASSWORD))
                connection.ApplicationSettings.SetWebserverPassword(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_PASSWORD]);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES))
                connection.ApplicationSettings.SetAllowedHostnames(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES]);
            else if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT))
                connection.ApplicationSettings.SetAllowedHostnames(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT]);

            if (commandlineOptions.ContainsKey(WebServerLoader.OPTION_WEBSERVICE_TIMEZONE) && !string.IsNullOrEmpty(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]))
                try
                {
                    connection.ApplicationSettings.Timezone = TimeZoneHelper.FindTimeZone(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]);
                }
                catch (Exception ex)
                {
                    throw new UserInformationException(Strings.Program.InvalidTimezone(commandlineOptions[WebServerLoader.OPTION_WEBSERVICE_TIMEZONE]), "InvalidTimeZone", ex);
                }

            // The database has recorded a new version
            if (connection.ApplicationSettings.UpdatedVersion != null)
            {
                // Check if the running version is newer than the recorded version
                if (UpdaterManager.TryParseVersion(connection.ApplicationSettings.UpdatedVersion.Version) <= UpdaterManager.TryParseVersion(UpdaterManager.SelfVersion.Version))
                {
                    // Clean up lingering update notifications
                    var updateNotifications = connection.GetNotifications().Where(x => x.Action == "update:new").ToList();
                    foreach (var n in updateNotifications)
                        connection.DismissNotification(n.ID);

                    // Clear up the recorded version
                    connection.ApplicationSettings.UpdatedVersion = null;
                }
            }
        }

        private static void EmitWarningsForConfigurationIssues(Connection connection, IApplicationSettings applicationSettings, Dictionary<string, string> commandlineOptions)
        {
            if (connection.ApplicationSettings.LastConfigIssueCheckVersion != UpdaterManager.SelfVersion.Version)
            {
                var updateNotifications = connection.GetNotifications().Where(x => x.Action.StartsWith("config:issue:")).ToList();
                foreach (var n in updateNotifications)
                    connection.DismissNotification(n.ID);

                if (!connection.IsEncryptingFields && !Library.Utility.Utility.ParseBoolOption(commandlineOptions, DISABLE_DB_ENCRYPTION_OPTION))
                {
                    connection.RegisterNotification(
                        Serialization.NotificationType.Warning,
                        "Unencrypted database",
                        "The database is not encrypted. This is a security risk and should be fixed as soon as possible.",
                        null,
                        null,
                        "config:issue:unencrypted-database",
                        null,
                        "UnencryptedDatabase",
                        null,
                        (self, all) =>
                        {
                            return all.FirstOrDefault(x => x.Action == "config:issue:unencrypted-database") ?? self;
                        }
                    );
                }

                if (OperatingSystem.IsWindows() && applicationSettings.DataFolder.StartsWith(Util.AppendDirSeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase))
                {
                    connection.RegisterNotification(
                        Serialization.NotificationType.Warning,
                        "Incorrect storage folder",
                        "The server configuraion is stored inside the Windows folder. Please move the configuration to a different location, or it may be deleted on Windows version upgrades.",
                        null,
                        null,
                        "config:issue:windows-folder-used",
                        null,
                        "UnencryptedDatabase",
                        null,
                        (self, all) =>
                        {
                            return all.FirstOrDefault(x => x.Action == "config:issue:windows-folder-used") ?? self;
                        }
                    );
                }

                connection.ApplicationSettings.LastConfigIssueCheckVersion = UpdaterManager.SelfVersion.Version;
            }
        }

        private static void CreateApplicationInstance(string dataFolder, bool writeToConsoleOnExceptionw)
        {
            try
            {
                //This will also create DATAFOLDER if it does not exist
                ApplicationInstance = new SingleInstance(dataFolder);
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

        private static async Task ApplySecretProvider(IApplicationSettings applicationSettings, Dictionary<string, string> commandlineOptions, CancellationToken cancellationToken)
            => applicationSettings.SecretProvider = await SecretProviderHelper.ApplySecretProviderAsync([], [], commandlineOptions, TempFolder.SystemTempPath, applicationSettings.SecretProvider, cancellationToken).ConfigureAwait(false);

        private class ConsoleLogDestination(LogMessageType level) : ILogDestination
        {
            public void WriteMessage(LogEntry entry)
            {
                if (entry.Level >= level)
                    Console.WriteLine(entry.AsString(true));
            }
        }

        private static IDisposable ConfigureLogging(ILogWriteHandler logWriteHandler, Dictionary<string, string> commandlineOptions)
        {
            IDisposable logScope;
            //Log various information in the logfile
            if (DEBUG_MODE && !commandlineOptions.ContainsKey(LOG_FILE_OPTION))
            {
                var prefix = System.Reflection.Assembly.GetEntryAssembly().GetName().Name.StartsWith("Duplicati.Server") ? "server" : "trayicon";
                commandlineOptions[LOG_FILE_OPTION] = System.IO.Path.Combine(StartupPath, $"Duplicati-{prefix}.debug.log");
                commandlineOptions[LOG_LEVEL_OPTION] = Duplicati.Library.Logging.LogMessageType.Profiling.ToString();
                if (System.IO.File.Exists(commandlineOptions[LOG_FILE_OPTION]))
                    System.IO.File.Delete(commandlineOptions[LOG_FILE_OPTION]);
            }

            logScope = Log.StartScope(logWriteHandler, null);

            if (commandlineOptions.ContainsKey(LOG_FILE_OPTION))
            {
                var loglevel = Library.Logging.LogMessageType.Warning;
                if (commandlineOptions.ContainsKey(LOG_LEVEL_OPTION))
                    Enum.TryParse(commandlineOptions[LOG_LEVEL_OPTION], true, out loglevel);

                logWriteHandler.SetServerFile(commandlineOptions[LOG_FILE_OPTION], loglevel);
            }

            if (Library.Utility.Utility.ParseBoolOption(commandlineOptions, LOG_CONSOLE_OPTION))
            {
                var loglevel = Library.Logging.LogMessageType.Information;
                if (commandlineOptions.ContainsKey(LOG_LEVEL_OPTION))
                    Enum.TryParse(commandlineOptions[LOG_LEVEL_OPTION], true, out loglevel);

                logWriteHandler.AppendLogDestination(new ConsoleLogDestination(loglevel), loglevel);
            }

            if (commandlineOptions.TryGetValue(WINDOWS_EVENTLOG_OPTION, out var source) && !string.IsNullOrEmpty(source))
            {
                if (!OperatingSystem.IsWindows())
                {
                    Log.WriteWarningMessage(LOGTAG, "WindowsLogNotSupported", null, Strings.Program.WindowsEventLogNotSupported);
                }
                else
                {
                    if (!WindowsEventLogSource.SourceExists(source))
                    {
                        Log.WriteInformationMessage(LOGTAG, "WindowsLogMissingCreating", null, Strings.Program.WindowsEventLogSourceNotFound(source));
                        try
                        {
                            WindowsEventLogSource.CreateEventSource(source);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "WindowsLogFailedCreate", ex, Strings.Program.WindowsEventLogSourceNotCreated(source));
                        }
                    }

                    if (WindowsEventLogSource.SourceExists(source))
                    {
                        var loglevel = LogMessageType.Information;
                        if (commandlineOptions.ContainsKey(WINDOWS_EVENTLOG_LEVEL_OPTION))
                            Enum.TryParse(commandlineOptions[WINDOWS_EVENTLOG_LEVEL_OPTION], true, out loglevel);

                        logWriteHandler.AppendLogDestination(new WindowsEventLogSource(source), loglevel);
                    }
                }
            }

            CrashlogHelper.OnUnobservedTaskException += (ex) => logWriteHandler.WriteMessage(new LogEntry(ex.Message, null, Library.Logging.LogMessageType.Error, LOGTAG, "UnobservedTaskException", ex));
            return logScope;
        }

        private static int ShowHelp(bool writeToConsoleOnExceptionw)
        {
            if (writeToConsoleOnExceptionw)
            {
                Console.WriteLine(Strings.Program.HelpDisplayDialog);

                foreach (var arg in SupportedCommands)
                    Console.WriteLine(Strings.Program.HelpDisplayFormat(arg.Name, arg.LongDescription));

                return 0;
            }

            throw new Exception("Server invoked with --help");
        }

        public static Connection GetDatabaseConnection(IApplicationSettings applicationSettings, Dictionary<string, string> commandlineOptions, bool silentConsole)
        {
            // Emit a warning if the database is stored in the Windows folder
            if (Util.IsPathUnderWindowsFolder(applicationSettings.DataFolder))
                Log.WriteWarningMessage(LOGTAG, "DatabaseInWindowsFolder", null, "The database is stored in the Windows folder, this is not recommended as it will be deleted on Windows upgrades.");

            CrashlogHelper.DefaultLogDir = applicationSettings.DataFolder;

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
                var databasePath = System.IO.Path.Combine(applicationSettings.DataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);

                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(databasePath)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(databasePath));

                // Attempt to open the database, removing any encryption present
                Library.SQLiteHelper.SQLiteLoader.OpenDatabase(con, databasePath, Library.SQLiteHelper.SQLiteRC4Decrypter.GetEncryptionPassword(commandlineOptions));

                Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, databasePath, typeof(Library.RestAPI.Database.DatabaseSchemaMarker));
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

            applicationSettings.SettingsEncryptionKeyProvidedExternally = hasValidEncryptionKey;

            if (requireDbEncryptionKey && !(hasValidEncryptionKey || disableDbEncryption))
                throw new UserInformationException(Strings.Program.DatabaseEncryptionKeyRequired(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION), "RequireDbEncryptionKey");

            if (!hasValidEncryptionKey)
            {
                try
                {
                    var hasEncryptedFields = false;
                    using (var cmd = con.CreateCommand(@$"SELECT ""Value"" FROM ""Option"" WHERE ""Name"" = @Name AND ""BackupID"" = @BackupId"))
                        hasEncryptedFields = Library.Utility.Utility.ParseBool(cmd
                            .SetParameterValue("@Name", Database.ServerSettings.CONST.ENCRYPTED_FIELDS)
                            .SetParameterValue("@BackupId", Connection.SERVER_SETTINGS_ID).ExecuteScalar()?.ToString(), false);

                    if (hasEncryptedFields)
                    {
                        Log.WriteWarningMessage(LOGTAG, "EncryptionKeyMissing", null, Strings.Program.EncryptionKeyMissing(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME));
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
                Log.WriteWarningMessage(LOGTAG, "MissingEncryptionKey", null, Strings.Program.NoEncryptionKeySpecified(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
                if (!silentConsole)
                    Console.WriteLine(Strings.Program.NoEncryptionKeySpecified(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
            }

            if (usingBlacklistedKey && !disableDbEncryption)
            {
                disableDbEncryption = true;
                Log.WriteErrorMessage(LOGTAG, "BlacklistedEncryptionKey", null, Strings.Program.BlacklistedEncryptionKey(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
                if (!silentConsole)
                    Console.WriteLine(Strings.Program.BlacklistedEncryptionKey(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, DISABLE_DB_ENCRYPTION_OPTION));
            }

            return new Connection(con, disableDbEncryption, encKey, applicationSettings.DataFolder, applicationSettings.StartOrStopUsageReporter);
        }

        private static void StartOrStopUsageReporter(Connection connection)
        {
            var disableUsageReporter =
                string.Equals(connection.ApplicationSettings.UsageReporterLevel, "none", StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(connection.ApplicationSettings.UsageReporterLevel, "disabled", StringComparison.OrdinalIgnoreCase);

            if (!Enum.TryParse<Library.UsageReporter.ReportType>(connection.ApplicationSettings.UsageReporterLevel, true, out var reportLevel))
                Library.UsageReporter.Reporter.SetReportLevel(null, disableUsageReporter);
            else
                Library.UsageReporter.Reporter.SetReportLevel(reportLevel, disableUsageReporter);
        }

        /// <summary>
        /// This event handler updates the trayicon menu with the current state of the runner.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void LiveControl_StateChanged(IQueueRunnerService queueRunnerService, Connection connection, EventPollNotify eventPollNotify, LiveControls.LiveControlEvent e)
        {
            var appSettings = connection.ApplicationSettings;
            switch (e.State)
            {
                case LiveControls.LiveControlState.Paused:
                    {
                        queueRunnerService.Pause();
                        queueRunnerService.GetCurrentTask()?.Pause(e.TransfersPaused);
                        appSettings.PausedUntil = e.WaitTimeExpiration;
                        break;
                    }
                case LiveControls.LiveControlState.Running:
                    {
                        queueRunnerService.Resume();
                        queueRunnerService.GetCurrentTask()?.Resume();
                        appSettings.PausedUntil = null;
                        break;
                    }
                default:
                    Log.WriteWarningMessage(LOGTAG, "InvalidPauseResumeState", null, Strings.Program.InvalidPauseResumeState(LiveControl.State));
                    break;
            }

            eventPollNotify.SignalNewEvent();

        }

        /// <summary>
        /// Simple method for tracking if the server has crashed
        /// </summary>
        /// <param name="applicationSettings">The application settings</param>
        private static void PingPongMethod(IApplicationSettings applicationSettings)
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
                    applicationSettings.ApplicationExitEvent.Set();
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
        /// Gets additional commandline arguments support on Windows
        /// </summary>
        private static readonly ICommandLineArgument[] WindowsOptions = OperatingSystem.IsWindows()
            ? [
                new CommandLineArgument(WINDOWS_EVENTLOG_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.LogwindowseventlogShort, Strings.Program.LogwindowseventlogLong),
                new CommandLineArgument(WINDOWS_EVENTLOG_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LogwindowseventloglevelShort, Strings.Program.LogwindowseventloglevelLong, Library.Logging.LogMessageType.Information.ToString(), null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
            ]
            : [];

        /// <summary>
        /// Gets a list of all supported commandline options
        /// </summary>
        public static ICommandLineArgument[] SupportedCommands =>
        [
            .. WindowsOptions,
            new CommandLineArgument(TEMPDIR_OPTION, CommandLineArgument.ArgumentType.Path, Strings.Program.TempdirShort, Strings.Program.TempdirLong, System.IO.Path.GetTempPath()),
            new CommandLineArgument(HELP_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.HelpCommandDescription, Strings.Program.HelpCommandDescription),
            new CommandLineArgument(PARAMETERS_FILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong, "", PARAMETERS_FILE_OPTION_EXTRAS),
            new CommandLineArgument(DataFolderManager.PORTABLE_MODE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.PortablemodeCommandDescription, Strings.Program.PortablemodeCommandDescription, DataFolderManager.PORTABLE_MODE.ToString().ToLowerInvariant()),
            new CommandLineArgument(LOG_FILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.Program.LogfileCommandDescription, Strings.Program.LogfileCommandDescription),
            new CommandLineArgument(LOG_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Program.LoglevelCommandDescription, Strings.Program.LoglevelCommandDescription, Library.Logging.LogMessageType.Warning.ToString(), null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
            new CommandLineArgument(LOG_CONSOLE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.LogConsoleDescription, Strings.Program.LogConsoleDescription, false.ToString()),
            new CommandLineArgument(WebServerLoader.OPTION_WEBROOT, CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverWebrootDescription, Strings.Program.WebserverWebrootDescription, WebServerLoader.DEFAULT_OPTION_WEBROOT),
            new CommandLineArgument(WebServerLoader.OPTION_PORT, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverPortDescription, Strings.Program.WebserverPortDescription, WebServerLoader.DEFAULT_OPTION_PORT.ToString()),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLEHTTPS, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverDisableHTTPSDescription, Strings.Program.WebserverDisableHTTPSDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_REMOVESSLCERTIFICATE, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverRemoveCertificateDescription, Strings.Program.WebserverRemoveCertificateDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SSLCERTIFICATEFILE, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificateFileDescription, Strings.Program.WebserverCertificateFileDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SSLCERTIFICATEFILEPASSWORD, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverCertificatePasswordDescription, Strings.Program.WebserverCertificatePasswordDescription),
            new CommandLineArgument(WebServerLoader.OPTION_INTERFACE, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverInterfaceDescription, Strings.Program.WebserverInterfaceDescription, WebServerLoader.DEFAULT_OPTION_INTERFACE),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_PASSWORD, CommandLineArgument.ArgumentType.Password, Strings.Program.WebserverPasswordDescription, Strings.Program.WebserverPasswordDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverAllowedhostnamesDescription, Strings.Program.WebserverAllowedhostnamesDescription, null, [WebServerLoader.OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT]),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_RESET_JWT_CONFIG, CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverResetJwtConfigDescription, Strings.Program.WebserverResetJwtConfigDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_ENABLE_FOREVER_TOKEN, CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverEnableForeverTokenDescription, Strings.Program.WebserverEnableForeverTokenDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLEAPIEXTENSIONS, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverDisableApiExtensionsDescription, Strings.Program.WebserverDisableApiExtensionsDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_API_ONLY, CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverApiOnlyDescription, Strings.Program.WebserverApiOnlyDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS, CommandLineArgument.ArgumentType.Boolean, Strings.Program.WebserverDisableSigninTokensDescription, Strings.Program.WebserverDisableSigninTokensDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_SPAPATHS, CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverSpaPathsDescription, Strings.Program.WebserverSpaPathsDescription, WebServerLoader.DEFAULT_OPTION_SPAPATHS),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_TIMEZONE, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverTimezoneDescription, Strings.Program.WebserverTimezoneDescription, TimeZoneHelper.GetLocalTimeZone(), null, TimeZoneHelper.GetTimeZones().Select(x => x.Id).ToArray()),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_CORS_ORIGINS, CommandLineArgument.ArgumentType.Path, Strings.Program.WebserverCorsOriginsDescription, Strings.Program.WebserverCorsOriginsDescription, WebServerLoader.DEFAULT_OPTION_SPAPATHS),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_PRE_AUTH_TOKENS, CommandLineArgument.ArgumentType.String, Strings.Program.WebserverPreAuthTokensDescription, Strings.Program.WebserverPreAuthTokensDescription),
            new CommandLineArgument(WebServerLoader.OPTION_WEBSERVICE_TOKENDURATION, CommandLineArgument.ArgumentType.Timespan, Strings.Program.WebserverTokenDurationDescription, Strings.Program.WebserverTokenDurationDescription, WebServerLoader.DEFAULT_OPTION_TOKENDURATION),
            new CommandLineArgument(PING_PONG_KEEPALIVE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.PingpongkeepaliveShort, Strings.Program.PingpongkeepaliveLong),
            new CommandLineArgument(DISABLE_UPDATE_CHECK_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.DisableupdatecheckShort, Strings.Program.DisableupdatecheckLong),
            new CommandLineArgument(LOG_RETENTION_OPTION, CommandLineArgument.ArgumentType.Timespan, Strings.Program.LogretentionShort, Strings.Program.LogretentionLong, DEFAULT_LOG_RETENTION),
            new CommandLineArgument(DataFolderManager.SERVER_DATAFOLDER_OPTION, CommandLineArgument.ArgumentType.Path, Strings.Program.ServerdatafolderShort, Strings.Program.ServerdatafolderLong(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly)), DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly)),
            new CommandLineArgument(DISABLE_DB_ENCRYPTION_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.DisabledbencryptionShort, Strings.Program.DisabledbencryptionLong),
            new CommandLineArgument(REQUIRE_DB_ENCRYPTION_KEY_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Program.RequiredbencryptionShort, Strings.Program.RequiredbencryptionLong),
            new CommandLineArgument(SETTINGS_ENCRYPTION_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Program.SettingsencryptionkeyShort, Strings.Program.SettingsencryptionkeyLong(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME)),
            new CommandLineArgument(REGISTER_REMOTE_CONTROL_OPTION, CommandLineArgument.ArgumentType.String, Strings.Program.RegisterRemoteControlShort, Strings.Program.RegisterRemoteControlLong),
            new CommandLineArgument(REGISTER_REMOTE_CONTROL_REREGISTER_OPTION, CommandLineArgument.ArgumentType.String, Strings.Program.RegisterRemoteControlReregisterShort, Strings.Program.RegisterRemoteControlReregisterLong),
            .. SECRET_PROVIDER_OPTIONS
        ];

        /// <summary>
        /// List of known duplicate option names
        /// </summary>
        public static readonly HashSet<string> KnownDuplicateOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool ReadOptionsFromFile(string filename, ref IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                var fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Environment.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var newsource = new List<string>();
                string newtarget = null;
                string prependfilter = null;
                string appendfilter = null;
                string replacefilter = null;

                var tmpparsed = FilterCollector.ExtractOptions(fargs, (key, value) =>
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
                    throw new UserInformationException(Strings.Program.FiltersCannotBeUsedWithFileError2, "FiltersCannotBeUsedOnCommandLineAndInParameterFile");

                if (!newfilter.Empty)
                    filter = newfilter;

                if (!string.IsNullOrWhiteSpace(prependfilter))
                    filter = FilterExpression.Combine(FilterExpression.Deserialize(prependfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)), filter);

                if (!string.IsNullOrWhiteSpace(appendfilter))
                    filter = FilterExpression.Combine(filter, FilterExpression.Deserialize(appendfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)));

                if (!string.IsNullOrWhiteSpace(replacefilter))
                    filter = FilterExpression.Deserialize(replacefilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));

                foreach (var keyvalue in opt)
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
                    Log.WriteVerboseMessage(LOGTAG, "NotUsingBackupSources", Strings.Program.SkippingSourceArgumentsOnNonBackupOperation);

                return true;
            }
            catch (Exception e)
            {
                throw new Exception(Strings.Program.FailedToParseParametersFileError(filename, e.Message));
            }
        }
    }
}
