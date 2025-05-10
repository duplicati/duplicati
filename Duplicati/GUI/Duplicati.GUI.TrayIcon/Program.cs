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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using CoCoL;
using Duplicati.CommandLine;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Uri = System.Uri;

namespace Duplicati.GUI.TrayIcon
{
    public static class Program
    {
        /// <summary>
        /// The source of the password
        /// </summary>
        public enum PasswordSource
        {
            /// <summary>
            /// No password, using token information from the database
            /// </summary>
            Database,
            /// <summary>
            /// No password supplied, using the hosted server
            /// </summary>
            HostedServer,
            /// <summary>
            /// The password was supplied on the commandline
            /// </summary>
            SuppliedPassword
        }

        public static HttpServerConnection Connection;

        private const string HOSTURL_OPTION = "hosturl";
        private const string NOHOSTEDSERVER_OPTION = "no-hosted-server";
        private const string READCONFIGFROMDB_OPTION = "read-config-from-db";
        private const string ACCEPTED_SSL_CERTIFICATE = "host-cert-hash";

        private const string DETACHED_PROCESS = "detached-process";
        private const string BROWSER_COMMAND_OPTION = "browser-command";
        private static readonly IReadOnlySet<string> DETATCHED_WEBERVER_OPTIONS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            WebServerLoader.OPTION_WEBSERVICE_PASSWORD,
            WebServerLoader.OPTION_PORT,
            DataFolderManager.SERVER_DATAFOLDER_OPTION,
            DataFolderManager.PORTABLE_MODE_OPTION
        };


        private static string DEFAULT_HOSTURL => $"http://{Utility.IpVersionCompatibleLoopback}:8200";

        private static string _browser_command = null;
        private static bool disableTrayIconLogin = false;
        private static bool openui = false;
        private static Uri serverURL = new(DEFAULT_HOSTURL);
        public static string BrowserCommand { get { return _browser_command; } }
        public static Server.Database.Connection databaseConnection = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, PackageHelper.NamedExecutable.TrayIcon);
            var args = new List<string>(_args);
            var options = CommandLineParser.ExtractOptions(args);

            if (OperatingSystem.IsWindows() && !Utility.ParseBoolOption(options, DETACHED_PROCESS))
                Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);

            if (HelpOptionExtensions.IsArgumentAnyHelpString(args))
            {
                Console.WriteLine("Supported commandline arguments:");
                Console.WriteLine();

                foreach (ICommandLineArgument arg in AllSupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                Console.WriteLine("Additionally, these server options are also supported:");
                Console.WriteLine();

                foreach (ICommandLineArgument arg in WebserverSupportedCommands)
                    Console.WriteLine("--{0}: {1}", arg.Name, arg.LongDescription);

                return 0;
            }

            options.TryGetValue(BROWSER_COMMAND_OPTION, out _browser_command);

            HostedInstanceKeeper hosted = null;

            string password = null;
            var passwordSource = PasswordSource.SuppliedPassword;
            var acceptedHostCertificate = options.GetValueOrDefault(ACCEPTED_SSL_CERTIFICATE, null);
            var detached = Utility.ParseBoolOption(options, NOHOSTEDSERVER_OPTION);

            var supportedCommands = BasicSupportedCommands.AsEnumerable();
            if (detached)
            {
                supportedCommands = DetachSupportedCommands
                    .Concat(supportedCommands);
            }
            else
            {
                supportedCommands = supportedCommands.Concat(WebserverSupportedCommands);
            }

            // Validate options, and log to console
            using (var logger = new ConsoleOutput(Console.Out, options))
                CommandLineArgumentValidator.ValidateArguments(supportedCommands, options, Server.Program.KnownDuplicateOptions, new HashSet<string>());

            if (!detached)
            {
                try
                {
                    // Tell the hosted server it was started by the TrayIcon
                    var applicationSettings = new ApplicationSettings();
                    applicationSettings.Origin = "Tray icon";
                    passwordSource = PasswordSource.HostedServer;
                    // Ignore TrayIcon specific settings
                    foreach (var c in BasicSupportedCommands.Select(x => x.Name))
                        Server.Program.ValidationIgnoredOptions.Add(c);
                    hosted = new HostedInstanceKeeper(applicationSettings, _args);
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null && ex.InnerException is Server.SingleInstance.MultipleInstanceException)
                    {
                        Console.WriteLine(Server.Strings.Program.AnotherInstanceDetected);
                        return 1;
                    }
                    else
                        throw;
                }

                // We have a hosted server, if this is the first run, 
                // we should open the main page
                var connection = Server.Program.DuplicatiWebserver.Provider.GetRequiredService<Connection>();
                openui = connection.ApplicationSettings.IsFirstRun || connection.ApplicationSettings.ServerPortChanged;

                var scheme = connection.ApplicationSettings.UseHTTPS ? "https" : "http";
                serverURL = new UriBuilder(serverURL)
                {
                    Port = Server.Program.DuplicatiWebserver.Port,
                    Scheme = scheme
                }.Uri;

                if (connection.ApplicationSettings.UseHTTPS && string.IsNullOrWhiteSpace(acceptedHostCertificate))
                    acceptedHostCertificate = connection.ApplicationSettings.ServerSSLCertificate?.FirstOrDefault(x => x.HasPrivateKey)?.GetCertHashString();

            }
            else if (Utility.ParseBoolOption(options, READCONFIGFROMDB_OPTION))
            {
                if (File.Exists(Path.Combine(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ReadWritePermissionSet), DataFolderManager.SERVER_DATABASE_FILENAME)))
                {
                    passwordSource = PasswordSource.Database;
                    databaseConnection = Server.Program.GetDatabaseConnection(new ApplicationSettings(), options, true);

                    if (databaseConnection != null)
                    {
                        disableTrayIconLogin = databaseConnection.ApplicationSettings.DisableTrayIconLogin;
                        if (databaseConnection.ApplicationSettings.UseHTTPS && string.IsNullOrWhiteSpace(acceptedHostCertificate))
                            acceptedHostCertificate = databaseConnection.ApplicationSettings.ServerSSLCertificate?.FirstOrDefault(x => x.HasPrivateKey)?.GetCertHashString();

                        var scheme = databaseConnection.ApplicationSettings.UseHTTPS ? "https" : "http";
                        serverURL = new UriBuilder(serverURL)
                        {
                            Port = databaseConnection.ApplicationSettings.LastWebserverPort == -1 ? serverURL.Port : databaseConnection.ApplicationSettings.LastWebserverPort,
                            Scheme = scheme
                        }.Uri;
                    }
                }
            }

            // Legacy undocumented way to provide the password
            if (options.TryGetValue("webserver-password", out var pwd))
                password = pwd;
            if (options.TryGetValue(WebServerLoader.OPTION_WEBSERVICE_PASSWORD, out pwd))
                password = pwd;

            // Let the user specify the port, if they are not providing a hosturl
            if (!options.ContainsKey(HOSTURL_OPTION) && options.TryGetValue(WebServerLoader.OPTION_PORT, out var portString) && int.TryParse(portString, out var port))
                serverURL = new UriBuilder(serverURL) { Port = port }.Uri;

            if (options.TryGetValue(HOSTURL_OPTION, out var url))
                serverURL = new Uri(url);

            if (string.IsNullOrWhiteSpace(password) && passwordSource == PasswordSource.SuppliedPassword)
            {
                Console.WriteLine($@"
When running the TrayIcon without a hosted server, you must provide the server password via the option --{WebServerLoader.OPTION_WEBSERVICE_PASSWORD}=<password>.
If the TrayIcon instance has read access to the server database, you can also or use the option --{READCONFIGFROMDB_OPTION}, possibly with --server-datafolder=<path>.

No password provided, unable to connect to server, exiting");
                return 1;
            }

            StartTray(_args, options, hosted, passwordSource, password, acceptedHostCertificate);

            return 0;
        }

        private static void StartTray(string[] _args, Dictionary<string, string> options, HostedInstanceKeeper hosted, PasswordSource passwordSource, string password, string acceptedHostCertificate)
        {
            using (hosted)
            {
                var reSpawn = 0;

                do
                {
                    if (reSpawn > 0)
                        Thread.Sleep(1000);

                    try
                    {
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
                        using (Connection = new HttpServerConnection(serverURL, password, passwordSource, disableTrayIconLogin, acceptedHostCertificate, options))
                        {
                            // Make sure we have the latest status, but don't care if it fails
                            Connection.UpdateStatus().FireAndForget();

                            using (var tk = RunTrayIcon())
                            {
                                if (hosted != null && Server.Program.ApplicationInstance != null)
                                    Server.Program.ApplicationInstance.SecondInstanceDetected +=
                                        new SingleInstance.SecondInstanceDelegate(
                                            x => tk.ShowStatusWindow());

                                // TODO: If we change to hosted browser this should be a callback
                                if (openui)
                                {
                                    Connection.GetStatusWindowURLAsync().ContinueWith(t =>
                                    {
                                        if (t.IsFaulted)
                                        {
                                            Console.WriteLine("Failed to get status window URL: " + t.Exception.Message);
                                            tk.NotifyUser("Failed to get status window URL", t.Exception.Message, NotificationType.Error);
                                            return;
                                        }

                                        tk.ShowUrlInWindow(t.Result);
                                        var connection = Server.Program.DuplicatiWebserver.Provider.GetRequiredService<Connection>();
                                        connection.ApplicationSettings.IsFirstRun = false;
                                        connection.ApplicationSettings.ServerPortChanged = false;

                                    });
                                }

                                // If the server shuts down, shut down the tray-icon as well
                                Action shutdownEvent = () =>
                                {
                                    // Make sure we do not start again after 
                                    // a controlled exit
                                    reSpawn = 100;
                                    tk.InvokeExit();
                                };

                                Connection.ConnectionClosed = shutdownEvent;
                                if (hosted != null)
                                    hosted.InstanceShutdown = shutdownEvent;

                                tk.Init(_args);

                                // If the tray-icon quits, stop the server
                                reSpawn = 100;

                                // Make sure that the server shutdown does not access the tray-icon,
                                // as it would be disposed by now
                                if (hosted != null)
                                    hosted.InstanceShutdown = null;
                                Connection.ConnectionClosed = null;

                            }
                        }
                    }
                    catch (HttpRequestException ex)
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

        public static ICommandLineArgument[] DetachSupportedCommands =>
        [
            new CommandLineArgument(NOHOSTEDSERVER_OPTION, CommandLineArgument.ArgumentType.String, "Disables local server", "Set this option to not spawn a local service, use if the TrayIcon should connect to a running service"),
            new CommandLineArgument(READCONFIGFROMDB_OPTION, CommandLineArgument.ArgumentType.String, "Read server connection info from DB", $"Set this option to read server connection info for running service from its database (only together with {NOHOSTEDSERVER_OPTION})"),
            .. WebserverSupportedCommands.Where(x => DETATCHED_WEBERVER_OPTIONS.Contains(x.Name))
        ];

        public static ICommandLineArgument[] BasicSupportedCommands =>
        [
            new CommandLineArgument(HOSTURL_OPTION, CommandLineArgument.ArgumentType.String, "Selects the url to connect to", "Supply the url that the TrayIcon will connect to and show status for", DEFAULT_HOSTURL),
            new CommandLineArgument(BROWSER_COMMAND_OPTION, CommandLineArgument.ArgumentType.String, "Sets the browser command", "Set this option to override the default browser detection"),
            new CommandLineArgument(ACCEPTED_SSL_CERTIFICATE, CommandLineArgument.ArgumentType.String, "Accepts a specific SSL certificate", "Set this option to accept a specific SSL certificate, the value should be the hash of the certificate in hexadecimal format. Use * to accept any certificate (dangerous)"),
            .. WindowsSupportedCommands
        ];

        private static ICommandLineArgument[] WindowsSupportedCommands =>
            OperatingSystem.IsWindows()
                ? [new CommandLineArgument(DETACHED_PROCESS, CommandLineArgument.ArgumentType.String, "Runs the tray-icon detached", "This option runs the tray-icon in detached mode, meaning that the process will exit immediately and not send output to the console of the caller")]
                : [];

        public static ICommandLineArgument[] WebserverSupportedCommands
            => Server.Program.SupportedCommands;

        public static ICommandLineArgument[] AllSupportedCommands =>
        [
            .. DetachSupportedCommands,
            .. BasicSupportedCommands
        ];
    }
}
